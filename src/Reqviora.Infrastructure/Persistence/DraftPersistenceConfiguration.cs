using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Reqviora.Domain.Entities;

namespace Reqviora.Infrastructure.Persistence;

internal static class DraftPersistenceConfiguration
{
    internal const int StableIdLength = 128;

    internal static void Configure(ModelBuilder model, bool sqlServer)
    {
        var versionColumn = sqlServer ? "[Version]" : "\"Version\"";
        var roleColumn = sqlServer ? "[Role]" : "\"Role\"";

        var workspace = model.Entity<Workspace>();
        workspace.Property(x => x.Name).HasMaxLength(150).IsRequired();
        workspace.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.NoAction);

        var project = model.Entity<Project>();
        project.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.NoAction);
        project.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.NoAction);

        var member = model.Entity<ProjectMember>();
        member.ToTable(table => table.HasCheckConstraint("CK_ProjectMembers_Role", $"TRIM({roleColumn}) <> ''"));
        member.Property(x => x.Role).HasMaxLength(30).IsRequired();
        member.HasIndex(x => new { x.ProjectId, x.UserId }).IsUnique();
        member.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        member.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);

        var draft = model.Entity<ProjectDraft>();
        draft.Property(x => x.IsReviewManaged)
    .HasDefaultValue(false)
    .IsRequired();
        draft.ToTable(table => table.HasCheckConstraint("CK_ProjectDrafts_Version", $"{versionColumn} > 0"));
        draft.HasIndex(x => x.ProjectId).IsUnique();
        draft.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        if (sqlServer)
            draft.Property(x => x.RowVersion).IsRowVersion().IsRequired();
        else
            draft.Ignore(x => x.RowVersion);

        draft.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
        draft.HasOne<ApprovedRevision>().WithMany()
            .HasForeignKey(x => new { x.ApprovedRevisionId, x.ProjectId })
            .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.NoAction);
        draft.HasOne<ApprovedRevision>().WithMany()
            .HasForeignKey(x => new { x.BasedOnRevisionId, x.ProjectId })
            .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.NoAction);

        var version = model.Entity<DesignDraftVersion>();
        version.ToTable(table =>
        {
            table.HasCheckConstraint("CK_DesignDraftVersions_Version", $"{versionColumn} > 0");
            if (sqlServer)
                table.UseSqlOutputClause(false);
        });
        version.HasIndex(x => new { x.ProjectDraftId, x.Version }).IsUnique();
        version.HasOne<ProjectDraft>().WithMany()
            .HasForeignKey(x => new { x.ProjectDraftId, x.ProjectId })
            .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.NoAction);
        version.HasOne<ApprovedRevision>().WithMany()
            .HasForeignKey(x => new { x.BasedOnRevisionId, x.ProjectId })
            .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.NoAction);

        ConfigureGraph(model, sqlServer);

        var validation = model.Entity<ValidationRun>();
        validation.HasOne<DesignDraftVersion>().WithMany().HasForeignKey(x => x.DraftVersionId)
            .OnDelete(DeleteBehavior.NoAction);
        validation.HasMany(x => x.Findings).WithOne().HasForeignKey(x => x.ValidationRunId)
            .OnDelete(DeleteBehavior.Cascade);
        model.Entity<ValidationFinding>().HasKey(x => new { x.ValidationRunId, x.Ordinal });

        var revision = model.Entity<ApprovedRevision>();
        if (sqlServer)
            revision.ToTable(table => table.UseSqlOutputClause(false));

        revision.HasIndex(x => x.DraftVersionId).IsUnique();
        revision.HasOne<DesignDraftVersion>().WithMany()
            .HasForeignKey(x => new { x.DraftVersionId, x.ProjectId })
            .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.NoAction);
        revision.HasOne<ValidationRun>().WithMany()
            .HasForeignKey(x => new { x.ValidationRunId, x.DraftVersionId })
            .HasPrincipalKey(x => new { x.Id, x.DraftVersionId }).OnDelete(DeleteBehavior.NoAction);
        revision.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedBy).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Approval>().HasOne<ApprovedRevision>().WithMany()
            .HasForeignKey(x => new { x.ApprovedRevisionId, x.ProjectId })
            .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.NoAction);

        var export = model.Entity<ExportRecord>();
        export.Property(x => x.GeneratorVersion).HasMaxLength(32);
        export.Property(x => x.ArchiveSha256).HasMaxLength(64).IsUnicode(false);
        export.Property(x => x.StoragePath).HasMaxLength(1000);
        export.HasIndex(x => new { x.ApprovedRevisionId, x.GeneratorVersion }).IsUnique();
        export.HasOne<ApprovedRevision>().WithMany().HasForeignKey(x => x.ApprovedRevisionId)
            .OnDelete(DeleteBehavior.NoAction);
        export.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureGraph(ModelBuilder model, bool sqlServer)
    {
        var requirement = GraphRow<DraftRequirement>(model, x => new { x.DraftVersionId, x.Id });
        StableId(requirement.Property(x => x.Id), sqlServer);

        var evidence = GraphRow<RequirementSourceEvidence>(model, x => new { x.DraftVersionId, x.RequirementId, x.Ordinal });
        StableId(evidence.Property(x => x.RequirementId), sqlServer);
        evidence.HasOne<DraftRequirement>().WithMany()
            .HasForeignKey(x => new { x.DraftVersionId, x.RequirementId }).OnDelete(DeleteBehavior.NoAction);

        var feature = GraphRow<DraftFeature>(model, x => new { x.DraftVersionId, x.Id });
        StableId(feature.Property(x => x.Id), sqlServer);
        var required = GraphRow<DraftFeatureRequirement>(model, x => new { x.DraftVersionId, x.FeatureId, x.RequirementId });
        StableId(required.Property(x => x.FeatureId), sqlServer);
        StableId(required.Property(x => x.RequirementId), sqlServer);
        required.HasOne<DraftFeature>().WithMany().HasForeignKey(x => new { x.DraftVersionId, x.FeatureId })
            .OnDelete(DeleteBehavior.NoAction);
        required.HasOne<DraftRequirement>().WithMany().HasForeignKey(x => new { x.DraftVersionId, x.RequirementId })
            .OnDelete(DeleteBehavior.NoAction);

        var screen = GraphRow<DraftScreen>(model, x => new { x.DraftVersionId, x.Id });
        StableId(screen.Property(x => x.Id), sqlServer);
        var mapping = GraphRow<DraftScreenFeature>(model, x => new { x.DraftVersionId, x.ScreenId, x.FeatureId });
        StableId(mapping.Property(x => x.ScreenId), sqlServer);
        StableId(mapping.Property(x => x.FeatureId), sqlServer);
        mapping.HasOne<DraftScreen>().WithMany().HasForeignKey(x => new { x.DraftVersionId, x.ScreenId })
            .OnDelete(DeleteBehavior.NoAction);
        mapping.HasOne<DraftFeature>().WithMany().HasForeignKey(x => new { x.DraftVersionId, x.FeatureId })
            .OnDelete(DeleteBehavior.NoAction);

        var action = GraphRow<ScreenAction>(model, x => new { x.DraftVersionId, x.Id });
        StableId(action.Property(x => x.ScreenId), sqlServer);
        StableId(action.Property(x => x.Id), sqlServer);
        StableId(action.Property(x => x.ComponentId), sqlServer);
        action.HasOne<DraftScreen>().WithMany().HasForeignKey(x => new { x.DraftVersionId, x.ScreenId })
            .OnDelete(DeleteBehavior.NoAction);

        var edge = GraphRow<NavigationEdge>(model, x => new { x.DraftVersionId, x.Id });
        StableId(edge.Property(x => x.SourceScreenId), sqlServer);
        StableId(edge.Property(x => x.TargetScreenId), sqlServer);
        StableId(edge.Property(x => x.ActionId), sqlServer);
        StableId(edge.Property(x => x.Id), sqlServer);
        edge.HasOne<DraftScreen>().WithMany().HasForeignKey(x => new { x.DraftVersionId, x.SourceScreenId })
            .OnDelete(DeleteBehavior.NoAction);
        edge.HasOne<DraftScreen>().WithMany().HasForeignKey(x => new { x.DraftVersionId, x.TargetScreenId })
            .OnDelete(DeleteBehavior.NoAction);
        edge.HasOne<ScreenAction>().WithMany().HasForeignKey(x => new { x.DraftVersionId, x.ActionId, x.SourceScreenId })
            .HasPrincipalKey(x => new { x.DraftVersionId, x.Id, x.ScreenId }).OnDelete(DeleteBehavior.NoAction);
    }

    private static EntityTypeBuilder<T> GraphRow<T>(ModelBuilder model, Expression<Func<T, object?>> key)
        where T : class, IDraftDesignRow
    {
        var entity = model.Entity<T>();
        entity.HasKey(key);
        entity.HasOne<DesignDraftVersion>().WithMany().HasForeignKey(nameof(IDraftDesignRow.DraftVersionId))
            .OnDelete(DeleteBehavior.NoAction);
        return entity;
    }

    private static void StableId(PropertyBuilder<string> property, bool sqlServer)
    {
        property.HasMaxLength(StableIdLength).IsUnicode(false);
        if (sqlServer)
            property.UseCollation("Latin1_General_100_BIN2");
    }
}
