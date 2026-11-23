
using Reqviora.Application.Features.AIOrchestration;

namespace Reqviora.Application.Features.RequirementReview;

public enum RequirementReviewStatus
{
    Suggested,
    Confirmed,
    Rejected,
    Deferred
}

public enum RequirementDecisionKind
{
    Accept,
    Edit,
    Reject,
    Defer
}

public sealed record ReviewedRequirement(
    string Id,
    string Title,
    string Description,
    RequirementReviewStatus Status,
    int Revision,
    IReadOnlyList<CandidateEvidence> Evidence,
    Guid? ConfirmedBy,
    DateTimeOffset? ConfirmedAt
);

public sealed record RequirementReviewDecision(
    string RequirementId,
    int ExpectedRevision,
    RequirementDecisionKind Decision,
    Guid ActorId,
    DateTimeOffset OccurredAt,
    string? EditedTitle = null,
    string? EditedDescription = null
);

public sealed record RequirementReviewEvent(
    string RequirementId,
    RequirementDecisionKind Decision,
    Guid ActorId,
    int PreviousRevision,
    int NewRevision,
    DateTimeOffset OccurredAt
);

public sealed record RequirementReviewResult(
    ReviewedRequirement Requirement,
    RequirementReviewEvent Event
);

public static class RequirementReviewService
{
    public static ReviewedRequirement CreateCandidate(
        RequirementCandidate candidate)
    {
        if (candidate is null ||
            string.IsNullOrWhiteSpace(candidate.Id) ||
            string.IsNullOrWhiteSpace(candidate.Title) ||
            candidate.Evidence is null ||
            candidate.Evidence.Count == 0)
        {
            throw new ArgumentException("Invalid requirement candidate.");
        }

        return new ReviewedRequirement(
            candidate.Id,
            candidate.Title,
            candidate.Description,
            RequirementReviewStatus.Suggested,
            1,
            candidate.Evidence.ToArray(),
            null,
            null
        );
    }

    public static RequirementReviewResult Apply(
        ReviewedRequirement current,
        RequirementReviewDecision decision)
    {
        if (current is null || decision is null)
            throw new ArgumentException("Review data is required.");

        if (current.Id != decision.RequirementId)
            throw new ArgumentException("Requirement ID mismatch.");

        if (current.Revision != decision.ExpectedRevision)
            throw new RequirementReviewConflictException(
                "Requirement revision conflict.");

        if (decision.ActorId == Guid.Empty ||
            decision.OccurredAt == default)
        {
            throw new ArgumentException(
                "An identified human reviewer and review time are required.");
        }

        if (!Enum.IsDefined(decision.Decision))
            throw new ArgumentException("Invalid review decision.");

        if (current.Status == RequirementReviewStatus.Rejected)
            throw new RequirementReviewConflictException(
                "Rejected requirement cannot be changed by this workflow.");

        if (current.Status == RequirementReviewStatus.Confirmed &&
            decision.Decision != RequirementDecisionKind.Edit)
        {
            throw new RequirementReviewConflictException(
                "Confirmed requirements must be edited before another decision.");
        }

        ReviewedRequirement updated;

        switch (decision.Decision)
        {
            case RequirementDecisionKind.Accept:
                updated = current with
                {
                    Status = RequirementReviewStatus.Confirmed,
                    ConfirmedBy = decision.ActorId,
                    ConfirmedAt = decision.OccurredAt,
                    Revision = current.Revision + 1
                };
                break;

            case RequirementDecisionKind.Edit:
                if (string.IsNullOrWhiteSpace(decision.EditedTitle) ||
                    decision.EditedTitle.Length > 200 ||
                    decision.EditedDescription is null ||
                    decision.EditedDescription.Length > 4000)
                {
                    throw new ArgumentException(
                        "Valid edited title and description are required.");
                }

                // An edit invalidates earlier confirmation.
                // User must explicitly accept the edited version.
                updated = current with
                {
                    Title = decision.EditedTitle,
                    Description = decision.EditedDescription,
                    Status = RequirementReviewStatus.Suggested,
                    ConfirmedBy = null,
                    ConfirmedAt = null,
                    Revision = current.Revision + 1
                };
                break;

            case RequirementDecisionKind.Reject:
                updated = current with
                {
                    Status = RequirementReviewStatus.Rejected,
                    ConfirmedBy = null,
                    ConfirmedAt = null,
                    Revision = current.Revision + 1
                };
                break;

            case RequirementDecisionKind.Defer:
                updated = current with
                {
                    Status = RequirementReviewStatus.Deferred,
                    ConfirmedBy = null,
                    ConfirmedAt = null,
                    Revision = current.Revision + 1
                };
                break;

            default:
                throw new ArgumentException("Unknown review decision.");
        }

        var reviewEvent = new RequirementReviewEvent(
            current.Id,
            decision.Decision,
            decision.ActorId,
            current.Revision,
            updated.Revision,
            decision.OccurredAt
        );

        return new RequirementReviewResult(updated, reviewEvent);
    }
}

public sealed record ClarificationQuestion(
    Guid Id,
    string RequirementId,
    string Question,
    string? Answer,
    int Revision,
    bool IsResolved
);

public sealed record ClarificationAnswerRequest(
    Guid QuestionId,
    int ExpectedRevision,
    string Answer
);

public static class ClarificationService
{
    public static ClarificationQuestion Answer(
        ClarificationQuestion current,
        ClarificationAnswerRequest request)
    {
        if (current is null || request is null)
            throw new ArgumentException("Clarification is required.");

        if (current.Id != request.QuestionId ||
            current.Revision != request.ExpectedRevision)
        {
            throw new InvalidOperationException(
                "Clarification revision conflict.");
        }

        if (current.IsResolved)
            throw new InvalidOperationException(
                "Clarification already resolved.");

        if (string.IsNullOrWhiteSpace(request.Answer) ||
            request.Answer.Length > 5000)
        {
            throw new ArgumentException(
                "Valid clarification answer is required.");
        }

        return current with
        {
            Answer = request.Answer,
            IsResolved = true,
            Revision = current.Revision + 1
        };
    }
}
