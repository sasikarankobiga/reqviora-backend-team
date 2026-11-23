using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.SqlServer.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Blueprints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blueprints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Components",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlueprintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PropertiesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Components", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Features",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Features", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NavigationRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NavigationRoutes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requirements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Screens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Screens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Projects_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProjectMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMembers", x => x.Id);
                    table.CheckConstraint("CK_ProjectMembers_Role", "TRIM([Role]) <> ''");
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
                name: "Approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Approvals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovedRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneratorVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ManifestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ArchiveSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Archive = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BasedOnRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDrafts", x => x.Id);
                    table.UniqueConstraint("AK_ProjectDrafts_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_ProjectDrafts_Version", "[Version] > 0");
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    DraftJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlueprintJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BasedOnRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SavedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesignDraftVersions", x => x.Id);
                    table.UniqueConstraint("AK_DesignDraftVersions_Id_ProjectId", x => new { x.Id, x.ProjectId });
                    table.CheckConstraint("CK_DesignDraftVersions_Version", "[Version] > 0");
                    table.ForeignKey(
                        name: "FK_DesignDraftVersions_ApprovedRevisions_BasedOnRevisionId_ProjectId",
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
                    DraftVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                    DraftVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                    DraftVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                    DraftVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeatureId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequirementId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2")
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
                        name: "FK_DraftFeatureRequirements_DraftFeatures_DraftVersionId_FeatureId",
                        columns: x => new { x.DraftVersionId, x.FeatureId },
                        principalTable: "DraftFeatures",
                        principalColumns: new[] { "DraftVersionId", "Id" });
                    table.ForeignKey(
                        name: "FK_DraftFeatureRequirements_DraftRequirements_DraftVersionId_RequirementId",
                        columns: x => new { x.DraftVersionId, x.RequirementId },
                        principalTable: "DraftRequirements",
                        principalColumns: new[] { "DraftVersionId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "RequirementSourceEvidence",
                columns: table => new
                {
                    DraftVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequirementId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Locator = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quote = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
                        name: "FK_RequirementSourceEvidence_DraftRequirements_DraftVersionId_RequirementId",
                        columns: x => new { x.DraftVersionId, x.RequirementId },
                        principalTable: "DraftRequirements",
                        principalColumns: new[] { "DraftVersionId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "DraftScreenFeatures",
                columns: table => new
                {
                    DraftVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScreenId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    FeatureId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2")
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
                    DraftVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScreenId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ComponentId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                    ValidationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                    DraftVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceScreenId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TargetScreenId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActionId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2")
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
                        name: "FK_NavigationEdges_ScreenActions_DraftVersionId_ActionId_SourceScreenId",
                        columns: x => new { x.DraftVersionId, x.ActionId, x.SourceScreenId },
                        principalTable: "ScreenActions",
                        principalColumns: new[] { "DraftVersionId", "Id", "ScreenId" });
                });

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
                name: "IX_Projects_OwnerId",
                table: "Projects",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_WorkspaceId",
                table: "Projects",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_ExpiresAt",
                table: "RefreshTokens",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScreenActions_DraftVersionId_ScreenId",
                table: "ScreenActions",
                columns: new[] { "DraftVersionId", "ScreenId" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);

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
                name: "FK_ApprovedRevisions_DesignDraftVersions_DraftVersionId_ProjectId",
                table: "ApprovedRevisions",
                columns: new[] { "DraftVersionId", "ProjectId" },
                principalTable: "DesignDraftVersions",
                principalColumns: new[] { "Id", "ProjectId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovedRevisions_ValidationRuns_ValidationRunId_DraftVersionId",
                table: "ApprovedRevisions",
                columns: new[] { "ValidationRunId", "DraftVersionId" },
                principalTable: "ValidationRuns",
                principalColumns: new[] { "Id", "DraftVersionId" });

            migrationBuilder.Sql(
                """
                EXEC(N'CREATE TRIGGER [dbo].[TR_ApprovedRevisions_Immutable]
                    ON [dbo].[ApprovedRevisions] INSTEAD OF UPDATE, DELETE AS
                    BEGIN
                        THROW 51000, N''Reqviora revision history is immutable'', 1;
                    END');
                EXEC(N'CREATE TRIGGER [dbo].[TR_DesignDraftVersions_Immutable]
                    ON [dbo].[DesignDraftVersions] INSTEAD OF UPDATE, DELETE AS
                    BEGIN
                        THROW 51000, N''Reqviora revision history is immutable'', 1;
                    END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DesignDraftVersions_ApprovedRevisions_BasedOnRevisionId_ProjectId",
                table: "DesignDraftVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectDrafts_ApprovedRevisions_ApprovedRevisionId_ProjectId",
                table: "ProjectDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectDrafts_ApprovedRevisions_BasedOnRevisionId_ProjectId",
                table: "ProjectDrafts");

            migrationBuilder.DropTable(
                name: "Approvals");

            migrationBuilder.DropTable(
                name: "Blueprints");

            migrationBuilder.DropTable(
                name: "Components");

            migrationBuilder.DropTable(
                name: "DraftFeatureRequirements");

            migrationBuilder.DropTable(
                name: "DraftScreenFeatures");

            migrationBuilder.DropTable(
                name: "ExportRecords");

            migrationBuilder.DropTable(
                name: "Features");

            migrationBuilder.DropTable(
                name: "NavigationEdges");

            migrationBuilder.DropTable(
                name: "NavigationRoutes");

            migrationBuilder.DropTable(
                name: "ProjectDocuments");

            migrationBuilder.DropTable(
                name: "ProjectMembers");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Requirements");

            migrationBuilder.DropTable(
                name: "RequirementSourceEvidence");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Screens");

            migrationBuilder.DropTable(
                name: "ValidationFindings");

            migrationBuilder.DropTable(
                name: "DraftFeatures");

            migrationBuilder.DropTable(
                name: "ScreenActions");

            migrationBuilder.DropTable(
                name: "DraftRequirements");

            migrationBuilder.DropTable(
                name: "DraftScreens");

            migrationBuilder.DropTable(
                name: "ApprovedRevisions");

            migrationBuilder.DropTable(
                name: "ValidationRuns");

            migrationBuilder.DropTable(
                name: "DesignDraftVersions");

            migrationBuilder.DropTable(
                name: "ProjectDrafts");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Workspaces");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
