using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class Role : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
}
