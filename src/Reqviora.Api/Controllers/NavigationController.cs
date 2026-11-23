using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/navigation")]
public sealed class NavigationController(ReqvioraDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NavigationResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NavigationResponse>>> GetAll(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireReadAsync(db, projectId, userId, cancellationToken);
        var version = await ProjectApiAccess.FindCurrentVersionAsync(db, projectId, cancellationToken);
        var blueprint = ProjectApiAccess.ReadBlueprint(version?.BlueprintJson);
        return Ok(blueprint?.Navigation.Select(item => new NavigationResponse(
            item.Id, item.SourceScreenId, item.TargetScreenId, item.ActionId)).ToArray() ?? []);
    }
}
