using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.SecondBrain;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Authorization;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class ProjectDraftService(ReqvioraDbContext db)
{
    public Task<ProjectDraft> SaveAsync(
        Guid projectId, Guid actorId, string draftJson, Guid? expectedToken, CancellationToken cancellationToken = default) =>
        SaveCoreAsync(projectId, actorId, draftJson, null, expectedToken, cancellationToken);

    public Task<ProjectDraft> SaveDesignAsync(
        Guid projectId, Guid actorId, string draftJson, BlueprintDocument blueprint, Guid? expectedToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        if (blueprint.ProjectId != projectId)
            throw new ArgumentException("The Blueprint must belong to the project.", nameof(blueprint));

        return SaveCoreAsync(projectId, actorId, draftJson, blueprint, expectedToken, cancellationToken);
    }

    public async Task<ProjectDraft?> ResumeAsync(Guid projectId, Guid actorId, CancellationToken cancellationToken = default)
    {
        await ProjectOwnerAccess.RequireReadAsync(db, projectId, actorId, cancellationToken);
        return await db.ProjectDrafts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);
    }

    public async Task<IReadOnlyList<DesignDraftVersion>> GetDraftHistoryAsync(
        Guid projectId, Guid actorId, CancellationToken cancellationToken = default)
    {
        await ProjectOwnerAccess.RequireReadAsync(db, projectId, actorId, cancellationToken);
        return await db.DesignDraftVersions.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.Version).ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApprovedRevision>> GetRevisionHistoryAsync(
        Guid projectId, Guid actorId, CancellationToken cancellationToken = default)
    {
        await ProjectOwnerAccess.RequireReadAsync(db, projectId, actorId, cancellationToken);
        return await db.ApprovedRevisions.AsNoTracking().Where(x => x.ProjectId == projectId)
            .Join(db.DesignDraftVersions, revision => revision.DraftVersionId, version => version.Id,
                (revision, version) => new { Revision = revision, version.Version })
            .OrderByDescending(x => x.Version).Select(x => x.Revision).ToArrayAsync(cancellationToken);
    }

    public async Task<ValidationRun> ValidateAsync(
        Guid projectId, Guid actorId, Guid expectedToken, CancellationToken cancellationToken = default)
    {
        await ProjectOwnerAccess.RequireWriteAsync(db, projectId, actorId, cancellationToken);
        var draft = await GetRequiredDraftAsync(projectId, expectedToken, cancellationToken);
        EnsureOpen(draft);
        var version = await GetCurrentVersionAsync(draft, cancellationToken);
        var issues = DraftBlueprint.ValidateStored(version.BlueprintJson);

        var run = new ValidationRun
        {
            DraftVersionId = version.Id,
            ValidatedAt = DateTimeOffset.UtcNow,
            Passed = issues.Count == 0
        };
        run.Findings.AddRange(issues.Select((issue, ordinal) =>
            new ValidationFinding(run.Id, ordinal, issue.Code, issue.Path, issue.Message)));
        db.ValidationRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<ApprovedRevision> ApproveAsync(
        Guid projectId, Guid actorId, Guid expectedToken, Guid validationRunId,
        CancellationToken cancellationToken = default)
    {
        var project = await ProjectOwnerAccess.RequireAsync(db, projectId, actorId, cancellationToken);
        var draft = await GetRequiredDraftAsync(projectId, expectedToken, cancellationToken);
        EnsureOpen(draft);
        var version = await GetCurrentVersionAsync(draft, cancellationToken);
        var validation = await db.ValidationRuns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == validationRunId && x.DraftVersionId == version.Id, cancellationToken);

        if (validation is null || !validation.Passed || version.BlueprintJson is null)
            throw new InvalidOperationException("Approval requires a successful validation run for the current draft version.");

        var blueprint = JsonSerializer.Deserialize<BlueprintDocument>(version.BlueprintJson, DraftBlueprint.Json)
            ?? throw new InvalidOperationException("The validated draft has no Blueprint.");
        var approvedAt = DateTimeOffset.UtcNow;
        var revision = new ApprovedRevision
        {
            ProjectId = projectId,
            DraftVersionId = version.Id,
            ValidationRunId = validation.Id,
            ApprovedBy = actorId,
            ApprovedAt = approvedAt
        };
        revision.SnapshotJson = JsonSerializer.Serialize(new ApprovedProjectSnapshot(
            projectId, project.Name, project.Description, project.OwnerId, revision.Id,
            version.Version, version.BasedOnRevisionId, actorId, approvedAt,
            validation.Id, validation.ValidatedAt, version.DraftJson, blueprint), DraftBlueprint.Json);
        db.ApprovedRevisions.Add(revision);
        db.Approvals.Add(new Approval
        {
            ApprovedRevisionId = revision.Id,
            ProjectId = projectId,
            ReviewerId = actorId,
            Status = "Approved",
            CreatedAt = approvedAt
        });
        draft.ApprovedRevisionId = revision.Id;
        RotateToken(draft);

        // The snapshot, approval audit and concurrency-checked draft update share one EF transaction.
        await PersistDraftAsync(cancellationToken);
        return revision;
    }

    public async Task<ProjectDraft> StartAfterApprovalAsync(
        Guid projectId, Guid actorId, Guid expectedToken, CancellationToken cancellationToken = default)
    {
        await ProjectOwnerAccess.RequireWriteAsync(db, projectId, actorId, cancellationToken);
        var draft = await GetRequiredDraftAsync(projectId, expectedToken, cancellationToken);
        if (draft.ApprovedRevisionId is not { } revisionId)
            throw new InvalidOperationException("The current draft is open.");

        var approved = await db.ApprovedRevisions.AsNoTracking().SingleAsync(
            x => x.Id == revisionId && x.ProjectId == projectId, cancellationToken);
        var snapshot = JsonSerializer.Deserialize<ApprovedProjectSnapshot>(approved.SnapshotJson, DraftBlueprint.Json)
            ?? throw new InvalidOperationException("The approved snapshot is unavailable.");

        var blueprintJson = JsonSerializer.Serialize(snapshot.Blueprint, DraftBlueprint.Json);
        draft.ApprovedRevisionId = null;
        draft.BasedOnRevisionId = revisionId;
        draft.DraftJson = snapshot.DraftJson;
        draft.Version = checked(draft.Version + 1);
        RotateToken(draft);
        AddDraftVersion(draft, snapshot.Blueprint, blueprintJson);
        await PersistDraftAsync(cancellationToken);
        return draft;
    }

    private async Task<ProjectDraft> SaveCoreAsync(
        Guid projectId, Guid actorId, string draftJson, BlueprintDocument? blueprint, Guid? expectedToken,
        CancellationToken cancellationToken)
    {
        await ProjectOwnerAccess.RequireWriteAsync(db, projectId, actorId, cancellationToken);
        RequireDraftJson(draftJson);
        var blueprintForVersion = blueprint;
        var blueprintJson = blueprint is null ? null : JsonSerializer.Serialize(blueprint, DraftBlueprint.Json);
        if (blueprintJson?.Length > 2_000_000)
            throw new ArgumentException("Blueprint JSON exceeds the supported size.", nameof(blueprint));

        var draft = await db.ProjectDrafts.SingleOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);

        if (draft is null)
        {
            if (expectedToken.HasValue)
                throw new DbUpdateConcurrencyException("The draft no longer exists.");

            draft = new ProjectDraft { ProjectId = projectId };
            db.ProjectDrafts.Add(draft);
        }
        else
        {
            RequireToken(draft, expectedToken);
            EnsureOpen(draft);
            if (blueprint is null)
            {
                var currentVersion = await GetCurrentVersionAsync(draft, cancellationToken);
                blueprintJson = currentVersion.BlueprintJson;
                blueprintForVersion = blueprintJson is null
                    ? null
                    : JsonSerializer.Deserialize<BlueprintDocument>(blueprintJson, DraftBlueprint.Json)
                        ?? throw new InvalidDataException("The current Blueprint could not be restored.");
            }
            draft.Version = checked(draft.Version + 1);
        }

        draft.DraftJson = draftJson;
        RotateToken(draft);
        AddDraftVersion(draft, blueprintForVersion, blueprintJson);
        await PersistDraftAsync(cancellationToken);
        return draft;
    }

    private async Task<ProjectDraft> GetRequiredDraftAsync(Guid projectId, Guid expectedToken, CancellationToken cancellationToken)
    {
        var draft = await db.ProjectDrafts.SingleOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken)
            ?? throw new KeyNotFoundException("The project has no draft.");
        RequireToken(draft, expectedToken);
        return draft;
    }

    private Task<DesignDraftVersion> GetCurrentVersionAsync(ProjectDraft draft, CancellationToken cancellationToken) =>
        db.DesignDraftVersions.AsNoTracking().SingleAsync(
            version => version.ProjectDraftId == draft.Id && version.Version == draft.Version, cancellationToken);

    private void AddDraftVersion(ProjectDraft draft, BlueprintDocument? blueprint, string? blueprintJson)
    {
        var version = new DesignDraftVersion
        {
            ProjectDraftId = draft.Id,
            ProjectId = draft.ProjectId,
            Version = draft.Version,
            DraftJson = draft.DraftJson,
            BlueprintJson = blueprintJson,
            BasedOnRevisionId = draft.BasedOnRevisionId,
            SavedAt = draft.UpdatedAtUtc
        };
        db.DesignDraftVersions.Add(version);
        if (blueprint is not null && DraftBlueprint.Validate(blueprint).Count == 0)
            DraftBlueprint.AddGraph(db, version.Id, blueprint);
    }

    private async Task PersistDraftAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception))
        {
            db.ChangeTracker.Clear();
            throw new DbUpdateConcurrencyException("The draft changed while saving. Reload and retry.", exception);
        }
    }

    private static void RequireToken(ProjectDraft draft, Guid? expectedToken)
    {
        if (expectedToken != draft.ConcurrencyToken)
            throw new DbUpdateConcurrencyException("The draft changed. Reload and retry.");
    }

    private static void EnsureOpen(ProjectDraft draft)
    {
        if (draft.ApprovedRevisionId.HasValue)
            throw new InvalidOperationException("Start a new draft from the approved revision before editing.");
    }

    private static void RotateToken(ProjectDraft draft)
    {
        draft.ConcurrencyToken = Guid.NewGuid();
        draft.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void RequireDraftJson(string draftJson)
    {
        if (string.IsNullOrWhiteSpace(draftJson) || draftJson.Length > 2_000_000)
            throw new ArgumentException("A draft JSON object of at most 2,000,000 characters is required.", nameof(draftJson));

        try
        {
            using var document = JsonDocument.Parse(draftJson, new JsonDocumentOptions
            {
                AllowDuplicateProperties = false,
                MaxDepth = 32
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("The draft must be a JSON object.", nameof(draftJson));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The draft must contain valid JSON.", nameof(draftJson), exception);
        }
    }
}
