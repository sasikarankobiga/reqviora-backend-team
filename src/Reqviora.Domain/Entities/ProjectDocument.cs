using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class ProjectDocument : AuditableEntity
{
    public Guid ProjectId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
}
