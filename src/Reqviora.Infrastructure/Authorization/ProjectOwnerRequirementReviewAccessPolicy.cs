using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Domain.Entities;

namespace Reqviora.Infrastructure.Authorization;

internal sealed class ProjectOwnerRequirementReviewAccessPolicy(ReqvioraDbContext dbContext)
    : IRequirementReviewAccessPolicy
{
    public Task<bool> CanReviewAsync(
        Guid projectId,
        Guid authenticatedUserId,
        CancellationToken cancellationToken = default) =>
        dbContext.Projects.AnyAsync(project => project.Id == projectId &&
            (project.OwnerId == authenticatedUserId || dbContext.ProjectMembers.Any(member =>
                member.ProjectId == projectId && member.UserId == authenticatedUserId &&
                member.Role == ProjectMemberRoles.Editor)), cancellationToken);
}
