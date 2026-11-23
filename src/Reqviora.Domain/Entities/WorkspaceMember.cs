using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class WorkspaceMember : BaseEntity
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "Member";
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
