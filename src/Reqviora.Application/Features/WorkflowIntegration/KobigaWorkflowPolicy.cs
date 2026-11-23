using System.Text.Json;
using System.Text.RegularExpressions;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.Documents;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;

namespace Reqviora.Application.Features.WorkflowIntegration;

/// <summary>Shared policy for candidate staging, human review and safe regeneration.</summary>
public static class KobigaWorkflowPolicy
{
    private static readonly Regex RequirementIdPattern = new(
        @"\AREQ-[A-Z0-9][A-Z0-9._-]*\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // Staging never confirms an AI candidate. A separate explicit human review is required.
    public static ReviewedRequirement StageCandidate(RequirementCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Id is null || candidate.Id.Length > 100 ||
            !RequirementIdPattern.IsMatch(candidate.Id) ||
            string.IsNullOrWhiteSpace(candidate.Title) || candidate.Title.Length > 200 ||
            candidate.Description is null || candidate.Description.Length > 4000 ||
            !InputStudioContractValidator.IsSupportedLanguage(candidate.LanguageCode) ||
            candidate.Confidence is < 0 or > 1 ||
            candidate.Evidence is null || candidate.Evidence.Count is < 1 or > 10 ||
            candidate.Evidence.Any(e => e is null ||
                string.IsNullOrWhiteSpace(e.SourceId) ||
                string.IsNullOrWhiteSpace(e.Locator) ||
                string.IsNullOrWhiteSpace(e.Quote)) ||
            candidate.Evidence.Distinct().Count() != candidate.Evidence.Count)
            throw new ArgumentException("Invalid requirement candidate or evidence.", nameof(candidate));
        return RequirementReviewService.CreateCandidate(candidate);
    }

    // UI design generation must never confirm, edit, reject, defer, or erase requirements.
    public static bool RequirementsUnchanged(BlueprintDocument current, BlueprintDocument proposed)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(proposed);
        var oldItems = current.Requirements.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        var newItems = proposed.Requirements.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        return JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(oldItems),
            JsonSerializer.SerializeToElement(newItems));
    }

    public static void ValidateConfirmedSelection(
        IReadOnlyList<string> ids,
        IReadOnlyList<ReviewedRequirement> requirements)
    {
        if (ids is null || requirements is null || ids.Count == 0 || ids.Count > 200 ||
            ids.Any(id => string.IsNullOrWhiteSpace(id)) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Count ||
            requirements.Count != ids.Count ||
            requirements.Any(r => r is null || r.Status != RequirementReviewStatus.Confirmed ||
                r.ConfirmedBy is null || r.ConfirmedBy == Guid.Empty || r.ConfirmedAt is null))
            throw new ArgumentException("Only distinct, explicitly confirmed requirements may generate the Blueprint.");
    }
}