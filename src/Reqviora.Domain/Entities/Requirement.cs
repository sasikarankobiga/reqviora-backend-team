using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class Requirement : AuditableEntity
{
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
