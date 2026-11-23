using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/screens")]
public sealed class ScreensController(ReqvioraDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ScreenResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ScreenResponse>>> GetAll(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var blueprint = await GetCurrentBlueprintAsync(projectId, cancellationToken);
        return Ok(blueprint?.Screens.Select(ToResponse).ToArray() ?? []);
    }

    [HttpGet("{screenId}")]
    [ProducesResponseType<ScreenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScreenResponse>> GetById(
        Guid projectId,
        string screenId,
        CancellationToken cancellationToken)
    {
        var blueprint = await GetCurrentBlueprintAsync(projectId, cancellationToken);
        var screen = blueprint?.Screens.SingleOrDefault(item => item.Id == screenId);
        return screen is null ? NotFound() : Ok(ToResponse(screen));
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

    private static ScreenResponse ToResponse(
        Application.Features.UIGeneration.Blueprint.Models.BlueprintScreen screen) =>
        new(screen.Id, screen.Name, screen.FeatureIds, screen.ComponentIds);
}
