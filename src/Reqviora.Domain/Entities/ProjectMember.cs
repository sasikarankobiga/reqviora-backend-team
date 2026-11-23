
using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class ProjectMember : BaseEntity
{
    public Guid ProjectId { get; set; }

    public Guid UserId { get; set; }

    public string Role { get; set; } = "Member";

    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}
