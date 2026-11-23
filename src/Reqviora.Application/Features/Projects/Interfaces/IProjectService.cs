using Reqviora.Application.Common.Models;
using Reqviora.Application.Features.Projects.DTOs;

namespace Reqviora.Application.Features.Projects.Interfaces;

public interface IProjectService
{
    Task<IReadOnlyCollection<ProjectResponseDto>> GetAllAsync(Guid ownerId, CancellationToken cancellationToken = default);
    Task<ProjectResponseDto> CreateAsync(CreateProjectDto request, Guid ownerId, CancellationToken cancellationToken = default);
    Task<Result<ProjectResponseDto>> GetByIdAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken = default);
    Task<Result<ProjectResponseDto>> UpdateAsync(
        Guid projectId,
        UpdateProjectDto request,
        Guid ownerId,
        CancellationToken cancellationToken = default);
}
