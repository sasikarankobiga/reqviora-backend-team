using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspaces")]
public sealed class WorkspacesController(ReqvioraDbContext db) : ControllerBase
{
    public sealed record CreateWorkspaceRequest(string Name);
    public sealed record AddWorkspaceMemberRequest(Guid UserId, string Role = "Member");

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WorkspaceResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkspaceResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        var workspaces = await db.Workspaces.AsNoTracking()
            .Where(workspace => workspace.OwnerUserId == userId ||
                db.WorkspaceMembers.Any(member => member.WorkspaceId == workspace.Id && member.UserId == userId))
            .OrderBy(workspace => workspace.Name)
            .Select(workspace => new WorkspaceResponse(
                workspace.Id,
                workspace.Name,
                workspace.OwnerUserId,
                db.Projects.Count(project => project.WorkspaceId == workspace.Id)))
            .ToArrayAsync(cancellationToken);
        return Ok(workspaces);
    }

    [HttpGet("{workspaceId:guid}")]
    [ProducesResponseType<WorkspaceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkspaceResponse>> GetById(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        var workspace = await db.Workspaces.AsNoTracking()
            .Where(item => item.Id == workspaceId && (item.OwnerUserId == userId ||
                db.WorkspaceMembers.Any(member => member.WorkspaceId == item.Id && member.UserId == userId)))
            .Select(item => new WorkspaceResponse(
                item.Id,
                item.Name,
                item.OwnerUserId,
                db.Projects.Count(project => project.WorkspaceId == item.Id)))
            .SingleOrDefaultAsync(cancellationToken);
        return workspace is null ? NotFound() : Ok(workspace);
    }

    [HttpPost]
    [ProducesResponseType<WorkspaceResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<WorkspaceResponse>> Create(
        CreateWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150)
            return ValidationProblem("Workspace name must contain between 1 and 150 characters.");

        var workspace = new Workspace { Name = name, OwnerUserId = userId, CreatedBy = userId.ToString() };
        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = userId,
            Role = "Owner"
        });
        await db.SaveChangesAsync(cancellationToken);
        var response = new WorkspaceResponse(workspace.Id, workspace.Name, workspace.OwnerUserId, 0);
        return CreatedAtAction(nameof(GetById), new { workspaceId = workspace.Id }, response);
    }

    [HttpGet("{workspaceId:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid workspaceId, CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        if (!await CanAccessAsync(workspaceId, userId, cancellationToken)) return NotFound();
        return Ok(await db.WorkspaceMembers.AsNoTracking()
            .Where(member => member.WorkspaceId == workspaceId)
            .OrderBy(member => member.JoinedAt)
            .ToArrayAsync(cancellationToken));
    }

    [HttpPost("{workspaceId:guid}/members")]
    public async Task<IActionResult> AddMember(
        Guid workspaceId,
        AddWorkspaceMemberRequest request,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        if (!await IsOwnerAsync(workspaceId, userId, cancellationToken)) return NotFound();
        var role = request.Role?.Trim();
        if (role is not ("Admin" or "Member" or "Viewer"))
            return ValidationProblem("Role must be Admin, Member, or Viewer.");
        if (!await db.Users.AnyAsync(user => user.Id == request.UserId, cancellationToken))
            return ValidationProblem("The requested user does not exist.");
        if (await db.WorkspaceMembers.AnyAsync(member =>
                member.WorkspaceId == workspaceId && member.UserId == request.UserId, cancellationToken))
            return Conflict();

        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = request.UserId,
            Role = role
        });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{workspaceId:guid}/members/{memberUserId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid workspaceId,
        Guid memberUserId,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        if (!await IsOwnerAsync(workspaceId, userId, cancellationToken)) return NotFound();
        var member = await db.WorkspaceMembers.SingleOrDefaultAsync(item =>
            item.WorkspaceId == workspaceId && item.UserId == memberUserId && item.Role != "Owner", cancellationToken);
        if (member is null) return NotFound();
        db.WorkspaceMembers.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Task<bool> IsOwnerAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        db.Workspaces.AnyAsync(workspace => workspace.Id == workspaceId && workspace.OwnerUserId == userId, cancellationToken);

    private async Task<bool> CanAccessAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        await IsOwnerAsync(workspaceId, userId, cancellationToken) ||
        await db.WorkspaceMembers.AnyAsync(member =>
            member.WorkspaceId == workspaceId && member.UserId == userId, cancellationToken);
}
