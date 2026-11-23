using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

// G0-07 V1 scope: authenticated, read-only project context. Mutating tools stay disabled.
[ApiController, Authorize, Route("api/mcp/v1")]
public sealed class McpV1Controller(ReqvioraDbContext db, IConfiguration configuration) : ControllerBase
{
    private static readonly object[] Tools =
    [
        new { name = "reqviora.project.overview", description = "Read workflow counts for an owned project." },
        new { name = "reqviora.blueprint.current", description = "Read the current draft Blueprint for an owned project." },
        new { name = "reqviora.validation.latest", description = "Read the latest validation result for an owned project." }
    ];

    private static readonly object[] Resources =
    [
        new { uriTemplate = "reqviora://projects/{projectId}/overview", mimeType = "application/json" },
        new { uriTemplate = "reqviora://projects/{projectId}/blueprint/current", mimeType = "application/json" }
    ];

    private static readonly object[] Prompts =
    [
        new { name = "requirements-review", description = "Review suggested requirements without auto-approving them." },
        new { name = "blueprint-validation", description = "Explain current Blueprint validation findings." }
    ];

    public sealed record ToolCallRequest(string Name, Guid ProjectId);

    [HttpGet("catalog")]
    public IActionResult Catalog() => Enabled()
        ? Ok(new { protocolVersion = "v1", readOnly = true, tools = Tools, resources = Resources, prompts = Prompts })
        : NotFound();

    [HttpPost("tools/call")]
    public async Task<IActionResult> Call(ToolCallRequest request, CancellationToken cancellationToken)
    {
        if (!Enabled()) return NotFound();
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireReadAsync(db, request.ProjectId, userId, cancellationToken);
        var current = await ProjectApiAccess.FindCurrentVersionAsync(db, request.ProjectId, cancellationToken);
        return request.Name switch
        {
            "reqviora.project.overview" => Ok(new
            {
                projectId = request.ProjectId,
                currentDraftVersion = current?.Version,
                requirements = current is null ? 0 : await db.DraftRequirements.CountAsync(
                    item => item.DraftVersionId == current.Id, cancellationToken),
                features = current is null ? 0 : await db.DraftFeatures.CountAsync(
                    item => item.DraftVersionId == current.Id, cancellationToken),
                screens = current is null ? 0 : await db.DraftScreens.CountAsync(
                    item => item.DraftVersionId == current.Id, cancellationToken)
            }),
            "reqviora.blueprint.current" => current?.BlueprintJson is null
                ? NotFound()
                : Content(current.BlueprintJson, "application/json"),
            "reqviora.validation.latest" => Ok(await db.ValidationRuns.AsNoTracking()
                .Where(run => db.DesignDraftVersions.Any(version =>
                    version.Id == run.DraftVersionId && version.ProjectId == request.ProjectId))
                .OrderByDescending(run => run.ValidatedAt)
                .Select(run => new { run.Id, run.DraftVersionId, run.Passed, run.ValidatedAt })
                .FirstOrDefaultAsync(cancellationToken)),
            _ => BadRequest(new { error = "MCP_TOOL_NOT_ALLOWED" })
        };
    }

    [HttpGet("prompts/{name}")]
    public IActionResult Prompt(string name)
    {
        if (!Enabled()) return NotFound();
        return name switch
        {
            "requirements-review" => Ok(new { name, template = "Review each candidate against its cited evidence. Never auto-approve; return suggested edits for human confirmation." }),
            "blueprint-validation" => Ok(new { name, template = "Explain each validation finding, its path, and a safe correction. Do not modify an approved revision." }),
            _ => NotFound()
        };
    }

    private bool Enabled() => configuration.GetValue("Mcp:Enabled", false);
}
