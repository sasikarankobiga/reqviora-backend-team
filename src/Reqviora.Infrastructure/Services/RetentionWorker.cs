using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Reqviora.Infrastructure.Services;

public sealed class RetentionWorker(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock,
    Microsoft.Extensions.Logging.ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<Persistence.ReqvioraDbContext>();
                var auditDays = Math.Max(30, configuration.GetValue("Security:AuditRetentionDays", 2555));
                var loginCutoff = clock.GetUtcNow().AddDays(-Math.Min(auditDays, 365));
                var auditCutoff = clock.GetUtcNow().AddDays(-auditDays);
                var expiredTokenCutoff = clock.GetUtcNow().AddDays(-1);
                await db.LoginAudits.Where(x => x.OccurredAt < loginCutoff).ExecuteDeleteAsync(stoppingToken);
                await db.AuditEvents.Where(x => x.OccurredAt < auditCutoff).ExecuteDeleteAsync(stoppingToken);
                await db.AccountActionTokens.Where(x => x.ExpiresAt < expiredTokenCutoff).ExecuteDeleteAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Retention cleanup failed");
            }
        }
    }
}
