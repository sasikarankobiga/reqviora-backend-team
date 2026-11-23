using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/exports")]
public sealed class ExportsController(
    ReqvioraDbContext db,
    ProjectExportService exports) : ControllerBase
{
    [HttpPost("from-revision/{revisionId:guid}")]
    [ProducesResponseType<ExportMetadataResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ExportMetadataResponse>> Create(
        Guid projectId,
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        var export = await exports.GetOrCreateAsync(projectId, revisionId, UserId, cancellationToken);
        var response = ProjectApiAccess.ToResponse(export);
        return CreatedAtAction(nameof(GetById), new { projectId, exportId = export.Id }, response);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ExportMetadataResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ExportMetadataResponse>>> GetAll(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(projectId, cancellationToken);
        var records = await db.ExportRecords.AsNoTracking()
            .Join(
                db.ApprovedRevisions.Where(revision => revision.ProjectId == projectId),
                export => export.ApprovedRevisionId,
                revision => revision.Id,
                (export, _) => export)
            .ToArrayAsync(cancellationToken);
        var verified = new List<ExportMetadataResponse>(records.Length);
        foreach (var record in records.OrderByDescending(export => export.CreatedAt))
        {
            var item = await exports.GetOrCreateAsync(
                projectId,
                record.ApprovedRevisionId,
                UserId,
                cancellationToken);
            verified.Add(ProjectApiAccess.ToResponse(item));
        }

        return Ok(verified);
    }

    [HttpGet("{exportId:guid}")]
    [ProducesResponseType<ExportMetadataResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExportMetadataResponse>> GetById(
        Guid projectId,
        Guid exportId,
        CancellationToken cancellationToken)
    {
        var export = await FindVerifiedAsync(projectId, exportId, cancellationToken);
        return export is null ? NotFound() : Ok(ProjectApiAccess.ToResponse(export));
    }

    [HttpGet("{exportId:guid}/download")]
    [Produces("application/zip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(
        Guid projectId,
        Guid exportId,
        CancellationToken cancellationToken)
    {
        var export = await FindVerifiedAsync(projectId, exportId, cancellationToken);
        return export is null
            ? NotFound()
            : File(export.Archive, "application/zip", $"reqviora-{projectId:N}-{export.ApprovedRevisionId:N}.zip");
    }

    private Guid UserId => ProjectApiAccess.RequireUserId(User);

    private Task RequireOwnerAsync(Guid projectId, CancellationToken cancellationToken) =>
        ProjectApiAccess.RequireOwnerAsync(db, projectId, UserId, cancellationToken);

    private async Task<Domain.Entities.ExportRecord?> FindVerifiedAsync(
        Guid projectId,
        Guid exportId,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(projectId, cancellationToken);
        var identity = await db.ExportRecords.AsNoTracking()
            .Join(
                db.ApprovedRevisions.Where(revision => revision.ProjectId == projectId),
                export => export.ApprovedRevisionId,
                revision => revision.Id,
                (export, _) => new { export.Id, export.ApprovedRevisionId })
            .SingleOrDefaultAsync(export => export.Id == exportId, cancellationToken);
        if (identity is null)
            return null;

        var verified = await exports.GetOrCreateAsync(
            projectId,
            identity.ApprovedRevisionId,
            UserId,
            cancellationToken);
        return verified.Id == exportId ? verified : null;
    }
}
