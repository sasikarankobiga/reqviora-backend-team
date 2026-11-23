using Reqviora.Domain.Entities;

namespace Reqviora.Application.Common.Interfaces;

public interface ITokenService
{
    AccessTokenResult CreateAccessToken(User user);
    RefreshTokenResult CreateRefreshToken();
    string HashRefreshToken(string token);
}

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);
public sealed record RefreshTokenResult(string Token, string TokenHash, DateTimeOffset ExpiresAt);
