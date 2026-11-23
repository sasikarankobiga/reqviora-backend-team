using System.Text.Json;
using System.Text.Json.Nodes;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.FeaturesAndScreens;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.WorkflowIntegration;
using Xunit;

namespace Reqviora.Application.Tests.WorkflowIntegration;

public sealed class KobigaWorkflowPolicyTests
{
    private static readonly Guid Reviewer = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

    [Fact]
    public void Candidate_is_staged_unconfirmed_and_only_accept_can_confirm()
    {
        var candidate = Candidate();
        var draft = KobigaWorkflowPolicy.StageCandidate(candidate);
        Assert.Equal(RequirementReviewStatus.Suggested, draft.Status);
        Assert.Null(draft.ConfirmedBy);
        var result = RequirementReviewService.Apply(draft,
            new RequirementReviewDecision(draft.Id, 1, RequirementDecisionKind.Accept,
                Reviewer, DateTimeOffset.UtcNow));
        Assert.Equal(RequirementReviewStatus.Confirmed, result.Requirement.Status);
        Assert.Equal(Reviewer, result.Requirement.ConfirmedBy);
    }

    [Fact]
    public void Candidate_with_duplicate_evidence_is_rejected()
    {
        var candidate = Candidate();
        Assert.Throws<ArgumentException>(() => KobigaWorkflowPolicy.StageCandidate(
            candidate with { Evidence = [candidate.Evidence[0], candidate.Evidence[0]] }));
    }

    [Fact]
    public void Edit_invalidates_confirmation_and_requires_fresh_accept()
    {
        var original = Confirmed();
        var edited = RequirementReviewService.Apply(original,
            new RequirementReviewDecision(original.Id, 2, RequirementDecisionKind.Edit,
                Reviewer, DateTimeOffset.UtcNow, "Updated", "Updated description"));
        Assert.Equal(RequirementReviewStatus.Suggested, edited.Requirement.Status);
        Assert.Null(edited.Requirement.ConfirmedBy);
        Assert.Throws<ArgumentException>(() =>
            KobigaWorkflowPolicy.ValidateConfirmedSelection([edited.Requirement.Id], [edited.Requirement]));
    }

    [Fact]
    public void Reject_and_defer_do_not_create_a_confirmation()
    {
        foreach (var kind in new[] { RequirementDecisionKind.Reject, RequirementDecisionKind.Defer })
        {
            var result = RequirementReviewService.Apply(KobigaWorkflowPolicy.StageCandidate(Candidate()),
                new RequirementReviewDecision("REQ-001", 1, kind, Reviewer, DateTimeOffset.UtcNow));
            Assert.NotEqual(RequirementReviewStatus.Confirmed, result.Requirement.Status);
            Assert.Null(result.Requirement.ConfirmedBy);
        }
    }

    [Fact]
    public void Clarification_does_not_approve_requirements()
    {
        var q = new ClarificationQuestion(Guid.NewGuid(), "REQ-001", "What roles?", null, 1, false);
        var answer = ClarificationService.Answer(q,
            new ClarificationAnswerRequest(q.Id, 1, "Owner and staff"));
        Assert.True(answer.IsResolved);
        Assert.Equal(2, answer.Revision);
        Assert.Throws<InvalidOperationException>(() => ClarificationService.Answer(answer,
            new ClarificationAnswerRequest(answer.Id, 1, "replay")));
        Assert.Equal(RequirementReviewStatus.Suggested,
            KobigaWorkflowPolicy.StageCandidate(Candidate()).Status);
    }

    [Fact]
    public void Blueprint_generation_is_based_on_confirmed_evidence_only()
    {
        var generator = new BlueprintDraftGenerator(new Day03StarterComponentRegistry());
        Assert.Throws<ArgumentException>(() => generator.Generate(GenerationRequest(
            KobigaWorkflowPolicy.StageCandidate(Candidate()))));
        var blueprint = generator.Generate(GenerationRequest(Confirmed()));
        Assert.Single(blueprint.Requirements);
        Assert.Equal("confirmed", blueprint.Requirements[0].Status);
        Assert.Equal("REQ-001", blueprint.Features[0].RequirementIds[0]);
    }

    [Fact]
    public void Regeneration_rejects_changed_requirements_and_protected_screen()
    {
        var current = new BlueprintDraftGenerator(new Day03StarterComponentRegistry())
            .Generate(GenerationRequest(Confirmed()));
        var proposed = CloneForNextRevision(current);
        Assert.True(KobigaWorkflowPolicy.RequirementsUnchanged(current, proposed));
        var service = new SafeRegenerationService(new Day03StarterComponentRegistry());
        var protectedRequest = new SafeRegenerationRequest(1, [], ["SCR-001"], []);
        var changedScreen = CloneForNextRevision(current, root =>
            root["screens"]![0]!["name"] = "AI changed protected screen");
        Assert.False(service.TryApply(current, changedScreen, protectedRequest).Applied);
        var changedRequirements = CloneForNextRevision(current, root =>
            root["requirements"]![0]!["text"] = "AI changed a human decision");
        Assert.False(KobigaWorkflowPolicy.RequirementsUnchanged(current, changedRequirements));
        Assert.True(service.TryApply(current, proposed, new SafeRegenerationRequest(1, [], [], [])).Applied);
        Assert.False(service.TryApply(current, proposed, new SafeRegenerationRequest(0, [], [], [])).Applied);
    }

    private static RequirementCandidate Candidate() => new(
        "REQ-001", "User can login", "Email and password", "en", 0.92m,
        [new CandidateEvidence("SRC-01", "page 1", "User can login")]);

    private static ReviewedRequirement Confirmed() => new(
        "REQ-001", "User can login", "Email and password",
        RequirementReviewStatus.Confirmed, 2,
        [new CandidateEvidence("SRC-01", "page 1", "User can login")],
        Reviewer, DateTimeOffset.UtcNow);

    private static BlueprintGenerationRequest GenerationRequest(ReviewedRequirement requirement) => new(
        Guid.Parse("11445678-9012-3456-7890-123456789012"), "BP-TEST-001", 1,
        [requirement],
        [new ProposedFeature("FEAT-001", "Authentication", ["REQ-001"])],
        [new ProposedScreen("SCR-001", "Login", ["FEAT-001"])],
        [new ProposedComponent("CMP-001", "SCR-001", "Button", "default")],
        [], []);

    private static BlueprintDocument CloneForNextRevision(
        BlueprintDocument document, Action<JsonObject>? edit = null)
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(document))!.AsObject();
        json["revision"] = document.Revision + 1;
        edit?.Invoke(json);
        return JsonSerializer.Deserialize<BlueprintDocument>(json.ToJsonString())!;
    }
}