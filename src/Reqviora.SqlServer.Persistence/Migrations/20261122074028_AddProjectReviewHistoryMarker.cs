using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reqviora.SqlServer.Persistence.Migrations
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
        type: "bit",
        nullable: false,
        defaultValue: false);

    // Protect legacy projects whose drafts were review-managed.
    migrationBuilder.Sql(
        """
        UPDATE project
        SET project.[HasRequirementReviewHistory] = 1
        FROM [Projects] AS project
        WHERE EXISTS
        (
            SELECT 1
            FROM [ProjectDrafts] AS draft
            WHERE draft.[ProjectId] = project.[Id]
              AND draft.[IsReviewManaged] = 1
        );
        """);

    // Protect projects with existing requirement review records.
    // Dynamic SQL avoids referencing an absent review table.
    migrationBuilder.Sql(
        """
        IF OBJECT_ID(
            N'reqviora_review.requirements',
            N'U'
        ) IS NOT NULL
        BEGIN
            EXEC sys.sp_executesql N'
                UPDATE project
                SET project.[HasRequirementReviewHistory] = 1
                FROM [Projects] AS project
                WHERE EXISTS
                (
                    SELECT 1
                    FROM [reqviora_review].[requirements] AS review
                    WHERE review.[project_id] = project.[Id]
                );
            ';
        END;
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
