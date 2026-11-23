using Reqviora.Domain.Entities;

namespace Reqviora.Application.Features.Authentication.Interfaces;

public interface ILoginSecurityService
{
    Task<bool> RequiresOtpAsync(User user, string ipAddress, string deviceId, CancellationToken cancellationToken);
    bool ValidateOtp(User user, string code);
    Task RecordAsync(User? user, string normalizedEmail, string ipAddress, string deviceId, bool succeeded, bool otpRequired,
        CancellationToken cancellationToken);
}
