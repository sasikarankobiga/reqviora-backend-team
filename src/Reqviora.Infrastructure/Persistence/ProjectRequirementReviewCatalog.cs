
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;
using Microsoft.EntityFrameworkCore.Storage;

namespace Reqviora.Infrastructure.Persistence;

public sealed class ProjectRequirementReviewCatalog(
    ReqvioraDbContext db) : IRequirementReviewCatalog
{
    public async Task<IReadOnlyList<ReviewedRequirement>>
        ListByProjectAsync(
            Guid projectId,
            CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException(
                "Valid project ID is required.",
                nameof(projectId));

        var connection = db.Database.GetDbConnection();
        var openedHere =
            connection.State != ConnectionState.Open;

        if (openedHere)
            await connection.OpenAsync(cancellationToken);

        try
        {
await using var command =
    connection.CreateCommand();

// Use the active EF Core transaction when one exists.
command.Transaction =
    db.Database.CurrentTransaction?.GetDbTransaction();

command.CommandText = """

                SELECT
                    requirement_id,
                    title,
                    description,
                    status,
                    revision,
                    evidence_json,
                    confirmed_by,
                    confirmed_at
                FROM reqviora_review.requirements
                WHERE project_id = @project_id
                ORDER BY requirement_id;
                """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@project_id";
            parameter.Value = projectId;
            command.Parameters.Add(parameter);

            var results = new List<ReviewedRequirement>();

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                // Fail closed rather than silently omitting
                // requirements from a synchronization audit.
                if (results.Count >= 10000)
                    throw new InvalidDataException(
                        "Review catalog exceeds supported size.");

                if (!Enum.TryParse<RequirementReviewStatus>(
                        reader.GetString(3),
                        false,
                        out var status) ||
                    !Enum.IsDefined(status))
                {
                    throw new InvalidDataException(
                        "Invalid stored review status.");
                }

                var evidence = JsonSerializer.Deserialize<
                    List<CandidateEvidence>>(
                        reader.GetString(5));

                if (evidence is null || evidence.Count == 0 ||
                    evidence.Any(e =>
                        e is null ||
                        string.IsNullOrWhiteSpace(e.SourceId) ||
                        string.IsNullOrWhiteSpace(e.Locator) ||
                        string.IsNullOrWhiteSpace(e.Quote)))
                {
                    throw new InvalidDataException(
                        "Invalid stored source evidence.");
                }

                Guid? confirmedBy =
                    reader.IsDBNull(6)
                        ? null
                        : reader.GetGuid(6);

                DateTimeOffset? confirmedAt =
                    reader.IsDBNull(7)
                        ? null
                        : reader.GetFieldValue<DateTimeOffset>(7);

                bool isConfirmed =
                    status == RequirementReviewStatus.Confirmed;

                if (isConfirmed !=
                        (confirmedBy.HasValue &&
                         confirmedBy != Guid.Empty &&
                         confirmedAt.HasValue))
                {
                    throw new InvalidDataException(
                        "Invalid stored confirmation.");
                }

                results.Add(new ReviewedRequirement(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    status,
                    reader.GetInt32(4),
                    evidence,
                    confirmedBy,
                    confirmedAt));
            }

            return results;
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }
}
