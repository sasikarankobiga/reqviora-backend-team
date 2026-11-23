
using System.Text.Json;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.UIGeneration.Blueprint.Validation;

namespace Reqviora.Application.Features.FeaturesAndScreens;

public sealed record ProposedFeature(
    string Id,
    string Name,
    IReadOnlyList<string> RequirementIds
);

public sealed record ProposedScreen(
    string Id,
    string Name,
    IReadOnlyList<string> FeatureIds
);

public sealed record ProposedComponent(
    string Id,
    string ScreenId,
    string Type,
    string Variant
);

public sealed record ProposedAction(
    string Id,
    string ComponentId,
    string Kind,
    string? NavigationId
);

public sealed record ProposedNavigation(
    string Id,
    string SourceScreenId,
    string TargetScreenId,
    string ActionId
);

public sealed record BlueprintGenerationRequest(
    Guid ProjectId,
    string BlueprintId,
    int Revision,
    IReadOnlyList<ReviewedRequirement> Requirements,
    IReadOnlyList<ProposedFeature> Features,
    IReadOnlyList<ProposedScreen> Screens,
    IReadOnlyList<ProposedComponent> Components,
    IReadOnlyList<ProposedAction> Actions,
    IReadOnlyList<ProposedNavigation> Navigation
);

public interface IDesignComponentRegistry
{
    bool Supports(string type, string variant);
}

// Temporary local starter registry, not the final registry service.

public sealed class Day03StarterComponentRegistry
    : IDesignComponentRegistry
{
    private static readonly Dictionary<string, string[]> Allowed =
        new(StringComparer.Ordinal)
        {
            ["Button"] = ["default", "filled", "outlined", "text"],
            ["Text"] = ["default"],
            ["TextInput"] = ["default", "outlined", "filled"],
            ["Card"] = ["default", "outlined", "elevated"],
            ["List"] = ["default"],
            ["Container"] = ["default"],
            ["Image"] = ["default"],
            ["Dialog"] = ["default"]
        };

    public bool Supports(string type, string variant) =>
        type is not null &&
        variant is not null &&
        Allowed.TryGetValue(type, out var variants) &&
        variants.Contains(variant, StringComparer.Ordinal);
}

public interface IBlueprintDraftGenerator
{
    BlueprintDocument Generate(BlueprintGenerationRequest request);
}

public sealed class BlueprintDraftGenerator(
    IDesignComponentRegistry registry) : IBlueprintDraftGenerator
{
    public BlueprintDocument Generate(
        BlueprintGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ProjectId == Guid.Empty ||
            request.Revision < 1 ||
            string.IsNullOrWhiteSpace(request.BlueprintId) ||
            !BlueprintContractValidator.MatchesId(request.BlueprintId, "BP"))
        {
            throw new ArgumentException(
                "Invalid Blueprint identity or revision.");
        }

        if (request.Requirements is null ||
            request.Features is null ||
            request.Screens is null ||
            request.Components is null ||
            request.Actions is null ||
            request.Navigation is null)
        {
            throw new ArgumentException(
                "Blueprint collections cannot be null.");
        }

        if (request.Requirements.Count == 0 ||
            request.Features.Count == 0 ||
            request.Screens.Count == 0)
        {
            throw new ArgumentException(
                "Confirmed requirements, features and screens are required.");
        }

        if (request.Requirements.Any(requirement =>
            requirement is null ||
            requirement.Status != RequirementReviewStatus.Confirmed ||
            requirement.ConfirmedBy is null ||
            requirement.ConfirmedBy == Guid.Empty ||
            requirement.ConfirmedAt is null ||
            requirement.ConfirmedAt == default(DateTimeOffset) ||
            string.IsNullOrWhiteSpace(requirement.Title) ||
            requirement.Evidence is null ||
            requirement.Evidence.Count == 0 ||
            requirement.Evidence.Any(evidence => evidence is null)))
        {
            throw new ArgumentException(
                "Only explicitly confirmed requirements with source evidence can generate a Blueprint.");
        }

        if (request.Features.Any(feature => feature?.RequirementIds is null) ||
            request.Screens.Any(screen => screen?.FeatureIds is null) ||
            request.Actions.Any(action => action is null) ||
            request.Navigation.Any(route => route is null))
        {
            throw new ArgumentException("Incomplete Blueprint proposal.");
        }

        if (request.Components.Any(component =>
            component is null || !registry.Supports(component.Type, component.Variant)))
        {
            throw new ArgumentException("Unsupported Blueprint component or variant.");
        }

        var componentsByScreen = request.Components.ToLookup(component => component.ScreenId);
        var actionsByComponent = request.Actions.ToLookup(action => action.ComponentId);
        var blueprint = new BlueprintDocument
        {
            SchemaVersion = "1.0.0",
            BlueprintId = request.BlueprintId,
            ProjectId = request.ProjectId,
            Revision = request.Revision,
            Lifecycle = "draft",
            TargetPlatforms = ["web"],

            Requirements = request.Requirements.Select(r =>
                new BlueprintRequirement
                {
                    Id = r.Id,
                    Text = r.Title + "\n" + r.Description,
                    Origin = "ai",
                    Status = "confirmed",
                    ConfirmedBy = r.ConfirmedBy!.Value.ToString(),
                    ConfirmedAt = r.ConfirmedAt,
                    Evidence = r.Evidence.Select(e =>
                        new BlueprintEvidence
                        {
                            SourceId = e.SourceId,
                            Locator = e.Locator,
                            Quote = e.Quote
                        }).ToList()
                }).ToList(),

            Features = request.Features.Select(f =>
                new BlueprintFeature
                {
                    Id = f.Id,
                    Name = f.Name,
                    RequirementIds = f.RequirementIds.ToList()
                }).ToList(),

            Screens = request.Screens.Select(s =>
                new BlueprintScreen
                {
                    Id = s.Id,
                    Name = s.Name,
                    FeatureIds = s.FeatureIds.ToList(),
                    ComponentIds = componentsByScreen[s.Id]
                        .Select(c => c.Id).ToList()
                }).ToList(),

            Components = request.Components.Select(c =>
                new BlueprintComponent
                {
                    Id = c.Id,
                    ScreenId = c.ScreenId,
                    Type = c.Type,
                    Props = new Dictionary<string, JsonElement>
                    {
                        ["variant"] =
                            JsonSerializer.SerializeToElement(c.Variant)
                    },
                    ActionIds = actionsByComponent[c.Id]
                        .Select(a => a.Id).ToList()
                }).ToList(),

            Actions = request.Actions.Select(a =>
                new BlueprintAction
                {
                    Id = a.Id,
                    ComponentId = a.ComponentId,
                    Kind = a.Kind,
                    NavigationId = a.NavigationId
                }).ToList(),

            Navigation = request.Navigation.Select(n =>
                new BlueprintNavigation
                {
                    Id = n.Id,
                    SourceScreenId = n.SourceScreenId,
                    TargetScreenId = n.TargetScreenId,
                    ActionId = n.ActionId
                }).ToList()
        };

        var validation = BlueprintContractValidator.Validate(blueprint);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Issues[0].Message);

        return blueprint;
    }
}
