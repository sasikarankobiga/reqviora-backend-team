
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;

namespace Reqviora.Application.Features.WorkflowIntegration;

public sealed record ReviewBlueprintMapping(
    string RequirementId,
    RequirementReviewStatus ReviewStatus,
    string? BlueprintStatus,
    bool IncludeInGeneration,
    bool RequiresRegeneration
);

public static class ReviewBlueprintDecisionMapper
{
    public static ReviewBlueprintMapping Map(
        ReviewedRequirement reviewed,
        BlueprintRequirement? current)
    {
        ArgumentNullException.ThrowIfNull(reviewed);

        if (string.IsNullOrWhiteSpace(reviewed.Id))
            throw new ArgumentException("Requirement ID is required.");

        string? targetStatus = reviewed.Status switch
        {
            RequirementReviewStatus.Confirmed => "confirmed",
            RequirementReviewStatus.Suggested => "suggested",
            RequirementReviewStatus.Rejected => "rejected",
            RequirementReviewStatus.Deferred => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(reviewed))
        };

        bool confirmed =
            reviewed.Status == RequirementReviewStatus.Confirmed;

        if (confirmed &&
            (reviewed.ConfirmedBy is null ||
             reviewed.ConfirmedBy == Guid.Empty ||
             reviewed.ConfirmedAt is null ||
             reviewed.Evidence is null ||
             reviewed.Evidence.Count == 0))
        {
            throw new ArgumentException(
                "Explicit human confirmation and evidence are required.");
        }

        bool synchronized = confirmed
            ? ConfirmedMatches(reviewed, current)
            : current is null;

        return new ReviewBlueprintMapping(
            reviewed.Id,
            reviewed.Status,
            targetStatus,
            confirmed,
            !synchronized
        );
    }

    private static bool ConfirmedMatches(
        ReviewedRequirement reviewed,
        BlueprintRequirement? current)
    {
        if (current is null ||
            current.Id != reviewed.Id ||
            current.Status != "confirmed" ||
            current.Text != reviewed.Title + "\n" + reviewed.Description ||
            current.ConfirmedBy != reviewed.ConfirmedBy?.ToString() ||
            current.ConfirmedAt != reviewed.ConfirmedAt ||
            current.Evidence is null ||
            reviewed.Evidence is null ||
            current.Evidence.Count != reviewed.Evidence.Count)
        {
            return false;
        }

        for (int i = 0; i < reviewed.Evidence.Count; i++)
        {
            var previous = current.Evidence[i];
            var latest = reviewed.Evidence[i];

            if (previous.SourceId != latest.SourceId ||
                previous.Locator != latest.Locator ||
                previous.Quote != latest.Quote)
            {
                return false;
            }
        }

        return true;
    }
}
