using System.ComponentModel.DataAnnotations;

namespace Reqviora.Application.Features.Projects.DTOs;

public sealed record UpdateProjectDto(
    [Required, StringLength(200, MinimumLength = 2)] string Name,
    [StringLength(4000)] string Description,
    [StringLength(120)] string Industry = "",
    [StringLength(500)] string TargetAudience = "",
    [StringLength(120)] string ProjectType = "",
    [StringLength(50)] string PrimaryPlatform = "web",
    [StringLength(2000)] string TechnologyPreferences = "",
    [StringLength(4000)] string Constraints = "",
    [StringLength(4000)] string Integrations = "");
