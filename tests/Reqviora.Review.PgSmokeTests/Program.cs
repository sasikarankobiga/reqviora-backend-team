
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Review.Persistence;

var connectionString =
    Environment.GetEnvironmentVariable(
        "REQVIORA_REVIEW_PG");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "Missing REQVIORA_REVIEW_PG environment variable.");
    return 2;
}

var projectId = Guid.NewGuid();
var requirementId =
    "REQ-SMOKE-" +
    Guid.NewGuid().ToString("N").ToUpperInvariant();
var reviewerId = Guid.NewGuid();

var evidence = new List<CandidateEvidence>
{
    new("SRC-SMOKE", "Test document", "Login is required.")
};

int passed = 0;
bool inserted = false;

void Check(bool condition, string description)
{
    if (!condition)
        throw new Exception("FAILED: " + description);

    passed++;
    Console.WriteLine("[PASS] " + description);
}

void AddIdentity(NpgsqlCommand command)
{
    command.Parameters.AddWithValue(
        "project_id", NpgsqlDbType.Uuid, projectId);

    command.Parameters.AddWithValue(
        "requirement_id",
        NpgsqlDbType.Varchar,
        requirementId);
}

await using var connection =
    new NpgsqlConnection(connectionString);

await connection.OpenAsync();

var schemaPath = Path.GetFullPath("database/reqviora_review/001_create_review_store.sql");
await using (var schema = new NpgsqlCommand(await File.ReadAllTextAsync(schemaPath), connection))
    await schema.ExecuteNonQueryAsync();

try
{
    // Seed one temporary requirement.
    const string seedSql = """
        INSERT INTO reqviora_review.requirements
        (
            project_id,
            requirement_id,
            title,
            description,
            status,
            revision,
            evidence_json
        )
        VALUES
        (
            @project_id,
            @requirement_id,
            @title,
            @description,
            'Suggested',
            1,
            @evidence_json
        );
        """;

    await using (var seed =
        new NpgsqlCommand(seedSql, connection))
    {
        AddIdentity(seed);

        seed.Parameters.AddWithValue(
            "title", NpgsqlDbType.Varchar, "Login");

        seed.Parameters.AddWithValue(
            "description",
            NpgsqlDbType.Varchar,
            "User login requirement");

        seed.Parameters.AddWithValue(
            "evidence_json",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(evidence));

        await seed.ExecuteNonQueryAsync();
        inserted = true;
    }

    var store = new PostgresRequirementReviewStore(
        connectionString);

    // TEST 1: Read candidate.
    var snapshot = await store.FindAsync(
        projectId, requirementId);

    Check(
        snapshot is not null &&
        snapshot.Requirement.Status ==
            RequirementReviewStatus.Suggested &&
        snapshot.Requirement.Evidence.Count == 1,
        "PostgreSQL reads suggested requirement");

    // TEST 2: Human review and commit.
    var decision = new RequirementReviewDecision(
        requirementId,
        1,
        RequirementDecisionKind.Accept,
        reviewerId,
        DateTimeOffset.UtcNow);

    var result = RequirementReviewService.Apply(
        snapshot!.Requirement,
        decision);

    var saved = await store.TryCommitAsync(
        projectId, 1, result);

    Check(saved, "Review revision commits");

    // TEST 3: Confirmed requirement persisted.
    var updated = await store.FindAsync(
        projectId, requirementId);

    Check(
        updated is not null &&
        updated.Requirement.Status ==
            RequirementReviewStatus.Confirmed &&
        updated.Requirement.Revision == 2 &&
        updated.Requirement.ConfirmedBy == reviewerId,
        "Human confirmation is persisted");

    // TEST 4: Prevent stale update.
    var stale = await store.TryCommitAsync(
        projectId, 1, result);

    Check(!stale, "Stale revision is rejected");

    // TEST 5: Audit event persisted once.
    const string auditSql = """
        SELECT COUNT(*)
        FROM reqviora_review.events
        WHERE project_id = @project_id
          AND requirement_id = @requirement_id
          AND actor_id = @actor_id;
        """;

    await using (var audit =
        new NpgsqlCommand(auditSql, connection))
    {
        AddIdentity(audit);

        audit.Parameters.AddWithValue(
            "actor_id",
            NpgsqlDbType.Uuid,
            reviewerId);

        var count = Convert.ToInt32(
            await audit.ExecuteScalarAsync());

        Check(
            count == 1,
            "One audit event created for reviewer");
    }

    Console.WriteLine();
    Console.WriteLine($"PASSED: {passed}");
    Console.WriteLine("FAILED: 0");
}
finally
{
    if (inserted)
    {
        // Delete ONLY this test's temporary records.
        const string cleanupSql = """
            DELETE FROM reqviora_review.events
            WHERE project_id = @project_id
              AND requirement_id = @requirement_id;

            DELETE FROM reqviora_review.requirements
            WHERE project_id = @project_id
              AND requirement_id = @requirement_id;
            """;

        await using var cleanup =
            new NpgsqlCommand(cleanupSql, connection);

        AddIdentity(cleanup);

        await cleanup.ExecuteNonQueryAsync();
    }
}

return 0;
