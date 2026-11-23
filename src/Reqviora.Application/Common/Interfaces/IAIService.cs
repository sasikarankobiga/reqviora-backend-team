namespace Reqviora.Application.Common.Interfaces;

public interface IAIService
{
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default);
}
