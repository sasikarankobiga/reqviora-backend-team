
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.WorkflowIntegration;
using Xunit;

namespace Reqviora.Application.Tests.WorkflowIntegration;

public sealed class ReviewBlueprintDecisionMapperTests
{
    private static readonly Guid Reviewer =
        Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

    private static readonly DateTimeOffset ConfirmedAt =
        new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    private static ReviewedRequirement Requirement(
        RequirementReviewStatus status) => new(
        "REQ-001",
        "User Login",
        "Email and password",
        status,
        2,
        [new CandidateEvidence(
            "DOC-001",
            "page 1",
            "User Login")],
        status == RequirementReviewStatus.Confirmed
            ? Reviewer : null,
        status == RequirementReviewStatus.Confirmed
            ? ConfirmedAt : null
    );

    private static BlueprintRequirement CurrentBlueprint() => new()
    {
        Id = "REQ-001",
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
    };

    [Fact]
    public void Accept_should_allow_generation()
    {
        var reviewed =
            Requirement(RequirementReviewStatus.Confirmed);

        var result = ReviewBlueprintDecisionMapper.Map(
            reviewed, null);

        Assert.True(result.IncludeInGeneration);
        Assert.True(result.RequiresRegeneration);
        Assert.Equal("confirmed", result.BlueprintStatus);
    }

    [Fact]
    public void Unchanged_confirmation_should_not_regenerate()
    {
        var result = ReviewBlueprintDecisionMapper.Map(
            Requirement(RequirementReviewStatus.Confirmed),
            CurrentBlueprint());

        Assert.True(result.IncludeInGeneration);
        Assert.False(result.RequiresRegeneration);
    }

    [Fact]
    public void Edit_should_invalidate_previous_confirmation()
    {
        var result = ReviewBlueprintDecisionMapper.Map(
            Requirement(RequirementReviewStatus.Suggested),
            CurrentBlueprint());

        Assert.False(result.IncludeInGeneration);
        Assert.True(result.RequiresRegeneration);
        Assert.Equal("suggested", result.BlueprintStatus);
    }

    [Fact]
    public void Reject_should_exclude_requirement()
    {
        var result = ReviewBlueprintDecisionMapper.Map(
            Requirement(RequirementReviewStatus.Rejected),
            CurrentBlueprint());

        Assert.False(result.IncludeInGeneration);
        Assert.True(result.RequiresRegeneration);
        Assert.Equal("rejected", result.BlueprintStatus);
    }

    [Fact]
    public void Defer_should_not_enter_blueprint_generation()
    {
        var result = ReviewBlueprintDecisionMapper.Map(
            Requirement(RequirementReviewStatus.Deferred),
            CurrentBlueprint());

        Assert.False(result.IncludeInGeneration);
        Assert.True(result.RequiresRegeneration);
        Assert.Null(result.BlueprintStatus);
    }

    [Fact]
    public void Confirmation_without_reviewer_should_fail()
    {
        var reviewed = Requirement(
            RequirementReviewStatus.Confirmed)
            with
            {
                ConfirmedBy = null
            };

        Assert.Throws<ArgumentException>(() =>
            ReviewBlueprintDecisionMapper.Map(
                reviewed, null));
    }

    [Fact]
    public void Changed_evidence_should_require_regeneration()
    {
        var current = CurrentBlueprint();

       var changed = current.withEvidenceForTest();

        var result = ReviewBlueprintDecisionMapper.Map(
            Requirement(RequirementReviewStatus.Confirmed),
            changed);

        Assert.True(result.RequiresRegeneration);
    }
}

internal static class BlueprintRequirementTestExtensions
{
    public static BlueprintRequirement withEvidenceForTest(
        this BlueprintRequirement value) => new()
    {
        Id = value.Id,
        Text = value.Text,
        Origin = value.Origin,
        Status = value.Status,
        ConfirmedBy = value.ConfirmedBy,
        ConfirmedAt = value.ConfirmedAt,
        Evidence =
        [
            new BlueprintEvidence
            {
                SourceId = "DOC-002",
                Locator = "page 2",
                Quote = "Changed evidence"
            }
        ]
    };
}
