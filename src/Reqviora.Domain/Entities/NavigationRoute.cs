using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class NavigationRoute : AuditableEntity
{
    public Guid ProjectId { get; set; }
    public string Path { get; set; } = string.Empty;
}
