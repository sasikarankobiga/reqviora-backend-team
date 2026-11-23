
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reqviora.Api.Controllers;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class ReviewBlueprintSyncEndpointsTests : IDisposable
{
    private readonly TestApiFactory _factory = new();

    private string SyncUrl =>
        $"/api/projects/{_factory.Project.Id}/workflow-intelligence/blueprints/review-sync-status";

    private string GenerateUrl =>
        $"/api/projects/{_factory.Project.Id}/workflow-intelligence/blueprints/generate";

    // TEST 1: Authorization
    [Fact]
    public async Task Anonymous_and_non_owner_are_blocked()
    {
        using var anonymous = _factory.CreateClient();
        using var response = await anonymous.GetAsync(SyncUrl);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        using var other = CreateAuthorizedClient(_factory.Other);
        using var forbidden = await other.GetAsync(SyncUrl);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            forbidden.StatusCode);
    }

    // TEST 2: Blueprint not found
    [Fact]
    public async Task Missing_blueprint_returns_not_found()
    {
        using var client = CreateAuthorizedClient(_factory.Owner);
        using var response = await client.GetAsync(SyncUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // TEST 3: Confirmed requirement is synchronized
    [Fact]
    public async Task Confirmed_requirement_is_synchronized()
    {
        using var client = CreateAuthorizedClient(_factory.Owner);

        await GenerateBlueprintAsync(client);

        using var response = await client.GetAsync(SyncUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<
            ReviewBlueprintSyncController.ReviewSyncResponse>();

        Assert.NotNull(result);
        Assert.True(result.ExistingRequirementsSynchronized);
        Assert.Equal(1, result.BlueprintRevision);

        var item = Assert.Single(result.Items);

        Assert.Equal("REQ-001", item.RequirementId);
        Assert.Equal("confirmed", item.ReviewStatus);
        Assert.Equal("confirmed", item.BlueprintStatus);
        Assert.True(item.IncludeInGeneration);
        Assert.False(item.RequiresRegeneration);
    }

    // TESTS 4, 5, 6: Edit / Reject / Defer
    [Theory]
    [InlineData(RequirementReviewStatus.Suggested)]
    [InlineData(RequirementReviewStatus.Rejected)]
    [InlineData(RequirementReviewStatus.Deferred)]
    public async Task Changed_review_status_marks_blueprint_stale(
        RequirementReviewStatus status)
    {
        using var client = CreateAuthorizedClient(_factory.Owner);

        await GenerateBlueprintAsync(client);

        _factory.Store.ChangeStatus(status);

        using var response = await client.GetAsync(SyncUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<
            ReviewBlueprintSyncController.ReviewSyncResponse>();

        Assert.NotNull(result);
        Assert.False(result.ExistingRequirementsSynchronized);

        var item = Assert.Single(result.Items);

        Assert.Equal(
            status.ToString().ToLowerInvariant(),
            item.ReviewStatus);

        Assert.False(item.IncludeInGeneration);
        Assert.True(item.RequiresRegeneration);
    }

    // TEST 7: Missing review record
    [Fact]
    public async Task Missing_review_record_marks_blueprint_stale()
    {
        using var client = CreateAuthorizedClient(_factory.Owner);

        await GenerateBlueprintAsync(client);

        _factory.Store.RecordExists = false;

        using var response = await client.GetAsync(SyncUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<
            ReviewBlueprintSyncController.ReviewSyncResponse>();

        Assert.NotNull(result);
        Assert.False(result.ExistingRequirementsSynchronized);

        var item = Assert.Single(result.Items);

        Assert.True(item.RequiresRegeneration);
        Assert.Equal(
            "REVIEW_RECORD_NOT_FOUND",
            item.Problem);
    }

    // TEST 8: Newly confirmed requirement
    [Fact]
    public async Task Newly_confirmed_requirement_is_detected_as_missing()
    {
        using var client = CreateAuthorizedClient(_factory.Owner);

        await GenerateBlueprintAsync(client);

        _factory.Store.AddConfirmedRequirement();

        using var response = await client.GetAsync(SyncUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<
            ReviewBlueprintSyncController.ReviewSyncResponse>();

        Assert.NotNull(result);
        Assert.False(result.ExistingRequirementsSynchronized);

        var missing = Assert.Single(
            result.Items,
            item => item.RequirementId == "REQ-002");

        Assert.Equal("confirmed", missing.ReviewStatus);
        Assert.Null(missing.BlueprintStatus);
        Assert.True(missing.IncludeInGeneration);
        Assert.True(missing.RequiresRegeneration);

        Assert.Equal(
            "CONFIRMED_REQUIREMENT_MISSING_FROM_BLUEPRINT",
            missing.Problem);
    }

    // TEST 9: Synchronized Blueprint allows approval
    [Fact]
    public async Task Synchronized_blueprint_allows_approval()
    {
        using var client = CreateAuthorizedClient(_factory.Owner);

        await GenerateBlueprintAsync(client);

        var (token, validationId) =
            await ValidateBlueprintAsync(client);

        using var response = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/approvals",
            new
            {
                expectedConcurrencyToken = token,
                validationRunId = validationId
            });

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());
    }

    // TEST 10: Newly confirmed requirement blocks approval
    [Fact]
    public async Task Newly_confirmed_requirement_blocks_approval()
    {
        using var client = CreateAuthorizedClient(_factory.Owner);

        await GenerateBlueprintAsync(client);

        var (token, validationId) =
            await ValidateBlueprintAsync(client);

        _factory.Store.AddConfirmedRequirement();

        using var response = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/approvals",
            new
            {
                expectedConcurrencyToken = token,
                validationRunId = validationId
            });

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

        using var history = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/approvals");

        Assert.Equal(HttpStatusCode.OK, history.StatusCode);

        var approvals = await history.Content
            .ReadFromJsonAsync<ApprovalResponse[]>();

        Assert.Empty(approvals ?? []);
    }

    // TESTS 11, 12, 13: Changed review status blocks approval
    [Theory]
    [InlineData(RequirementReviewStatus.Suggested)]
    [InlineData(RequirementReviewStatus.Rejected)]
    [InlineData(RequirementReviewStatus.Deferred)]
    public async Task Changed_review_status_blocks_approval(
        RequirementReviewStatus status)
    {
        using var client = CreateAuthorizedClient(_factory.Owner);

        await GenerateBlueprintAsync(client);

        var (token, validationId) =
            await ValidateBlueprintAsync(client);

        _factory.Store.ChangeStatus(status);

        using var response = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/approvals",
            new
            {
                expectedConcurrencyToken = token,
                validationRunId = validationId
            });

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

    }
    // TEST 14: Missing all review records must block approval.
    [Fact]
    public async Task Review_managed_blueprint_with_no_reviews_blocks_approval()
    {
        using var client = CreateAuthorizedClient(_factory.Owner);

        // Step 1: Generate a Blueprint from confirmed reviews.
        await GenerateBlueprintAsync(client);

        // Step 2: Validate the generated Blueprint.
        var (token, validationId) =
            await ValidateBlueprintAsync(client);

        // Step 3: Simulate missing review records.
        _factory.Store.RecordExists = false;

        // Step 4: Attempt to approve the Blueprint.
        using var response = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/approvals",
            new
            {
                expectedConcurrencyToken = token,
                validationRunId = validationId
            });

        // Step 5: Approval must be blocked.
        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

        // Step 6: No approval record must be created.
        using var history = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/approvals");

        Assert.Equal(HttpStatusCode.OK, history.StatusCode);

        var approvals = await history.Content
            .ReadFromJsonAsync<ApprovalResponse[]>();

        Assert.Empty(approvals ?? []);
    }



    // Helper: Validate current Blueprint
    private async Task<(Guid Token, Guid ValidationId)>
        ValidateBlueprintAsync(HttpClient client)
    {
        using var draftResponse = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current");

        Assert.Equal(
            HttpStatusCode.OK,
            draftResponse.StatusCode);

        var draft = await draftResponse.Content
            .ReadFromJsonAsync<ProjectDraftResponse>();

        Assert.NotNull(draft);

        using var validationResponse =
            await client.PostAsJsonAsync(
                $"/api/projects/{_factory.Project.Id}/blueprints/validate",
                new
                {
                    expectedConcurrencyToken =
                        draft.ConcurrencyToken
                });

        Assert.True(
            validationResponse.StatusCode == HttpStatusCode.OK,
            await validationResponse.Content.ReadAsStringAsync());

        var validation = await validationResponse.Content
            .ReadFromJsonAsync<ValidationRunResponse>();

        Assert.NotNull(validation);
        Assert.True(validation.Passed);

        return (draft.ConcurrencyToken, validation.Id);
    }

    // Helper: Generate a valid Blueprint
    private async Task GenerateBlueprintAsync(HttpClient client)
    {
        _factory.Store.Confirm(_factory.Owner.Id);

        var request = new
        {
            expectedConcurrencyToken = (Guid?)null,
            blueprintId = "BP-SYNC-001",
            confirmedRequirementIds = new[] { "REQ-001" },

            features = new[]
            {
                new
                {
                    id = "FEAT-001",
                    name = "Authentication",
                    requirementIds = new[] { "REQ-001" }
                }
            },

            screens = new[]
            {
                new
                {
                    id = "SCR-001",
                    name = "Login",
                    featureIds = new[] { "FEAT-001" }
                }
            },

            components = new[]
            {
                new
                {
                    id = "CMP-001",
                    screenId = "SCR-001",
                    type = "Button",
                    variant = "default"
                }
            },

            actions = Array.Empty<object>(),
            navigation = Array.Empty<object>()
        };

        using var response =
            await client.PostAsJsonAsync(GenerateUrl, request);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
    }

    // Helper: Authorized HTTP client
    private HttpClient CreateAuthorizedClient(User user)
    {
        var client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();

        var tokens = scope.ServiceProvider
            .GetRequiredService<ITokenService>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                tokens.CreateAccessToken(user).Token);

        return client;
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    // In-memory API test factory
    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _sqlite =
            new("Data Source=:memory:");

        public User Owner { get; } =
            CreateUser("sync-owner@example.test");

        public User Other { get; } =
            CreateUser("sync-other@example.test");

        public Project Project { get; }

        public FakeReviewStore Store { get; } = new();

        public TestApiFactory()
        {
            Project = new Project
            {
                Name = "Blueprint Sync Test",
                OwnerId = Owner.Id,
                PrimaryPlatform = "web"
            };

            Store.Seed(Project.Id);

            _sqlite.Open();

            using var scope = Services.CreateScope();

            var db = scope.ServiceProvider
                .GetRequiredService<ReqvioraDbContext>();

            db.Database.EnsureCreated();

            db.Users.AddRange(Owner, Other);
            db.Projects.Add(Project);

            db.SaveChanges();
        }

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseSetting(
                "ConnectionStrings:ReqvioraDatabase",
                "Host=localhost;Database=unused");

            builder.UseSetting(
                "Jwt:Key",
                "kobiga-sync-test-key-with-at-least-32-characters");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<
                    DbContextOptions<ReqvioraDbContext>>();

                services.RemoveAll<
                    IDbContextOptionsConfiguration<ReqvioraDbContext>>();

                services.RemoveAll<ReqvioraDbContext>();
                services.RemoveAll<IRequirementReviewStore>();
                services.RemoveAll<IRequirementReviewCatalog>();

                services.AddDbContext<ReqvioraDbContext>(
                    options => options.UseSqlite(_sqlite));

                services.AddSingleton<IRequirementReviewStore>(Store);
                services.AddSingleton<IRequirementReviewCatalog>(Store);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _sqlite.Dispose();
            }
        }

        private static User CreateUser(string email) => new()
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "test-only",
            DisplayName = email
        };
    }

    // Fake Review Database
    private sealed class FakeReviewStore :
        IRequirementReviewStore,
        IRequirementReviewCatalog
    {
        private Guid _projectId;

        public bool RecordExists { get; set; } = true;

        public ReviewedRequirement Current { get; private set; } = null!;

        public ReviewedRequirement? AdditionalConfirmed
        {
            get;
            private set;
        }

        public void Seed(Guid projectId)
        {
            _projectId = projectId;

            Current = new ReviewedRequirement(
                "REQ-001",
                "User Login",
                "Email and password",
                RequirementReviewStatus.Suggested,
                1,
                [
                    new CandidateEvidence(
                        "DOC-001",
                        "page 1",
                        "User Login")
                ],
                null,
                null);
        }

        public void Confirm(Guid reviewerId)
        {
            Current = Current with
            {
                Status = RequirementReviewStatus.Confirmed,
                Revision = 2,
                ConfirmedBy = reviewerId,
                ConfirmedAt = DateTimeOffset.UtcNow
            };
        }

        public void ChangeStatus(RequirementReviewStatus status)
        {
            Current = Current with
            {
                Status = status,
                Revision = 3,
                ConfirmedBy = null,
                ConfirmedAt = null
            };
        }

        public void AddConfirmedRequirement()
        {
            if (Current.Status != RequirementReviewStatus.Confirmed)
            {
                throw new InvalidOperationException(
                    "The initial requirement must be confirmed.");
            }

            AdditionalConfirmed = Current with
            {
                Id = "REQ-002",
                Title = "User Profile",
                Description = "Users can manage their profile"
            };
        }

        public Task<RequirementReviewSnapshot?> FindAsync(
            Guid projectId,
            string requirementId,
            CancellationToken cancellationToken = default)
        {
            if (!RecordExists || projectId != _projectId)
            {
                return Task.FromResult<RequirementReviewSnapshot?>(
                    null);
            }

            ReviewedRequirement? matching = null;

            if (requirementId == Current.Id)
            {
                matching = Current;
            }
            else if (AdditionalConfirmed is not null &&
                     requirementId == AdditionalConfirmed.Id)
            {
                matching = AdditionalConfirmed;
            }

            RequirementReviewSnapshot? snapshot =
                matching is null
                    ? null
                    : new RequirementReviewSnapshot(
                        projectId, matching);

            return Task.FromResult(snapshot);
        }

        public Task<IReadOnlyList<ReviewedRequirement>>
            ListByProjectAsync(
                Guid projectId,
                CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ReviewedRequirement> results;

            if (!RecordExists || projectId != _projectId)
            {
                results = [];
            }
            else if (AdditionalConfirmed is null)
            {
                results = [Current];
            }
            else
            {
                results = [Current, AdditionalConfirmed];
            }

            return Task.FromResult(results);
        }

        public Task<bool> TryCommitAsync(
            Guid projectId,
            int expectedRevision,
            RequirementReviewResult result,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }
}
