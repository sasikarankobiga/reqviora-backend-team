using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;

namespace Reqviora.Api.Features.ProjectWorkflow;

public sealed record SaveDraftRequest(
    [Required] JsonElement? Draft,
    Guid? ExpectedConcurrencyToken);

public sealed record SaveBlueprintRequest(
    [Required] JsonElement? Draft,
    [Required] BlueprintDocument Blueprint,
    Guid? ExpectedConcurrencyToken);

public sealed record DraftTokenRequest(Guid ExpectedConcurrencyToken);

public sealed record ApproveDraftRequest(
    Guid ExpectedConcurrencyToken,
    Guid ValidationRunId);

public sealed record ProjectDraftResponse(
    Guid Id,
    Guid ProjectId,
    JsonElement Draft,
    int Version,
    Guid ConcurrencyToken,
    DateTime UpdatedAtUtc,
    Guid? ApprovedRevisionId,
    Guid? BasedOnRevisionId);

public sealed record BlueprintVersionResponse(
    Guid DraftVersionId,
    int Version,
    DateTimeOffset SavedAt,
    Guid? BasedOnRevisionId,
    BlueprintDocument? Blueprint);

public sealed record ValidationFindingResponse(
    int Ordinal,
    string Code,
    string Path,
    string Message);

public sealed record ValidationRunResponse(
    Guid Id,
    Guid DraftVersionId,
    bool Passed,
    DateTimeOffset ValidatedAt,
    IReadOnlyList<ValidationFindingResponse> Findings);

public sealed record ApprovedBlueprintResponse(
    Guid RevisionId,
    int DraftVersion,
    Guid? BasedOnRevisionId,
    Guid ApprovedBy,
    DateTimeOffset ApprovedAt,
    Guid ValidationRunId,
    BlueprintDocument Blueprint);

public sealed record RequirementEvidenceResponse(
    string SourceId,
    string Locator,
    string? Quote);

public sealed record RequirementResponse(
    string Id,
    string Text,
    string Origin,
    string Status,
    string? ConfirmedBy,
    DateTimeOffset? ConfirmedAt,
    IReadOnlyList<RequirementEvidenceResponse> Evidence);

public sealed record FeatureResponse(
    string Id,
    string Name,
    IReadOnlyList<string> RequirementIds);

public sealed record ScreenResponse(
    string Id,
    string Name,
    IReadOnlyList<string> FeatureIds,
    IReadOnlyList<string> ComponentIds);

public sealed record NavigationResponse(
    string Id,
    string SourceScreenId,
    string TargetScreenId,
    string ActionId);

public sealed record ApprovalResponse(
    Guid RevisionId,
    Guid ProjectId,
    Guid ApprovedBy,
    DateTimeOffset ApprovedAt,
    Guid ValidationRunId,
    int DraftVersion);

public sealed record ExportMetadataResponse(
    Guid ExportId,
    Guid ApprovedRevisionId,
    string GeneratorVersion,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    string ArchiveSha256,
    long ArchiveBytes);

public sealed record WorkspaceResponse(
    Guid Id,
    string Name,
    Guid OwnerUserId,
    int ProjectCount);

public sealed record ProjectOverviewResponse(
    Guid ProjectId,
    string Name,
    string Description,
    Guid OwnerId,
    Guid? WorkspaceId,
    int? CurrentDraftVersion,
    Guid? DraftConcurrencyToken,
    Guid? ApprovedRevisionId,
    int RequirementCount,
    int FeatureCount,
    int ScreenCount,
    int NavigationCount);
