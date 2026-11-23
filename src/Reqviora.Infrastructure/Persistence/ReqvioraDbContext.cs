using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;

namespace Reqviora.Infrastructure.Persistence;

public class ReqvioraDbContext : DbContext
{
    public ReqvioraDbContext(DbContextOptions<ReqvioraDbContext> options) : base(options) { }

    protected ReqvioraDbContext(DbContextOptions options) : base(options) { }

    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<Blueprint> Blueprints => Set<Blueprint>();
    public DbSet<Component> Components => Set<Component>();
    public DbSet<Feature> Features => Set<Feature>();
    public DbSet<NavigationRoute> NavigationRoutes => Set<NavigationRoute>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
    public DbSet<Requirement> Requirements => Set<Requirement>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AccountActionToken> AccountActionTokens => Set<AccountActionToken>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Screen> Screens => Set<Screen>();
    public DbSet<User> Users => Set<User>();
    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ProjectDraft> ProjectDrafts => Set<ProjectDraft>();
    public DbSet<DesignDraftVersion> DesignDraftVersions => Set<DesignDraftVersion>();
    public DbSet<DraftRequirement> DraftRequirements => Set<DraftRequirement>();
    public DbSet<RequirementSourceEvidence> RequirementSourceEvidence => Set<RequirementSourceEvidence>();
    public DbSet<DraftFeature> DraftFeatures => Set<DraftFeature>();
    public DbSet<DraftFeatureRequirement> DraftFeatureRequirements => Set<DraftFeatureRequirement>();
    public DbSet<DraftScreen> DraftScreens => Set<DraftScreen>();
    public DbSet<DraftScreenFeature> DraftScreenFeatures => Set<DraftScreenFeature>();
    public DbSet<ScreenAction> ScreenActions => Set<ScreenAction>();
    public DbSet<NavigationEdge> NavigationEdges => Set<NavigationEdge>();
    public DbSet<ValidationRun> ValidationRuns => Set<ValidationRun>();
    public DbSet<ValidationFinding> ValidationFindings => Set<ValidationFinding>();
    public DbSet<ApprovedRevision> ApprovedRevisions => Set<ApprovedRevision>();
    public DbSet<ExportRecord> ExportRecords => Set<ExportRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        DraftPersistenceConfiguration.Configure(modelBuilder, Database.IsSqlServer());

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.UserId, token.ExpiresAt });
            entity.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(token => token.ReplacedByTokenHash).HasMaxLength(64);
            entity.HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccountActionToken>(entity =>
        {
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.UserId, token.Purpose, token.ExpiresAt });
            entity.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(token => token.TargetValue).HasMaxLength(320);
            entity.HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasIndex(role => role.Name).IsUnique();
            entity.Property(role => role.Name).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasIndex(project => project.OwnerId);
            entity.Property(project => project.Name).HasMaxLength(200).IsRequired();
            entity.Property(project => project.Description).HasMaxLength(4000);
            entity.Property(project => project.Industry).HasMaxLength(120);
            entity.Property(project => project.TargetAudience).HasMaxLength(500);
            entity.Property(project => project.ProjectType).HasMaxLength(120);
            entity.Property(project => project.PrimaryPlatform).HasMaxLength(50).IsRequired();
            entity.Property(project => project.TechnologyPreferences).HasMaxLength(2000);
            entity.Property(project => project.Constraints).HasMaxLength(4000);
            entity.Property(project => project.Integrations).HasMaxLength(4000);
        });

        modelBuilder.Entity<Approval>().Property(approval => approval.Status).HasMaxLength(50).IsRequired();
        modelBuilder.Entity<Blueprint>().Property(blueprint => blueprint.Name).HasMaxLength(200).IsRequired();
        modelBuilder.Entity<Component>().Property(component => component.Type).HasMaxLength(100).IsRequired();
        modelBuilder.Entity<Feature>().Property(feature => feature.Name).HasMaxLength(200).IsRequired();
        modelBuilder.Entity<NavigationRoute>().Property(route => route.Path).HasMaxLength(500).IsRequired();
        modelBuilder.Entity<ProjectDocument>().Property(document => document.FileName).HasMaxLength(255).IsRequired();
        modelBuilder.Entity<ProjectDocument>().Property(document => document.StoragePath).HasMaxLength(1000).IsRequired();
        modelBuilder.Entity<Requirement>().Property(requirement => requirement.Title).HasMaxLength(300).IsRequired();
        modelBuilder.Entity<Screen>().Property(screen => screen.Name).HasMaxLength(200).IsRequired();

        modelBuilder.Entity<WorkspaceMember>(entity =>
        {
            entity.HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique();
            entity.Property(x => x.Role).HasMaxLength(50).IsRequired();
            entity.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<LoginAudit>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.OccurredAt });
            entity.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DeviceId).HasMaxLength(200).IsRequired();
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasIndex(x => x.OccurredAt);
            entity.Property(x => x.Method).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Path).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(64).IsRequired();
        });
    }
}
