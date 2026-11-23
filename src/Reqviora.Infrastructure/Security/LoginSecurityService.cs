using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Reqviora.Application.Features.Authentication.Interfaces;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Security;

public sealed class LoginSecurityService(
    ReqvioraDbContext db,
    IDataProtectionProvider protection,
    IConfiguration configuration,
    TimeProvider clock) : ILoginSecurityService
{
    private readonly IDataProtector _protector = protection.CreateProtector("Reqviora.MfaSecret.v1");

    public async Task<bool> RequiresOtpAsync(User user, string ipAddress, string deviceId, CancellationToken cancellationToken)
    {
        var admin = (configuration.GetSection("Security:AdminEmails").Get<string[]>() ?? [])
            .Contains(user.Email, StringComparer.OrdinalIgnoreCase);
        if (!user.MfaEnabled || string.IsNullOrWhiteSpace(user.MfaSecretProtected)) return admin;
        var since = clock.GetUtcNow().AddMinutes(-30);
        var failureTimes = await db.LoginAudits.AsNoTracking()
            .Where(x => x.UserId == user.Id && !x.Succeeded)
            .Select(x => x.OccurredAt)
            .ToArrayAsync(cancellationToken);
        var failures = failureTimes.Count(occurredAt => occurredAt >= since);
        var known = await db.LoginAudits.AnyAsync(x => x.UserId == user.Id && x.Succeeded &&
            x.IpAddress == ipAddress && x.DeviceId == deviceId, cancellationToken);
        var hasHistory = await db.LoginAudits.AnyAsync(x => x.UserId == user.Id && x.Succeeded, cancellationToken);
        return admin || failures >= 3 || (hasHistory && !known);
    }

    public bool ValidateOtp(User user, string code)
    {
        if (string.IsNullOrWhiteSpace(user.MfaSecretProtected)) return false;
        if (code.Length == 6 && long.TryParse(code, out _))
        {
            var secret = Convert.FromBase64String(_protector.Unprotect(user.MfaSecretProtected));
            var counter = clock.GetUtcNow().ToUnixTimeSeconds() / 30;
            for (var drift = -1; drift <= 1; drift++)
                if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(Totp(secret, counter + drift)),
                    System.Text.Encoding.ASCII.GetBytes(code))) return true;
        }
        var hash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code.Trim())));
        var hashes = (user.RecoveryCodeHashes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        var index = hashes.FindIndex(candidate => CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(candidate), Convert.FromHexString(hash)));
        if (index < 0) return false;

        // Recovery codes are single-use. The caller's normal unit-of-work save persists the removal.
        hashes.RemoveAt(index);
        user.RecoveryCodeHashes = string.Join(',', hashes);
        return true;
    }

    public async Task RecordAsync(User? user, string normalizedEmail, string ipAddress, string deviceId, bool succeeded,
        bool otpRequired, CancellationToken cancellationToken)
    {
        db.LoginAudits.Add(new LoginAudit
        {
            UserId = user?.Id,
            NormalizedEmail = normalizedEmail,
            IpAddress = ipAddress[..Math.Min(ipAddress.Length, 64)],
            DeviceId = deviceId[..Math.Min(deviceId.Length, 200)],
            Succeeded = succeeded,
            OtpRequired = otpRequired,
            OccurredAt = clock.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    internal static string Totp(byte[] secret, long counter)
    {
        Span<byte> input = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(input, counter);
        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(input.ToArray());
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }
}
