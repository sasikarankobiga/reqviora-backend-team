using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/drafts")]
public sealed class DraftsController(ProjectDraftService drafts) : ControllerBase
{
    [HttpGet("current")]
    [ProducesResponseType<ProjectDraftResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDraftResponse>> GetCurrent(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var draft = await drafts.ResumeAsync(
            projectId,
            ProjectApiAccess.RequireUserId(User),
            cancellationToken);
        return draft is null ? NotFound() : Ok(ProjectApiAccess.ToResponse(draft));
    }

    [HttpPut("current")]
    [ProducesResponseType<ProjectDraftResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProjectDraftResponse>> Save(
        Guid projectId,
        SaveDraftRequest request,
        CancellationToken cancellationToken)
    {
        var draft = await drafts.SaveAsync(
            projectId,
            ProjectApiAccess.RequireUserId(User),
            request.Draft!.Value.GetRawText(),
            request.ExpectedConcurrencyToken,
            cancellationToken);
        return Ok(ProjectApiAccess.ToResponse(draft));
    }

    [HttpGet("versions")]
    [ProducesResponseType<IReadOnlyList<BlueprintVersionResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BlueprintVersionResponse>>> GetVersions(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var versions = await drafts.GetDraftHistoryAsync(
            projectId,
            ProjectApiAccess.RequireUserId(User),
            cancellationToken);
        return Ok(versions.Select(ProjectApiAccess.ToResponse).ToArray());
    }

    [HttpPost("start-after-approval")]
    [ProducesResponseType<ProjectDraftResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDraftResponse>> StartAfterApproval(
        Guid projectId,
        DraftTokenRequest request,
        CancellationToken cancellationToken)
    {
        var draft = await drafts.StartAfterApprovalAsync(
            projectId,
            ProjectApiAccess.RequireUserId(User),
            request.ExpectedConcurrencyToken,
            cancellationToken);
        return Ok(ProjectApiAccess.ToResponse(draft));
    }
}
