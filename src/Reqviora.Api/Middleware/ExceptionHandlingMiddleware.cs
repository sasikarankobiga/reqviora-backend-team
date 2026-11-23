using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace Reqviora.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            var (status, title) = exception switch
            {
                DbUpdateConcurrencyException => (HttpStatusCode.Conflict, "The resource changed. Reload and retry."),
                UnauthorizedAccessException => (HttpStatusCode.Forbidden, "Project access is denied."),
                KeyNotFoundException => (HttpStatusCode.NotFound, "The requested resource was not found."),
                ArgumentException => (HttpStatusCode.BadRequest, "The request content is invalid."),
                InvalidOperationException => (HttpStatusCode.Conflict, "The requested workflow transition is not allowed."),
                _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
            };

            if (status == HttpStatusCode.InternalServerError)
                logger.LogError(exception, "Unhandled request failure");
            else
                logger.LogInformation("Request rejected with {StatusCode}: {Reason}", (int)status, title);

            context.Response.StatusCode = (int)status;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = (int)status,
                Title = title,
                Type = $"https://httpstatuses.com/{(int)status}",
                Instance = context.Request.Path
            });
        }
    }
}
