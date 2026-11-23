using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class Project : AggregateRoot
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string TargetAudience { get; set; } = string.Empty;
    public string ProjectType { get; set; } = string.Empty;
    public string PrimaryPlatform { get; set; } = "web";
    public string TechnologyPreferences { get; set; } = string.Empty;
    public string Constraints { get; set; } = string.Empty;
    public string Integrations { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }

    public Guid? WorkspaceId { get; set; }
}
