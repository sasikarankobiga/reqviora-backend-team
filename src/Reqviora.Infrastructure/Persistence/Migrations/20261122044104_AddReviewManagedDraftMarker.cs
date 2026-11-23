using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewManagedDraftMarker : Migration
    {
        /// <inheritdoc />

protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<bool>(
        name: "IsReviewManaged",
        table: "ProjectDrafts",
        type: "boolean",
        nullable: false,
        defaultValue: false);

    // Backfill legacy projects that already have
    // authoritative requirement review records.
    migrationBuilder.Sql(
        """
        DO $review_backfill$
        BEGIN
            IF to_regclass('reqviora_review.requirements')
                IS NOT NULL THEN

                UPDATE "ProjectDrafts" AS draft
                SET "IsReviewManaged" = TRUE
                WHERE EXISTS
                (
                    SELECT 1
                    FROM reqviora_review.requirements AS review
                    WHERE review.project_id = draft."ProjectId"
                );

            END IF;
        END
        $review_backfill$;
        """);
}

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsReviewManaged",
                table: "ProjectDrafts");
        }
    }
}
