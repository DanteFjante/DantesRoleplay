using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationScopedEdgesAndLegacyAdoption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_ecs_containment",
                columns: table => new
                {
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContainedEntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContainerEntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slot = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_ecs_containment", x => new { x.StateSpaceId, x.ContainedEntityId });
                    table.CheckConstraint("CK_system_ecs_containment_revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_system_ecs_containment_system_ecs_entity_StateSpaceId_ContainedEntityId",
                        columns: x => new { x.StateSpaceId, x.ContainedEntityId },
                        principalTable: "system_ecs_entity",
                        principalColumns: new[] { "StateSpaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_ecs_containment_system_ecs_entity_StateSpaceId_ContainerEntityId",
                        columns: x => new { x.StateSpaceId, x.ContainerEntityId },
                        principalTable: "system_ecs_entity",
                        principalColumns: new[] { "StateSpaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_ecs_relationship",
                columns: table => new
                {
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FromEntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ToEntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    QualifiedKind = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Data = table.Column<string>(type: "TEXT", maxLength: 1048576, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_ecs_relationship", x => new { x.StateSpaceId, x.FromEntityId, x.ToEntityId, x.QualifiedKind });
                    table.CheckConstraint("CK_system_ecs_relationship_data", "json_valid(\"Data\")");
                    table.CheckConstraint("CK_system_ecs_relationship_revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_system_ecs_relationship_system_ecs_entity_StateSpaceId_FromEntityId",
                        columns: x => new { x.StateSpaceId, x.FromEntityId },
                        principalTable: "system_ecs_entity",
                        principalColumns: new[] { "StateSpaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_ecs_relationship_system_ecs_entity_StateSpaceId_ToEntityId",
                        columns: x => new { x.StateSpaceId, x.ToEntityId },
                        principalTable: "system_ecs_entity",
                        principalColumns: new[] { "StateSpaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_legacy_state_adoption",
                columns: table => new
                {
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EvidenceFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ComponentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ContainmentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RelationshipCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_legacy_state_adoption", x => x.StateSpaceId);
                    table.CheckConstraint("CK_system_legacy_state_adoption_counts", "\"EntityCount\" >= 0 AND \"ComponentCount\" >= 0 AND \"ContainmentCount\" >= 0 AND \"RelationshipCount\" >= 0");
                    table.CheckConstraint("CK_system_legacy_state_adoption_fingerprints", "length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"SourceFingerprint\") = 64 AND \"SourceFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"EvidenceFingerprint\") = 64 AND \"EvidenceFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.ForeignKey(
                        name: "FK_system_legacy_state_adoption_operation_OperationId",
                        column: x => x.OperationId,
                        principalTable: "operation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_legacy_state_adoption_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_legacy_state_adoption_system_state_space_StateSpaceId",
                        column: x => x.StateSpaceId,
                        principalTable: "system_state_space",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_ecs_containment_StateSpaceId_ContainerEntityId",
                table: "system_ecs_containment",
                columns: new[] { "StateSpaceId", "ContainerEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_system_ecs_relationship_StateSpaceId_QualifiedKind",
                table: "system_ecs_relationship",
                columns: new[] { "StateSpaceId", "QualifiedKind" });

            migrationBuilder.CreateIndex(
                name: "IX_system_ecs_relationship_StateSpaceId_ToEntityId",
                table: "system_ecs_relationship",
                columns: new[] { "StateSpaceId", "ToEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_system_legacy_state_adoption_ApplicationId",
                table: "system_legacy_state_adoption",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_system_legacy_state_adoption_OperationId",
                table: "system_legacy_state_adoption",
                column: "OperationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_legacy_state_adoption");

            migrationBuilder.DropTable(
                name: "system_ecs_containment");

            migrationBuilder.DropTable(
                name: "system_ecs_relationship");
        }
    }
}
