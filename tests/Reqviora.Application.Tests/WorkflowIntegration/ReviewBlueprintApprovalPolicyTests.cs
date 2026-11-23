
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.WorkflowIntegration;
using Xunit;

namespace Reqviora.Application.Tests.WorkflowIntegration;

public sealed class ReviewBlueprintApprovalPolicyTests
{
    private static readonly Guid ReviewerId =
        Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

    private static readonly DateTimeOffset ApprovalTime =
        new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    private static ReviewedRequirement CreateReview(
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
                ? ReviewerId : null,
            status == RequirementReviewStatus.Confirmed
                ? ApprovalTime : null
        );
    }

    private static BlueprintDocument CreateBlueprint()
    {
        return new BlueprintDocument
        {
            BlueprintId = "BP-APPROVAL-001",
            ProjectId = Guid.NewGuid(),
            Revision = 1,
            Lifecycle = "draft",
            TargetPlatforms = ["web"],

            Requirements =
            [
                new BlueprintRequirement
                {
                    Id = "REQ-001",
                    Text = "User Login\nEmail and password",
                    Origin = "ai",
                    Status = "confirmed",
                    ConfirmedBy = ReviewerId.ToString(),
                    ConfirmedAt = ApprovalTime,

                    Evidence =
                    [
                        new BlueprintEvidence
                        {
                            SourceId = "DOC-001",
                            Locator = "page 1",
                            Quote = "User Login"
                        }
                    ]
                }
            ]
        };
    }

    // TEST 1
    [Fact]
    public void Matching_review_allows_approval()
    {
        var blueprint = CreateBlueprint();

        var reviews = new[]
        {
            CreateReview("REQ-001")
        };

        var exception = Record.Exception(() =>
            ReviewBlueprintApprovalPolicy.EnsureSynchronized(
                blueprint,
                reviews));

        Assert.Null(exception);
    }

    // TEST 2
    [Fact]
    public void Newly_confirmed_requirement_blocks_approval()
    {
        var blueprint = CreateBlueprint();

        var reviews = new[]
        {
            CreateReview("REQ-001"),
            CreateReview("REQ-002")
        };

        Assert.Throws<RequirementReviewConflictException>(() =>
            ReviewBlueprintApprovalPolicy.EnsureSynchronized(
                blueprint,
                reviews));
    }

    // TESTS 3, 4, 5
    [Theory]
    [InlineData(RequirementReviewStatus.Suggested)]
    [InlineData(RequirementReviewStatus.Rejected)]
    [InlineData(RequirementReviewStatus.Deferred)]
    public void Changed_review_status_blocks_approval(
        RequirementReviewStatus status)
    {
        var blueprint = CreateBlueprint();

        var reviews = new[]
        {
            CreateReview("REQ-001", status)
        };

        Assert.Throws<RequirementReviewConflictException>(() =>
            ReviewBlueprintApprovalPolicy.EnsureSynchronized(
                blueprint,
                reviews));
    }

    // TEST 6
    [Fact]
    public void Missing_review_record_blocks_approval()
    {
        var blueprint = CreateBlueprint();

        Assert.Throws<RequirementReviewConflictException>(() =>
            ReviewBlueprintApprovalPolicy.EnsureSynchronized(
                blueprint,
                Array.Empty<ReviewedRequirement>()));
    }

    // TEST 7
    [Fact]
    public void Rejected_requirement_outside_blueprint_is_ignored()
    {
        var blueprint = CreateBlueprint();

        var reviews = new[]
        {
            CreateReview("REQ-001"),
            CreateReview(
                "REQ-002",
                RequirementReviewStatus.Rejected)
        };

        var exception = Record.Exception(() =>
            ReviewBlueprintApprovalPolicy.EnsureSynchronized(
                blueprint,
                reviews));

        Assert.Null(exception);
    }
}
