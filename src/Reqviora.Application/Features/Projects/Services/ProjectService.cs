using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Common.Models;
using Reqviora.Application.Features.Projects.DTOs;
using Reqviora.Application.Features.Projects.Interfaces;
using Reqviora.Domain.Entities;

namespace Reqviora.Application.Features.Projects.Services;

public sealed class ProjectService(
    IProjectRepository projects,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IProjectService
{
    public async Task<IReadOnlyCollection<ProjectResponseDto>> GetAllAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        (await projects.GetAllAsync(ownerId, cancellationToken)).Select(ToResponse).ToArray();

    public async Task<ProjectResponseDto> CreateAsync(CreateProjectDto request, Guid ownerId, CancellationToken cancellationToken = default)
    {
        var project = new Project
        {
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            Industry = request.Industry.Trim(),
            TargetAudience = request.TargetAudience.Trim(),
            ProjectType = request.ProjectType.Trim(),
            PrimaryPlatform = request.PrimaryPlatform.Trim().ToLowerInvariant(),
            TechnologyPreferences = request.TechnologyPreferences.Trim(),
            Constraints = request.Constraints.Trim(),
            Integrations = request.Integrations.Trim(),
            OwnerId = ownerId,
            CreatedAt = timeProvider.GetUtcNow(),
            CreatedBy = ownerId.ToString()
        };
        await projects.AddAsync(project, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(project);
    }

    public async Task<Result<ProjectResponseDto>> GetByIdAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, ownerId, cancellationToken: cancellationToken);
        return project is null
            ? Result<ProjectResponseDto>.Failure("Project was not found.")
            : Result<ProjectResponseDto>.Success(ToResponse(project));
    }

    public async Task<Result<ProjectResponseDto>> UpdateAsync(
        Guid projectId,
        UpdateProjectDto request,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(
            projectId,
            ownerId,
            requireOwner: true,
            trackChanges: true,
            cancellationToken: cancellationToken);
        if (project is null)
            return Result<ProjectResponseDto>.Failure("Project was not found.");

        project.Name = request.Name.Trim();
        project.Description = request.Description.Trim();
        project.Industry = request.Industry.Trim();
        project.TargetAudience = request.TargetAudience.Trim();
        project.ProjectType = request.ProjectType.Trim();
        project.PrimaryPlatform = request.PrimaryPlatform.Trim().ToLowerInvariant();
        project.TechnologyPreferences = request.TechnologyPreferences.Trim();
        project.Constraints = request.Constraints.Trim();
        project.Integrations = request.Integrations.Trim();
        project.LastModifiedAt = timeProvider.GetUtcNow();
        project.LastModifiedBy = ownerId.ToString();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ProjectResponseDto>.Success(ToResponse(project));
    }

    private static ProjectResponseDto ToResponse(Project project) => new(
        project.Id,
        project.Name,
        project.Description,
        project.OwnerId,
        project.CreatedAt,
        project.LastModifiedAt,
        project.Industry,
        project.TargetAudience,
        project.ProjectType,
        project.PrimaryPlatform,
        project.TechnologyPreferences,
        project.Constraints,
        project.Integrations);
}
