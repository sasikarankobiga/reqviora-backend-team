
using System.Text.Json;
using System.Text.RegularExpressions;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;

namespace Reqviora.Application.Features.UIGeneration.Blueprint.Validation;

public sealed record BlueprintValidationIssue(
    string Code, string Path, string Message);

public sealed record BlueprintValidationResult(
    IReadOnlyList<BlueprintValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

// Semantic/reference validation.
// This is NOT a JSON Schema 2020-12 engine.
public static class BlueprintContractValidator
{
    private static readonly HashSet<string> Origins =
        ["user", "document", "voice", "ai"];

    private static readonly HashSet<string> Statuses =
        ["suggested", "confirmed", "rejected"];

    private static readonly HashSet<string> Platforms =
        ["web", "mobile", "desktop"];

    private static readonly HashSet<string> ComponentTypes =
        [
            "Button", "TextInput", "Text", "Card",
            "List", "Container", "Image", "Dialog"
        ];

    private static readonly HashSet<string> ActionKinds =
        [
            "navigate", "submit", "update",
            "openDialog", "closeDialog"
        ];

    public static BlueprintValidationResult Validate(
        BlueprintDocument? document)
    {
        var issues = new List<BlueprintValidationIssue>();

        void Add(string code, string path, string message) =>
            issues.Add(new(code, path, message));

        if (document is null)
        {
            Add("NULL_BLUEPRINT", "$", "Blueprint is required.");
            return new(issues);
        }

        if (document.SchemaVersion != "1.0.0")
            Add("SCHEMA_VERSION", "$.schemaVersion",
                "Expected 1.0.0.");

        if (!MatchesId(document.BlueprintId, "BP"))
            Add("INVALID_ID", "$.blueprintId", "Invalid BP- ID.");

        if (document.ProjectId == Guid.Empty)
            Add("PROJECT_ID", "$.projectId",
                "Project ID is required.");

        if (document.Revision < 1)
            Add("REVISION", "$.revision",
                "Revision must be positive.");

        if (document.Lifecycle != "draft")
            Add("LIFECYCLE", "$.lifecycle",
                "Blueprint v1 must be draft.");

        if (document.TargetPlatforms is null ||
            document.TargetPlatforms.Count == 0)
        {
            Add("TARGET_PLATFORM", "$.targetPlatforms",
                "At least one target platform is required.");
        }
        else
        {
            var seen = new HashSet<string>(
                StringComparer.Ordinal);

            foreach (var platform in document.TargetPlatforms)
            {
                if (platform is null ||
                    !Platforms.Contains(platform))
                {
                    Add("TARGET_PLATFORM", "$.targetPlatforms",
                        "Unsupported target platform.");
                }

                if (platform is null || !seen.Add(platform))
                {
                    Add("DUPLICATE_REFERENCE",
                        "$.targetPlatforms", "Repeated platform.");
                }
            }
        }

        if (document.Requirements is null ||
            document.Features is null ||
            document.Screens is null ||
            document.Components is null ||
            document.Actions is null ||
            document.Navigation is null)
        {
            Add("MISSING_COLLECTION", "$",
                "All six Blueprint collections are required.");

            return new(issues);
        }

        var requirements = Index(
            document.Requirements, x => x.Id,
            "REQ", "requirements", issues);

        var features = Index(
            document.Features, x => x.Id,
            "FEAT", "features", issues);

        var screens = Index(
            document.Screens, x => x.Id,
            "SCR", "screens", issues);

        var components = Index(
            document.Components, x => x.Id,
            "CMP", "components", issues);

        var actions = Index(
            document.Actions, x => x.Id,
            "ACT", "actions", issues);

        var navigation = Index(
            document.Navigation, x => x.Id,
            "NAV", "navigation", issues);

        // 1. REQUIREMENT VALIDATION

        foreach (var requirement in requirements.Values)
        {
            var path = "$.requirements[" +
                requirement.Id + "]";

            if (string.IsNullOrWhiteSpace(requirement.Text))
                Add("REQUIREMENT_TEXT", path,
                    "Requirement text cannot be blank.");

            if (!Origins.Contains(requirement.Origin))
                Add("REQUIREMENT_ORIGIN", path,
                    "Unknown requirement origin.");

            if (!Statuses.Contains(requirement.Status))
                Add("REQUIREMENT_STATUS", path,
                    "Unknown requirement status.");

            if (requirement.Status == "confirmed")
            {
                if (string.IsNullOrWhiteSpace(
                        requirement.ConfirmedBy) ||
                    requirement.ConfirmedAt is null)
                {
                    Add("MISSING_CONFIRMATION", path,
                        "Human confirmation is required.");
                }
            }
            else if (
                requirement.ConfirmedBy is not null ||
                requirement.ConfirmedAt is not null)
            {
                Add("UNEXPECTED_CONFIRMATION", path,
                    "Unconfirmed requirements cannot have confirmation fields.");
            }

            if (requirement.Evidence is null ||
                requirement.Evidence.Count == 0)
            {
                Add("MISSING_EVIDENCE", path,
                    "At least one source evidence entry is required.");
            }
            else
            {
                foreach (var evidence in requirement.Evidence)
                {
                    if (evidence is null ||
                        string.IsNullOrWhiteSpace(evidence.SourceId) ||
                        string.IsNullOrWhiteSpace(evidence.Locator) ||
                        (evidence.Quote is not null &&
                         string.IsNullOrWhiteSpace(evidence.Quote)))
                    {
                        Add("INVALID_EVIDENCE", path,
                            "Evidence needs sourceId, locator and a nonblank optional quote.");
                    }
                }
            }
        }

        // 2. FEATURE VALIDATION

        foreach (var feature in features.Values)
        {
            var path = "$.features[" + feature.Id + "]";

            if (string.IsNullOrWhiteSpace(feature.Name))
                Add("FEATURE_NAME", path,
                    "Feature name cannot be blank.");

            CheckRefs(
                feature.RequirementIds,
                requirements,
                path + ".requirementIds",
                true,
                issues);

            foreach (var id in feature.RequirementIds ?? [])
            {
                if (id is not null &&
                    requirements.TryGetValue(id, out var r) &&
                    r.Status != "confirmed")
                {
                    Add("UNCONFIRMED_REQUIREMENT", path,
                        "Feature references an unconfirmed requirement.");
                }
            }
        }

        // 3. SCREEN VALIDATION

        foreach (var screen in screens.Values)
        {
            var path = "$.screens[" + screen.Id + "]";

            if (string.IsNullOrWhiteSpace(screen.Name))
                Add("SCREEN_NAME", path,
                    "Screen name cannot be blank.");

            CheckRefs(
                screen.FeatureIds,
                features,
                path + ".featureIds",
                true,
                issues);

            CheckRefs(
                screen.ComponentIds,
                components,
                path + ".componentIds",
                false,
                issues);

            foreach (var id in screen.ComponentIds ?? [])
            {
                if (id is not null &&
                    components.TryGetValue(id, out var c) &&
                    c.ScreenId != screen.Id)
                {
                    Add("SCREEN_COMPONENT_MISMATCH", path,
                        "Component belongs to another screen.");
                }
            }
        }

        // 4. COMPONENT VALIDATION

        foreach (var component in components.Values)
        {
            var path = "$.components[" + component.Id + "]";

            if (!ComponentTypes.Contains(component.Type))
                Add("UNSUPPORTED_COMPONENT", path,
                    "Component type is not in Blueprint v1.");

            if (component.Props is null || component.Props.Values.Any(
                value => value.ValueKind == JsonValueKind.Undefined))
                Add("COMPONENT_PROPS", path,
                    "Component props must be an object.");

            if (component.ScreenId is null ||
                !screens.TryGetValue(component.ScreenId,
                    out var owner))
            {
                Add("BROKEN_REFERENCE",
                    path + ".screenId",
                    "Component screen is missing.");
            }
            else if (owner.ComponentIds is null ||
                     !owner.ComponentIds.Contains(component.Id))
            {
                Add("COMPONENT_NOT_LISTED", path,
                    "Owning screen does not list this component.");
            }

            CheckRefs(
                component.ActionIds,
                actions,
                path + ".actionIds",
                false,
                issues);

            foreach (var id in component.ActionIds ?? [])
            {
                if (id is not null && actions.TryGetValue(id, out var action) &&
                    action.ComponentId != component.Id)
                {
                    Add("COMPONENT_ACTION_MISMATCH", path,
                        "Listed action belongs to a different component.");
                }
            }

            CheckRefs(
                component.ChildComponentIds,
                components,
                path + ".childComponentIds",
                false,
                issues);

            foreach (var id in component.ChildComponentIds ?? [])
            {
                if (id is null ||
                    !components.TryGetValue(id, out var child))
                    continue;

                if (child.ScreenId != component.ScreenId)
                    Add("CROSS_SCREEN_CHILD", path,
                        "Child belongs to a different screen.");
            }
        }

        // 5. ACTION VALIDATION

        foreach (var action in actions.Values)
        {
            var path = "$.actions[" + action.Id + "]";

            if (!ActionKinds.Contains(action.Kind))
                Add("ACTION_KIND", path,
                    "Unsupported action kind.");

            if (action.ComponentId is null ||
                !components.TryGetValue(
                    action.ComponentId, out var owner))
            {
                Add("BROKEN_REFERENCE",
                    path + ".componentId",
                    "Action component is missing.");
            }
            else if (owner.ActionIds is null ||
                     !owner.ActionIds.Contains(action.Id))
            {
                Add("ACTION_NOT_LISTED", path,
                    "Component does not list this action.");
            }

            if (action.Kind == "navigate")
            {
                if (action.NavigationId is null ||
                    !navigation.TryGetValue(action.NavigationId, out var route))
                {
                    Add("BROKEN_REFERENCE",
                        path + ".navigationId",
                        "Navigate action needs a valid NAV- reference.");
                }
                else if (route.ActionId != action.Id)
                {
                    Add("NAVIGATION_ACTION_MISMATCH", path,
                        "Referenced route belongs to a different action.");
                }
            }
            else if (action.NavigationId is not null)
            {
                Add("INVALID_ACTION_ROUTE", path,
                    "Only navigate may have navigationId.");
            }
        }

        // 6. NAVIGATION VALIDATION

        foreach (var route in navigation.Values)
        {
            var path = "$.navigation[" + route.Id + "]";

            if (route.SourceScreenId is null ||
                route.TargetScreenId is null ||
                !screens.ContainsKey(route.SourceScreenId) ||
                !screens.ContainsKey(route.TargetScreenId))
            {
                Add("BROKEN_REFERENCE", path,
                    "Navigation source/target screen is missing.");
            }

            if (route.ActionId is null ||
                !actions.TryGetValue(route.ActionId,
                    out var action))
            {
                Add("BROKEN_REFERENCE",
                    path + ".actionId",
                    "Navigation action is missing.");

                continue;
            }

            if (action.Kind != "navigate" ||
                action.NavigationId != route.Id)
            {
                Add("NAVIGATION_ACTION_MISMATCH", path,
                    "Route and navigate action disagree.");
            }

            if (action.ComponentId is not null &&
                components.TryGetValue(
                    action.ComponentId, out var component) &&
                component.ScreenId != route.SourceScreenId)
            {
                Add("NAVIGATION_SOURCE_MISMATCH", path,
                    "Route source is not the action component's screen.");
            }
        }

        // 7. COMPONENT CYCLE DETECTION

        var color = new Dictionary<string, int>(
            StringComparer.Ordinal);

        bool Visit(string id, int depth)
        {
            if (depth > 256)
            {
                Add("COMPONENT_DEPTH_LIMIT",
                    "$.components",
                    "Component nesting exceeds safe depth.");

                return false;
            }

            if (color.TryGetValue(id, out var state))
                return state == 1;

            color[id] = 1;

            foreach (var child in
                     components[id].ChildComponentIds ?? [])
            {
                if (child is not null &&
                    components.ContainsKey(child) &&
                    Visit(child, depth + 1))
                {
                    return true;
                }
            }

            color[id] = 2;
            return false;
        }

        if (components.Keys.Any(id => Visit(id, 0)))
        {
            Add("COMPONENT_CYCLE", "$.components",
                "Circular component children are not allowed.");
        }

        return new(issues);
    }

    private static Dictionary<string, T> Index<T>(
        IReadOnlyList<T> items,
        Func<T, string?> getId,
        string prefix,
        string section,
        List<BlueprintValidationIssue> issues)
        where T : class
    {
        var result = new Dictionary<string, T>(
            StringComparer.Ordinal);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (item is null)
            {
                issues.Add(new(
                    "NULL_ITEM",
                    "$." + section + "[" + i + "]",
                    "Null Blueprint entry."));

                continue;
            }

            var id = getId(item);

            if (!MatchesId(id, prefix))
            {
                issues.Add(new(
                    "INVALID_ID",
                    "$." + section + "[" + i + "].id",
                    "Invalid " + prefix + "- ID."));

                continue;
            }

            if (!result.TryAdd(id!, item))
            {
                issues.Add(new(
                    "DUPLICATE_ID",
                    "$." + section + "[" + i + "].id",
                    "Duplicate ID: " + id));
            }
        }

        return result;
    }

    internal static bool MatchesId(
        string? id,
        string prefix) =>
        id is not null &&
        Regex.IsMatch(
            id,
            @"\A" + prefix +
                @"-[A-Z0-9][A-Z0-9._-]*\z",
            RegexOptions.CultureInvariant);

    private static void CheckRefs<T>(
        IReadOnlyList<string>? ids,
        IReadOnlyDictionary<string, T> known,
        string path,
        bool requireOne,
        List<BlueprintValidationIssue> issues)
    {
        if (ids is null)
        {
            issues.Add(new(
                "MISSING_REFERENCE_LIST", path,
                "Reference list cannot be null."));

            return;
        }

        if (requireOne && ids.Count == 0)
        {
            issues.Add(new(
                "EMPTY_REFERENCE_LIST", path,
                "At least one reference is required."));
        }

        var seen = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                !known.ContainsKey(id))
            {
                issues.Add(new(
                    "BROKEN_REFERENCE", path,
                    "Referenced ID not found: " + id));
            }

            if (id is not null && !seen.Add(id))
            {
                issues.Add(new(
                    "DUPLICATE_REFERENCE", path,
                    "Duplicate reference: " + id));
            }
        }
    }
}
