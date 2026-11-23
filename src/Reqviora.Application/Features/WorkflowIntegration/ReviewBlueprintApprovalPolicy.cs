
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;

namespace Reqviora.Application.Features.WorkflowIntegration;

public static class ReviewBlueprintApprovalPolicy
{
    public static void EnsureSynchronized(
        BlueprintDocument blueprint,
        IReadOnlyCollection<ReviewedRequirement> reviews)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(reviews);

        var audit = ReviewBlueprintSyncPolicy.Audit(
            blueprint,
            reviews);

        if (!audit.IsSynchronized)
        {
            throw new RequirementReviewConflictException(
                "Blueprint approval blocked. " +
                "Reviewed requirements have changed or are missing. " +
                "Regenerate and validate the Blueprint before approval.");
        }
    }
}
