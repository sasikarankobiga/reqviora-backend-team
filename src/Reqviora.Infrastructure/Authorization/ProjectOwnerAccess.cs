using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Authorization;

public static class ProjectOwnerAccess
{
    public static Task<Project> RequireReadAsync(
        ReqvioraDbContext db, Guid projectId, Guid actorId, CancellationToken cancellationToken) =>
        RequireByAccessAsync(db, projectId, actorId, write: false, cancellationToken);

    public static Task<Project> RequireWriteAsync(
        ReqvioraDbContext db, Guid projectId, Guid actorId, CancellationToken cancellationToken) =>
        RequireByAccessAsync(db, projectId, actorId, write: true, cancellationToken);

    internal static async Task<Project> RequireAsync(
        ReqvioraDbContext db, Guid projectId, Guid actorId, CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty || actorId == Guid.Empty)
            throw new UnauthorizedAccessException("A project and verified actor are required.");

        return await db.Projects.AsNoTracking().SingleOrDefaultAsync(
            project => project.Id == projectId && project.OwnerId == actorId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Project owner access is required.");
    }

    private static async Task<Project> RequireByAccessAsync(
        ReqvioraDbContext db, Guid projectId, Guid actorId, bool write, CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty || actorId == Guid.Empty)
            throw new UnauthorizedAccessException("A project and verified actor are required.");

        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == projectId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Project access is required.");
        if (project.OwnerId == actorId)
            return project;

        var allowed = await db.ProjectMembers.AsNoTracking().AnyAsync(member =>
            member.ProjectId == projectId && member.UserId == actorId &&
            (!write || member.Role == ProjectMemberRoles.Editor), cancellationToken);
        return allowed
            ? project
            : throw new UnauthorizedAccessException(write
                ? "Project editor access is required."
                : "Project member access is required.");
    }
}
