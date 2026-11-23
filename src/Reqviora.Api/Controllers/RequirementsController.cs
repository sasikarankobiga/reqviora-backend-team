using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/requirements")]
public sealed class RequirementsController(ReqvioraDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RequirementResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RequirementResponse>>> GetAll(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var blueprint = await GetCurrentBlueprintAsync(projectId, cancellationToken);
        return Ok(blueprint?.Requirements.Select(ToResponse).ToArray() ?? []);
    }

    [HttpGet("{requirementId}")]
    [ProducesResponseType<RequirementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RequirementResponse>> GetById(
        Guid projectId,
        string requirementId,
        CancellationToken cancellationToken)
    {
        var blueprint = await GetCurrentBlueprintAsync(projectId, cancellationToken);
        var requirement = blueprint?.Requirements.SingleOrDefault(item => item.Id == requirementId);
        return requirement is null ? NotFound() : Ok(ToResponse(requirement));
    }

    private async Task<Application.Features.UIGeneration.Blueprint.Models.BlueprintDocument?> GetCurrentBlueprintAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireReadAsync(db, projectId, userId, cancellationToken);
        var version = await ProjectApiAccess.FindCurrentVersionAsync(db, projectId, cancellationToken);
        return ProjectApiAccess.ReadBlueprint(version?.BlueprintJson);
    }

    private static RequirementResponse ToResponse(
        Application.Features.UIGeneration.Blueprint.Models.BlueprintRequirement requirement) => new(
        requirement.Id,
        requirement.Text,
        requirement.Origin,
        requirement.Status,
        requirement.ConfirmedBy,
        requirement.ConfirmedAt,
        requirement.Evidence.Select(evidence => new RequirementEvidenceResponse(
            evidence.SourceId, evidence.Locator, evidence.Quote)).ToArray());
}
