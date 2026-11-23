
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.WorkflowIntegration;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/workflow-intelligence/blueprints")]
public sealed class ReviewBlueprintSyncController(
    ReqvioraDbContext db,
    IRequirementReviewCatalog reviewCatalog) : ControllerBase
{
    public sealed record ReviewSyncItem(
        string RequirementId,
        string ReviewStatus,
        string? BlueprintStatus,
        bool IncludeInGeneration,
        bool RequiresRegeneration,
        string? Problem);

    public sealed record ReviewSyncResponse(
        Guid ProjectId,
        int BlueprintRevision,
        bool ExistingRequirementsSynchronized,
        IReadOnlyList<ReviewSyncItem> Items);

    [HttpGet("review-sync-status")]
    public async Task<IActionResult> GetStatus(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var actorId = ProjectApiAccess.RequireUserId(User);

        await ProjectApiAccess.RequireOwnerAsync(
            db, projectId, actorId, cancellationToken);

        var version = await ProjectApiAccess.FindCurrentVersionAsync(
            db, projectId, cancellationToken);

        var blueprint = ProjectApiAccess.ReadBlueprint(
            version?.BlueprintJson);

        if (blueprint is null)
        {
            return NotFound(new
            {
                error = "BLUEPRINT_NOT_FOUND"
            });
        }

        // Read ALL project requirements from the database.
        var reviews = await reviewCatalog.ListByProjectAsync(
            projectId, cancellationToken);

        // Compare the complete review catalog with Blueprint.
        var audit = ReviewBlueprintSyncPolicy.Audit(
            blueprint, reviews);

        var reviewById = reviews.ToDictionary(
            review => review.Id, StringComparer.Ordinal);

        var blueprintIds = blueprint.Requirements
            .Select(requirement => requirement.Id)
            .ToHashSet(StringComparer.Ordinal);

        var issueById = audit.Issues
            .GroupBy(issue => issue.RequirementId,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Code,
                StringComparer.Ordinal);

        var items = new List<ReviewSyncItem>();

        // Check existing Blueprint requirements.
        foreach (var requirement in blueprint.Requirements)
        {
            if (!reviewById.TryGetValue(
                    requirement.Id, out var reviewed))
            {
                items.Add(new ReviewSyncItem(
                    requirement.Id,
                    "missing",
                    requirement.Status,
                    false,
                    true,
                    "REVIEW_RECORD_NOT_FOUND"));

                continue;
            }

            ReviewBlueprintMapping? mapping = null;

            try
            {
                mapping = ReviewBlueprintDecisionMapper.Map(
                    reviewed, requirement);
            }
            catch (ArgumentException)
            {
                // Invalid review confirmation is reported
                // by the synchronization audit.
            }

            issueById.TryGetValue(
                requirement.Id, out var problem);

            items.Add(new ReviewSyncItem(
                requirement.Id,
                reviewed.Status.ToString().ToLowerInvariant(),
                requirement.Status,
                mapping?.IncludeInGeneration ?? false,
                problem is not null,
                problem));
        }

        // Detect newly confirmed requirements not yet
        // included in the current Blueprint.
        foreach (var reviewed in reviews)
        {
            if (blueprintIds.Contains(reviewed.Id) ||
                reviewed.Status != RequirementReviewStatus.Confirmed)
            {
                continue;
            }

            ReviewBlueprintMapping? mapping = null;

            try
            {
                mapping = ReviewBlueprintDecisionMapper.Map(
                    reviewed, null);
            }
            catch (ArgumentException)
            {
                // Invalid confirmation is excluded.
            }

            issueById.TryGetValue(
                reviewed.Id, out var problem);

            items.Add(new ReviewSyncItem(
                reviewed.Id,
                reviewed.Status.ToString().ToLowerInvariant(),
                null,
                mapping?.IncludeInGeneration ?? false,
                true,
                problem ?? "CONFIRMED_REQUIREMENT_MISSING_FROM_BLUEPRINT"));
        }

        // Retain the existing response property name
        // for backward compatibility with frontend clients.
        // It now checks existing AND newly confirmed requirements.
        return Ok(new ReviewSyncResponse(
            projectId,
            blueprint.Revision,
            audit.IsSynchronized,
            items));
    }
}
