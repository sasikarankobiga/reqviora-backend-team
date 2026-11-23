
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;

namespace Reqviora.Application.Features.WorkflowIntegration;

public sealed record ReviewBlueprintSyncIssue(
    string RequirementId,
    string Code
);

public sealed record ReviewBlueprintSyncAudit(
    bool IsSynchronized,
    IReadOnlyList<ReviewBlueprintSyncIssue> Issues
);

public static class ReviewBlueprintSyncPolicy
{
    public static ReviewBlueprintSyncAudit Audit(
        BlueprintDocument blueprint,
        IReadOnlyCollection<ReviewedRequirement> reviews)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(reviews);

        if (blueprint.Requirements is null ||
            reviews.Any(r => r is null ||
                string.IsNullOrWhiteSpace(r.Id)))
        {
            throw new ArgumentException(
                "Invalid requirements supplied for synchronization.");
        }

        var reviewList = reviews.ToArray();

        if (reviewList.Select(r => r.Id)
            .Distinct(StringComparer.Ordinal).Count() != reviewList.Length)
        {
            throw new ArgumentException(
                "Duplicate review requirement IDs.");
        }

        var reviewById = reviewList.ToDictionary(
            r => r.Id, StringComparer.Ordinal);

        var blueprintIds = new HashSet<string>(
            StringComparer.Ordinal);

        var issues = new List<ReviewBlueprintSyncIssue>();

        // Check every requirement already present in Blueprint.
        foreach (var existing in blueprint.Requirements)
        {
            if (existing is null ||
                string.IsNullOrWhiteSpace(existing.Id))
            {
                issues.Add(new ReviewBlueprintSyncIssue(
                    "(invalid)", "INVALID_BLUEPRINT_REQUIREMENT"));
                continue;
            }

            if (!blueprintIds.Add(existing.Id))
            {
                issues.Add(new ReviewBlueprintSyncIssue(
                    existing.Id, "DUPLICATE_BLUEPRINT_REQUIREMENT"));
                continue;
            }

            if (!reviewById.TryGetValue(existing.Id, out var reviewed))
            {
                issues.Add(new ReviewBlueprintSyncIssue(
                    existing.Id, "REVIEW_RECORD_NOT_FOUND"));
                continue;
            }

            try
            {
                var mapping = ReviewBlueprintDecisionMapper.Map(
                    reviewed, existing);

                if (mapping.RequiresRegeneration)
                {
                    issues.Add(new ReviewBlueprintSyncIssue(
                        existing.Id, "REVIEW_BLUEPRINT_MISMATCH"));
                }
            }
            catch (ArgumentException)
            {
                issues.Add(new ReviewBlueprintSyncIssue(
                    existing.Id, "INVALID_REVIEW_CONFIRMATION"));
            }
        }

        // Check newly confirmed requirements absent from Blueprint.
        foreach (var reviewed in reviewList)
        {
            if (reviewed.Status != RequirementReviewStatus.Confirmed ||
                blueprintIds.Contains(reviewed.Id))
            {
                continue;
            }

            try
            {
                ReviewBlueprintDecisionMapper.Map(reviewed, null);

                issues.Add(new ReviewBlueprintSyncIssue(
                    reviewed.Id,
                    "CONFIRMED_REQUIREMENT_MISSING_FROM_BLUEPRINT"));
            }
            catch (ArgumentException)
            {
                issues.Add(new ReviewBlueprintSyncIssue(
                    reviewed.Id, "INVALID_REVIEW_CONFIRMATION"));
            }
        }

        return new ReviewBlueprintSyncAudit(
            issues.Count == 0,
            issues);
    }
}
