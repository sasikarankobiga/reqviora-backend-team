using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class User : AggregateRoot
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public Guid? RoleId { get; set; }
    public bool MfaEnabled { get; set; }
    public string? MfaSecretProtected { get; set; }
    public string? RecoveryCodeHashes { get; set; }
}
