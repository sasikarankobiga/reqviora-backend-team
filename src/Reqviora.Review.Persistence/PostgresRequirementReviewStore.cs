
using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;

namespace Reqviora.Review.Persistence;

public sealed class PostgresRequirementReviewStore
    : IRequirementReviewStore
{
    private readonly string _connectionString;

    public PostgresRequirementReviewStore(
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "PostgreSQL connection string required.");
        }

        _connectionString = connectionString;
    }

    // READ REQUIREMENT
    public async Task<RequirementReviewSnapshot?> FindAsync(
        Guid projectId,
        string requirementId,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty ||
            string.IsNullOrWhiteSpace(requirementId))
            return null;

        await using var connection =
            new NpgsqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                title,
                description,
                status,
                revision,
                evidence_json::text,
                confirmed_by,
                confirmed_at
            FROM reqviora_review.requirements
            WHERE project_id = @project_id
              AND requirement_id = @requirement_id;
            """;

        await using var command = new NpgsqlCommand(
            sql, connection);

        AddIdentity(command, projectId, requirementId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        if (!Enum.TryParse<RequirementReviewStatus>(
                reader.GetString(2),
                false,
                out var status) ||
            !Enum.IsDefined(status))
        {
            throw new InvalidDataException(
                "Invalid stored requirement status.");
        }

        var evidence =
            JsonSerializer.Deserialize<List<CandidateEvidence>>(
                reader.GetString(4))
            ?? throw new InvalidDataException(
                "Invalid stored evidence.");

        var requirement = new ReviewedRequirement(
            requirementId,
            reader.GetString(0),
            reader.GetString(1),
            status,
            reader.GetInt32(3),
            evidence,
            reader.IsDBNull(5)
                ? null
                : reader.GetGuid(5),
            reader.IsDBNull(6)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(6)
        );

        return new RequirementReviewSnapshot(
            projectId,
            requirement);
    }

    // ATOMIC REVISION UPDATE + AUDIT INSERT
    public async Task<bool> TryCommitAsync(
        Guid projectId,
        int expectedRevision,
        RequirementReviewResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var requirement = result.Requirement;
        var reviewEvent = result.Event;

        if (projectId == Guid.Empty ||
            expectedRevision < 1 ||
            requirement is null ||
            reviewEvent is null ||
            string.IsNullOrWhiteSpace(requirement.Id) ||
            requirement.Id != reviewEvent.RequirementId ||
            requirement.Revision != expectedRevision + 1 ||
            reviewEvent.PreviousRevision != expectedRevision ||
            reviewEvent.NewRevision != requirement.Revision ||
            reviewEvent.ActorId == Guid.Empty ||
            reviewEvent.OccurredAt == default ||
            string.IsNullOrWhiteSpace(requirement.Title) ||
            requirement.Title.Length > 200 ||
            requirement.Description is null ||
            requirement.Description.Length > 4000 ||
            requirement.Evidence is null ||
            requirement.Evidence.Count == 0 ||
            !Enum.IsDefined(requirement.Status) ||
            !Enum.IsDefined(reviewEvent.Decision))
        {
            throw new ArgumentException(
                "Invalid review commit.", nameof(result));
        }

        // Human confirmation must match the review event.
        if (requirement.Status ==
            RequirementReviewStatus.Confirmed)
        {
            if (reviewEvent.Decision !=
                    RequirementDecisionKind.Accept ||
                requirement.ConfirmedBy !=
                    reviewEvent.ActorId ||
                requirement.ConfirmedAt !=
                    reviewEvent.OccurredAt)
            {
                throw new ArgumentException(
                    "Invalid human confirmation.");
            }
        }
        else if (requirement.ConfirmedBy is not null ||
                 requirement.ConfirmedAt is not null)
        {
            throw new ArgumentException(
                "Unconfirmed record has approval fields.");
        }

        var evidenceJson =
            JsonSerializer.Serialize(requirement.Evidence);

        await using var connection =
            new NpgsqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        const string updateSql = """
            UPDATE reqviora_review.requirements
            SET
                title = @title,
                description = @description,
                status = @status,
                revision = @new_revision,
                evidence_json = @evidence_json,
                confirmed_by = @confirmed_by,
                confirmed_at = @confirmed_at
            WHERE project_id = @project_id
              AND requirement_id = @requirement_id
              AND revision = @expected_revision;
            """;

        await using (var update = new NpgsqlCommand(
            updateSql, connection, transaction))
        {
            AddIdentity(
                update, projectId, requirement.Id);

            update.Parameters.AddWithValue(
                "title",
                NpgsqlDbType.Varchar,
                requirement.Title);

            update.Parameters.AddWithValue(
                "description",
                NpgsqlDbType.Varchar,
                requirement.Description);

            update.Parameters.AddWithValue(
                "status",
                NpgsqlDbType.Varchar,
                requirement.Status.ToString());

            update.Parameters.AddWithValue(
                "new_revision",
                NpgsqlDbType.Integer,
                requirement.Revision);

            update.Parameters.AddWithValue(
                "expected_revision",
                NpgsqlDbType.Integer,
                expectedRevision);

            update.Parameters.AddWithValue(
                "evidence_json",
                NpgsqlDbType.Jsonb,
                evidenceJson);

            AddNullable(
                update,
                "confirmed_by",
                NpgsqlDbType.Uuid,
                requirement.ConfirmedBy);

            AddNullable(
                update,
                "confirmed_at",
                NpgsqlDbType.TimestampTz,
                requirement.ConfirmedAt?.ToUniversalTime());

            int changed =
                await update.ExecuteNonQueryAsync(
                    cancellationToken);

            // Prevent overwriting another reviewer's update.
            if (changed != 1)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }
        }

        const string auditSql = """
            INSERT INTO reqviora_review.events
            (
                project_id,
                requirement_id,
                decision,
                actor_id,
                previous_revision,
                new_revision,
                occurred_at
            )
            VALUES
            (
                @project_id,
                @requirement_id,
                @decision,
                @actor_id,
                @previous_revision,
                @new_revision,
                @occurred_at
            );
            """;

        await using (var audit = new NpgsqlCommand(
            auditSql, connection, transaction))
        {
            AddIdentity(
                audit, projectId, requirement.Id);

            audit.Parameters.AddWithValue(
                "decision",
                NpgsqlDbType.Varchar,
                reviewEvent.Decision.ToString());

            audit.Parameters.AddWithValue(
                "actor_id",
                NpgsqlDbType.Uuid,
                reviewEvent.ActorId);

            audit.Parameters.AddWithValue(
                "previous_revision",
                NpgsqlDbType.Integer,
                reviewEvent.PreviousRevision);

            audit.Parameters.AddWithValue(
                "new_revision",
                NpgsqlDbType.Integer,
                reviewEvent.NewRevision);

            audit.Parameters.AddWithValue(
                "occurred_at",
                NpgsqlDbType.TimestampTz,
                reviewEvent.OccurredAt.ToUniversalTime());

            await audit.ExecuteNonQueryAsync(
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private static void AddIdentity(
        NpgsqlCommand command,
        Guid projectId,
        string requirementId)
    {
        command.Parameters.AddWithValue(
            "project_id",
            NpgsqlDbType.Uuid,
            projectId);

        command.Parameters.AddWithValue(
            "requirement_id",
            NpgsqlDbType.Varchar,
            requirementId);
    }

    private static void AddNullable(
        NpgsqlCommand command,
        string parameterName,
        NpgsqlDbType type,
        object? value)
    {
        command.Parameters.AddWithValue(
            parameterName,
            type,
            value ?? DBNull.Value);
    }
}
