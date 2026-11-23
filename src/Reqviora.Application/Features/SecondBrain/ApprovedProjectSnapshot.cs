using Reqviora.Application.Features.UIGeneration.Blueprint.Models;

namespace Reqviora.Application.Features.SecondBrain;

public sealed record ApprovedProjectSnapshot(
    Guid ProjectId,
    string ProjectName,
    string ProjectDescription,
    Guid ProjectOwnerId,
    Guid RevisionId,
    int DraftVersion,
    Guid? BasedOnRevisionId,
    Guid ApprovedBy,
    DateTimeOffset ApprovedAt,
    Guid ValidationRunId,
    DateTimeOffset ValidatedAt,
    string DraftJson,
    BlueprintDocument Blueprint);
