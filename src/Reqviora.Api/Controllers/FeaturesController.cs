using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/features")]
public sealed class FeaturesController(ReqvioraDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FeatureResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FeatureResponse>>> GetAll(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var blueprint = await GetCurrentBlueprintAsync(projectId, cancellationToken);
        return Ok(blueprint?.Features.Select(ToResponse).ToArray() ?? []);
    }

    [HttpGet("{featureId}")]
    [ProducesResponseType<FeatureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FeatureResponse>> GetById(
        Guid projectId,
        string featureId,
        CancellationToken cancellationToken)
    {
        var blueprint = await GetCurrentBlueprintAsync(projectId, cancellationToken);
        var feature = blueprint?.Features.SingleOrDefault(item => item.Id == featureId);
        return feature is null ? NotFound() : Ok(ToResponse(feature));
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

    private static FeatureResponse ToResponse(
        Application.Features.UIGeneration.Blueprint.Models.BlueprintFeature feature) =>
        new(feature.Id, feature.Name, feature.RequirementIds);
}
