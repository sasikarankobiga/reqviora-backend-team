namespace Reqviora.Application.Common.Interfaces;

public sealed record AiJobStatus(Guid Id, Guid OwnerId, string State, string? Result, string? Error);

public interface IAIJobQueue
{
    ValueTask<Guid> EnqueueAsync(Guid ownerId, string prompt, CancellationToken cancellationToken = default);
    AiJobStatus? Get(Guid id, Guid ownerId);
}
