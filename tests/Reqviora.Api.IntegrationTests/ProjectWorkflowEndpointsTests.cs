using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.Projects.DTOs;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.UIGeneration.Blueprint.Validation;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Xunit;
using Reqviora.Application.Features.RequirementReview;

namespace Reqviora.Api.IntegrationTests;

public sealed class ProjectWorkflowEndpointsTests : IDisposable
{
    private readonly WorkflowApiFactory _factory = new();

    [Theory]
    [InlineData("/api/projects/{0}/drafts/current")]
    [InlineData("/api/projects/{0}/blueprints/current")]
    [InlineData("/api/projects/{0}/requirements")]
    [InlineData("/api/projects/{0}/features")]
    [InlineData("/api/projects/{0}/screens")]
    [InlineData("/api/projects/{0}/navigation")]
    [InlineData("/api/projects/{0}/approvals")]
    [InlineData("/api/projects/{0}/exports")]
    [InlineData("/api/projects/{0}/overview")]
    public async Task Sensitive_routes_require_authentication(string route)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(string.Format(route, _factory.Project.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/projects/{0}/drafts/current")]
    [InlineData("/api/projects/{0}/blueprints/current")]
    [InlineData("/api/projects/{0}/requirements")]
    [InlineData("/api/projects/{0}/features")]
    [InlineData("/api/projects/{0}/screens")]
    [InlineData("/api/projects/{0}/navigation")]
    [InlineData("/api/projects/{0}/approvals")]
    [InlineData("/api/projects/{0}/exports")]
    [InlineData("/api/projects/{0}/overview")]
    public async Task Other_users_cannot_read_project_content(string route)
    {
        using var client = CreateClient(_factory.OtherUser);
        using var response = await client.GetAsync(string.Format(route, _factory.Project.Id));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Other_users_cannot_write_project_workflows()
    {
        using var client = CreateClient(_factory.OtherUser);
        using var draft = await client.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current",
            new { draft = new { unauthorized = true }, expectedConcurrencyToken = (Guid?)null });
        Assert.Equal(HttpStatusCode.Forbidden, draft.StatusCode);

        using var approval = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/approvals",
            new { expectedConcurrencyToken = Guid.NewGuid(), validationRunId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, approval.StatusCode);

        using var export = await client.PostAsync(
            $"/api/projects/{_factory.Project.Id}/exports/from-revision/{Guid.NewGuid()}",
            null);
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
    }

    [Fact]
    public async Task Owner_can_assign_viewer_and_editor_permissions()
    {
        using var owner = CreateClient(_factory.Owner);
        using var viewer = CreateClient(_factory.OtherUser);

        using var addViewer = await owner.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/members/{_factory.OtherUser.Id}",
            new { role = "viewer" });
        Assert.Equal(HttpStatusCode.OK, addViewer.StatusCode);

        using var project = await viewer.GetAsync($"/api/projects/{_factory.Project.Id}");
        Assert.Equal(HttpStatusCode.OK, project.StatusCode);
        using var overview = await viewer.GetAsync($"/api/projects/{_factory.Project.Id}/overview");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        using var viewerWrite = await viewer.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current",
            new { draft = new { step = "viewer" }, expectedConcurrencyToken = (Guid?)null });
        Assert.Equal(HttpStatusCode.Forbidden, viewerWrite.StatusCode);
        using var viewerMetadataWrite = await viewer.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}",
            new { name = "Blocked", description = "Blocked" });
        Assert.Equal(HttpStatusCode.NotFound, viewerMetadataWrite.StatusCode);

        using var promote = await owner.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/members/{_factory.OtherUser.Id}",
            new { role = "EDITOR" });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        using var editorWrite = await viewer.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current",
            new { draft = new { step = "editor" }, expectedConcurrencyToken = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, editorWrite.StatusCode);

        using var remove = await owner.DeleteAsync(
            $"/api/projects/{_factory.Project.Id}/members/{_factory.OtherUser.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        using var deniedAgain = await viewer.GetAsync($"/api/projects/{_factory.Project.Id}/overview");
        Assert.Equal(HttpStatusCode.Forbidden, deniedAgain.StatusCode);
    }

    [Fact]
    public async Task Draft_body_is_required()
    {
        using var client = CreateClient(_factory.Owner);
        using var response = await client.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current",
            new { expectedConcurrencyToken = (Guid?)null });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pending_ai_and_disabled_mcp_are_not_available()
    {
        using var client = CreateClient(_factory.Owner);
        using var aiJob = await client.PostAsJsonAsync("/api/ai/jobs", new { prompt = "test" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, aiJob.StatusCode);

        using var extraction = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/input-intelligence/requirements/extract",
            new { languageCode = "en", inputText = "test", evidence = Array.Empty<object>(), consentGranted = true });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, extraction.StatusCode);

        using var mcp = await client.GetAsync("/api/mcp/v1/catalog");
        Assert.Equal(HttpStatusCode.NotFound, mcp.StatusCode);
    }

    [Fact]
    public async Task Voice_transcription_requires_explicit_consent_before_provider_availability()
    {
        using var client = CreateClient(_factory.Owner);
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent([0x52, 0x49, 0x46, 0x46]), "audio", "voice.wav" },
            { new StringContent("en"), "languageCode" },
            { new StringContent("5"), "durationSeconds" },
            { new StringContent("false"), "consentGranted" }
        };
        form.First().Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        using var response = await client.PostAsync(
            $"/api/projects/{_factory.Project.Id}/input-intelligence/voice/transcribe", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Text_extraction_requires_explicit_consent_and_project_access()
    {
        using var owner = CreateClient(_factory.Owner);
        using var missingConsent = await owner.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/input-intelligence/requirements/extract",
            new { languageCode = "en", inputText = "Users need sign in", evidence = Array.Empty<object>(), consentGranted = false });
        Assert.Equal(HttpStatusCode.BadRequest, missingConsent.StatusCode);

        using var other = CreateClient(_factory.OtherUser);
        using var foreignProject = await other.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/input-intelligence/requirements/extract",
            new { languageCode = "en", inputText = "Users need sign in", evidence = Array.Empty<object>(), consentGranted = true });
        Assert.Equal(HttpStatusCode.Forbidden, foreignProject.StatusCode);
    }

    [Fact]
    public async Task Flutter_project_contract_round_trips_all_nine_fields()
    {
        using var client = CreateClient(_factory.Owner);
        using var created = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Mobile Banking",
            description = "Flutter client",
            industry = "Finance",
            targetAudience = "Retail customers",
            projectType = "Mobile app",
            primaryPlatform = "MOBILE",
            technologyPreferences = "Flutter, .NET",
            constraints = "WCAG AA",
            integrations = "Open Banking"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var project = await created.Content.ReadFromJsonAsync<ProjectResponseDto>();
        Assert.NotNull(project);
        Assert.Equal("mobile", project.PrimaryPlatform);

        using var updated = await client.PutAsJsonAsync($"/api/projects/{project.Id}", new
        {
            name = "Mobile Banking V2",
            description = "Flutter integration verified",
            industry = "Fintech",
            targetAudience = "SME customers",
            projectType = "Cross-platform app",
            primaryPlatform = "WEB",
            technologyPreferences = "Flutter Web, .NET",
            constraints = "Offline-first",
            integrations = "Payments"
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.Content.ReadFromJsonAsync<ProjectResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(new[]
        {
            "Mobile Banking V2", "Flutter integration verified", "Fintech", "SME customers",
            "Cross-platform app", "web", "Flutter Web, .NET", "Offline-first", "Payments"
        }, new[]
        {
            body.Name, body.Description, body.Industry, body.TargetAudience, body.ProjectType,
            body.PrimaryPlatform, body.TechnologyPreferences, body.Constraints, body.Integrations
        });
    }

    [Fact]
    public async Task Document_upload_is_owner_scoped_validated_and_does_not_expose_storage_path()
    {
        using var owner = CreateClient(_factory.Owner);
        using var validForm = DocumentForm("requirements.txt", "text/plain");
        using var uploaded = await owner.PostAsync(
            $"/api/projects/{_factory.Project.Id}/documents",
            validForm);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var responseBody = await uploaded.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storagePath", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, _factory.Storage.Saves);

        using var mismatchedForm = DocumentForm("requirements.exe", "text/plain");
        using var mismatched = await owner.PostAsync(
            $"/api/projects/{_factory.Project.Id}/documents",
            mismatchedForm);
        Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);
        Assert.Equal(1, _factory.Storage.Saves);

        _factory.Storage.RejectNext = true;
        using var infectedForm = DocumentForm("infected.txt", "text/plain");
        using var infected = await owner.PostAsync(
            $"/api/projects/{_factory.Project.Id}/documents",
            infectedForm);
        Assert.Equal(HttpStatusCode.BadRequest, infected.StatusCode);
        Assert.Equal(1, _factory.Storage.Saves);

        using var other = CreateClient(_factory.OtherUser);
        using var foreignForm = DocumentForm("requirements.txt", "text/plain");
        using var foreign = await other.PostAsync(
            $"/api/projects/{_factory.Project.Id}/documents",
            foreignForm);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(1, _factory.Storage.Saves);
    }

    [Fact]
    public async Task Owner_can_read_workspace_and_project_overview()
    {
        using var client = CreateClient(_factory.Owner);
        using var workspaces = await client.GetAsync("/api/workspaces");
        Assert.True(workspaces.StatusCode == HttpStatusCode.OK, await workspaces.Content.ReadAsStringAsync());
        var workspaceItems = await workspaces.Content.ReadFromJsonAsync<WorkspaceResponse[]>();
        Assert.Single(workspaceItems ?? []);

        using var workspace = await client.GetAsync($"/api/workspaces/{_factory.Workspace.Id}");
        Assert.Equal(HttpStatusCode.OK, workspace.StatusCode);

        using var overview = await client.GetAsync($"/api/projects/{_factory.Project.Id}/overview");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        var item = await overview.Content.ReadFromJsonAsync<ProjectOverviewResponse>();
        Assert.Equal(_factory.Workspace.Id, item?.WorkspaceId);
    }

    [Fact]
    public async Task Draft_save_resume_history_and_stale_token_are_enforced()
    {
        using var client = CreateClient(_factory.Owner);
        var path = $"/api/projects/{_factory.Project.Id}/drafts/current";

        using var first = await client.PutAsJsonAsync(path, new
        {
            draft = new { title = "First" },
            expectedConcurrencyToken = (Guid?)null
        });
        Assert.True(first.StatusCode == HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var saved = await first.Content.ReadFromJsonAsync<ProjectDraftResponse>();
        Assert.NotNull(saved);

        using var resumed = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);

        using var updated = await client.PutAsJsonAsync(path, new
        {
            draft = new { title = "Second" },
            expectedConcurrencyToken = saved.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var stale = await client.PutAsJsonAsync(path, new
        {
            draft = new { title = "Stale" },
            expectedConcurrencyToken = saved.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var history = await client.GetAsync($"/api/projects/{_factory.Project.Id}/drafts/versions");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var versions = await history.Content.ReadFromJsonAsync<BlueprintVersionResponse[]>();
        Assert.Equal(2, versions?.Length);
    }

    [Fact]
    public async Task Blueprint_graph_approval_and_verified_export_are_available_to_owner()
    {
        using var client = CreateClient(_factory.Owner);
        var blueprint = LoadBlueprint(_factory.Project.Id);

        using var save = await client.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/current",
            new { draft = new { secondBrain = new { } }, blueprint, expectedConcurrencyToken = (Guid?)null });
        Assert.True(save.StatusCode == HttpStatusCode.OK, await save.Content.ReadAsStringAsync());
        var draft = await save.Content.ReadFromJsonAsync<ProjectDraftResponse>();
        Assert.NotNull(draft);

        using var currentBlueprint = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/current");
        Assert.Equal(HttpStatusCode.OK, currentBlueprint.StatusCode);
        using var blueprintVersions = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/versions");
        Assert.Equal(HttpStatusCode.OK, blueprintVersions.StatusCode);

        using var features = await client.GetAsync($"/api/projects/{_factory.Project.Id}/features");
        Assert.Equal(HttpStatusCode.OK, features.StatusCode);
        Assert.NotEmpty(await features.Content.ReadFromJsonAsync<FeatureResponse[]>() ?? []);

        using var requirements = await client.GetAsync($"/api/projects/{_factory.Project.Id}/requirements");
        Assert.Equal(HttpStatusCode.OK, requirements.StatusCode);
        Assert.NotEmpty(await requirements.Content.ReadFromJsonAsync<RequirementResponse[]>() ?? []);
        using var requirement = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/requirements/{blueprint.Requirements[0].Id}");
        Assert.Equal(HttpStatusCode.OK, requirement.StatusCode);

        using var feature = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/features/{blueprint.Features[0].Id}");
        Assert.Equal(HttpStatusCode.OK, feature.StatusCode);
        using var screens = await client.GetAsync($"/api/projects/{_factory.Project.Id}/screens");
        Assert.Equal(HttpStatusCode.OK, screens.StatusCode);
        using var screen = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/screens/{blueprint.Screens[0].Id}");
        Assert.Equal(HttpStatusCode.OK, screen.StatusCode);
        using var navigation = await client.GetAsync($"/api/projects/{_factory.Project.Id}/navigation");
        Assert.Equal(HttpStatusCode.OK, navigation.StatusCode);

        using var validationResponse = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/validate",
            new { expectedConcurrencyToken = draft.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        var validation = await validationResponse.Content.ReadFromJsonAsync<ValidationRunResponse>();
        Assert.NotNull(validation);
        Assert.True(validation.Passed);
        using var validationDetails = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/validations/{validation.Id}");
        Assert.Equal(HttpStatusCode.OK, validationDetails.StatusCode);

        using var approvalResponse = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/approvals",
            new { expectedConcurrencyToken = draft.ConcurrencyToken, validationRunId = validation.Id });
        Assert.Equal(HttpStatusCode.Created, approvalResponse.StatusCode);
        var approval = await approvalResponse.Content.ReadFromJsonAsync<ApprovalResponse>();
        Assert.NotNull(approval);
        using var approvals = await client.GetAsync($"/api/projects/{_factory.Project.Id}/approvals");
        Assert.Equal(HttpStatusCode.OK, approvals.StatusCode);
        using var approvalDetails = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/approvals/{approval.RevisionId}");
        Assert.Equal(HttpStatusCode.OK, approvalDetails.StatusCode);
        using var approvedBlueprints = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/approved");
        Assert.Equal(HttpStatusCode.OK, approvedBlueprints.StatusCode);
        using var approvedBlueprint = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/approved/{approval.RevisionId}");
        Assert.Equal(HttpStatusCode.OK, approvedBlueprint.StatusCode);

        using var lockedDraftResponse = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current");
        var lockedDraft = await lockedDraftResponse.Content.ReadFromJsonAsync<ProjectDraftResponse>();
        Assert.NotNull(lockedDraft);
        using var lockedWrite = await client.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current",
            new { draft = new { forbiddenAfterApproval = true }, expectedConcurrencyToken = lockedDraft.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, lockedWrite.StatusCode);

        using var exportResponse = await client.PostAsync(
            $"/api/projects/{_factory.Project.Id}/exports/from-revision/{approval.RevisionId}",
            null);
        Assert.Equal(HttpStatusCode.Created, exportResponse.StatusCode);
        var export = await exportResponse.Content.ReadFromJsonAsync<ExportMetadataResponse>();
        Assert.NotNull(export);
        Assert.Equal(64, export.ArchiveSha256.Length);

        using var exportList = await client.GetAsync($"/api/projects/{_factory.Project.Id}/exports");
        Assert.Equal(HttpStatusCode.OK, exportList.StatusCode);
        using var exportDetails = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/exports/{export.ExportId}");
        Assert.Equal(HttpStatusCode.OK, exportDetails.StatusCode);

        using var download = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/exports/{export.ExportId}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await download.Content.ReadAsByteArrayAsync());

        using var nextDraft = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/start-after-approval",
            new { expectedConcurrencyToken = lockedDraft.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, nextDraft.StatusCode);
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient(User user)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.CreateAccessToken(user).Token);
        return client;
    }

    private static BlueprintDocument LoadBlueprint(Guid projectId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "blueprint.valid.v1.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json["projectId"] = projectId.ToString();
        return BlueprintJsonContract.Parse(json.ToJsonString());
    }

    private static MultipartFormDataContent DocumentForm(string fileName, string mediaType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("REQVIORA requirements"));
        file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(file, "file", fileName);
        return form;
    }

    private sealed class WorkflowApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public User Owner { get; } = NewUser("owner@example.test");
        public User OtherUser { get; } = NewUser("other@example.test");
        public Workspace Workspace { get; }
        public Project Project { get; }
        public TestFileStorage Storage { get; } = new();

        public WorkflowApiFactory()
        {
            Workspace = new Workspace { Name = "Owner workspace", OwnerUserId = Owner.Id };
            Project = new Project
            {
                Name = "Workflow",
                Description = "API tests",
                OwnerId = Owner.Id,
                WorkspaceId = Workspace.Id
            };
            _connection.Open();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            db.Database.EnsureCreated();
            db.Users.AddRange(Owner, OtherUser);
            db.Workspaces.Add(Workspace);
            db.Projects.Add(Project);
            db.SaveChanges();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:ReqvioraDatabase", "Host=localhost;Database=unused");
            builder.UseSetting("Jwt:Key", "workflow-test-signing-key-at-least-32-characters");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ReqvioraDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ReqvioraDbContext>>();
                services.RemoveAll<ReqvioraDbContext>();
                services.RemoveAll<IRequirementReviewCatalog>();
                services.RemoveAll<IFileStorageService>();
                services.AddDbContext<ReqvioraDbContext>(options => options.UseSqlite(_connection));
                services.AddSingleton<IRequirementReviewCatalog>(
    new EmptyRequirementReviewCatalog());
                services.AddSingleton<IFileStorageService>(Storage);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                _connection.Dispose();
        }

        private static User NewUser(string email) => new()
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "not-used-in-tests",
            DisplayName = email,
            EmailVerified = true
        };



private sealed class EmptyRequirementReviewCatalog
    : IRequirementReviewCatalog
 {
            public Task<IReadOnlyList<ReviewedRequirement>>
                ListByProjectAsync(
                    Guid projectId,
                    CancellationToken cancellationToken = default)
            {
                IReadOnlyList<ReviewedRequirement> reviews = [];

                return Task.FromResult(reviews);
            }
        }
public sealed class TestFileStorage : IFileStorageService
        {
            public int Saves { get; private set; }
            public bool RejectNext { get; set; }

            public Task<string> SaveAsync(
                Stream content,
                string fileName,
                CancellationToken cancellationToken = default)
            {
                if (RejectNext)
                {
                    RejectNext = false;
                    throw new InvalidDataException("Simulated malware detection.");
                }

                Saves++;
                return Task.FromResult($"private/{Guid.NewGuid():N}{Path.GetExtension(fileName)}");
            }
        }
    }
}
