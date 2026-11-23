using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.SecondBrain;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Authorization;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class ProjectExportService(ReqvioraDbContext db, Reqviora.Application.Common.Interfaces.IFileStorageService? storage = null)
{
    public async Task<ExportRecord> GetOrCreateAsync(
        Guid projectId, Guid revisionId, Guid actorId, CancellationToken cancellationToken = default)
    {
        await ProjectOwnerAccess.RequireAsync(db, projectId, actorId, cancellationToken);
        var revision = await db.ApprovedRevisions.AsNoTracking().SingleOrDefaultAsync(
            x => x.ProjectId == projectId && x.Id == revisionId, cancellationToken)
            ?? throw new KeyNotFoundException("Approved revision was not found.");

        var existing = await FindAsync(revisionId, cancellationToken);
        if (existing is not null)
            return EnsureValid(existing, projectId, revisionId);

        var snapshot = JsonSerializer.Deserialize<ApprovedProjectSnapshot>(revision.SnapshotJson, DraftBlueprint.Json)
            ?? throw new InvalidOperationException("The approved snapshot is unavailable.");
        if (snapshot.ProjectId != projectId || snapshot.RevisionId != revisionId)
            throw new InvalidDataException("The persisted snapshot identity is inconsistent.");

        var package = SecondBrainPackageBuilder.Build(snapshot);
        var record = new ExportRecord
        {
            ApprovedRevisionId = revisionId,
            GeneratorVersion = SecondBrainPackageBuilder.GeneratorVersion,
            CreatedBy = actorId,
            CreatedAt = DateTimeOffset.UtcNow,
            ManifestJson = package.ManifestJson,
            Archive = package.Archive,
            ArchiveSha256 = package.ArchiveSha256
        };
        EnsureValid(record, projectId, revisionId);
        if (storage is not null)
        {
            await using var archive = new MemoryStream(package.Archive, writable: false);
            record.StoragePath = await storage.SaveAsync(archive, $"{projectId}-{revisionId}.zip", cancellationToken);
        }
        db.ExportRecords.Add(record);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsUniqueViolation(exception))
        {
            db.Entry(record).State = EntityState.Detached;
            var winner = await FindAsync(revisionId, cancellationToken);
            if (winner is null)
                throw;

            return EnsureValid(winner, projectId, revisionId);
        }

        db.Entry(record).State = EntityState.Detached;
        return record;
    }

    private Task<ExportRecord?> FindAsync(Guid revisionId, CancellationToken cancellationToken) =>
        db.ExportRecords.AsNoTracking().SingleOrDefaultAsync(x => x.ApprovedRevisionId == revisionId &&
            x.GeneratorVersion == SecondBrainPackageBuilder.GeneratorVersion, cancellationToken);

    private static ExportRecord EnsureValid(ExportRecord record, Guid projectId, Guid revisionId)
    {
        if (!SecondBrainPackageBuilder.Verify(new(record.ManifestJson, record.Archive, record.ArchiveSha256), projectId, revisionId))
            throw new InvalidDataException("The export failed its manifest or SHA-256 checks.");

        return record;
    }
}
