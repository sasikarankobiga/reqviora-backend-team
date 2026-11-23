using System.ComponentModel.DataAnnotations;

namespace Reqviora.Application.Features.Authentication.DTOs;

public sealed record RefreshTokenRequestDto(
    [Required, StringLength(512, MinimumLength = 32)] string RefreshToken);
