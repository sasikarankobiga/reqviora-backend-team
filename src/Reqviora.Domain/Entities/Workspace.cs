
using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class Workspace : AggregateRoot
{
    public string Name { get; set; } = string.Empty;

    public Guid OwnerUserId { get; set; }
}
