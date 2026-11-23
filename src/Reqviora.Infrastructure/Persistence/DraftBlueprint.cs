using System.Text.Json;
using System.Text.Json.Serialization;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.UIGeneration.Blueprint.Validation;
using Reqviora.Domain.Entities;

namespace Reqviora.Infrastructure.Persistence;

internal static class DraftBlueprint
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static IReadOnlyList<BlueprintValidationIssue> Validate(BlueprintDocument? blueprint)
    {
        var result = BlueprintContractValidator.Validate(blueprint);
        if (!result.IsValid)
            return result.Issues;

        var ids = blueprint!.Requirements.Select(x => x.Id)
            .Concat(blueprint.Features.Select(x => x.Id))
            .Concat(blueprint.Screens.Select(x => x.Id))
            .Concat(blueprint.Components.Select(x => x.Id))
            .Concat(blueprint.Actions.Select(x => x.Id))
            .Concat(blueprint.Navigation.Select(x => x.Id));
        return ids.Any(id => id.Length > DraftPersistenceConfiguration.StableIdLength)
            ? [new("ID_TOO_LONG", "$", $"Persisted IDs can contain up to {DraftPersistenceConfiguration.StableIdLength} characters.")]
            : [];
    }

    internal static IReadOnlyList<BlueprintValidationIssue> ValidateStored(string? blueprintJson)
    {
        if (blueprintJson is null)
            return Validate(null);

        try
        {
            return Validate(BlueprintJsonContract.Parse(blueprintJson));
        }
        catch (ArgumentException exception)
        {
            return [new("BLUEPRINT_SCHEMA", "$", exception.Message)];
        }
    }

    internal static void AddGraph(ReqvioraDbContext db, Guid versionId, BlueprintDocument blueprint)
    {
        db.DraftRequirements.AddRange(blueprint.Requirements.Select(r =>
            new DraftRequirement(versionId, r.Id, r.Text, r.Origin, r.Status)));
        db.RequirementSourceEvidence.AddRange(blueprint.Requirements.SelectMany(r => r.Evidence.Select(
            (evidence, ordinal) => new RequirementSourceEvidence(
                versionId, r.Id, ordinal, evidence.SourceId, evidence.Locator, evidence.Quote))));

        db.DraftFeatures.AddRange(blueprint.Features.Select(f => new DraftFeature(versionId, f.Id, f.Name)));
        db.DraftFeatureRequirements.AddRange(blueprint.Features.SelectMany(f => f.RequirementIds.Select(
            requirement => new DraftFeatureRequirement(versionId, f.Id, requirement))));

        db.DraftScreens.AddRange(blueprint.Screens.Select(s => new DraftScreen(versionId, s.Id, s.Name)));
        db.DraftScreenFeatures.AddRange(blueprint.Screens.SelectMany(s => s.FeatureIds.Select(
            feature => new DraftScreenFeature(versionId, s.Id, feature))));

        var screenByComponent = blueprint.Components.ToDictionary(c => c.Id, c => c.ScreenId, StringComparer.Ordinal);
        db.ScreenActions.AddRange(blueprint.Actions.Select(a =>
            new ScreenAction(versionId, a.Id, screenByComponent[a.ComponentId], a.ComponentId, a.Kind)));
        db.NavigationEdges.AddRange(blueprint.Navigation.Select(n =>
            new NavigationEdge(versionId, n.Id, n.SourceScreenId, n.TargetScreenId, n.ActionId)));
    }
}
