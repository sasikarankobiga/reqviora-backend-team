using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reqviora.Application.Features.UIGeneration.Blueprint.Models;

public sealed class BlueprintDocument
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "1.0.0";

    [JsonPropertyName("blueprintId")]
    public string BlueprintId { get; init; } = string.Empty;

    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; init; }

    [JsonPropertyName("revision")]
    public int Revision { get; init; }

    [JsonPropertyName("lifecycle")]
    public string Lifecycle { get; init; } = "draft";

    [JsonPropertyName("targetPlatforms")]
    public List<string> TargetPlatforms { get; init; } = [];

    [JsonPropertyName("requirements")]
    public List<BlueprintRequirement> Requirements { get; init; } = [];

    [JsonPropertyName("features")]
    public List<BlueprintFeature> Features { get; init; } = [];

    [JsonPropertyName("screens")]
    public List<BlueprintScreen> Screens { get; init; } = [];

    [JsonPropertyName("components")]
    public List<BlueprintComponent> Components { get; init; } = [];

    [JsonPropertyName("actions")]
    public List<BlueprintAction> Actions { get; init; } = [];

    [JsonPropertyName("navigation")]
    public List<BlueprintNavigation> Navigation { get; init; } = [];
}

public sealed class BlueprintRequirement
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("origin")]
    public string Origin { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("confirmedBy")]
    public string? ConfirmedBy { get; init; }

    [JsonPropertyName("confirmedAt")]
    public DateTimeOffset? ConfirmedAt { get; init; }

    [JsonPropertyName("evidence")]
    public List<BlueprintEvidence> Evidence { get; init; } = [];
}

public sealed class BlueprintEvidence
{
    [JsonPropertyName("sourceId")]
    public string SourceId { get; init; } = string.Empty;

    [JsonPropertyName("locator")]
    public string Locator { get; init; } = string.Empty;

    [JsonPropertyName("quote")]
    public string? Quote { get; init; }
}

public sealed class BlueprintFeature
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("requirementIds")]
    public List<string> RequirementIds { get; init; } = [];
}

public sealed class BlueprintScreen
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("featureIds")]
    public List<string> FeatureIds { get; init; } = [];

    [JsonPropertyName("componentIds")]
    public List<string> ComponentIds { get; init; } = [];
}

public sealed class BlueprintComponent
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("screenId")]
    public string ScreenId { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("props")]
    public Dictionary<string, JsonElement> Props { get; init; } = [];

    [JsonPropertyName("actionIds")]
    public List<string> ActionIds { get; init; } = [];

    [JsonPropertyName("childComponentIds")]
    public List<string> ChildComponentIds { get; init; } = [];
}

public sealed class BlueprintAction
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("componentId")]
    public string ComponentId { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("navigationId")]
    public string? NavigationId { get; init; }
}

public sealed class BlueprintNavigation
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("sourceScreenId")]
    public string SourceScreenId { get; init; } = string.Empty;

    [JsonPropertyName("targetScreenId")]
    public string TargetScreenId { get; init; } = string.Empty;

    [JsonPropertyName("actionId")]
    public string ActionId { get; init; } = string.Empty;
}
