using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.SqlServer.Persistence.Migrations
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
        type: "bit",
        nullable: false,
        defaultValue: false);

    // Backfill existing projects that have requirement reviews.
    // The review schema might not exist in manual-only installations.
    migrationBuilder.Sql(
        """
        IF OBJECT_ID(
            N'reqviora_review.requirements',
            N'U'
        ) IS NOT NULL
        BEGIN
            EXEC sys.sp_executesql N'
                UPDATE draft
                SET draft.[IsReviewManaged] = 1
                FROM [dbo].[ProjectDrafts] AS draft
                WHERE EXISTS
                (
                    SELECT 1
                    FROM [reqviora_review].[requirements] AS review
                    WHERE review.[project_id] = draft.[ProjectId]
                );
            ';
        END;
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
