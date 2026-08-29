using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ProjectionMaterialization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_projection_definition",
                columns: table => new
                {
                    QualifiedId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_projection_definition", x => x.QualifiedId);
                    table.ForeignKey(
                        name: "FK_system_projection_definition_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_projection_definition_version",
                columns: table => new
                {
                    QualifiedId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OutputSchemaJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    OutputSchemaHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_projection_definition_version", x => new { x.QualifiedId, x.Version });
                    table.CheckConstraint("CK_system_projection_definition_version_content_hash", "length(\"ContentHash\") = 64 AND \"ContentHash\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_projection_definition_version_number", "\"Version\" > 0");
                    table.CheckConstraint("CK_system_projection_definition_version_output_hash", "length(\"OutputSchemaHash\") = 64 AND \"OutputSchemaHash\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_projection_definition_version_schema", "json_valid(\"OutputSchemaJson\")");
                    table.ForeignKey(
                        name: "FK_system_projection_definition_version_system_projection_definition_QualifiedId",
                        column: x => x.QualifiedId,
                        principalTable: "system_projection_definition",
                        principalColumn: "QualifiedId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_projection_component_input",
                columns: table => new
                {
                    QualifiedId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    InputId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EntityRole = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    QualifiedTypeId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TypeVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SchemaHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_projection_component_input", x => new { x.QualifiedId, x.Version, x.InputId });
                    table.ForeignKey(
                        name: "FK_system_projection_component_input_system_component_type_version_QualifiedTypeId_TypeVersion",
                        columns: x => new { x.QualifiedTypeId, x.TypeVersion },
                        principalTable: "system_component_type_version",
                        principalColumns: new[] { "QualifiedId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_projection_component_input_system_projection_definition_version_QualifiedId_Version",
                        columns: x => new { x.QualifiedId, x.Version },
                        principalTable: "system_projection_definition_version",
                        principalColumns: new[] { "QualifiedId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_projection_dependency_input",
                columns: table => new
                {
                    QualifiedId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    InputId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DependencyQualifiedId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DependencyVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    DependencyContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RoleBindingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_projection_dependency_input", x => new { x.QualifiedId, x.Version, x.InputId });
                    table.CheckConstraint("CK_system_projection_dependency_input_role_bindings", "json_valid(\"RoleBindingsJson\")");
                    table.ForeignKey(
                        name: "FK_system_projection_dependency_input_system_projection_definition_version_DependencyQualifiedId_DependencyVersion",
                        columns: x => new { x.DependencyQualifiedId, x.DependencyVersion },
                        principalTable: "system_projection_definition_version",
                        principalColumns: new[] { "QualifiedId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_projection_dependency_input_system_projection_definition_version_QualifiedId_Version",
                        columns: x => new { x.QualifiedId, x.Version },
                        principalTable: "system_projection_definition_version",
                        principalColumns: new[] { "QualifiedId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_projection_mapping",
                columns: table => new
                {
                    QualifiedId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetPointer = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    InputId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourcePointer = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_projection_mapping", x => new { x.QualifiedId, x.Version, x.TargetPointer });
                    table.ForeignKey(
                        name: "FK_system_projection_mapping_system_projection_definition_version_QualifiedId_Version",
                        columns: x => new { x.QualifiedId, x.Version },
                        principalTable: "system_projection_definition_version",
                        principalColumns: new[] { "QualifiedId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_projection_component_input_QualifiedTypeId_TypeVersion",
                table: "system_projection_component_input",
                columns: new[] { "QualifiedTypeId", "TypeVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_system_projection_definition_ApplicationId",
                table: "system_projection_definition",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_system_projection_definition_version_QualifiedId_ContentHash",
                table: "system_projection_definition_version",
                columns: new[] { "QualifiedId", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_projection_dependency_input_DependencyQualifiedId_DependencyVersion",
                table: "system_projection_dependency_input",
                columns: new[] { "DependencyQualifiedId", "DependencyVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_system_projection_mapping_QualifiedId_Version_Ordinal",
                table: "system_projection_mapping",
                columns: new[] { "QualifiedId", "Version", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new InvalidOperationException(
                "Projection definition history is immutable runtime evidence. Restore a database backup rather than "
                + "downgrading this migration and deleting projection contracts.");
    }
}
