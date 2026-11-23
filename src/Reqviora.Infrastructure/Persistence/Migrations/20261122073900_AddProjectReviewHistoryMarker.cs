using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectReviewHistoryMarker : Migration
    {
        /// <inheritdoc />

protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<bool>(
        name: "HasRequirementReviewHistory",
        table: "Projects",
        type: "boolean",
        nullable: false,
        defaultValue: false);

    // Preserve history for previously review-managed drafts.
    migrationBuilder.Sql(
        """
        UPDATE "Projects" AS project
        SET "HasRequirementReviewHistory" = TRUE
        WHERE EXISTS
        (
            SELECT 1
            FROM "ProjectDrafts" AS draft
            WHERE draft."ProjectId" = project."Id"
              AND draft."IsReviewManaged" = TRUE
        );
        """);

    // Also protect projects with existing review records,
    // including projects without a draft.
    migrationBuilder.Sql(
        """
        DO $project_review_backfill$
        BEGIN
            IF to_regclass('reqviora_review.requirements')
                IS NOT NULL THEN

                UPDATE "Projects" AS project
                SET "HasRequirementReviewHistory" = TRUE
                WHERE EXISTS
                (
                    SELECT 1
                    FROM reqviora_review.requirements AS review
                    WHERE review.project_id = project."Id"
                );

            END IF;
        END
        $project_review_backfill$;
        """);
}


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasRequirementReviewHistory",
                table: "Projects");
        }
    }
}
