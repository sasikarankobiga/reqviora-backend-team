using Reqviora.Domain.Common;
namespace Reqviora.Domain.Entities;

public sealed class ProjectDraft : AggregateRoot
{
    public Guid ProjectId { get; set; }
    public bool IsReviewManaged { get; set; } = false;
    public string DraftJson { get; set; } = "{}";
    public int Version { get; set; } = 1;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public byte[]? RowVersion { get; private set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? ApprovedRevisionId { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
}
