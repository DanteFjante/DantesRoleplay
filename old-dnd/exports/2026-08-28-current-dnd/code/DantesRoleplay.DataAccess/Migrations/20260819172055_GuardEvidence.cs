using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class GuardEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GuardEvidenceJson",
                table: "operation",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuardEvidenceJson",
                table: "operation");
        }
    }
}
