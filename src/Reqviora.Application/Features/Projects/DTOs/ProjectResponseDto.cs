namespace Reqviora.Application.Features.Projects.DTOs;

public sealed record ProjectResponseDto(
    Guid Id,
    string Name,
    string Description,
    Guid OwnerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt,
    string Industry = "",
    string TargetAudience = "",
    string ProjectType = "",
    string PrimaryPlatform = "web",
    string TechnologyPreferences = "",
    string Constraints = "",
    string Integrations = "");
