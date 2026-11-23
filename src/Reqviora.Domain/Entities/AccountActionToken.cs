using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public enum AccountActionPurpose
{
    EmailVerification = 1,
    PasswordReset = 2,
    EmailChange = 3
}

public sealed class AccountActionToken : BaseEntity
{
    public Guid UserId { get; set; }
    public AccountActionPurpose Purpose { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string? TargetValue { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public User User { get; set; } = null!;

    public bool IsActive(DateTimeOffset now) => ConsumedAt is null && ExpiresAt > now;
}
