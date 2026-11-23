using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/blueprints")]
public sealed class BlueprintsController(ReqvioraDbContext db, ProjectDraftService drafts) : ControllerBase
{
    [HttpGet("current")]
    [ProducesResponseType<BlueprintVersionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BlueprintVersionResponse>> GetCurrent(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await RequireReadAsync(projectId, cancellationToken);
        var version = await ProjectApiAccess.FindCurrentVersionAsync(db, projectId, cancellationToken);
        return version?.BlueprintJson is null ? NotFound() : Ok(ProjectApiAccess.ToResponse(version));
    }

    [HttpGet("versions")]
    [ProducesResponseType<IReadOnlyList<BlueprintVersionResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BlueprintVersionResponse>>> GetVersions(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var versions = await drafts.GetDraftHistoryAsync(projectId, UserId, cancellationToken);
        return Ok(versions.Where(version => version.BlueprintJson is not null)
            .Select(ProjectApiAccess.ToResponse).ToArray());
    }

    [HttpPut("current")]
    [ProducesResponseType<ProjectDraftResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDraftResponse>> Save(
        Guid projectId,
        SaveBlueprintRequest request,
        CancellationToken cancellationToken)
    {
        var draft = await drafts.SaveDesignAsync(
            projectId,
            UserId,
            request.Draft!.Value.GetRawText(),
            request.Blueprint,
            request.ExpectedConcurrencyToken,
            cancellationToken);
        return Ok(ProjectApiAccess.ToResponse(draft));
    }

    [HttpPost("validate")]
    [ProducesResponseType<ValidationRunResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ValidationRunResponse>> Validate(
        Guid projectId,
        DraftTokenRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await drafts.ValidateAsync(
            projectId,
            UserId,
            request.ExpectedConcurrencyToken,
            cancellationToken);
        return Ok(new ValidationRunResponse(
            validation.Id,
            validation.DraftVersionId,
            validation.Passed,
            validation.ValidatedAt,
            validation.Findings.Select(finding => new ValidationFindingResponse(
                finding.Ordinal, finding.Code, finding.Path, finding.Message)).ToArray()));
    }

    [HttpGet("validations/{validationId:guid}")]
    [ProducesResponseType<ValidationRunResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ValidationRunResponse>> GetValidation(
        Guid projectId,
        Guid validationId,
        CancellationToken cancellationToken)
    {
        await RequireReadAsync(projectId, cancellationToken);
        var validation = await db.ValidationRuns.AsNoTracking()
            .Include(run => run.Findings)
            .Join(
                db.DesignDraftVersions,
                run => run.DraftVersionId,
                version => version.Id,
                (run, version) => new { Run = run, version.ProjectId })
            .SingleOrDefaultAsync(item => item.Run.Id == validationId && item.ProjectId == projectId, cancellationToken);
        if (validation is null)
            return NotFound();

        return Ok(new ValidationRunResponse(
            validation.Run.Id,
            validation.Run.DraftVersionId,
            validation.Run.Passed,
            validation.Run.ValidatedAt,
            validation.Run.Findings.OrderBy(finding => finding.Ordinal)
                .Select(finding => new ValidationFindingResponse(
                    finding.Ordinal, finding.Code, finding.Path, finding.Message)).ToArray()));
    }

    [HttpGet("approved")]
    [ProducesResponseType<IReadOnlyList<ApprovedBlueprintResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ApprovedBlueprintResponse>>> GetApproved(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await RequireReadAsync(projectId, cancellationToken);
        var revisions = await db.ApprovedRevisions.AsNoTracking()
            .Where(revision => revision.ProjectId == projectId)
            .ToArrayAsync(cancellationToken);
        return Ok(revisions.OrderByDescending(revision => revision.ApprovedAt)
            .Select(ProjectApiAccess.ToResponse).ToArray());
    }

    [HttpGet("approved/{revisionId:guid}")]
    [ProducesResponseType<ApprovedBlueprintResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApprovedBlueprintResponse>> GetApproved(
        Guid projectId,
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        await RequireReadAsync(projectId, cancellationToken);
        var revision = await db.ApprovedRevisions.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == revisionId && item.ProjectId == projectId,
            cancellationToken);
        return revision is null ? NotFound() : Ok(ProjectApiAccess.ToResponse(revision));
    }

    private Guid UserId => ProjectApiAccess.RequireUserId(User);

    private Task RequireReadAsync(Guid projectId, CancellationToken cancellationToken) =>
        ProjectApiAccess.RequireReadAsync(db, projectId, UserId, cancellationToken);
}
