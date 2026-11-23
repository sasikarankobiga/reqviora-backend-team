
namespace Reqviora.Application.Features.RequirementReview;

public sealed record SubmitRequirementReview(
    Guid ProjectId,
    string RequirementId,
    int ExpectedRevision,
    RequirementDecisionKind Decision,
    string? EditedTitle = null,
    string? EditedDescription = null
);

public sealed record RequirementReviewSnapshot(
    Guid ProjectId,
    ReviewedRequirement Requirement
);

// TryCommitAsync saves the new revision and its audit event atomically,
// using ExpectedRevision for optimistic concurrency.
public interface IRequirementReviewStore
{
    Task<RequirementReviewSnapshot?> FindAsync(
        Guid projectId,
        string requirementId,
        CancellationToken cancellationToken = default);

    Task<bool> TryCommitAsync(
        Guid projectId,
        int expectedRevision,
        RequirementReviewResult result,
        CancellationToken cancellationToken = default);
}

// Check whether the authenticated actor has review access to the project.
public interface IRequirementReviewAccessPolicy
{
    Task<bool> CanReviewAsync(
        Guid projectId,
        Guid authenticatedUserId,
        CancellationToken cancellationToken = default);
}

public sealed class RequirementReviewAccessDeniedException(string message)
    : UnauthorizedAccessException(message);

public sealed class RequirementReviewNotFoundException(string message)
    : KeyNotFoundException(message);

public sealed class RequirementReviewConflictException(string message)
    : InvalidOperationException(message);

public sealed class RequirementReviewWorkflow(
    IRequirementReviewStore store,
    IRequirementReviewAccessPolicy accessPolicy)
{
    public async Task<RequirementReviewResult> SubmitAsync(
        SubmitRequirementReview request,
        Guid authenticatedUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ProjectId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RequirementId) ||
            request.ExpectedRevision < 1)
        {
            throw new ArgumentException(
                "Valid project, requirement and revision are required.");
        }

        if (authenticatedUserId == Guid.Empty)
            throw new UnauthorizedAccessException(
                "An authenticated reviewer is required.");

        // Authorization happens before reading requirement data.
        bool allowed = await accessPolicy.CanReviewAsync(
            request.ProjectId,
            authenticatedUserId,
            cancellationToken);

        if (!allowed)
            throw new RequirementReviewAccessDeniedException(
                "User is not authorized to review this project.");

        var snapshot = await store.FindAsync(
            request.ProjectId,
            request.RequirementId,
            cancellationToken);

        if (snapshot is null)
            throw new RequirementReviewNotFoundException(
                "Requirement was not found.");

        if (snapshot.ProjectId != request.ProjectId ||
            snapshot.Requirement.Id != request.RequirementId)
        {
            throw new InvalidOperationException(
                "Requirement/project association mismatch.");
        }

        var decision = new RequirementReviewDecision(
            request.RequirementId,
            request.ExpectedRevision,
            request.Decision,
            authenticatedUserId,
            DateTimeOffset.UtcNow,
            request.EditedTitle,
            request.EditedDescription
        );

        // Reuse the tested Day 03 business rules.
        var result = RequirementReviewService.Apply(
            snapshot.Requirement,
            decision);

        bool saved = await store.TryCommitAsync(
            request.ProjectId,
            request.ExpectedRevision,
            result,
            cancellationToken);

        if (!saved)
            throw new RequirementReviewConflictException(
                "Concurrent review update. Reload and retry.");

        return result;
    }
}
