using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/ai/jobs")]
public sealed class AIJobsController(IAIJobQueue jobs, IOptions<AiOptions> options) : ControllerBase
{
    public sealed record CreateAiJob(string Prompt);

    [HttpPost]
    public async Task<IActionResult> Create(CreateAiJob request, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "AI provider approval is pending.");
        if (!UserId(out var ownerId)) return Unauthorized();
        var id = await jobs.EnqueueAsync(ownerId, request.Prompt, cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id }, new { id, state = "queued" });
    }

    [HttpGet("{id:guid}")]
    public IActionResult Get(Guid id)
    {
        if (!options.Value.Enabled)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "AI provider approval is pending.");
        return UserId(out var ownerId) && jobs.Get(id, ownerId) is { } job ? Ok(job) : NotFound();
    }

    private bool UserId(out Guid id) => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out id);
}
