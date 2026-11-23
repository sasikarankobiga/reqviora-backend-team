using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectDraftApprovalAndExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedRevisionId",
                table: "Approvals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMembers", x => x.Id);
                    table.CheckConstraint("CK_ProjectMembers_Role", "TRIM(\"Role\") <> ''");
                    table.ForeignKey(
                        name: "FK_ProjectMembers_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Workspaces_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApprovedRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovedRevisions", x => x.Id);
                    table.UniqueConstraint("AK_ApprovedRevisions_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.ForeignKey(
                        name: "FK_ApprovedRevisions_Users_ApprovedBy",
                        column: x => x.ApprovedBy,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExportRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratorVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    ArchiveSha256 = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    Archive = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExportRecords_ApprovedRevisions_ApprovedRevisionId",
                        column: x => x.ApprovedRevisionId,
                        principalTable: "ApprovedRevisions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExportRecords_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProjectDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftJson = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDrafts", x => x.Id);
                    table.UniqueConstraint("AK_ProjectDrafts_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_ProjectDrafts_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_ProjectDrafts_ApprovedRevisions_ApprovedRevisionId_ProjectId",
                        columns: x => new { x.ApprovedRevisionId, x.ProjectId },
                        principalTable: "ApprovedRevisions",
                        principalColumns: new[] { "Id", "ProjectId" });
                    table.ForeignKey(
                        name: "FK_ProjectDrafts_ApprovedRevisions_BasedOnRevisionId_ProjectId",
                        columns: x => new { x.BasedOnRevisionId, x.ProjectId },
                        principalTable: "ApprovedRevisions",
                        principalColumns: new[] { "Id", "ProjectId" });
                    table.ForeignKey(
                        name: "FK_ProjectDrafts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DesignDraftVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DraftJson = table.Column<string>(type: "text", nullable: false),
                    BlueprintJson = table.Column<string>(type: "text", nullable: true),
                    BasedOnRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesignDraftVersions", x => x.Id);
                    table.UniqueConstraint("AK_DesignDraftVersions_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_DesignDraftVersions_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_DesignDraftVersions_ApprovedRevisions_BasedOnRevisionId_Pro~",
                        columns: x => new { x.BasedOnRevisionId, x.ProjectId },
                        principalTable: "ApprovedRevisions",
                        principalColumns: new[] { "Id", "ProjectId" });
                    table.ForeignKey(
                        name: "FK_DesignDraftVersions_ProjectDrafts_ProjectDraftId_ProjectId",
                        columns: x => new { x.ProjectDraftId, x.ProjectId },
                        principalTable: "ProjectDrafts",
                        principalColumns: new[] { "Id", "ProjectId" });
                });

            migrationBuilder.CreateTable(
                name: "DraftFeatures",
                columns: table => new
                {
                    DraftVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftFeatures", x => new { x.DraftVersionId, x.Id });
                    table.ForeignKey(
                        name: "FK_DraftFeatures_DesignDraftVersions_DraftVersionId",
                        column: x => x.DraftVersionId,
                        principalTable: "DesignDraftVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DraftRequirements",
                columns: table => new
                {
                    DraftVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftRequirements", x => new { x.DraftVersionId, x.Id });
                    table.ForeignKey(
                        name: "FK_DraftRequirements_DesignDraftVersions_DraftVersionId",
                        column: x => x.DraftVersionId,
                        principalTable: "DesignDraftVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DraftScreens",
                columns: table => new
                {
                    DraftVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftScreens", x => new { x.DraftVersionId, x.Id });
                    table.ForeignKey(
                        name: "FK_DraftScreens_DesignDraftVersions_DraftVersionId",
                        column: x => x.DraftVersionId,
                        principalTable: "DesignDraftVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ValidationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationRuns", x => x.Id);
                    table.UniqueConstraint("AK_ValidationRuns_Id_DraftVersionId", x => new { x.Id, x.DraftVersionId });
                    table.ForeignKey(
                        name: "FK_ValidationRuns_DesignDraftVersions_DraftVersionId",
                        column: x => x.DraftVersionId,
                        principalTable: "DesignDraftVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DraftFeatureRequirements",
                columns: table => new
                {
                    DraftVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureId = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    RequirementId = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftFeatureRequirements", x => new { x.DraftVersionId, x.FeatureId, x.RequirementId });
                    table.ForeignKey(
                        name: "FK_DraftFeatureRequirements_DesignDraftVersions_DraftVersionId",
                        column: x => x.DraftVersionId,
                        principalTable: "DesignDraftVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DraftFeatureRequirements_DraftFeatures_DraftVersionId_Featu~",
                        columns: x => new { x.DraftVersionId, x.FeatureId },
                        principalTable: "DraftFeatures",
                        principalColumns: new[] { "DraftVersionId", "Id" });
                    table.ForeignKey(
                        name: "FK_DraftFeatureRequirements_DraftRequirements_DraftVersionId_R~",
                        columns: x => new { x.DraftVersionId, x.RequirementId },
                        principalTable: "DraftRequirements",
                        principalColumns: new[] { "DraftVersionId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "RequirementSourceEvidence",
                columns: table => new
                {
                    DraftVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequirementId = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<string>(type: "text", nullable: false),
                    Locator = table.Column<string>(type: "text", nullable: false),
                    Quote = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequirementSourceEvidence", x => new { x.DraftVersionId, x.RequirementId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_RequirementSourceEvidence_DesignDraftVersions_DraftVersionId",
                        column: x => x.DraftVersionId,
                        principalTable: "DesignDraftVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequirementSourceEvidence_DraftRequirements_DraftVersionId_~",
                        columns: x => new { x.DraftVersionId, x.RequirementId },
                        principalTable: "DraftRequirements",
                        principalColumns: new[] { "DraftVersionId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "DraftScreenFeatures",
                columns: table => new
                {
                    DraftVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScreenId = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    FeatureId = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftScreenFeatures", x => new { x.DraftVersionId, x.ScreenId, x.FeatureId });
                    table.ForeignKey(
                        name: "FK_DraftScreenFeatures_DesignDraftVersions_DraftVersionId",
                        column: x => x.DraftVersionId,
                        principalTable: "DesignDraftVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DraftScreenFeatures_DraftFeatures_DraftVersionId_FeatureId",
                        columns: x => new { x.DraftVersionId, x.FeatureId },
                        principalTable: "DraftFeatures",
                        principalColumns: new[] { "DraftVersionId", "Id" });
                    table.ForeignKey(
                        name: "FK_DraftScreenFeatures_DraftScreens_DraftVersionId_ScreenId",
                        columns: x => new { x.DraftVersionId, x.ScreenId },
                        principalTable: "DraftScreens",
                        principalColumns: new[] { "DraftVersionId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ScreenActions",
                columns: table => new
                {
                    DraftVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    ScreenId = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    ComponentId = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreenActions", x => new { x.DraftVersionId, x.Id });
                    table.UniqueConstraint("AK_ScreenActions_DraftVersionId_Id_ScreenId", x => new { x.DraftVersionId, x.Id, x.ScreenId });
                    table.ForeignKey(
                        name: "FK_ScreenActions_DesignDraftVersions_DraftVersionId",
                        column: x => x.DraftVersionId,
                        principalTable: "DesignDraftVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScreenActions_DraftScreens_DraftVersionId_ScreenId",
                        columns: x => new { x.DraftVersionId, x.ScreenId },
                        principalTable: "DraftScreens",
                        principalColumns: new[] { "DraftVersionId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ValidationFindings",
                columns: table => new
                {
                    ValidationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationFindings", x => new { x.ValidationRunId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_ValidationFindings_ValidationRuns_ValidationRunId",
                        column: x => x.ValidationRunId,
                        principalTable: "ValidationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NavigationEdges",
                columns: table => new
                {
                    DraftVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    SourceScreenId = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    TargetScreenId = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    ActionId = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NavigationEdges", x => new { x.DraftVersionId, x.Id });
                    table.ForeignKey(
                        name: "FK_NavigationEdges_DesignDraftVersions_DraftVersionId",
                        column: x => x.DraftVersionId,
                        principalTable: "DesignDraftVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NavigationEdges_DraftScreens_DraftVersionId_SourceScreenId",
                        columns: x => new { x.DraftVersionId, x.SourceScreenId },
                        principalTable: "DraftScreens",
                        principalColumns: new[] { "DraftVersionId", "Id" });
                    table.ForeignKey(
                        name: "FK_NavigationEdges_DraftScreens_DraftVersionId_TargetScreenId",
                        columns: x => new { x.DraftVersionId, x.TargetScreenId },
                        principalTable: "DraftScreens",
                        principalColumns: new[] { "DraftVersionId", "Id" });
                    table.ForeignKey(
                        name: "FK_NavigationEdges_ScreenActions_DraftVersionId_ActionId_Sourc~",
                        columns: x => new { x.DraftVersionId, x.ActionId, x.SourceScreenId },
                        principalTable: "ScreenActions",
                        principalColumns: new[] { "DraftVersionId", "Id", "ScreenId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_WorkspaceId",
                table: "Projects",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_ApprovedRevisionId_ProjectId",
                table: "Approvals",
                columns: new[] { "ApprovedRevisionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedRevisions_ApprovedBy",
                table: "ApprovedRevisions",
                column: "ApprovedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedRevisions_DraftVersionId",
                table: "ApprovedRevisions",
                column: "DraftVersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedRevisions_DraftVersionId_ProjectId",
                table: "ApprovedRevisions",
                columns: new[] { "DraftVersionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedRevisions_ValidationRunId_DraftVersionId",
                table: "ApprovedRevisions",
                columns: new[] { "ValidationRunId", "DraftVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_DesignDraftVersions_BasedOnRevisionId_ProjectId",
                table: "DesignDraftVersions",
                columns: new[] { "BasedOnRevisionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_DesignDraftVersions_ProjectDraftId_ProjectId",
                table: "DesignDraftVersions",
                columns: new[] { "ProjectDraftId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_DesignDraftVersions_ProjectDraftId_Version",
                table: "DesignDraftVersions",
                columns: new[] { "ProjectDraftId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DraftFeatureRequirements_DraftVersionId_RequirementId",
                table: "DraftFeatureRequirements",
                columns: new[] { "DraftVersionId", "RequirementId" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftScreenFeatures_DraftVersionId_FeatureId",
                table: "DraftScreenFeatures",
                columns: new[] { "DraftVersionId", "FeatureId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExportRecords_ApprovedRevisionId_GeneratorVersion",
                table: "ExportRecords",
                columns: new[] { "ApprovedRevisionId", "GeneratorVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExportRecords_CreatedBy",
                table: "ExportRecords",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_NavigationEdges_DraftVersionId_ActionId_SourceScreenId",
                table: "NavigationEdges",
                columns: new[] { "DraftVersionId", "ActionId", "SourceScreenId" });

            migrationBuilder.CreateIndex(
                name: "IX_NavigationEdges_DraftVersionId_SourceScreenId",
                table: "NavigationEdges",
                columns: new[] { "DraftVersionId", "SourceScreenId" });

            migrationBuilder.CreateIndex(
                name: "IX_NavigationEdges_DraftVersionId_TargetScreenId",
                table: "NavigationEdges",
                columns: new[] { "DraftVersionId", "TargetScreenId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDrafts_ApprovedRevisionId_ProjectId",
                table: "ProjectDrafts",
                columns: new[] { "ApprovedRevisionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDrafts_BasedOnRevisionId_ProjectId",
                table: "ProjectDrafts",
                columns: new[] { "BasedOnRevisionId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDrafts_ProjectId",
                table: "ProjectDrafts",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_ProjectId_UserId",
                table: "ProjectMembers",
                columns: new[] { "ProjectId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_UserId",
                table: "ProjectMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScreenActions_DraftVersionId_ScreenId",
                table: "ScreenActions",
                columns: new[] { "DraftVersionId", "ScreenId" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_DraftVersionId",
                table: "ValidationRuns",
                column: "DraftVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_OwnerUserId",
                table: "Workspaces",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Approvals_ApprovedRevisions_ApprovedRevisionId_ProjectId",
                table: "Approvals",
                columns: new[] { "ApprovedRevisionId", "ProjectId" },
                principalTable: "ApprovedRevisions",
                principalColumns: new[] { "Id", "ProjectId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Users_OwnerId",
                table: "Projects",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Workspaces_WorkspaceId",
                table: "Projects",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovedRevisions_DesignDraftVersions_DraftVersionId_Projec~",
                table: "ApprovedRevisions",
                columns: new[] { "DraftVersionId", "ProjectId" },
                principalTable: "DesignDraftVersions",
                principalColumns: new[] { "Id", "ProjectId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovedRevisions_ValidationRuns_ValidationRunId_DraftVersi~",
                table: "ApprovedRevisions",
                columns: new[] { "ValidationRunId", "DraftVersionId" },
                principalTable: "ValidationRuns",
                principalColumns: new[] { "Id", "DraftVersionId" });

            migrationBuilder.Sql(
                """
                CREATE FUNCTION public.reqviora_reject_history_change() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog AS $history$
                BEGIN
                    RAISE EXCEPTION 'Reqviora revision history is immutable' USING ERRCODE = '23514';
                END;
                $history$;
                REVOKE ALL ON FUNCTION public.reqviora_reject_history_change() FROM PUBLIC;

                CREATE TRIGGER "TR_ApprovedRevisions_Immutable"
                BEFORE UPDATE OR DELETE ON public."ApprovedRevisions"
                FOR EACH ROW EXECUTE FUNCTION public.reqviora_reject_history_change();
                CREATE TRIGGER "TR_DesignDraftVersions_Immutable"
                BEFORE UPDATE OR DELETE ON public."DesignDraftVersions"
                FOR EACH ROW EXECUTE FUNCTION public.reqviora_reject_history_change();

                CREATE TRIGGER "TR_ApprovedRevisions_NoTruncate"
                BEFORE TRUNCATE ON public."ApprovedRevisions"
                FOR EACH STATEMENT EXECUTE FUNCTION public.reqviora_reject_history_change();
                CREATE TRIGGER "TR_DesignDraftVersions_NoTruncate"
                BEFORE TRUNCATE ON public."DesignDraftVersions"
                FOR EACH STATEMENT EXECUTE FUNCTION public.reqviora_reject_history_change();

                DO $private_tables$
                DECLARE
                    private_tables text[] := ARRAY[
                            'Users', 'RefreshTokens', 'Roles', 'Projects', '__EFMigrationsHistory',
                            'Approvals', 'Blueprints', 'Components', 'Features',
                            'NavigationRoutes', 'ProjectDocuments', 'Requirements', 'Screens',
                            'ProjectMembers', 'Workspaces', 'ApprovedRevisions', 'ExportRecords',
                            'ProjectDrafts', 'DesignDraftVersions', 'DraftFeatures', 'DraftRequirements',
                            'DraftScreens', 'ValidationRuns', 'DraftFeatureRequirements', 'RequirementSourceEvidence',
                            'DraftScreenFeatures', 'ScreenActions', 'ValidationFindings', 'NavigationEdges'];
                    table_name text;
                    role_name text;
                BEGIN
                    FOREACH table_name IN ARRAY private_tables
                    LOOP
                        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', table_name);
                        EXECUTE format('REVOKE ALL ON TABLE public.%I FROM PUBLIC', table_name);
                    END LOOP;

                    FOR role_name IN SELECT rolname::text FROM pg_catalog.pg_roles
                        WHERE rolname IN ('anon', 'authenticated', 'service_role')
                    LOOP
                        EXECUTE format('REVOKE ALL ON FUNCTION public.reqviora_reject_history_change() FROM %I', role_name);
                        FOREACH table_name IN ARRAY private_tables
                        LOOP
                            EXECUTE format('REVOKE ALL ON TABLE public.%I FROM %I', table_name, role_name);
                        END LOOP;
                    END LOOP;
                END;
                $private_tables$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Approvals_ApprovedRevisions_ApprovedRevisionId_ProjectId",
                table: "Approvals");

            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Users_OwnerId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Workspaces_WorkspaceId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovedRevisions_DesignDraftVersions_DraftVersionId_Projec~",
                table: "ApprovedRevisions");

            migrationBuilder.DropForeignKey(
                name: "FK_ValidationRuns_DesignDraftVersions_DraftVersionId",
                table: "ValidationRuns");

            migrationBuilder.DropTable(
                name: "DraftFeatureRequirements");

            migrationBuilder.DropTable(
                name: "DraftScreenFeatures");

            migrationBuilder.DropTable(
                name: "ExportRecords");

            migrationBuilder.DropTable(
                name: "NavigationEdges");

            migrationBuilder.DropTable(
                name: "ProjectMembers");

            migrationBuilder.DropTable(
                name: "RequirementSourceEvidence");

            migrationBuilder.DropTable(
                name: "ValidationFindings");

            migrationBuilder.DropTable(
                name: "Workspaces");

            migrationBuilder.DropTable(
                name: "DraftFeatures");

            migrationBuilder.DropTable(
                name: "ScreenActions");

            migrationBuilder.DropTable(
                name: "DraftRequirements");

            migrationBuilder.DropTable(
                name: "DraftScreens");

            migrationBuilder.DropTable(
                name: "DesignDraftVersions");

            migrationBuilder.DropTable(
                name: "ProjectDrafts");

            migrationBuilder.DropTable(
                name: "ApprovedRevisions");

            migrationBuilder.DropTable(
                name: "ValidationRuns");

            migrationBuilder.DropIndex(
                name: "IX_Projects_WorkspaceId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Approvals_ApprovedRevisionId_ProjectId",
                table: "Approvals");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ApprovedRevisionId",
                table: "Approvals");

            migrationBuilder.Sql("DROP FUNCTION public.reqviora_reject_history_change();");
        }
    }
}
