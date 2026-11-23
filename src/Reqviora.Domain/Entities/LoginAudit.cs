using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class LoginAudit : BaseEntity
{
    public Guid? UserId { get; set; }
    public string NormalizedEmail { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public bool Succeeded { get; set; }
    public bool OtpRequired { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
