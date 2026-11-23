using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Net.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.Authentication.DTOs;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController]
[Route("api/account-security")]
[EnableRateLimiting("authentication")]
public sealed class AccountSecurityController(
    ReqvioraDbContext db,
    IEmailService email,
    IPasswordHasher<User> passwordHasher,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<AccountSecurityController> logger) : ControllerBase
{
    [Authorize]
    [HttpPost("email-verification/request")]
    public async Task<IActionResult> RequestEmailVerification(CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        if (user.EmailVerified) return NoContent();

        var token = await IssueTokenAsync(user, AccountActionPurpose.EmailVerification, null, cancellationToken);
        if (!await TrySendAsync(
                user.Email,
                "Verify your REQVIORA email",
                BuildLink("verify-email", token),
                cancellationToken))
        {
            return Problem(
                title: "Email delivery is temporarily unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Accepted();
    }

    [AllowAnonymous]
    [HttpPost("email-verification/confirm")]
    public async Task<IActionResult> ConfirmEmail(
        ConfirmAccountActionDto request,
        CancellationToken cancellationToken)
    {
        var action = await FindActiveTokenAsync(
            request.Token,
            AccountActionPurpose.EmailVerification,
            cancellationToken);
        if (action is null)
            return Problem(title: "The verification token is invalid or expired.", statusCode: StatusCodes.Status400BadRequest);

        action.User.EmailVerified = true;
        action.ConsumedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("password/forgot")]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is not null)
        {
            var token = await IssueTokenAsync(user, AccountActionPurpose.PasswordReset, null, cancellationToken);
            await TrySendAsync(
                user.Email,
                "Reset your REQVIORA password",
                BuildLink("reset-password", token),
                cancellationToken);
        }

        // The response is deliberately identical for known and unknown addresses.
        return Accepted();
    }

    [AllowAnonymous]
    [HttpPost("password/reset")]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        var action = await FindActiveTokenAsync(request.Token, AccountActionPurpose.PasswordReset, cancellationToken);
        if (action is null)
            return Problem(title: "The password-reset token is invalid or expired.", statusCode: StatusCodes.Status400BadRequest);

        action.User.PasswordHash = passwordHasher.HashPassword(action.User, request.NewPassword);
        action.ConsumedAt = clock.GetUtcNow();
        await RevokeSessionsAsync(action.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPost("email-change/request")]
    public async Task<IActionResult> RequestEmailChange(
        ChangeEmailRequestDto request,
        CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();

        var emailAddress = request.NewEmail.Trim();
        var normalizedEmail = NormalizeEmail(emailAddress);
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail && x.Id != user.Id, cancellationToken))
            return Problem(title: "The email address is already registered.", statusCode: StatusCodes.Status409Conflict);

        var token = await IssueTokenAsync(user, AccountActionPurpose.EmailChange, emailAddress, cancellationToken);
        if (!await TrySendAsync(
                emailAddress,
                "Confirm your new REQVIORA email",
                BuildLink("confirm-email-change", token),
                cancellationToken))
        {
            return Problem(
                title: "Email delivery is temporarily unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Accepted();
    }

    [AllowAnonymous]
    [HttpPost("email-change/confirm")]
    public async Task<IActionResult> ConfirmEmailChange(
        ConfirmAccountActionDto request,
        CancellationToken cancellationToken)
    {
        var action = await FindActiveTokenAsync(request.Token, AccountActionPurpose.EmailChange, cancellationToken);
        if (action is null || string.IsNullOrWhiteSpace(action.TargetValue))
            return Problem(title: "The email-change token is invalid or expired.", statusCode: StatusCodes.Status400BadRequest);

        var normalizedEmail = NormalizeEmail(action.TargetValue);
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail && x.Id != action.UserId, cancellationToken))
            return Problem(title: "The email address is already registered.", statusCode: StatusCodes.Status409Conflict);

        action.User.Email = action.TargetValue;
        action.User.NormalizedEmail = normalizedEmail;
        action.User.EmailVerified = true;
        action.ConsumedAt = clock.GetUtcNow();
        await RevokeSessionsAsync(action.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<User?> CurrentUserAsync(CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId)
            ? await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            : null;
    }

    private async Task<string> IssueTokenAsync(
        User user,
        AccountActionPurpose purpose,
        string? targetValue,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var existing = await db.AccountActionTokens
            .Where(x => x.UserId == user.Id && x.Purpose == purpose && x.ConsumedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var item in existing) item.ConsumedAt = now;

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        db.AccountActionTokens.Add(new AccountActionToken
        {
            UserId = user.Id,
            Purpose = purpose,
            TokenHash = HashToken(rawToken),
            TargetValue = targetValue,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30)
        });
        await db.SaveChangesAsync(cancellationToken);
        return rawToken;
    }

    private async Task<AccountActionToken?> FindActiveTokenAsync(
        string rawToken,
        AccountActionPurpose purpose,
        CancellationToken cancellationToken)
    {
        var hash = HashToken(rawToken);
        var action = await db.AccountActionTokens
            .Include(x => x.User)
            .SingleOrDefaultAsync(
                x => x.TokenHash == hash && x.Purpose == purpose,
                cancellationToken);
        return action is not null && action.IsActive(clock.GetUtcNow()) ? action : null;
    }

    private async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var sessions = await db.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions.Where(x => x.ExpiresAt > now)) session.RevokedAt = now;
    }

    private async Task<bool> TrySendAsync(
        string recipient,
        string subject,
        string link,
        CancellationToken cancellationToken)
    {
        try
        {
            await email.SendAsync(recipient, subject, link, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or SmtpException)
        {
            logger.LogError(exception, "Account security email delivery failed for action {Action}.", subject);
            return false;
        }
    }

    private string BuildLink(string route, string token)
    {
        var baseUrl = configuration["AccountLinks:BaseUrl"] ?? "https://localhost";
        return $"{baseUrl.TrimEnd('/')}/{route}?token={Uri.EscapeDataString(token)}";
    }

    private static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
}
