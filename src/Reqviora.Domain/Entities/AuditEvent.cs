using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class AuditEvent : BaseEntity
{
    public Guid? UserId { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int StatusCode { get; set; }
    public string IpAddress { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
