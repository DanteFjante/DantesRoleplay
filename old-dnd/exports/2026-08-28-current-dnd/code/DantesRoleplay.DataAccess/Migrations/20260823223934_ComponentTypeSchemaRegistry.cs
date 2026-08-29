using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ComponentTypeSchemaRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_component_type",
                columns: table => new
                {
                    QualifiedId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_component_type", x => x.QualifiedId);
                    table.ForeignKey(
                        name: "FK_system_component_type_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_component_type_version",
                columns: table => new
                {
                    QualifiedId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    SchemaHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_component_type_version", x => new { x.QualifiedId, x.Version });
                    table.CheckConstraint("CK_system_component_type_version_hash", "length(\"SchemaHash\") = 64 AND \"SchemaHash\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_component_type_version_number", "\"Version\" > 0");
                    table.CheckConstraint("CK_system_component_type_version_profile", "\"ProfileId\" = 'system-json-schema-2020-12/v1'");
                    table.CheckConstraint("CK_system_component_type_version_schema_json", "json_valid(\"SchemaJson\")");
                    table.ForeignKey(
                        name: "FK_system_component_type_version_system_component_type_QualifiedId",
                        column: x => x.QualifiedId,
                        principalTable: "system_component_type",
                        principalColumn: "QualifiedId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_component_type_ApplicationId",
                table: "system_component_type",
                column: "ApplicationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new InvalidOperationException(
                "Component type versions are immutable application contracts. Restore a database backup rather than "
                + "downgrading this migration and deleting schema history.");
    }
}
