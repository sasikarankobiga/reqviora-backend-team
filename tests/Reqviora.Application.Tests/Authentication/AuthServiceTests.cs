using Microsoft.AspNetCore.Identity;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.Authentication.DTOs;
using Reqviora.Application.Features.Authentication.Interfaces;
using Reqviora.Application.Features.Authentication.Services;
using Reqviora.Domain.Entities;
using Xunit;

namespace Reqviora.Application.Tests.Authentication;

public sealed class AuthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Register_hashes_password_and_stores_only_refresh_token_hash()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RegisterAsync(
            new RegisterRequestDto(" Person@Example.com ", "StrongPassword123!", "Test Person"));

        Assert.True(result.Succeeded);
        var user = Assert.Single(fixture.Repository.Users);
        Assert.Equal("PERSON@EXAMPLE.COM", user.NormalizedEmail);
        Assert.NotEqual("StrongPassword123!", user.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            fixture.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, "StrongPassword123!"));
        var refreshToken = Assert.Single(fixture.Repository.RefreshTokens);
        Assert.Equal("HASH-refresh-1", refreshToken.TokenHash);
        Assert.Equal("refresh-1", result.Value!.RefreshToken);
        Assert.NotEqual(result.Value.RefreshToken, refreshToken.TokenHash);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_generic_failure_without_session()
    {
        var fixture = new Fixture();
        var user = fixture.AddUser("person@example.com", "CorrectPassword123!");

        var result = await fixture.Service.LoginAsync(
            new LoginRequestDto(user.Email, "WrongPassword123!"));

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid email or password.", result.Error);
        Assert.Empty(fixture.Repository.RefreshTokens);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Unverified_user_cannot_login()
    {
        var fixture = new Fixture();
        var user = fixture.AddUser(
            "person@example.com",
            "CorrectPassword123!");

        user.EmailVerified = false;

        var result = await fixture.Service.LoginAsync(
            new LoginRequestDto(
                user.Email,
                "CorrectPassword123!"));

        Assert.False(result.Succeeded);
        Assert.Empty(fixture.Repository.RefreshTokens);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Unverified_user_cannot_refresh_session()
    {
        var fixture = new Fixture();
        var user = fixture.AddUser(
            "person@example.com",
            "CorrectPassword123!");

        user.EmailVerified = false;

        var token = new RefreshToken
        {
            UserId = user.Id,
            User = user,
            TokenHash = "HASH-old-refresh",
            ExpiresAt = Now.AddDays(1)
        };

        fixture.Repository.RefreshTokens.Add(token);

        var result = await fixture.Service.RefreshAsync(
            new RefreshTokenRequestDto("old-refresh"));

        Assert.False(result.Succeeded);
        Assert.Null(token.RevokedAt);
        Assert.Single(fixture.Repository.RefreshTokens);
    }

    [Fact]
    public async Task Refresh_rotates_token_and_revokes_previous_token()
    {
        var fixture = new Fixture();
        var user = fixture.AddUser("person@example.com", "CorrectPassword123!");
        var previousToken = new RefreshToken
        {
            UserId = user.Id,
            User = user,
            TokenHash = "HASH-old-refresh",
            ExpiresAt = Now.AddDays(1)
        };
        fixture.Repository.RefreshTokens.Add(previousToken);

        var result = await fixture.Service.RefreshAsync(new RefreshTokenRequestDto("old-refresh"));

        Assert.True(result.Succeeded);
        Assert.Equal(Now, previousToken.RevokedAt);
        Assert.Equal("HASH-refresh-1", previousToken.ReplacedByTokenHash);
        Assert.Equal(2, fixture.Repository.RefreshTokens.Count);
        Assert.Equal("refresh-1", result.Value!.RefreshToken);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Logout_revokes_active_token_and_is_idempotent()
    {
        var fixture = new Fixture();
        var user = fixture.AddUser("person@example.com", "CorrectPassword123!");
        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            User = user,
            TokenHash = "HASH-active-refresh",
            ExpiresAt = Now.AddDays(1)
        };
        fixture.Repository.RefreshTokens.Add(refreshToken);

        var first = await fixture.Service.LogoutAsync(new RefreshTokenRequestDto("active-refresh"));
        var second = await fixture.Service.LogoutAsync(new RefreshTokenRequestDto("active-refresh"));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(Now, refreshToken.RevokedAt);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    private sealed class Fixture
    {
        public FakeUserRepository Repository { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public PasswordHasher<User> PasswordHasher { get; } = new();
        public AuthService Service { get; }

        public Fixture()
        {
            Service = new AuthService(
                Repository,
                new FakeTokenService(),
                PasswordHasher,
                UnitOfWork,
                new FixedTimeProvider(Now));
        }

        public User AddUser(string email, string password)
        {
            var user = new User
            {
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                DisplayName = "Test Person",
                EmailVerified = true
            };
            user.PasswordHash = PasswordHasher.HashPassword(user, password);
            Repository.Users.Add(user);
            return user;
        }
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public List<User> Users { get; } = [];
        public List<RefreshToken> RefreshTokens { get; } = [];

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.SingleOrDefault(user => user.NormalizedEmail == email));

        public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Id == userId));

        public Task<RefreshToken?> GetRefreshTokenByHashAsync(
            string tokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RefreshTokens.SingleOrDefault(token => token.TokenHash == tokenHash));

        public Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return Task.CompletedTask;
        }

        public Task AddRefreshTokenAsync(
            RefreshToken refreshToken,
            CancellationToken cancellationToken = default)
        {
            RefreshTokens.Add(refreshToken);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeTokenService : ITokenService
    {
        private int _refreshTokenCount;

        public AccessTokenResult CreateAccessToken(User user) =>
            new("access-token", Now.AddMinutes(15));

        public RefreshTokenResult CreateRefreshToken()
        {
            var token = $"refresh-{++_refreshTokenCount}";
            return new RefreshTokenResult(token, HashRefreshToken(token), Now.AddDays(7));
        }

        public string HashRefreshToken(string token) => $"HASH-{token}";
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
