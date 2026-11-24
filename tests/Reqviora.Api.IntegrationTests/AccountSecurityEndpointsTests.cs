using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Authentication;
using Reqviora.Infrastructure.Persistence;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class AccountSecurityEndpointsTests
{
    [Fact]
    public async Task Verified_token_can_reach_protected_catalog()
    {
        using var factory = new AccountApiFactory(initiallyVerified: true);

        using var client = factory.AuthenticatedClient();

        using var response = await client.GetAsync(
            "/api/mcp/v1/catalog");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unverified_token_cannot_access_protected_catalog()
    {
        using var factory = new AccountApiFactory();
        using var client = factory.AuthenticatedClient();

        using var catalog = await client.GetAsync("/api/mcp/v1/catalog");

        Assert.Equal(HttpStatusCode.Forbidden, catalog.StatusCode);

        using var verification = await client.PostAsync(
            "/api/account-security/email-verification/request",
            null);

        Assert.Equal(HttpStatusCode.Accepted, verification.StatusCode);
    }

    [Fact]
    public async Task Email_verification_token_is_single_use()
    {
        using var factory = new AccountApiFactory();
        using var client = factory.AuthenticatedClient();

        using var requested = await client.PostAsync("/api/account-security/email-verification/request", null);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var token = factory.Email.LastToken();

        using var confirmed = await client.PostAsJsonAsync(
            "/api/account-security/email-verification/confirm",
            new { token });
        Assert.True(
            confirmed.StatusCode == HttpStatusCode.NoContent,
            await confirmed.Content.ReadAsStringAsync());

        using var replayed = await client.PostAsJsonAsync(
            "/api/account-security/email-verification/confirm",
            new { token });
        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);
        Assert.True(factory.ReadUser().EmailVerified);
    }

    [Fact]
    public async Task Password_reset_does_not_enumerate_accounts_and_revokes_sessions()
    {
        using var factory = new AccountApiFactory();
        using var client = factory.CreateClient();

        using var unknown = await client.PostAsJsonAsync(
            "/api/account-security/password/forgot",
            new { email = "missing@example.test" });
        using var known = await client.PostAsJsonAsync(
            "/api/account-security/password/forgot",
            new { email = factory.User.Email });
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);

        var token = factory.Email.LastToken();
        using var reset = await client.PostAsJsonAsync(
            "/api/account-security/password/reset",
            new { token, newPassword = "Updated-Password-123!" });
        Assert.True(
            reset.StatusCode == HttpStatusCode.NoContent,
            await reset.Content.ReadAsStringAsync());

        using var replayed = await client.PostAsJsonAsync(
            "/api/account-security/password/reset",
            new { token, newPassword = "Another-Password-123!" });
        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);

        var state = factory.ReadSecurityState();
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(state.User, state.User.PasswordHash, "Updated-Password-123!"));
        Assert.NotNull(state.RefreshToken.RevokedAt);
    }

    [Fact]
    public async Task Email_change_confirms_new_address_and_revokes_sessions()
    {
        using var factory = new AccountApiFactory(initiallyVerified: true);
        using var client = factory.AuthenticatedClient();

        using var requested = await client.PostAsJsonAsync(
            "/api/account-security/email-change/request",
            new { newEmail = "updated@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var token = factory.Email.LastToken();

        using var confirmed = await client.PostAsJsonAsync(
            "/api/account-security/email-change/confirm",
            new { token });
        Assert.True(
            confirmed.StatusCode == HttpStatusCode.NoContent,
            await confirmed.Content.ReadAsStringAsync());

        var state = factory.ReadSecurityState();
        Assert.Equal("updated@example.test", state.User.Email);
        Assert.Equal("UPDATED@EXAMPLE.TEST", state.User.NormalizedEmail);
        Assert.True(state.User.EmailVerified);
        Assert.NotNull(state.RefreshToken.RevokedAt);
    }

    [Fact]
    public async Task Expired_account_token_is_rejected()
    {
        using var factory = new AccountApiFactory();
        using var client = factory.AuthenticatedClient();
        using var requested = await client.PostAsync("/api/account-security/email-verification/request", null);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var token = factory.Email.LastToken();
        factory.ExpireAccountTokens();

        using var confirmed = await client.PostAsJsonAsync(
            "/api/account-security/email-verification/confirm",
            new { token });
        Assert.Equal(HttpStatusCode.BadRequest, confirmed.StatusCode);
    }

    [Fact]
    public async Task Authentication_endpoints_are_rate_limited()
    {
        using var factory = new AccountApiFactory();
        using var client = factory.CreateClient();
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            response?.Dispose();
            response = await client.PostAsJsonAsync(
                "/api/account-security/password/forgot",
                new { email = "missing@example.test" });
        }

        using (response)
            Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
    }

    private sealed class AccountApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public User User { get; } = new()
        {
            Email = "account@example.test",
            NormalizedEmail = "ACCOUNT@EXAMPLE.TEST",
            DisplayName = "Account Test",
            EmailVerified = false
        };

        public CapturingEmailService Email { get; } = new();
        public RefreshToken RefreshToken { get; }

        public AccountApiFactory(bool initiallyVerified = false)
        {
            User.EmailVerified = initiallyVerified;
            User.PasswordHash = new PasswordHasher<User>().HashPassword(User, "Initial-Password-123!");
            RefreshToken = new RefreshToken
            {
                UserId = User.Id,
                TokenHash = new string('a', 64),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
            };

            _connection.Open();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            db.Database.EnsureCreated();
            db.Users.Add(User);
            db.RefreshTokens.Add(RefreshToken);
            db.SaveChanges();
        }

        public HttpClient AuthenticatedClient()
        {
            var client = CreateClient();
            using var scope = Services.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens.CreateAccessToken(User).Token);
            return client;
        }

        public User ReadUser()
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>().Users.AsNoTracking().Single();
        }

        public (User User, RefreshToken RefreshToken) ReadSecurityState()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            return (db.Users.AsNoTracking().Single(), db.RefreshTokens.AsNoTracking().Single());
        }

        public void ExpireAccountTokens()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            foreach (var token in db.AccountActionTokens) token.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            db.SaveChanges();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:ReqvioraDatabase", "Host=localhost;Database=unused");
            builder.UseSetting("Jwt:Key", "account-test-signing-key-at-least-32-characters");
            builder.UseSetting("AccountLinks:BaseUrl", "https://app.example.test");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ReqvioraDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ReqvioraDbContext>>();
                services.RemoveAll<ReqvioraDbContext>();
                services.RemoveAll<IEmailService>();
                services.AddDbContext<ReqvioraDbContext>(options => options.UseSqlite(_connection));
                services.AddSingleton<IEmailService>(Email);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }

    public sealed class CapturingEmailService : IEmailService
    {
        private readonly List<string> _bodies = [];

        public Task SendAsync(
            string to,
            string subject,
            string body,
            CancellationToken cancellationToken = default)
        {
            _bodies.Add(body);
            return Task.CompletedTask;
        }

        public string LastToken()
        {
            var query = new Uri(_bodies[^1]).Query.TrimStart('?');
            var value = query.Split('=', 2)[1];
            return Uri.UnescapeDataString(value);
        }
    }
}
