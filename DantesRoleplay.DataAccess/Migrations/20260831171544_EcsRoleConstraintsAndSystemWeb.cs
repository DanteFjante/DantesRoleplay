using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class EcsRoleConstraintsAndSystemWeb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "system_state_space_binding_revision",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "runtime-state-space");

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "system_state_space",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "runtime-state-space");

            migrationBuilder.CreateIndex(
                name: "IX_system_state_space_ApplicationId",
                table: "system_state_space",
                column: "ApplicationId",
                unique: true,
                filter: "\"Scope\" = 'application-publication'");

            migrationBuilder.Sql("""
                PRAGMA ignore_check_constraints = ON;
                INSERT OR IGNORE INTO system_application (Id, DisplayName, Description, CreatedAtUtc)
                VALUES ('system', 'System contracts', 'Reserved relational owner for kernel component contracts.', '2026-08-31T00:00:00Z');
                PRAGMA ignore_check_constraints = OFF;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM system_application WHERE Id = 'system';");

            migrationBuilder.DropIndex(
                name: "IX_system_state_space_ApplicationId",
                table: "system_state_space");

            // Native SQLite DROP COLUMN remains transactional; EF's emulated table rebuild does not.
            migrationBuilder.Sql("ALTER TABLE system_state_space_binding_revision DROP COLUMN Scope;");
            migrationBuilder.Sql("ALTER TABLE system_state_space DROP COLUMN Scope;");
        }
    }
}
