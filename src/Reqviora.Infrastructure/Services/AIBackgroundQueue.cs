using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Reqviora.Application.Common.Interfaces;

namespace Reqviora.Infrastructure.Services;

public sealed class AIBackgroundQueue : BackgroundService, IAIJobQueue
{
    private readonly Channel<(Guid Id, Guid OwnerId, string Prompt)> _queue =
        Channel.CreateBounded<(Guid, Guid, string)>(100);
    private readonly ConcurrentDictionary<Guid, AiJobStatus> _jobs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AIBackgroundQueue> _logger;

    public AIBackgroundQueue(IServiceScopeFactory scopeFactory, ILogger<AIBackgroundQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async ValueTask<Guid> EnqueueAsync(Guid ownerId, string prompt, CancellationToken cancellationToken = default)
    {
        if (ownerId == Guid.Empty)
            throw new ArgumentException("A job owner is required.", nameof(ownerId));
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 100_000)
            throw new ArgumentException("Prompt is empty or too large.", nameof(prompt));
        var id = Guid.NewGuid();
        _jobs[id] = new(id, ownerId, "queued", null, null);
        await _queue.Writer.WriteAsync((id, ownerId, prompt), cancellationToken);
        return id;
    }

    public AiJobStatus? Get(Guid id, Guid ownerId) =>
        _jobs.GetValueOrDefault(id) is { } job && job.OwnerId == ownerId ? job : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            _jobs[job.Id] = new(job.Id, job.OwnerId, "running", null, null);
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var ai = scope.ServiceProvider.GetRequiredService<IAIService>();
                var result = await ai.CompleteAsync(job.Prompt, stoppingToken);
                _jobs[job.Id] = new(job.Id, job.OwnerId, "completed", result, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "AI job {JobId} failed", job.Id);
                _jobs[job.Id] = new(job.Id, job.OwnerId, "failed", null, "AI provider request failed.");
            }
        }
    }
}
