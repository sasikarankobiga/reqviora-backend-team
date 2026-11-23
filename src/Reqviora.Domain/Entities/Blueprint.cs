using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class Blueprint : AggregateRoot
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = "{}";
}
