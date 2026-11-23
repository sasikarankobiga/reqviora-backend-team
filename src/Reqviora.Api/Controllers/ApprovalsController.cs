using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/approvals")]
public sealed class ApprovalsController(ReqvioraDbContext db, ProjectDraftService drafts) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ApprovalResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApprovalResponse>> Approve(
        Guid projectId,
        ApproveDraftRequest request,
        CancellationToken cancellationToken)
    {
        var revision = await drafts.ApproveAsync(
            projectId,
            UserId,
            request.ExpectedConcurrencyToken,
            request.ValidationRunId,
            cancellationToken);
        var version = await db.DesignDraftVersions.AsNoTracking().SingleAsync(
            item => item.Id == revision.DraftVersionId,
            cancellationToken);
        var response = ProjectApiAccess.ToApprovalResponse(revision, version.Version);
        return CreatedAtAction(nameof(GetById), new { projectId, revisionId = revision.Id }, response);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ApprovalResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ApprovalResponse>>> GetAll(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await RequireReadAsync(projectId, cancellationToken);
        var revisions = await db.ApprovedRevisions.AsNoTracking()
            .Where(revision => revision.ProjectId == projectId)
            .Join(
                db.DesignDraftVersions,
                revision => revision.DraftVersionId,
                version => version.Id,
                (revision, version) => new { Revision = revision, version.Version })
            .ToArrayAsync(cancellationToken);
        return Ok(revisions.OrderByDescending(item => item.Version).Select(item =>
            ProjectApiAccess.ToApprovalResponse(item.Revision, item.Version)).ToArray());
    }

    [HttpGet("{revisionId:guid}")]
    [ProducesResponseType<ApprovalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApprovalResponse>> GetById(
        Guid projectId,
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        await RequireReadAsync(projectId, cancellationToken);
        var item = await db.ApprovedRevisions.AsNoTracking()
            .Where(revision => revision.Id == revisionId && revision.ProjectId == projectId)
            .Join(
                db.DesignDraftVersions,
                revision => revision.DraftVersionId,
                version => version.Id,
                (revision, version) => new { Revision = revision, version.Version })
            .SingleOrDefaultAsync(cancellationToken);
        return item is null
            ? NotFound()
            : Ok(ProjectApiAccess.ToApprovalResponse(item.Revision, item.Version));
    }

    private Guid UserId => ProjectApiAccess.RequireUserId(User);

    private Task RequireReadAsync(Guid projectId, CancellationToken cancellationToken) =>
        ProjectApiAccess.RequireReadAsync(db, projectId, UserId, cancellationToken);
}
