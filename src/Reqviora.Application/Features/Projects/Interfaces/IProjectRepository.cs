using Reqviora.Domain.Entities;

namespace Reqviora.Application.Features.Projects.Interfaces;

public interface IProjectRepository
{
    Task<IReadOnlyCollection<Project>> GetAllAsync(Guid actorId, CancellationToken cancellationToken = default);
    Task<Project?> GetByIdAsync(
        Guid projectId,
        Guid actorId,
        bool requireOwner = false,
        bool trackChanges = false,
        CancellationToken cancellationToken = default);
    Task AddAsync(Project project, CancellationToken cancellationToken = default);
}
