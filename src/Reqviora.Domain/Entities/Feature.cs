using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class Feature : AuditableEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
}
