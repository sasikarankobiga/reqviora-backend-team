using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/members")]
public sealed class ProjectMembersController(ReqvioraDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectMemberResponse>>> GetAll(
        Guid projectId, CancellationToken cancellationToken)
    {
        var actorId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireReadAsync(db, projectId, actorId, cancellationToken);
        var owner = await db.Projects.AsNoTracking().Where(project => project.Id == projectId)
            .Select(project => new ProjectMemberResponse(project.OwnerId, "Owner", default)).SingleAsync(cancellationToken);
        var members = await db.ProjectMembers.AsNoTracking().Where(member => member.ProjectId == projectId)
            .OrderBy(member => member.JoinedAtUtc)
            .Select(member => new ProjectMemberResponse(member.UserId, member.Role, member.JoinedAtUtc))
            .ToArrayAsync(cancellationToken);
        return Ok(new[] { owner }.Concat(members));
    }

    [HttpPut("{memberUserId:guid}")]
    public async Task<ActionResult<ProjectMemberResponse>> Put(
        Guid projectId, Guid memberUserId, UpdateProjectMemberRequest request, CancellationToken cancellationToken)
    {
        await ProjectApiAccess.RequireOwnerAsync(
            db, projectId, ProjectApiAccess.RequireUserId(User), cancellationToken);
        if (!ProjectMemberRoles.IsSupported(request.Role))
            return BadRequest(new ValidationProblemDetails(
                new Dictionary<string, string[]> { [nameof(request.Role)] = ["Role must be Viewer or Editor."] }));
        if (!await db.Users.AsNoTracking().AnyAsync(user => user.Id == memberUserId, cancellationToken))
            return NotFound();

        var project = await db.Projects.AsNoTracking().SingleAsync(item => item.Id == projectId, cancellationToken);
        if (project.OwnerId == memberUserId)
            return Conflict(new ProblemDetails { Title = "The project owner cannot also be a member." });

        var member = await db.ProjectMembers.SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.UserId == memberUserId, cancellationToken);
        if (member is null)
        {
            member = new ProjectMember { ProjectId = projectId, UserId = memberUserId };
            db.ProjectMembers.Add(member);
        }
        member.Role = ProjectMemberRoles.Normalize(request.Role);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new ProjectMemberResponse(member.UserId, member.Role, member.JoinedAtUtc));
    }

    [HttpDelete("{memberUserId:guid}")]
    public async Task<IActionResult> Delete(
        Guid projectId, Guid memberUserId, CancellationToken cancellationToken)
    {
        await ProjectApiAccess.RequireOwnerAsync(
            db, projectId, ProjectApiAccess.RequireUserId(User), cancellationToken);
        var member = await db.ProjectMembers.SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.UserId == memberUserId, cancellationToken);
        if (member is null)
            return NotFound();
        db.ProjectMembers.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record UpdateProjectMemberRequest(
    [Required, StringLength(30)] string Role);

public sealed record ProjectMemberResponse(Guid UserId, string Role, DateTime? JoinedAtUtc);
