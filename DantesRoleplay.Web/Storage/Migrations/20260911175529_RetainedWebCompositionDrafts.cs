using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.Web.Storage.Migrations
{
    /// <inheritdoc />
    public partial class RetainedWebCompositionDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_web_page_active_revision",
                table: "web_page");

            migrationBuilder.AddColumn<string>(
                name: "CompositionHash",
                table: "web_page_revision",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompositionJson",
                table: "web_page_revision",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentFormat",
                table: "web_page_revision",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "html");

            migrationBuilder.AddCheckConstraint(
                name: "CK_web_page_revision_content",
                table: "web_page_revision",
                sql: "(\"ContentFormat\" = 'html' AND \"CompositionJson\" IS NULL AND \"CompositionHash\" IS NULL)\r\nOR (\"ContentFormat\" = 'composition-v1' AND \"Html\" = '' AND \"CompositionJson\" IS NOT NULL\r\n    AND json_valid(\"CompositionJson\") AND length(CAST(\"CompositionJson\" AS BLOB)) <= 1048576\r\n    AND \"CompositionHash\" IS NOT NULL AND length(\"CompositionHash\") = 64\r\n    AND \"CompositionHash\" NOT GLOB '*[^0-9A-F]*')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_web_page_active_revision",
                table: "web_page",
                sql: "\"ActiveRevision\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Older hosts cannot represent retained compositions or never-published pages.
            // Refuse a destructive downgrade before dropping their content columns.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE __web_composition_downgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT composition_or_inert_draft_prevents_downgrade CHECK (blocked = 0)
                );
                INSERT INTO __web_composition_downgrade_guard (blocked)
                SELECT 1 WHERE EXISTS (SELECT 1 FROM web_page WHERE ActiveRevision = 0)
                    OR EXISTS (SELECT 1 FROM web_page_revision WHERE ContentFormat <> 'html');
                DROP TABLE __web_composition_downgrade_guard;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_web_page_revision_content",
                table: "web_page_revision");

            migrationBuilder.DropCheckConstraint(
                name: "CK_web_page_active_revision",
                table: "web_page");

            migrationBuilder.DropColumn(
                name: "CompositionHash",
                table: "web_page_revision");

            migrationBuilder.DropColumn(
                name: "CompositionJson",
                table: "web_page_revision");

            migrationBuilder.DropColumn(
                name: "ContentFormat",
                table: "web_page_revision");

            migrationBuilder.AddCheckConstraint(
                name: "CK_web_page_active_revision",
                table: "web_page",
                sql: "\"ActiveRevision\" > 0");
        }
    }
}
