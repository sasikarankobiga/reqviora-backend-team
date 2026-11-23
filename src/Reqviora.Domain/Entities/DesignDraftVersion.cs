using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class DesignDraftVersion : BaseEntity
{
    public Guid ProjectDraftId { get; init; }
    public Guid ProjectId { get; init; }
    public int Version { get; init; }
    public string DraftJson { get; init; } = "{}";
    public string? BlueprintJson { get; init; }
    public Guid? BasedOnRevisionId { get; init; }
    public DateTimeOffset SavedAt { get; init; }
}

public interface IDraftDesignRow
{
    Guid DraftVersionId { get; }
}

public sealed record DraftRequirement(
    Guid DraftVersionId, string Id, string Text, string Origin, string Status) : IDraftDesignRow;

public sealed record RequirementSourceEvidence(
    Guid DraftVersionId, string RequirementId, int Ordinal, string SourceId, string Locator, string? Quote) : IDraftDesignRow;

public sealed record DraftFeature(Guid DraftVersionId, string Id, string Name) : IDraftDesignRow;

public sealed record DraftFeatureRequirement(Guid DraftVersionId, string FeatureId, string RequirementId) : IDraftDesignRow;

public sealed record DraftScreen(Guid DraftVersionId, string Id, string Name) : IDraftDesignRow;

public sealed record DraftScreenFeature(Guid DraftVersionId, string ScreenId, string FeatureId) : IDraftDesignRow;

public sealed record ScreenAction(
    Guid DraftVersionId, string Id, string ScreenId, string ComponentId, string Kind) : IDraftDesignRow;

public sealed record NavigationEdge(
    Guid DraftVersionId, string Id, string SourceScreenId, string TargetScreenId, string ActionId) : IDraftDesignRow;
