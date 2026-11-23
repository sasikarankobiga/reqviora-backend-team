using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Review.Persistence;

var connectionString = Environment.GetEnvironmentVariable("REQVIORA_REVIEW_SQLSERVER");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Missing REQVIORA_REVIEW_SQLSERVER environment variable.");
    return 2;
}

var projectId = Guid.NewGuid();
var requirementId = "REQ-SMOKE-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
var reviewerId = Guid.NewGuid();
var evidence = new List<CandidateEvidence> { new("SRC-SMOKE", "Test document", "Login is required.") };
var passed = 0;
var inserted = false;

void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAILED: " + description);
    passed++;
    Console.WriteLine("[PASS] " + description);
}

void AddIdentity(SqlCommand command)
{
    command.Parameters.Add("project_id", SqlDbType.UniqueIdentifier).Value = projectId;
    command.Parameters.Add("requirement_id", SqlDbType.VarChar, 100).Value = requirementId;
}

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();
var schemaPath = Path.GetFullPath("database/reqviora_review/001_create_review_store.sqlserver.sql");
var script = await File.ReadAllTextAsync(schemaPath);
foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
{
    if (string.IsNullOrWhiteSpace(batch)) continue;
    await using var schema = new SqlCommand(batch, connection);
    await schema.ExecuteNonQueryAsync();
}

try
{
    const string seedSql = """
        INSERT INTO reqviora_review.requirements
            (project_id, requirement_id, title, description, status, revision, evidence_json)
        VALUES
            (@project_id, @requirement_id, @title, @description, 'Suggested', 1, @evidence_json);
        """;
    await using (var seed = new SqlCommand(seedSql, connection))
    {
        AddIdentity(seed);
        seed.Parameters.Add("title", SqlDbType.NVarChar, 200).Value = "Login";
        seed.Parameters.Add("description", SqlDbType.NVarChar, 4000).Value = "User login requirement";
        seed.Parameters.Add("evidence_json", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(evidence);
        await seed.ExecuteNonQueryAsync();
        inserted = true;
    }

    var store = new SqlServerRequirementReviewStore(connectionString);
    var snapshot = await store.FindAsync(projectId, requirementId);
    Check(snapshot is not null && snapshot.Requirement.Status == RequirementReviewStatus.Suggested &&
        snapshot.Requirement.Evidence.Count == 1, "SQL Server reads suggested requirement");

    var decision = new RequirementReviewDecision(
        requirementId, 1, RequirementDecisionKind.Accept, reviewerId, DateTimeOffset.UtcNow);
    var result = RequirementReviewService.Apply(snapshot!.Requirement, decision);
    Check(await store.TryCommitAsync(projectId, 1, result), "Review revision commits");

    var updated = await store.FindAsync(projectId, requirementId);
    Check(updated is not null && updated.Requirement.Status == RequirementReviewStatus.Confirmed &&
        updated.Requirement.Revision == 2 && updated.Requirement.ConfirmedBy == reviewerId,
        "Human confirmation is persisted");
    Check(!await store.TryCommitAsync(projectId, 1, result), "Stale revision is rejected");

    const string auditSql = """
        SELECT COUNT(*) FROM reqviora_review.events
        WHERE project_id = @project_id AND requirement_id = @requirement_id AND actor_id = @actor_id;
        """;
    await using (var audit = new SqlCommand(auditSql, connection))
    {
        AddIdentity(audit);
        audit.Parameters.Add("actor_id", SqlDbType.UniqueIdentifier).Value = reviewerId;
        Check(Convert.ToInt32(await audit.ExecuteScalarAsync()) == 1, "One audit event created for reviewer");
    }
    Console.WriteLine($"PASSED: {passed}");
    Console.WriteLine("FAILED: 0");
}
finally
{
    if (inserted)
    {
        const string cleanupSql = """
            DELETE FROM reqviora_review.events WHERE project_id = @project_id AND requirement_id = @requirement_id;
            DELETE FROM reqviora_review.requirements WHERE project_id = @project_id AND requirement_id = @requirement_id;
            """;
        await using var cleanup = new SqlCommand(cleanupSql, connection);
        AddIdentity(cleanup);
        await cleanup.ExecuteNonQueryAsync();
    }
}
return 0;
