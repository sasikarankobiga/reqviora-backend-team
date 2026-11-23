using System.Data.Common;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Reqviora.Application.Features.SecondBrain;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Reqviora.Application.Features.RequirementReview;

namespace Reqviora.Infrastructure.Tests;

public sealed class ProjectDraftServiceTests
{
    public static IEnumerable<object[]> Providers()
    {
        yield return ["sqlite"];
        if (Environment.GetEnvironmentVariable("REQVIORA_TEST_POSTGRES") is not null)
            yield return ["postgres"];
        if (Environment.GetEnvironmentVariable("REQVIORA_TEST_SQLSERVER") is not null)
            yield return ["sqlserver"];
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Save_and_resume_preserve_identity_and_reject_stale_updates(string provider)
    {
        await using var database = await PersistenceDatabase.CreateAsync(provider);
        await using var db = database.Open();
        var (owner, project) = await SeedProjectAsync(db);
        var service = CreateService(db);
        Assert.Null(await service.ResumeAsync(project.Id, owner.Id));

        var original = await service.SaveAsync(project.Id, owner.Id, """{"step":"scope"}""", null);
        Assert.Equal(project.Id, original.ProjectId);
        Assert.Equal(1, original.Version);
        Assert.NotEqual(Guid.Empty, original.ConcurrencyToken);
        Assert.Equal(1, await db.ProjectDrafts.CountAsync());
        var originalToken = original.ConcurrencyToken;
        var nativeVersion = original.RowVersion?.ToArray();
        var id = original.Id;

        await using var anotherDb = database.Open();
        var anotherService = CreateService(anotherDb);
        var resumed = await anotherService.ResumeAsync(project.Id, owner.Id);
        Assert.NotNull(resumed);
        Assert.Equal(id, resumed.Id);
        Assert.Equal(project.Id, resumed.ProjectId);
        Assert.Equal(1, resumed.Version);
        Assert.Equal("""{"step":"scope"}""", resumed.DraftJson);

        var edited = await anotherService.SaveAsync(project.Id, owner.Id, """{"step":"review"}""", originalToken);
        Assert.Equal(id, edited.Id);
        Assert.Equal(2, edited.Version);
        Assert.NotEqual(originalToken, edited.ConcurrencyToken);
        if (provider == "sqlserver")
        {
            Assert.Equal(8, Assert.IsType<byte[]>(nativeVersion).Length);
            Assert.False(nativeVersion!.SequenceEqual(Assert.IsType<byte[]>(edited.RowVersion)));
        }

        await using var staleDb = database.Open();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            CreateService(staleDb).SaveAsync(project.Id, owner.Id, "{}", originalToken));
        Assert.Equal(1, await staleDb.ProjectDrafts.CountAsync());
        Assert.Equal(2, await staleDb.DesignDraftVersions.CountAsync());
        Assert.Equal("""{"step":"review"}""", (await anotherService.ResumeAsync(project.Id, owner.Id))!.DraftJson);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Generic_draft_save_preserves_the_current_blueprint(string provider)
    {
        await using var database = await PersistenceDatabase.CreateAsync(provider);
        await using var db = database.Open();
        var (owner, project) = await SeedProjectAsync(db);
      var service = CreateService(db);
        var blueprint = await LoadBlueprintAsync(project.Id);

        var designed = await service.SaveDesignAsync(
            project.Id, owner.Id, """{"step":"blueprint"}""", blueprint, null);
        var saved = await service.SaveAsync(
            project.Id, owner.Id, """{"step":"notes"}""", designed.ConcurrencyToken);

        var versions = await db.DesignDraftVersions.AsNoTracking()
            .Where(version => version.ProjectId == project.Id)
            .OrderBy(version => version.Version)
            .ToArrayAsync();
        Assert.Equal(2, versions.Length);
        Assert.NotNull(versions[0].BlueprintJson);
        Assert.Equal(versions[0].BlueprintJson, versions[1].BlueprintJson);
        Assert.Equal(blueprint.BlueprintId,
            JsonSerializer.Deserialize<BlueprintDocument>(versions[1].BlueprintJson!,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!.BlueprintId);
        Assert.Equal(2, saved.Version);
        Assert.Equal(blueprint.Requirements.Count * 2, await db.DraftRequirements.CountAsync());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Approval_is_bound_to_current_validation_and_exports_the_frozen_graph(string provider)
    {
        await using var database = await PersistenceDatabase.CreateAsync(provider);
        await using var db = database.Open();
        var (owner, project) = await SeedProjectAsync(db);
        var service = CreateService(db);
        var exports = new ProjectExportService(db);

        var broken = await LoadBlueprintAsync(project.Id, breakRoute: true);
        var draft = await service.SaveDesignAsync(project.Id, owner.Id, "{}", broken, null);
        var invalid = await service.ValidateAsync(project.Id, owner.Id, draft.ConcurrencyToken);
        Assert.False(invalid.Passed);
        Assert.NotEmpty(invalid.Findings);
        Assert.Equal(invalid.Findings.Count, await db.ValidationFindings.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(project.Id, owner.Id, draft.ConcurrencyToken, invalid.Id));

        var blueprint = await LoadBlueprintAsync(project.Id);
        const string notes = """{"secondBrain":{"designTokens":"Primary blue","accessibilityRequirements":"Keyboard support"}}""";
        draft = await service.SaveDesignAsync(project.Id, owner.Id, notes, blueprint, draft.ConcurrencyToken);
        var valid = await service.ValidateAsync(project.Id, owner.Id, draft.ConcurrencyToken);
        Assert.True(valid.Passed);
        Assert.Empty(valid.Findings);
        Assert.Equal(2, await db.RequirementSourceEvidence.CountAsync());
        Assert.Equal("REQ-001", (await db.DraftFeatureRequirements.SingleAsync()).RequirementId);
        Assert.Equal("FEAT-001", (await db.DraftScreenFeatures.FirstAsync()).FeatureId);
        Assert.Equal("SCR-001", (await db.ScreenActions.SingleAsync()).ScreenId);
        Assert.Equal("ACT-001", (await db.NavigationEdges.SingleAsync()).ActionId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ApproveAsync(project.Id, Guid.NewGuid(), draft.ConcurrencyToken, valid.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(project.Id, owner.Id, draft.ConcurrencyToken, invalid.Id));
        var revision = await service.ApproveAsync(project.Id, owner.Id, draft.ConcurrencyToken, valid.Id);
        Assert.Equal(revision.Id, (await db.Approvals.SingleAsync()).ApprovedRevisionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(project.Id, owner.Id, "{}", draft.ConcurrencyToken));

        var export = await exports.GetOrCreateAsync(project.Id, revision.Id, owner.Id);
        var package = new SecondBrainPackage(export.ManifestJson, export.Archive, export.ArchiveSha256);
        Assert.True(SecondBrainPackageBuilder.Verify(package, project.Id, revision.Id));
        Assert.False(SecondBrainPackageBuilder.Verify(package, project.Id, Guid.NewGuid()));
        Assert.False(SecondBrainPackageBuilder.Verify(ChangeExportBytes(package), project.Id, revision.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            exports.GetOrCreateAsync(project.Id, revision.Id, Guid.NewGuid()));

        using (var archive = new ZipArchive(new MemoryStream(export.Archive), ZipArchiveMode.Read))
        {
            Assert.Equal(20, archive.Entries.Count);
            var manifest = JsonSerializer.Deserialize<SecondBrainManifest>(export.ManifestJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(19, manifest.ReferenceCount);
            Assert.Equal(19, manifest.Files.Count);
            Assert.Equal(19, manifest.Files.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count());
            Assert.All(manifest.Files, file =>
                Assert.NotNull(archive.GetEntry(file.Path)));
            using var tokens = new StreamReader(archive.GetEntry("design-tokens.md")!.Open());
            Assert.Contains("Primary blue", await tokens.ReadToEndAsync());
            using var json = JsonDocument.Parse(archive.GetEntry("project.json")!.Open());
            Assert.Equal("suggested", json.RootElement.GetProperty("blueprint").GetProperty("requirements")[1]
                .GetProperty("status").GetString());
        }

        var reopened = await service.StartAfterApprovalAsync(project.Id, owner.Id, draft.ConcurrencyToken);
        Assert.Null(reopened.ApprovedRevisionId);
        Assert.Equal(revision.Id, reopened.BasedOnRevisionId);
        Assert.Equal(3, reopened.Version);
        Assert.Equal(notes, reopened.DraftJson);
        await service.SaveAsync(project.Id, owner.Id, """{"step":"next"}""", reopened.ConcurrencyToken);

        var history = await service.GetDraftHistoryAsync(project.Id, owner.Id);
        Assert.Equal(new[] { 4, 3, 2, 1 }, history.Select(x => x.Version));
        Assert.Equal(revision.SnapshotJson, (await service.GetRevisionHistoryAsync(project.Id, owner.Id)).Single().SnapshotJson);
        var sameExport = await exports.GetOrCreateAsync(project.Id, revision.Id, owner.Id);
        Assert.Equal(export.Id, sameExport.Id);
        Assert.Equal(export.ArchiveSha256, sameExport.ArchiveSha256);
        Assert.Equal(1, await db.ExportRecords.CountAsync());

        if (provider != "sqlite")
        {
            foreach (var write in new Func<Task<int>>[]
            {
                () => db.ApprovedRevisions.Where(x => x.Id == revision.Id)
                    .ExecuteUpdateAsync(x => x.SetProperty(item => item.SnapshotJson, "{}")),
                () => db.ApprovedRevisions.Where(x => x.Id == revision.Id).ExecuteDeleteAsync(),
                () => db.DesignDraftVersions.Where(x => x.Id == valid.DraftVersionId)
                    .ExecuteUpdateAsync(x => x.SetProperty(item => item.DraftJson, "{}"))
            })
            {
                var blocked = await Assert.ThrowsAnyAsync<DbException>(write);
                Assert.Contains("Reqviora revision history is immutable", blocked.Message);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Workspace_membership_rejects_duplicates_and_missing_users(string provider)
    {
        await using var database = await PersistenceDatabase.CreateAsync(provider);
        await using var db = database.Open();
        var (owner, project) = await SeedProjectAsync(db);
        var workspace = new Workspace { OwnerUserId = owner.Id, Name = "Library workspace" };
        db.Workspaces.Add(workspace);
        project.WorkspaceId = workspace.Id;
        db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = owner.Id, Role = "Owner" });
        await db.SaveChangesAsync();

        db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = owner.Id, Role = "Editor" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = Guid.NewGuid(), Role = "Viewer" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(owner.Id, (await db.ProjectMembers.SingleAsync()).UserId);
    }

    [Fact]
    public void Sql_server_migration_generates_native_rowversion_and_immutability_triggers()
    {
        using var db = PersistenceDatabase.SqlServer(
            "Server=127.0.0.1,1;Database=ReqvioraDesignTime;Integrated Security=True;TrustServerCertificate=True");
        var sql = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("[RowVersion] rowversion NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TR_ApprovedRevisions_Immutable", sql);
        Assert.Contains("TR_DesignDraftVersions_Immutable", sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void PostgreSql_migrations_keep_backend_tables_private_for_supabase()
    {
        using var db = new ReqvioraDbContext(
            new DbContextOptionsBuilder<ReqvioraDbContext>()
                .UseNpgsql("Host=127.0.0.1;Port=1;Database=ReqvioraDesignTime;Username=unused;Password=unused")
                .Options);
        var sql = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        foreach (var table in new[]
        {
            "Users", "Projects", "ProjectMembers", "ProjectDrafts", "DesignDraftVersions",
            "ApprovedRevisions", "ExportRecords", "AuditEvents", "LoginAudits", "AccountActionTokens"
        })
        {
            Assert.Contains($"'{table}'", sql, StringComparison.Ordinal);
        }
        Assert.Contains("ENABLE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
        Assert.Contains("REVOKE ALL ON TABLE", sql, StringComparison.Ordinal);
        Assert.Contains("'anon', 'authenticated', 'service_role'", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER DEFAULT PRIVILEGES", sql, StringComparison.Ordinal);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Native_provider_migrations_handle_upgrade_and_database_concurrency(string provider)
    {
        var previousMigration = provider == "postgres" ? "20261118085932_AddAuthenticationSessions" : null;
        await using var database = await PersistenceDatabase.CreateAsync(provider, previousMigration);
        await using var db = database.Open();
        if (previousMigration is not null)
        {
            var userId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Users" ("Id", "Email", "NormalizedEmail", "PasswordHash", "DisplayName", "CreatedAt")
                VALUES ({userId}, 'legacy@example.invalid', 'LEGACY@EXAMPLE.INVALID', 'fixture', 'Legacy', {DateTimeOffset.UtcNow});
                INSERT INTO "Projects" ("Id", "Name", "Description", "OwnerId", "CreatedAt")
                VALUES ({projectId}, 'Existing project', '', {userId}, {DateTimeOffset.UtcNow});
                """);
            await db.Database.MigrateAsync();
            var project = await db.Projects.SingleAsync(x => x.Id == projectId);
            Assert.Null(project.WorkspaceId);
            Assert.Equal(userId, project.OwnerId);
            Assert.False(db.Database.HasPendingModelChanges());
        }

        var (owner, current) = await SeedProjectAsync(db);
        var draft = await CreateService(db).SaveAsync(current.Id, owner.Id, "{}", null);
        if (provider == "sqlserver")
        {
            await using var anotherDb = database.Open();
            await anotherDb.ProjectDrafts.Where(x => x.Id == draft.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, DateTime.UtcNow));
            draft.DraftJson = """{"changed":true}""";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        }
        else
        {
            Assert.Null(draft.RowVersion);
        }

        if (provider == "postgres")
        {
            await using var competitor = database.Open();
            var token = draft.ConcurrencyToken;
            var outcomes = await Task.WhenAll(
                TrySaveAsync(db, current.Id, owner.Id, token),
                TrySaveAsync(competitor, current.Id, owner.Id, token));
            Assert.Single(outcomes, saved => saved);
            Assert.Equal(2, await db.DesignDraftVersions.CountAsync());
        }
    }

    private static async Task<bool> TrySaveAsync(ReqvioraDbContext db, Guid project, Guid actor, Guid token)
    {
        try
        {
            await CreateService(db).SaveAsync(project, actor, "{}", token);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private static ProjectDraftService CreateService(
        ReqvioraDbContext db)
    {
        return new ProjectDraftService(
            db,
            new EmptyRequirementReviewCatalog());
    }

    private sealed class EmptyRequirementReviewCatalog
        : IRequirementReviewCatalog
    {
        public Task<IReadOnlyList<ReviewedRequirement>>
            ListByProjectAsync(
                Guid projectId,
                CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ReviewedRequirement> reviews = [];

            return Task.FromResult(reviews);
        }
    }


    private static async Task<(User Owner, Project Project)> SeedProjectAsync(ReqvioraDbContext db)
    {
        var email = $"{Guid.NewGuid():N}@example.invalid";
        var owner = new User
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = "Fixture owner",
            PasswordHash = "fixture"
        };
        var project = new Project { OwnerId = owner.Id, Name = "School library" };
        db.AddRange(owner, project);
        await db.SaveChangesAsync();
        return (owner, project);
    }

    private static async Task<BlueprintDocument> LoadBlueprintAsync(Guid projectId, bool breakRoute = false)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "blueprint.valid.v1.json"));
        var root = JsonNode.Parse(json)!;
        root["projectId"] = projectId;
        if (breakRoute)
            root["navigation"]![0]!["targetScreenId"] = "SCR-MISSING";

        return root.Deserialize<BlueprintDocument>()!;
    }

    private static SecondBrainPackage ChangeExportBytes(SecondBrainPackage package)
    {
        using var stream = new MemoryStream();
        stream.Write(package.Archive);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var changed = archive.GetEntry("feature-catalog.md")!.Open();
            changed.SetLength(0);
            changed.Write(Encoding.UTF8.GetBytes("Changed content"));
        }
        var bytes = stream.ToArray();
        return package with { Archive = bytes, ArchiveSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
    }
}
