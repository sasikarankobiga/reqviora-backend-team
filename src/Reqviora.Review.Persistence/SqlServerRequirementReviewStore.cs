using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;

namespace Reqviora.Review.Persistence;

public sealed class SqlServerRequirementReviewStore(string connectionString) : IRequirementReviewStore
{
    private readonly string _connectionString = string.IsNullOrWhiteSpace(connectionString)
        ? throw new ArgumentException("SQL Server connection string required.")
        : connectionString;

    public async Task<RequirementReviewSnapshot?> FindAsync(
        Guid projectId,
        string requirementId,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || string.IsNullOrWhiteSpace(requirementId)) return null;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT title, description, status, revision, evidence_json, confirmed_by, confirmed_at
            FROM reqviora_review.requirements
            WHERE project_id = @project_id AND requirement_id = @requirement_id;
            """;
        await using var command = new SqlCommand(sql, connection);
        AddIdentity(command, projectId, requirementId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!Enum.TryParse<RequirementReviewStatus>(reader.GetString(2), false, out var status) || !Enum.IsDefined(status))
            throw new InvalidDataException("Invalid stored requirement status.");
        var evidence = JsonSerializer.Deserialize<List<CandidateEvidence>>(reader.GetString(4))
            ?? throw new InvalidDataException("Invalid stored evidence.");
        var requirement = new ReviewedRequirement(
            requirementId,
            reader.GetString(0),
            reader.GetString(1),
            status,
            reader.GetInt32(3),
            evidence,
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6));
        return new RequirementReviewSnapshot(projectId, requirement);
    }

    public async Task<bool> TryCommitAsync(
        Guid projectId,
        int expectedRevision,
        RequirementReviewResult result,
        CancellationToken cancellationToken = default)
    {
        ValidateCommit(projectId, expectedRevision, result);
        var requirement = result.Requirement;
        var reviewEvent = result.Event;
        var evidenceJson = JsonSerializer.Serialize(requirement.Evidence);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        // Lock the project before committing a review decision.
        // Other transactions following the same protocol must wait.
        const string projectLockSql = """
            SELECT [Id]
            FROM [Projects] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Id] = @project_id;
            """;

        await using (var projectLock = new SqlCommand(
            projectLockSql, connection, transaction))
        {
            projectLock.Parameters.Add(
                "@project_id",
                SqlDbType.UniqueIdentifier
            ).Value = projectId;

            var lockedProject =
                await projectLock.ExecuteScalarAsync(
                    cancellationToken);

            if (lockedProject is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }


        // Block review changes while the current project draft
        // is approved. A new draft must be started first.
        const string approvedDraftSql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM [ProjectDrafts]
                WHERE [ProjectId] = @project_id
                  AND [ApprovedRevisionId] IS NOT NULL
            ) THEN 1 ELSE 0 END;
            """;

        await using (var approvedCheck = new SqlCommand(
            approvedDraftSql, connection, transaction))
        {
            approvedCheck.Parameters.Add(
                "@project_id",
                SqlDbType.UniqueIdentifier
            ).Value = projectId;

            var approved =
                await approvedCheck.ExecuteScalarAsync(
                    cancellationToken);

            if (Convert.ToInt32(approved) != 0)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }
        }





        const string updateSql = """
            UPDATE reqviora_review.requirements
            SET title = @title, description = @description, status = @status,
                revision = @new_revision, evidence_json = @evidence_json,
                confirmed_by = @confirmed_by, confirmed_at = @confirmed_at
            WHERE project_id = @project_id AND requirement_id = @requirement_id
              AND revision = @expected_revision;
            """;
        await using (var update = new SqlCommand(updateSql, connection, transaction))
        {
            AddIdentity(update, projectId, requirement.Id);
            update.Parameters.Add("title", SqlDbType.NVarChar, 200).Value = requirement.Title;
            update.Parameters.Add("description", SqlDbType.NVarChar, 4000).Value = requirement.Description;
            update.Parameters.Add("status", SqlDbType.VarChar, 16).Value = requirement.Status.ToString();
            update.Parameters.Add("new_revision", SqlDbType.Int).Value = requirement.Revision;
            update.Parameters.Add("expected_revision", SqlDbType.Int).Value = expectedRevision;
            update.Parameters.Add("evidence_json", SqlDbType.NVarChar, -1).Value = evidenceJson;
            update.Parameters.Add("confirmed_by", SqlDbType.UniqueIdentifier).Value =
                requirement.ConfirmedBy is { } confirmedBy ? confirmedBy : DBNull.Value;
            update.Parameters.Add("confirmed_at", SqlDbType.DateTimeOffset).Value =
                requirement.ConfirmedAt is { } confirmedAt ? confirmedAt : DBNull.Value;
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }
        const string auditSql = """
            INSERT INTO reqviora_review.events
                (project_id, requirement_id, decision, actor_id, previous_revision, new_revision, occurred_at)
            VALUES
                (@project_id, @requirement_id, @decision, @actor_id, @previous_revision, @new_revision, @occurred_at);
            """;
        await using (var audit = new SqlCommand(auditSql, connection, transaction))
        {
            AddIdentity(audit, projectId, requirement.Id);
            audit.Parameters.Add("decision", SqlDbType.VarChar, 12).Value = reviewEvent.Decision.ToString();
            audit.Parameters.Add("actor_id", SqlDbType.UniqueIdentifier).Value = reviewEvent.ActorId;
            audit.Parameters.Add("previous_revision", SqlDbType.Int).Value = reviewEvent.PreviousRevision;
            audit.Parameters.Add("new_revision", SqlDbType.Int).Value = reviewEvent.NewRevision;
            audit.Parameters.Add("occurred_at", SqlDbType.DateTimeOffset).Value = reviewEvent.OccurredAt;
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static void ValidateCommit(Guid projectId, int expectedRevision, RequirementReviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var requirement = result.Requirement;
        var reviewEvent = result.Event;
        if (projectId == Guid.Empty || expectedRevision < 1 || requirement is null || reviewEvent is null ||
            string.IsNullOrWhiteSpace(requirement.Id) || requirement.Id != reviewEvent.RequirementId ||
            requirement.Revision != expectedRevision + 1 || reviewEvent.PreviousRevision != expectedRevision ||
            reviewEvent.NewRevision != requirement.Revision || reviewEvent.ActorId == Guid.Empty ||
            reviewEvent.OccurredAt == default || string.IsNullOrWhiteSpace(requirement.Title) ||
            requirement.Title.Length > 200 || requirement.Description is null || requirement.Description.Length > 4000 ||
            requirement.Evidence is null || requirement.Evidence.Count == 0 ||
            !Enum.IsDefined(requirement.Status) || !Enum.IsDefined(reviewEvent.Decision))
            throw new ArgumentException("Invalid review commit.", nameof(result));
        if (requirement.Status == RequirementReviewStatus.Confirmed)
        {
            if (reviewEvent.Decision != RequirementDecisionKind.Accept ||
                requirement.ConfirmedBy != reviewEvent.ActorId || requirement.ConfirmedAt != reviewEvent.OccurredAt)
                throw new ArgumentException("Invalid human confirmation.", nameof(result));
        }
        else if (requirement.ConfirmedBy is not null || requirement.ConfirmedAt is not null)
            throw new ArgumentException("Unconfirmed record has approval fields.", nameof(result));
    }

    private static void AddIdentity(SqlCommand command, Guid projectId, string requirementId)
    {
        command.Parameters.Add("project_id", SqlDbType.UniqueIdentifier).Value = projectId;
        command.Parameters.Add("requirement_id", SqlDbType.VarChar, 100).Value = requirementId;
    }
}
