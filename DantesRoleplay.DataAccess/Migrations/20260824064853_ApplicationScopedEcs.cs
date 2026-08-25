using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationScopedEcs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_state_space",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    ApplicationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    ManifestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_state_space", x => x.Id);
                    table.CheckConstraint("CK_system_state_space_manifest", "length(\"ManifestFingerprint\") = 64 AND \"ManifestFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_state_space_revision", "\"ApplicationRevision\" > 0");
                    table.ForeignKey(
                        name: "FK_system_state_space_system_application_revision_ApplicationId_ApplicationRevision",
                        columns: x => new { x.ApplicationId, x.ApplicationRevision },
                        principalTable: "system_application_revision",
                        principalColumns: new[] { "ApplicationId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_ecs_entity",
                columns: table => new
                {
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_ecs_entity", x => new { x.StateSpaceId, x.Id });
                    table.CheckConstraint("CK_system_ecs_entity_revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_system_ecs_entity_system_state_space_StateSpaceId",
                        column: x => x.StateSpaceId,
                        principalTable: "system_state_space",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_ecs_component",
                columns: table => new
                {
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    QualifiedTypeId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TypeVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SchemaHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Data = table.Column<string>(type: "TEXT", maxLength: 1048576, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_ecs_component", x => new { x.StateSpaceId, x.EntityId, x.QualifiedTypeId });
                    table.CheckConstraint("CK_system_ecs_component_data", "json_valid(\"Data\")");
                    table.CheckConstraint("CK_system_ecs_component_hash", "length(\"SchemaHash\") = 64 AND \"SchemaHash\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_ecs_component_revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_system_ecs_component_type_version", "\"TypeVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_system_ecs_component_system_component_type_version_QualifiedTypeId_TypeVersion",
                        columns: x => new { x.QualifiedTypeId, x.TypeVersion },
                        principalTable: "system_component_type_version",
                        principalColumns: new[] { "QualifiedId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_ecs_component_system_ecs_entity_StateSpaceId_EntityId",
                        columns: x => new { x.StateSpaceId, x.EntityId },
                        principalTable: "system_ecs_entity",
                        principalColumns: new[] { "StateSpaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_ecs_component_QualifiedTypeId_TypeVersion",
                table: "system_ecs_component",
                columns: new[] { "QualifiedTypeId", "TypeVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_system_ecs_component_StateSpaceId_QualifiedTypeId",
                table: "system_ecs_component",
                columns: new[] { "StateSpaceId", "QualifiedTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_system_state_space_ApplicationId_ApplicationRevision",
                table: "system_state_space",
                columns: new[] { "ApplicationId", "ApplicationRevision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new InvalidOperationException(
                "Application-scoped ECS state is immutable runtime evidence. Restore a database backup rather than "
                + "downgrading this migration and deleting state-space/component history.");
    }
}
