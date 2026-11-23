using System.ComponentModel.DataAnnotations;

namespace Reqviora.Application.Features.Authentication.DTOs;

public sealed record ConfirmAccountActionDto(
    [Required, StringLength(512, MinimumLength = 32)] string Token);

public sealed record ForgotPasswordRequestDto(
    [Required, EmailAddress, StringLength(320)] string Email);

public sealed record ResetPasswordRequestDto(
    [Required, StringLength(512, MinimumLength = 32)] string Token,
    [Required, StringLength(128, MinimumLength = 8)] string NewPassword);

public sealed record ChangeEmailRequestDto(
    [Required, EmailAddress, StringLength(320)] string NewEmail);
