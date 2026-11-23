using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class Approval : AuditableEntity
{
    public Guid? ApprovedRevisionId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ReviewerId { get; set; }
    public string Status { get; set; } = "Pending";
}
