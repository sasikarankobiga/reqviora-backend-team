using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/overview")]
public sealed class ProjectOverviewController(ReqvioraDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ProjectOverviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectOverviewResponse>> Get(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireReadAsync(db, projectId, userId, cancellationToken);
        var project = await db.Projects.AsNoTracking().SingleAsync(
            item => item.Id == projectId,
            cancellationToken);
        var draft = await db.ProjectDrafts.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == projectId,
            cancellationToken);
        var version = draft is null
            ? null
            : await db.DesignDraftVersions.AsNoTracking().SingleAsync(
                item => item.ProjectDraftId == draft.Id && item.Version == draft.Version,
                cancellationToken);
        var blueprint = ProjectApiAccess.ReadBlueprint(version?.BlueprintJson);

        return Ok(new ProjectOverviewResponse(
            project.Id,
            project.Name,
            project.Description,
            project.OwnerId,
            project.WorkspaceId,
            draft?.Version,
            draft?.ConcurrencyToken,
            draft?.ApprovedRevisionId,
            blueprint?.Requirements.Count ?? 0,
            blueprint?.Features.Count ?? 0,
            blueprint?.Screens.Count ?? 0,
            blueprint?.Navigation.Count ?? 0));
    }
}
