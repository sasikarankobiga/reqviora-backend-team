using System.Text.Json;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.FeaturesAndScreens;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.UIGeneration.Blueprint.Validation;

namespace Reqviora.Application.Features.UIGeneration;

public sealed record SafeRegenerationRequest(
    int ExpectedRevision,
    IReadOnlyCollection<string> ProtectedFeatureIds,
    IReadOnlyCollection<string> ProtectedScreenIds,
    IReadOnlyCollection<string> ProtectedComponentIds
);

public sealed record SafeRegenerationResult(
    bool Applied,
    string Reason,
    BlueprintDocument Blueprint
);

public sealed class SafeRegenerationService(IDesignComponentRegistry registry)
{
    public SafeRegenerationResult TryApplyAiOutput(
        BlueprintDocument current,
        SafeRegenerationRequest request,
        AiProviderRequest aiRequest,
        AiProviderResponse? response)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(aiRequest);

        if (aiRequest.ProjectId != current.ProjectId ||
            aiRequest.Operation != AiOperation.ScreenSuggestion ||
            (response is not null && response.RequestId != aiRequest.RequestId))
        {
            return new(false, "AI_RESPONSE_MISMATCH", current);
        }

        if (response is null)
            return new(false, "AI_UNAVAILABLE", current);

        BlueprintDocument proposal;
        try
        {
            AiContractValidator.Validate(aiRequest);
            AiContractValidator.Validate(response);

            if (response.Status == AiRunStatus.Unavailable)
                return new(false, "AI_UNAVAILABLE", current);

            if (response.Status == AiRunStatus.Failed)
                return new(false, "AI_FAILED", current);

            proposal = BlueprintJsonContract.Parse(response.StructuredJson!);
        }
        catch (ArgumentException)
        {
            return new(false, "INVALID_AI_OUTPUT", current);
        }

        return TryApply(current, proposal, request);
    }

    public SafeRegenerationResult TryApply(
        BlueprintDocument current,
        BlueprintDocument? proposed,
        SafeRegenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);

        SafeRegenerationResult Reject(string reason) => new(false, reason, current);

        if (request.ProtectedFeatureIds is null ||
            request.ProtectedScreenIds is null ||
            request.ProtectedComponentIds is null)
            return Reject("INVALID_PROTECTION_POLICY");

        if (current.Revision != request.ExpectedRevision)
            return Reject("REVISION_CONFLICT");

        if (current.Lifecycle != "draft")
            return Reject("IMMUTABLE_REVISION");

        if (proposed is null)
            return Reject("INVALID_GENERATED_GRAPH");

        if (proposed.Lifecycle != "draft")
            return Reject("INVALID_PROPOSED_LIFECYCLE");

        if (current.Revision == int.MaxValue ||
            proposed.Revision != current.Revision + 1 ||
            proposed.BlueprintId != current.BlueprintId ||
            proposed.ProjectId != current.ProjectId ||
            proposed.SchemaVersion != current.SchemaVersion)
            return Reject("BLUEPRINT_IDENTITY_MISMATCH");

        if (!BlueprintContractValidator.Validate(current).IsValid)
            return Reject("INVALID_CURRENT_BLUEPRINT");

        if (!BlueprintContractValidator.Validate(proposed).IsValid ||
            proposed.Features.Count == 0 ||
            proposed.Screens.Count == 0 ||
            !proposed.TargetPlatforms.SequenceEqual(["web"]) ||
            proposed.Components.Any(component => !Supported(component)))
            return Reject("INVALID_GENERATED_GRAPH");

        // Confirmations come from the existing revision and include their source evidence.
        var oldConfirmed = current.Requirements
            .Where(requirement => requirement.Status == "confirmed")
            .ToDictionary(requirement => requirement.Id, StringComparer.Ordinal);
        var newConfirmed = proposed.Requirements
            .Where(requirement => requirement.Status == "confirmed")
            .ToDictionary(requirement => requirement.Id, StringComparer.Ordinal);

        if (oldConfirmed.Count != newConfirmed.Count || oldConfirmed.Any(entry =>
            !newConfirmed.TryGetValue(entry.Key, out var confirmed) || !Same(entry.Value, confirmed)))
            return Reject("CONFIRMATION_CHANGED");

        if (!ProtectedUnchanged(current.Features, proposed.Features, request.ProtectedFeatureIds, item => item.Id))
            return Reject("PROTECTED_FEATURE_CHANGED");

        if (!ProtectedUnchanged(current.Screens, proposed.Screens, request.ProtectedScreenIds, item => item.Id))
            return Reject("PROTECTED_SCREEN_CHANGED");

        if (!ProtectedUnchanged(current.Components, proposed.Components, request.ProtectedComponentIds, item => item.Id) ||
            !ProtectedActionsUnchanged(current, proposed, request.ProtectedComponentIds))
            return Reject("PROTECTED_COMPONENT_CHANGED");

        return new(true, "APPLIED_AS_NEW_DRAFT", proposed);
    }

    private bool Supported(BlueprintComponent component) =>
        component.Props.TryGetValue("variant", out var variant) &&
        variant.ValueKind == JsonValueKind.String &&
        registry.Supports(component.Type, variant.GetString()!);

    private static bool ProtectedActionsUnchanged(
        BlueprintDocument current,
        BlueprintDocument proposed,
        IReadOnlyCollection<string> protectedComponentIds)
    {
        if (protectedComponentIds.Count == 0)
            return true;

        var componentIds = protectedComponentIds.ToHashSet(StringComparer.Ordinal);
        var actions = current.Actions.Where(action => componentIds.Contains(action.ComponentId)).ToArray();

        return ProtectedUnchanged(current.Actions, proposed.Actions, actions.Select(action => action.Id), item => item.Id) &&
            ProtectedUnchanged(current.Navigation, proposed.Navigation, actions.Select(action => action.NavigationId).OfType<string>(), item => item.Id);
    }

    private static bool Same<T>(T left, T right) =>
        JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left), JsonSerializer.SerializeToElement(right));

    private static bool ProtectedUnchanged<T>(
        IReadOnlyCollection<T> oldItems,
        IReadOnlyCollection<T> newItems,
        IEnumerable<string> protectedIds,
        Func<T, string> idSelector)
    {
        foreach (var id in protectedIds)
        {
            var previous = oldItems.FirstOrDefault(item => idSelector(item) == id);
            var next = newItems.FirstOrDefault(item => idSelector(item) == id);
            if (previous is null || next is null || !Same(previous, next))
                return false;
        }

        return true;
    }
}
