using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationObjectChangeDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_application_object_change",
                columns: table => new
                {
                    Cursor = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContractVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ObjectQualifiedId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ObjectVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    ReadPerspectivesJson = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_object_change", x => x.Cursor);
                    table.CheckConstraint("CK_system_application_object_change_contract", "\"ContractVersion\" = 1");
                    table.CheckConstraint("CK_system_application_object_change_perspectives", "json_valid(\"ReadPerspectivesJson\") AND json_type(\"ReadPerspectivesJson\") = 'array'");
                    table.CheckConstraint("CK_system_application_object_change_scope", "\"Scope\" IN ('object', 'application', 'none')");
                    table.CheckConstraint("CK_system_application_object_change_target", "(\"Scope\" = 'object' AND \"ObjectQualifiedId\" IS NOT NULL AND \"ObjectVersion\" > 0) OR (\"Scope\" IN ('application', 'none') AND \"ObjectQualifiedId\" IS NULL AND \"ObjectVersion\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_system_application_object_change_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_application_object_change_system_state_space_StateSpaceId",
                        column: x => x.StateSpaceId,
                        principalTable: "system_state_space",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_object_change_ApplicationId_StateSpaceId_Cursor",
                table: "system_application_object_change",
                columns: new[] { "ApplicationId", "StateSpaceId", "Cursor" });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_object_change_OperationId_Scope_ObjectQualifiedId_ObjectVersion",
                table: "system_application_object_change",
                columns: new[] { "OperationId", "Scope", "ObjectQualifiedId", "ObjectVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_application_object_change_StateSpaceId",
                table: "system_application_object_change",
                column: "StateSpaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_application_object_change");
        }
    }
}
