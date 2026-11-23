using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class Screen : AuditableEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
}
