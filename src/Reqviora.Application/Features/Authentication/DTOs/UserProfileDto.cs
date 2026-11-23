namespace Reqviora.Application.Features.Authentication.DTOs;

public sealed record UserProfileDto(Guid UserId, string Email, string DisplayName, bool EmailVerified);
