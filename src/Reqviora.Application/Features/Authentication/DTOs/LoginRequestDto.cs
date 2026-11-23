using System.ComponentModel.DataAnnotations;

namespace Reqviora.Application.Features.Authentication.DTOs;

public sealed record LoginRequestDto(
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, StringLength(128)] string Password,
    [StringLength(12)] string? OtpCode = null,
    [StringLength(200)] string? DeviceId = null,
    string? IpAddress = null);
