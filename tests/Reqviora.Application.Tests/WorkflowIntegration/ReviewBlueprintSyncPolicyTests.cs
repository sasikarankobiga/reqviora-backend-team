
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.WorkflowIntegration;
using Xunit;

namespace Reqviora.Application.Tests.WorkflowIntegration;

public sealed class ReviewBlueprintSyncPolicyTests
{
    private static readonly Guid Reviewer =
        Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

    private static readonly DateTimeOffset ConfirmedAt =
        new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    private static ReviewedRequirement Review(
        string id,
        RequirementReviewStatus status =
            RequirementReviewStatus.Confirmed)
    {
        return new ReviewedRequirement(
            id,
            "User Login",
            "Email and password",
            status,
            2,
            [
                new CandidateEvidence(
                    "DOC-001",
                    "page 1",
                    "User Login")
            ],
            status == RequirementReviewStatus.Confirmed
                ? Reviewer : null,
            status == RequirementReviewStatus.Confirmed
                ? ConfirmedAt : null);
    }

    private static BlueprintDocument Blueprint(
        params string[] requirementIds)
    {
        return new BlueprintDocument
        {
            BlueprintId = "BP-TEST-001",
            ProjectId = Guid.NewGuid(),
            Revision = 1,
            Lifecycle = "draft",
            TargetPlatforms = ["web"],
            Requirements = requirementIds.Select(id =>
                new BlueprintRequirement
                {
                    Id = id,
                    Text = "User Login\nEmail and password",
                    Origin = "ai",
                    Status = "confirmed",
                    ConfirmedBy = Reviewer.ToString(),
                    ConfirmedAt = ConfirmedAt,
                    Evidence =
                    [
                        new BlueprintEvidence
                        {
                            SourceId = "DOC-001",
                            Locator = "page 1",
                            Quote = "User Login"
                        }
                    ]
                }).ToList()
        };
    }

    [Fact]
    public void Matching_reviews_and_blueprint_are_synchronized()
    {
        var result = ReviewBlueprintSyncPolicy.Audit(
            Blueprint("REQ-001"),
            [Review("REQ-001")]);

        Assert.True(result.IsSynchronized);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Newly_confirmed_requirement_requires_regeneration()
    {
        var result = ReviewBlueprintSyncPolicy.Audit(
            Blueprint("REQ-001"),
            [
                Review("REQ-001"),
                Review("REQ-002")
            ]);

        Assert.False(result.IsSynchronized);

        Assert.Contains(result.Issues, issue =>
            issue.RequirementId == "REQ-002" &&
            issue.Code ==
                "CONFIRMED_REQUIREMENT_MISSING_FROM_BLUEPRINT");
    }

    [Fact]
    public void Edited_requirement_marks_blueprint_stale()
    {
        var result = ReviewBlueprintSyncPolicy.Audit(
            Blueprint("REQ-001"),
            [
                Review(
                    "REQ-001",
                    RequirementReviewStatus.Suggested)
            ]);

        Assert.False(result.IsSynchronized);

        Assert.Contains(result.Issues, issue =>
            issue.Code == "REVIEW_BLUEPRINT_MISMATCH");
    }

    [Fact]
    public void Rejected_requirement_is_excluded_from_generation()
    {
        var result = ReviewBlueprintSyncPolicy.Audit(
            Blueprint("REQ-001"),
            [
                Review("REQ-001"),
                Review(
                    "REQ-002",
                    RequirementReviewStatus.Rejected)
            ]);

        Assert.True(result.IsSynchronized);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Deferred_requirement_is_excluded_from_generation()
    {
        var result = ReviewBlueprintSyncPolicy.Audit(
            Blueprint("REQ-001"),
            [
                Review("REQ-001"),
                Review(
                    "REQ-002",
                    RequirementReviewStatus.Deferred)
            ]);

        Assert.True(result.IsSynchronized);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Missing_review_record_marks_blueprint_stale()
    {
        var result = ReviewBlueprintSyncPolicy.Audit(
            Blueprint("REQ-001"),
            Array.Empty<ReviewedRequirement>());

        Assert.False(result.IsSynchronized);

        Assert.Contains(result.Issues, issue =>
            issue.Code == "REVIEW_RECORD_NOT_FOUND");
    }

    [Fact]
    public void Duplicate_review_ids_are_rejected()
    {
        var reviews = new[]
        {
            Review("REQ-001"),
            Review("REQ-001")
        };

        Assert.Throws<ArgumentException>(() =>
            ReviewBlueprintSyncPolicy.Audit(
                Blueprint("REQ-001"), reviews));
    }
}
