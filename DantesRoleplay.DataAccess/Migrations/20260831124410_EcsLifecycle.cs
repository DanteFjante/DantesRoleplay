using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class EcsLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_system_component_type_ApplicationId",
                table: "system_component_type");

            migrationBuilder.AddColumn<DateTime>(
                name: "DisabledAtUtc",
                table: "system_component_type",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_component_type_ApplicationId_DisabledAtUtc_QualifiedId",
                table: "system_component_type",
                columns: new[] { "ApplicationId", "DisabledAtUtc", "QualifiedId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_system_component_type_ApplicationId_DisabledAtUtc_QualifiedId",
                table: "system_component_type");

            migrationBuilder.DropColumn(
                name: "DisabledAtUtc",
                table: "system_component_type");

            migrationBuilder.CreateIndex(
                name: "IX_system_component_type_ApplicationId",
                table: "system_component_type",
                column: "ApplicationId");
        }
    }
}
