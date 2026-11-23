using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reqviora.Api.Features.RequirementReview;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Domain.Entities;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class RequirementReviewsTests : IDisposable
{
    private const string RequirementId = "REQ-LOGIN";
    private const string Accept = """{"expectedRevision":1,"decision":"accept"}""";
    private readonly ReviewApiFactory _factory = new();

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task Project_write_validates_request_body_before_review(string method)
    {
        using var client = CreateClient(_factory.Owner);
        var path = method == "POST" ? "/api/projects" : $"/api/projects/{_factory.ProjectId}";
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { name = "", description = "Invalid project name" })
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-access-token")]
    public async Task Submit_requires_a_verified_access_token(string? accessToken)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = accessToken is null ? null : new("Bearer", accessToken);

        using var response = await SubmitAsync(client, Accept);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _factory.Store.Reads);
    }

    [Fact]
    public async Task Submit_denies_other_users_before_loading_the_requirement()
    {
        using var client = CreateClient(new User());

        using var response = await SubmitAsync(client, Accept);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _factory.Store.Reads);
    }

    [Theory]
    [InlineData("""{"expectedRevision":1,"decision":"accept","actorId":"someone-else"}""")]
    [InlineData("""{"expectedRevision":1,"decision":"accept","decision":"reject"}""")]
    [InlineData("""{"expectedRevision":"1","decision":"accept"}""")]
    public async Task Submit_rejects_invalid_json_contracts(string json)
    {
        using var client = CreateClient(_factory.Owner);

        using var response = await SubmitAsync(client, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.Store.Reads);
    }

    [Fact]
    public async Task Submit_confirms_for_the_verified_owner_and_conflicts_on_a_stale_revision()
    {
        using var client = CreateClient(_factory.Owner);

        using var response = await SubmitAsync(client, Accept);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<ReviewApiDecisionReceipt>();
        Assert.Equal(new(RequirementId, "confirmed", 2), receipt);
        Assert.Equal(_factory.Owner.Id, _factory.Store.Current.ConfirmedBy);
        Assert.Equal(_factory.Owner.Id, _factory.Store.LastEvent?.ActorId);

        var savedEvent = _factory.Store.LastEvent;
        using var stale = await SubmitAsync(client, Accept);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Same(savedEvent, _factory.Store.LastEvent);
    }

    [Fact]
    public async Task Submit_returns_not_found_for_a_missing_requirement_in_an_owned_project()
    {
        using var client = CreateClient(_factory.Owner);

        using var response = await SubmitAsync(client, Accept, "REQ-MISSING");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(_factory.Store.LastEvent);
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

    private async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client,
        string json,
        string requirementId = RequirementId)
    {
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync($"/api/projects/{_factory.ProjectId}/requirements/{requirementId}/review", body);
    }

    private sealed class ReviewApiFactory : WebApplicationFactory<Program>
    {
        public Guid ProjectId { get; } = Guid.NewGuid();
        public User Owner { get; } = new();
        public TestReviewStore Store { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:ReqvioraDatabase", "Host=localhost;Database=unused");
            builder.UseSetting("Jwt:Key", "integration-test-signing-key-at-least-32-characters");
            builder.UseSetting("Security:AuditEnabled", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequirementReviewAccessPolicy>();
                services.RemoveAll<IRequirementReviewStore>();
                services.AddSingleton<IRequirementReviewAccessPolicy>(new ProjectAccess(ProjectId, Owner.Id));
                services.AddSingleton<IRequirementReviewStore>(Store);
            });
        }
    }

    private sealed class ProjectAccess(Guid allowedProject, Guid ownerId) : IRequirementReviewAccessPolicy
    {
        public Task<bool> CanReviewAsync(Guid projectId, Guid authenticatedUserId, CancellationToken cancellationToken) =>
            Task.FromResult(projectId == allowedProject && authenticatedUserId == ownerId);
    }

    private sealed class TestReviewStore : IRequirementReviewStore
    {
        public ReviewedRequirement Current { get; private set; } = new(
            RequirementId, "Login", "User can sign in.", RequirementReviewStatus.Suggested, 1,
            [new("SRC-LOGIN", "Input", "User can sign in.")], null, null);
        public RequirementReviewEvent? LastEvent { get; private set; }
        public int Reads { get; private set; }

        public Task<RequirementReviewSnapshot?> FindAsync(
            Guid projectId, string requirementId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<RequirementReviewSnapshot?>(
                requirementId == Current.Id ? new(projectId, Current) : null);
        }

        public Task<bool> TryCommitAsync(
            Guid projectId, int expectedRevision, RequirementReviewResult result, CancellationToken cancellationToken)
        {
            Current = result.Requirement;
            LastEvent = result.Event;
            return Task.FromResult(true);
        }
    }
}
