using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class Component : AuditableEntity
{
    public Guid BlueprintId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string PropertiesJson { get; set; } = "{}";
}
