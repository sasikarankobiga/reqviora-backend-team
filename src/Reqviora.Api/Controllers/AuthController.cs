using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Reqviora.Application.Features.Authentication.DTOs;
using Reqviora.Application.Features.Authentication.Interfaces;

namespace Reqviora.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("authentication")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequestDto request, CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Problem(title: result.Error, statusCode: StatusCodes.Status409Conflict);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequestDto request, CancellationToken cancellationToken)
    {
        request = request with
        {
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            DeviceId = string.IsNullOrWhiteSpace(request.DeviceId)
                ? Request.Headers.UserAgent.ToString()[..Math.Min(Request.Headers.UserAgent.ToString().Length, 200)]
                : request.DeviceId
        };
        var result = await authService.LoginAsync(request, cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : Problem(title: result.Error, statusCode: StatusCodes.Status401Unauthorized);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(RefreshTokenRequestDto request, CancellationToken cancellationToken)
    {
        var result = await authService.RefreshAsync(request, cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : Problem(title: result.Error, statusCode: StatusCodes.Status401Unauthorized);
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshTokenRequestDto request, CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request, cancellationToken);
        return NoContent();
    }
}
