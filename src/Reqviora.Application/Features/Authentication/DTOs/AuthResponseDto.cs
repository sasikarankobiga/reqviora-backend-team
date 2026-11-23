namespace Reqviora.Application.Features.Authentication.DTOs;

public sealed record AuthResponseDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    string TokenType = "Bearer");
