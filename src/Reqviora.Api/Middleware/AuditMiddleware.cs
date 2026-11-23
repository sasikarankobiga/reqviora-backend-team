using System.Security.Claims;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Middleware;

public sealed class AuditMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ReqvioraDbContext db, TimeProvider clock, IConfiguration configuration)
    {
        await next(context);
        if (!configuration.GetValue("Security:AuditEnabled", true) ||
            context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/swagger") ||
            context.RequestAborted.IsCancellationRequested) return;
        Guid? userId = Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub"), out var id) ? id : null;
        db.AuditEvents.Add(new AuditEvent
        {
            UserId = userId,
            Method = context.Request.Method,
            Path = context.Request.Path,
            StatusCode = context.Response.StatusCode,
            IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            OccurredAt = clock.GetUtcNow()
        });
        try { await db.SaveChangesAsync(context.RequestAborted); } catch { /* request outcome must not be changed by audit sink failure */ }
    }
}
