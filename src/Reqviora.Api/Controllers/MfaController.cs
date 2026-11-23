using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Security;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/security/mfa")]
public sealed class MfaController(ReqvioraDbContext db, IDataProtectionProvider protection) : ControllerBase
{
    public sealed record VerifyMfa(string Code);

    [HttpPost("provision")]
    public async Task<IActionResult> Provision(
        [FromQuery] string? currentCode,
        [FromServices] LoginSecurityService security,
        CancellationToken ct)
    {
        if (!UserId(out var id)) return Unauthorized();
        var user = await db.Users.SingleAsync(x => x.Id == id, ct);
        if (user.MfaEnabled && (string.IsNullOrWhiteSpace(currentCode) || !security.ValidateOtp(user, currentCode)))
            return Unauthorized();
        var secret = RandomNumberGenerator.GetBytes(20);
        var secretText = Convert.ToBase64String(secret);
        user.MfaSecretProtected = protection.CreateProtector("Reqviora.MfaSecret.v1").Protect(secretText);
        user.MfaEnabled = false;
        var recovery = Enumerable.Range(0, 8).Select(_ => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(5))).ToArray();
        user.RecoveryCodeHashes = string.Join(',', recovery.Select(x => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(x)))));
        await db.SaveChangesAsync(ct);
        var uri = $"otpauth://totp/REQVIORA:{Uri.EscapeDataString(user.Email)}?secret={Base32(secret)}&issuer=REQVIORA";
        return Ok(new { secret = Base32(secret), uri, recoveryCodes = recovery });
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify(VerifyMfa request, [FromServices] LoginSecurityService security, CancellationToken ct)
    {
        if (!UserId(out var id)) return Unauthorized();
        var user = await db.Users.SingleAsync(x => x.Id == id, ct);
        if (!security.ValidateOtp(user, request.Code)) return Unauthorized();
        user.MfaEnabled = true;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Disable(VerifyMfa request, [FromServices] LoginSecurityService security, CancellationToken ct)
    {
        if (!UserId(out var id)) return Unauthorized();
        var user = await db.Users.SingleAsync(x => x.Id == id, ct);
        if (!security.ValidateOtp(user, request.Code)) return Unauthorized();
        user.MfaEnabled = false; user.MfaSecretProtected = null; user.RecoveryCodeHashes = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private bool UserId(out Guid id) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out id);

    private static string Base32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new System.Text.StringBuilder(); var buffer = 0; var bits = 0;
        foreach (var value in data) { buffer = (buffer << 8) | value; bits += 8; while (bits >= 5) { output.Append(alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; } }
        if (bits > 0) output.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }
}
