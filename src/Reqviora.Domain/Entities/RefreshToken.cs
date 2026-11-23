using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public User User { get; set; } = null!;

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
