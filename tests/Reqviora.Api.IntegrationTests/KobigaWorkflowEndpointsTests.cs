using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reqviora.Api.Controllers;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class KobigaWorkflowEndpointsTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private string Prefix => $"/api/projects/{_factory.Project.Id}/workflow-intelligence";

    [Fact]
    public async Task Authentication_and_owner_access_are_enforced()
    {
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync(Prefix + "/reviews/REQ-001")).StatusCode);
        using var other = Client(_factory.Other);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await other.GetAsync(Prefix + "/reviews/REQ-001")).StatusCode);
    }

    [Fact]
    public async Task Only_human_confirmed_reviews_can_generate_a_saved_blueprint()
    {
        using var client = Client(_factory.Owner);
        var body = GenerateBody();
        using var rejected = await client.PostAsJsonAsync(Prefix + "/blueprints/generate", body);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        _factory.Store.Confirm(_factory.Owner.Id);
        using var created = await client.PostAsJsonAsync(Prefix + "/blueprints/generate", body);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var result = await created.Content.ReadFromJsonAsync<KobigaWorkflowController.BlueprintWorkflowResponse>();
        Assert.NotNull(result);
        Assert.True(result.Applied);
        Assert.Equal("confirmed", Assert.Single(result.Blueprint.Requirements).Status);
        Assert.Equal(1, result.DraftVersion);

        using var stale = await client.PostAsJsonAsync(Prefix + "/blueprints/generate", body);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // Preview preserves the previously saved draft and refuses protected screen edits.
        var proposed = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(result.Blueprint))!.AsObject();
        proposed["revision"] = 2;
        proposed["screens"]![0]!["name"] = "Unexpected AI rename";
        var previewBody = new
        {
            expectedConcurrencyToken = result.ConcurrencyToken,
            policy = new { expectedRevision = 1, protectedFeatureIds = Array.Empty<string>(),
                protectedScreenIds = new[] { "SCR-001" }, protectedComponentIds = Array.Empty<string>() },
            proposedBlueprint = proposed
        };
        using var preview = await client.PostAsJsonAsync(Prefix + "/blueprints/regenerate-preview", previewBody);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var fallback = await preview.Content.ReadFromJsonAsync<KobigaWorkflowController.BlueprintWorkflowResponse>();
        Assert.NotNull(fallback);
        Assert.False(fallback.Applied);
        Assert.Equal(result.ConcurrencyToken, fallback.ConcurrencyToken);
    }

    [Fact]
    public async Task Clarification_answers_are_saved_without_confirming_a_requirement()
    {
        using var client = Client(_factory.Owner);
        using var created = await client.PostAsJsonAsync(Prefix + "/clarifications", new
        {
            expectedConcurrencyToken = (Guid?)null,
            requirementId = "REQ-001",
            question = "Should staff also log in?"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var objectNode = JsonNode.Parse(await created.Content.ReadAsStringAsync())!.AsObject();
        var questionId = Guid.Parse(objectNode["clarification"]!["id"]!.GetValue<string>());
        var token = Guid.Parse(objectNode["concurrencyToken"]!.GetValue<string>());
        using var answered = await client.PostAsJsonAsync(
            Prefix + $"/clarifications/{questionId}/answer", new
            {
                expectedConcurrencyToken = token,
                expectedRevision = 1,
                answer = "Yes, after user confirmation"
            });
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        using var list = await client.GetAsync(Prefix + "/clarifications");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var questions = JsonNode.Parse(await list.Content.ReadAsStringAsync())!.AsArray();
        Assert.True(questions[0]!["isResolved"]!.GetValue<bool>());
        Assert.Equal(RequirementReviewStatus.Suggested, _factory.Store.Current.Status);
    }

    [Fact]
    public async Task Approved_draft_rejects_candidate_without_database_insert()
    {
        // Arrange: authenticated project owner.
        using var client = Client(_factory.Owner);

        // Create an existing project draft using the real workflow.
        _factory.Store.Confirm(_factory.Owner.Id);

        using var generated = await client.PostAsJsonAsync(
            Prefix + "/blueprints/generate",
            GenerateBody());

        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);

        // Simulate the persisted approved-draft marker.
        // This isolates the candidate-staging protection rule.

        // Arrange: create valid related database records
        // before marking the draft as approved.
        await using (var scope =
            _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<ReqvioraDbContext>();

            await using var seedTransaction =
                await db.Database.BeginTransactionAsync();

            var draft = await db.ProjectDrafts.SingleAsync(
                x => x.ProjectId == _factory.Project.Id);

            var version = await db.DesignDraftVersions
                .Where(x => x.ProjectId == _factory.Project.Id)
                .OrderByDescending(x => x.Version)
                .FirstAsync();

            // The approved revision must reference a
            // real validation run.

var validation = new ValidationRun
{
    DraftVersionId = version.Id,
    Passed = true,
    ValidatedAt = DateTimeOffset.UtcNow
};


            db.ValidationRuns.Add(validation);

            // Create an actual ApprovedRevisions database row.
            // This is test fixture data, not an API approval.

var approvedRevision = new ApprovedRevision
{
    ProjectId = _factory.Project.Id,
    DraftVersionId = version.Id,
    ValidationRunId = validation.Id,
    ApprovedBy = _factory.Owner.Id,
    ApprovedAt = DateTimeOffset.UtcNow,
    SnapshotJson = version.BlueprintJson
        ?? throw new InvalidOperationException(
            "Generated blueprint is missing.")
};

            db.ApprovedRevisions.Add(approvedRevision);

            // Insert referenced records first.
            await db.SaveChangesAsync();

            // Now the foreign key points to an existing row.
            draft.ApprovedRevisionId = approvedRevision.Id;

            await db.SaveChangesAsync();
            await seedTransaction.CommitAsync();
        }

        // This is the same valid candidate shape used in
        // the existing successful staging test.
        var candidate = new RequirementCandidate(
            "REQ-002",
            "User profile",
            "Users can update their profile",
            "en",
            0.95m,
            [
                new CandidateEvidence(
                    "SRC-002",
                    "page 2",
                    "Users can update their profile")
            ]);

        // Act: attempt to stage a requirement after approval.
        using var response = await client.PostAsJsonAsync(
            Prefix + "/candidates",
            candidate);

        // Assert: approved drafts must reject staging.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Verify the rejected candidate was not inserted.
        await using var verifyScope =
            _factory.Services.CreateAsyncScope();

        var verifyDb = verifyScope.ServiceProvider
            .GetRequiredService<ReqvioraDbContext>();

        var connection = verifyDb.Database.GetDbConnection();

        var openedHere =
            connection.State != System.Data.ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();

            command.CommandText = """
                SELECT COUNT(*)
                FROM reqviora_review.requirements
                WHERE project_id = @project_id
                  AND requirement_id = @requirement_id;
                """;

            var projectParameter = command.CreateParameter();
            projectParameter.ParameterName = "@project_id";
            projectParameter.Value = _factory.Project.Id;
            command.Parameters.Add(projectParameter);

            var requirementParameter = command.CreateParameter();
            requirementParameter.ParameterName = "@requirement_id";
            requirementParameter.Value = "REQ-002";
            command.Parameters.Add(requirementParameter);

            var count = Convert.ToInt64(
                await command.ExecuteScalarAsync());

            Assert.Equal(0L, count);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }


    public void Dispose() => _factory.Dispose();

    private HttpClient Client(User user)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.CreateAccessToken(user).Token);
        return client;
    }

    private static object GenerateBody() => new
    {
        expectedConcurrencyToken = (Guid?)null,
        blueprintId = "BP-TEST-001",
        confirmedRequirementIds = new[] { "REQ-001" },
        features = new[] { new { id = "FEAT-001", name = "Login", requirementIds = new[] { "REQ-001" } } },
        screens = new[] { new { id = "SCR-001", name = "Login", featureIds = new[] { "FEAT-001" } } },
        components = new[] { new { id = "CMP-001", screenId = "SCR-001", type = "Button", variant = "default" } },
        actions = Array.Empty<object>(),
        navigation = Array.Empty<object>()
    };

    [Fact]
    public async Task Staging_candidate_saves_review_history_and_unconfirmed_record()
    {
        using var client = Client(_factory.Owner);

        var candidate = new RequirementCandidate(
            "REQ-002",
            "User profile",
            "Users can update their profile",
            "en",
            0.95m,
            [
                new CandidateEvidence(
                    "SRC-002",
                    "page 2",
                    "Users can update their profile")
            ]);

        using var response = await client.PostAsJsonAsync(
            Prefix + "/candidates",
            candidate);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<ReqvioraDbContext>();

        var project = await db.Projects
            .AsNoTracking()
            .SingleAsync(x => x.Id == _factory.Project.Id);

        // The permanent review-history marker must be saved.
        Assert.True(project.HasRequirementReviewHistory);

        // The candidate must remain unconfirmed.
        using var command = db.Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = """
            SELECT status
            FROM reqviora_review.requirements
            WHERE requirement_id = 'REQ-002'
            """;

        var status = (string?)await command.ExecuteScalarAsync();

        Assert.Equal("Suggested", status);
    }

    [Fact]
    public async Task Only_project_owner_can_stage_requirement_candidates()
    {
        var candidate = new RequirementCandidate(
            "REQ-003",
            "Admin dashboard",
            "Only authorized users can manage the dashboard",
            "en",
            0.95m,
            [
                new CandidateEvidence(
                    "SRC-003",
                    "page 3",
                    "Only authorized users can manage the dashboard")
            ]);

        // Anonymous requests must be rejected.
        using (var anonymous = _factory.CreateClient())
        {
            using var response = await anonymous.PostAsJsonAsync(
                Prefix + "/candidates",
                candidate);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
        }

        // An authenticated non-owner must also be rejected.
        using (var other = Client(_factory.Other))
        {
            using var response = await other.PostAsJsonAsync(
                Prefix + "/candidates",
                candidate);

            Assert.Equal(
                HttpStatusCode.Forbidden,
                response.StatusCode);
        }

        // Verify that unauthorized requests changed no project marker.
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<ReqvioraDbContext>();

        var project = await db.Projects
            .AsNoTracking()
            .SingleAsync(x => x.Id == _factory.Project.Id);

        Assert.False(project.HasRequirementReviewHistory);

        // Verify that no candidate was inserted.
        using var command = db.Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM reqviora_review.requirements
            WHERE requirement_id = 'REQ-003'
            """;

        var count = Convert.ToInt64(
            await command.ExecuteScalarAsync());

        Assert.Equal(0L, count);
    }

    [Fact]
    public async Task Duplicate_candidate_returns_conflict_and_preserves_original()
    {
        using var client = Client(_factory.Owner);

        var candidate = new RequirementCandidate(
            "REQ-004",
            "Project notifications",
            "Users receive project notifications",
            "en",
            0.95m,
            [
                new CandidateEvidence(
                    "SRC-004",
                    "page 4",
                    "Users receive project notifications")
            ]);

        // First request must create the candidate.
        using var first = await client.PostAsJsonAsync(
            Prefix + "/candidates", candidate);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // The same requirement cannot be staged twice.
        using var duplicate = await client.PostAsJsonAsync(
            Prefix + "/candidates", candidate);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<ReqvioraDbContext>();

        var project = await db.Projects
            .AsNoTracking()
            .SingleAsync(x => x.Id == _factory.Project.Id);

        Assert.True(project.HasRequirementReviewHistory);

        using var command = db.Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM reqviora_review.requirements
            WHERE requirement_id = 'REQ-004'
            """;

        var count = Convert.ToInt64(
            await command.ExecuteScalarAsync());

        // Only the original candidate must exist.
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task Failed_marker_update_rolls_back_staged_candidate()
    {
        // Simulate a database failure during marker update.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<ReqvioraDbContext>();

            using var trigger = db.Database
                .GetDbConnection()
                .CreateCommand();

            trigger.CommandText = """
                CREATE TRIGGER TR_Test_BlockReviewMarker
                BEFORE UPDATE OF HasRequirementReviewHistory
                ON Projects
                WHEN NEW.HasRequirementReviewHistory = 1
                BEGIN
                    SELECT RAISE(
                        ABORT,
                        'Injected marker write failure'
                    );
                END;
                """;

            await trigger.ExecuteNonQueryAsync();
        }

        using var client = Client(_factory.Owner);

        var candidate = new RequirementCandidate(
            "REQ-005",
            "Project analytics",
            "Users can view project analytics",
            "en",
            0.95m,
            [
                new CandidateEvidence(
                    "SRC-005",
                    "page 5",
                    "Users can view project analytics")
            ]);

        using var response = await client.PostAsJsonAsync(
            Prefix + "/candidates",
            candidate);

        // The simulated database failure must reject the request.
        Assert.Equal(
            HttpStatusCode.InternalServerError,
            response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();

        var verifyDb = verifyScope.ServiceProvider
            .GetRequiredService<ReqvioraDbContext>();

        var project = await verifyDb.Projects
            .AsNoTracking()
            .SingleAsync(x => x.Id == _factory.Project.Id);

        // Project marker must remain unchanged.
        Assert.False(project.HasRequirementReviewHistory);

        using var command = verifyDb.Database
            .GetDbConnection()
            .CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM reqviora_review.requirements
            WHERE requirement_id = 'REQ-005'
            """;

        var count = Convert.ToInt64(
            await command.ExecuteScalarAsync());

        // Failed marker update must also roll back candidate insert.
        Assert.Equal(0L, count);
    }





    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _sqlite = new("Data Source=:memory:");
        public User Owner { get; } = UserWithEmail("kobiga-owner@example.test");
        public User Other { get; } = UserWithEmail("other-user@example.test");
        public Project Project { get; }
        public ReviewStore Store { get; } = new();

        public ApiFactory()
        {
            Project = new Project { Name = "Kobiga test", OwnerId = Owner.Id, PrimaryPlatform = "web" };
            Store.Seed(Project.Id);
            _sqlite.Open();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();

db.Database.EnsureCreated();
db.Users.AddRange(Owner, Other);
db.Projects.Add(Project);

// Persist test owner and project before API requests.
db.SaveChanges();

            // Test-only review schema.
            // The production schema is created by database scripts.
            using var schemaCommand = _sqlite.CreateCommand();

            schemaCommand.CommandText = """
                ATTACH DATABASE ':memory:' AS reqviora_review;

                CREATE TABLE reqviora_review.requirements
                (
                    project_id TEXT NOT NULL,
                    requirement_id TEXT NOT NULL,
                    title TEXT NOT NULL,
                    description TEXT NOT NULL,
                    status TEXT NOT NULL,
                    revision INTEGER NOT NULL,
                    evidence_json TEXT NOT NULL,
                    confirmed_by TEXT NULL,
                    confirmed_at TEXT NULL,
                    PRIMARY KEY (project_id, requirement_id)
                );
                """;

            schemaCommand.ExecuteNonQuery();

        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:ReqvioraDatabase", "Host=localhost;Database=unused");
            builder.UseSetting("Jwt:Key", "kobiga-contract-test-key-with-at-least-32-characters");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ReqvioraDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ReqvioraDbContext>>();
                services.RemoveAll<ReqvioraDbContext>();
                services.RemoveAll<IRequirementReviewStore>();
                services.AddDbContext<ReqvioraDbContext>(options => options.UseSqlite(_sqlite));
                services.AddSingleton<IRequirementReviewStore>(Store);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _sqlite.Dispose();
        }

        private static User UserWithEmail(string email) => new()
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "test-only", DisplayName = email
        };
    }

    private sealed class ReviewStore : IRequirementReviewStore
    {
        private Guid _project;
        public ReviewedRequirement Current { get; private set; } = null!;
        public void Seed(Guid project)
        {
            _project = project;
            Current = new ReviewedRequirement("REQ-001", "User can login", "Email and password",
                RequirementReviewStatus.Suggested, 1,
                [new CandidateEvidence("SRC-001", "page 1", "User can login")], null, null);
        }
        public void Confirm(Guid actor) => Current = Current with
        {
            Status = RequirementReviewStatus.Confirmed, Revision = 2,
            ConfirmedBy = actor, ConfirmedAt = DateTimeOffset.UtcNow
        };
        public Task<RequirementReviewSnapshot?> FindAsync(
            Guid projectId, string requirementId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RequirementReviewSnapshot?>(
                projectId == _project && Current.Id == requirementId
                    ? new RequirementReviewSnapshot(projectId, Current) : null);
        public Task<bool> TryCommitAsync(
            Guid projectId, int expectedRevision, RequirementReviewResult result,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}