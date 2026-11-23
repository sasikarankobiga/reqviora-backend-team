using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.SecondBrain;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Authorization;

namespace Reqviora.Api.Features.ProjectWorkflow;

internal static class ProjectApiAccess
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static Guid RequireUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("A verified user identity is required.");
    }

    internal static async Task RequireOwnerAsync(
        ReqvioraDbContext db,
        Guid projectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var allowed = await db.Projects.AsNoTracking().AnyAsync(
            project => project.Id == projectId && project.OwnerId == userId,
            cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Project owner access is required.");
    }

    internal static async Task RequireReadAsync(
        ReqvioraDbContext db, Guid projectId, Guid userId, CancellationToken cancellationToken) =>
        _ = await ProjectOwnerAccess.RequireReadAsync(db, projectId, userId, cancellationToken);

    internal static async Task RequireWriteAsync(
        ReqvioraDbContext db, Guid projectId, Guid userId, CancellationToken cancellationToken) =>
        _ = await ProjectOwnerAccess.RequireWriteAsync(db, projectId, userId, cancellationToken);

    internal static async Task<DesignDraftVersion?> FindCurrentVersionAsync(
        ReqvioraDbContext db,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var draft = await db.ProjectDrafts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProjectId == projectId, cancellationToken);
        if (draft is null)
            return null;

        return await db.DesignDraftVersions.AsNoTracking().SingleAsync(
            version => version.ProjectDraftId == draft.Id && version.Version == draft.Version,
            cancellationToken);
    }

    internal static BlueprintDocument? ReadBlueprint(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<BlueprintDocument>(json, Json);

    internal static ApprovedProjectSnapshot ReadSnapshot(ApprovedRevision revision) =>
        JsonSerializer.Deserialize<ApprovedProjectSnapshot>(revision.SnapshotJson, Json)
        ?? throw new InvalidDataException("The approved snapshot is unavailable.");

    internal static JsonElement ReadDraft(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    internal static ProjectDraftResponse ToResponse(ProjectDraft draft) => new(
        draft.Id,
        draft.ProjectId,
        ReadDraft(draft.DraftJson),
        draft.Version,
        draft.ConcurrencyToken,
        draft.UpdatedAtUtc,
        draft.ApprovedRevisionId,
        draft.BasedOnRevisionId);

    internal static BlueprintVersionResponse ToResponse(DesignDraftVersion version) => new(
        version.Id,
        version.Version,
        version.SavedAt,
        version.BasedOnRevisionId,
        ReadBlueprint(version.BlueprintJson));

    internal static ApprovedBlueprintResponse ToResponse(ApprovedRevision revision)
    {
        var snapshot = ReadSnapshot(revision);
        return new(
            revision.Id,
            snapshot.DraftVersion,
            snapshot.BasedOnRevisionId,
            revision.ApprovedBy,
            revision.ApprovedAt,
            revision.ValidationRunId,
            snapshot.Blueprint);
    }

    internal static ApprovalResponse ToApprovalResponse(ApprovedRevision revision, int draftVersion) => new(
        revision.Id,
        revision.ProjectId,
        revision.ApprovedBy,
        revision.ApprovedAt,
        revision.ValidationRunId,
        draftVersion);

    internal static ExportMetadataResponse ToResponse(ExportRecord export) => new(
        export.Id,
        export.ApprovedRevisionId,
        export.GeneratorVersion,
        export.CreatedBy,
        export.CreatedAt,
        export.ArchiveSha256,
        export.Archive.LongLength);
}
