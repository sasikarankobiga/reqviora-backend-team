using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Authentication;
using Xunit;

namespace Reqviora.Application.Tests.Authentication;

public sealed class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private const string SigningKey = "test-signing-key-with-at-least-32-characters";

    [Fact]
    public void CreateAccessToken_creates_valid_signed_token_with_user_claims()
    {
        var service = CreateService();
        var user = new User
        {
            Email = "person@example.com",
            NormalizedEmail = "PERSON@EXAMPLE.COM",
            DisplayName = "Test Person",
            PasswordHash = "not-used"
        };

        var result = service.CreateAccessToken(user);
        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = tokenHandler.ValidateToken(
            result.Token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "Reqviora.Tests",
                ValidateAudience = true,
                ValidAudience = "Reqviora.Tests.Client",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                ValidateLifetime = false,
                NameClaimType = ClaimTypes.Name
            },
            out _);

        Assert.Equal(user.Id.ToString(), principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(user.Email, principal.FindFirstValue(JwtRegisteredClaimNames.Email));
        Assert.Equal(user.DisplayName, principal.Identity!.Name);
        Assert.Equal(Now.AddMinutes(15), result.ExpiresAt);
    }

    [Fact]
    public void CreateRefreshToken_returns_unique_random_values_and_only_stable_hashes()
    {
        var service = CreateService();

        var first = service.CreateRefreshToken();
        var second = service.CreateRefreshToken();

        Assert.NotEqual(first.Token, second.Token);
        Assert.NotEqual(first.TokenHash, second.TokenHash);
        Assert.NotEqual(first.Token, first.TokenHash);
        Assert.Equal(first.TokenHash, service.HashRefreshToken(first.Token));
        Assert.Equal(64, first.TokenHash.Length);
    }

    private static JwtTokenService CreateService() => new(
        Options.Create(new JwtOptions
        {
            Issuer = "Reqviora.Tests",
            Audience = "Reqviora.Tests.Client",
            Key = SigningKey,
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        }),
        new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
