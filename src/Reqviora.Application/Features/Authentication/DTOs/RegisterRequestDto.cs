
using System.ComponentModel.DataAnnotations;

namespace Reqviora.Application.Features.Authentication.DTOs;

public sealed record RegisterRequestDto(
    [property: Required, EmailAddress, StringLength(320)] string Email,
    [property: Required, StringLength(128, MinimumLength = 8)] string Password,
    [property: Required, StringLength(200, MinimumLength = 2)] string DisplayName);
