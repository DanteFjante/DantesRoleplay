using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ActionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MechanicId",
                table: "operation",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MechanicVersion",
                table: "operation",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectionJson",
                table: "operation",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "Seed",
                table: "operation",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MechanicId",
                table: "operation");

            migrationBuilder.DropColumn(
                name: "MechanicVersion",
                table: "operation");

            migrationBuilder.DropColumn(
                name: "ProjectionJson",
                table: "operation");

            migrationBuilder.DropColumn(
                name: "Seed",
                table: "operation");
        }
    }
}
