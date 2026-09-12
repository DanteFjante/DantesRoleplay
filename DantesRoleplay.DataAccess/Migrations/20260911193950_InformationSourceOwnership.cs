using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InformationSourceOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_information_source_target_identity",
                columns: table => new
                {
                    QualifiedTargetId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedByOperationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_information_source_target_identity", x => x.QualifiedTargetId);
                    table.UniqueConstraint("AK_system_information_source_target_identity_QualifiedTargetId_SourceId", x => new { x.QualifiedTargetId, x.SourceId });
                    table.ForeignKey(
                        name: "FK_system_information_source_target_identity_information_source_SourceId",
                        column: x => x.SourceId,
                        principalTable: "information_source",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_information_source_target_identity_operation_CreatedByOperationId",
                        column: x => x.CreatedByOperationId,
                        principalTable: "operation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_information_source_owner_revision",
                columns: table => new
                {
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    QualifiedTargetId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContentFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreviousFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    BoundByOperationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_information_source_owner_revision", x => new { x.SourceId, x.Revision });
                    table.UniqueConstraint("AK_system_information_source_owner_revision_SourceId_Revision_QualifiedTargetId", x => new { x.SourceId, x.Revision, x.QualifiedTargetId });
                    table.CheckConstraint("CK_system_information_source_owner_revision_hashes", "length(\"ContentFingerprint\") = 64 AND \"ContentFingerprint\" NOT GLOB '*[^0-9A-F]*' AND (\"PreviousFingerprint\" IS NULL OR length(\"PreviousFingerprint\") = 64 AND \"PreviousFingerprint\" NOT GLOB '*[^0-9A-F]*')");
                    table.CheckConstraint("CK_system_information_source_owner_revision_positive", "\"Revision\" > 0");
                    table.CheckConstraint("CK_system_information_source_owner_revision_previous", "(\"Revision\" = 1 AND \"PreviousFingerprint\" IS NULL) OR (\"Revision\" > 1 AND \"PreviousFingerprint\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_system_information_source_owner_revision_information_source_SourceId",
                        column: x => x.SourceId,
                        principalTable: "information_source",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_information_source_owner_revision_operation_BoundByOperationId",
                        column: x => x.BoundByOperationId,
                        principalTable: "operation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_information_source_owner_revision_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_information_source_owner_revision_system_information_source_target_identity_QualifiedTargetId_SourceId",
                        columns: x => new { x.QualifiedTargetId, x.SourceId },
                        principalTable: "system_information_source_target_identity",
                        principalColumns: new[] { "QualifiedTargetId", "SourceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_information_source_owner_current",
                columns: table => new
                {
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    QualifiedTargetId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_information_source_owner_current", x => x.SourceId);
                    table.CheckConstraint("CK_system_information_source_owner_current_revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_system_information_source_owner_current_information_source_SourceId",
                        column: x => x.SourceId,
                        principalTable: "information_source",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_information_source_owner_current_system_information_source_owner_revision_SourceId_Revision_QualifiedTargetId",
                        columns: x => new { x.SourceId, x.Revision, x.QualifiedTargetId },
                        principalTable: "system_information_source_owner_revision",
                        principalColumns: new[] { "SourceId", "Revision", "QualifiedTargetId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_information_source_owner_current_QualifiedTargetId",
                table: "system_information_source_owner_current",
                column: "QualifiedTargetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_information_source_owner_current_SourceId_Revision_QualifiedTargetId",
                table: "system_information_source_owner_current",
                columns: new[] { "SourceId", "Revision", "QualifiedTargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_system_information_source_owner_revision_ApplicationId",
                table: "system_information_source_owner_revision",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_system_information_source_owner_revision_BoundByOperationId",
                table: "system_information_source_owner_revision",
                column: "BoundByOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_system_information_source_owner_revision_QualifiedTargetId_SourceId",
                table: "system_information_source_owner_revision",
                columns: new[] { "QualifiedTargetId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_system_information_source_target_identity_CreatedByOperationId",
                table: "system_information_source_target_identity",
                column: "CreatedByOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_system_information_source_target_identity_SourceId",
                table: "system_information_source_target_identity",
                column: "SourceId");

            migrationBuilder.Sql("""
                CREATE TRIGGER system_information_source_target_identity_immutable_update
                BEFORE UPDATE ON system_information_source_target_identity
                BEGIN SELECT RAISE(ABORT, 'INFORMATION_SOURCE_OWNERSHIP_IMMUTABLE'); END;
                CREATE TRIGGER system_information_source_target_identity_immutable_delete
                BEFORE DELETE ON system_information_source_target_identity
                BEGIN SELECT RAISE(ABORT, 'INFORMATION_SOURCE_OWNERSHIP_IMMUTABLE'); END;
                CREATE TRIGGER system_information_source_owner_revision_immutable_update
                BEFORE UPDATE ON system_information_source_owner_revision
                BEGIN SELECT RAISE(ABORT, 'INFORMATION_SOURCE_OWNERSHIP_IMMUTABLE'); END;
                CREATE TRIGGER system_information_source_owner_revision_immutable_delete
                BEFORE DELETE ON system_information_source_owner_revision
                BEGIN SELECT RAISE(ABORT, 'INFORMATION_SOURCE_OWNERSHIP_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE __information_source_ownership_downgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT retained_information_source_ownership_prevents_downgrade CHECK (blocked = 0)
                );
                INSERT INTO __information_source_ownership_downgrade_guard (blocked)
                SELECT 1 WHERE EXISTS (SELECT 1 FROM system_information_source_target_identity)
                    OR EXISTS (SELECT 1 FROM system_information_source_owner_revision)
                    OR EXISTS (SELECT 1 FROM system_information_source_owner_current);
                DROP TABLE __information_source_ownership_downgrade_guard;
                DROP TRIGGER IF EXISTS system_information_source_owner_revision_immutable_delete;
                DROP TRIGGER IF EXISTS system_information_source_owner_revision_immutable_update;
                DROP TRIGGER IF EXISTS system_information_source_target_identity_immutable_delete;
                DROP TRIGGER IF EXISTS system_information_source_target_identity_immutable_update;
                """);

            migrationBuilder.DropTable(
                name: "system_information_source_owner_current");

            migrationBuilder.DropTable(
                name: "system_information_source_owner_revision");

            migrationBuilder.DropTable(
                name: "system_information_source_target_identity");
        }
    }
}
