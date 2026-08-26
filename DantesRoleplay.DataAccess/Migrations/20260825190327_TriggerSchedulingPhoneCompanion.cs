using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TriggerSchedulingPhoneCompanion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            DropObservationStructureTriggers(migrationBuilder);

            migrationBuilder.Sql("""
                ALTER TABLE trigger_observation_structure
                ADD COLUMN DataClassification TEXT NOT NULL DEFAULT 'general'
                CHECK (DataClassification IN ('general', 'privacy-minimized-signal', 'raw-location', 'third-party-notification-content'));
                """);

            migrationBuilder.CreateTable(
                name: "trigger_phone_device",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    PrincipalId = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CredentialVerifier = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PermissionProfile = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_phone_device", x => new { x.ApplicationId, x.DeviceId });
                    table.CheckConstraint("CK_trigger_phone_device_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"DeviceId\") = 45 AND substr(\"DeviceId\", 1, 13) = 'phone-device.' AND substr(\"DeviceId\", 14) NOT GLOB '*[^0-9a-f]*' AND length(\"PrincipalId\") = 74 AND substr(\"PrincipalId\", 1, 10) = 'principal.' AND substr(\"PrincipalId\", 11) NOT GLOB '*[^0-9a-f]*' AND length(\"SourceId\") BETWEEN 3 AND 200 AND \"SourceVersion\" > 0 AND \"PermissionProfile\" = 'privacy-minimized-signals'");
                    table.CheckConstraint("CK_trigger_phone_device_verifier", "length(\"CredentialVerifier\") = 64 AND \"CredentialVerifier\" NOT GLOB '*[^0-9A-F]*'");
                    table.ForeignKey(
                        name: "FK_trigger_phone_device_trigger_observation_source_ApplicationId_SourceId_SourceVersion",
                        columns: x => new { x.ApplicationId, x.SourceId, x.SourceVersion },
                        principalTable: "trigger_observation_source",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_phone_device_status",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_phone_device_status", x => new { x.ApplicationId, x.DeviceId, x.Revision });
                    table.CheckConstraint("CK_trigger_phone_device_status_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"DeviceId\") = 45 AND substr(\"DeviceId\", 1, 13) = 'phone-device.' AND \"Revision\" BETWEEN 1 AND 2 AND \"Status\" IN ('active', 'revoked')");
                    table.ForeignKey(
                        name: "FK_trigger_phone_device_status_trigger_phone_device_ApplicationId_DeviceId",
                        columns: x => new { x.ApplicationId, x.DeviceId },
                        principalTable: "trigger_phone_device",
                        principalColumns: new[] { "ApplicationId", "DeviceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_phone_device_structure",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    StructureId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StructureVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    StructureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_phone_device_structure", x => new { x.ApplicationId, x.DeviceId, x.Ordinal });
                    table.CheckConstraint("CK_trigger_phone_device_structure_hash", "length(\"StructureHash\") = 64 AND \"StructureHash\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_trigger_phone_device_structure_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"DeviceId\") = 45 AND substr(\"DeviceId\", 1, 13) = 'phone-device.' AND \"Ordinal\" BETWEEN 0 AND 7 AND length(\"StructureId\") BETWEEN 3 AND 200 AND \"StructureVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_trigger_phone_device_structure_trigger_observation_structure_ApplicationId_StructureId_StructureVersion",
                        columns: x => new { x.ApplicationId, x.StructureId, x.StructureVersion },
                        principalTable: "trigger_observation_structure",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_phone_device_structure_trigger_phone_device_ApplicationId_DeviceId",
                        columns: x => new { x.ApplicationId, x.DeviceId },
                        principalTable: "trigger_phone_device",
                        principalColumns: new[] { "ApplicationId", "DeviceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_phone_device_current",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    CurrentRevision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_phone_device_current", x => new { x.ApplicationId, x.DeviceId });
                    table.CheckConstraint("CK_trigger_phone_device_current_revision", "\"CurrentRevision\" BETWEEN 1 AND 2");
                    table.ForeignKey(
                        name: "FK_trigger_phone_device_current_trigger_phone_device_status_ApplicationId_DeviceId_CurrentRevision",
                        columns: x => new { x.ApplicationId, x.DeviceId, x.CurrentRevision },
                        principalTable: "trigger_phone_device_status",
                        principalColumns: new[] { "ApplicationId", "DeviceId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_phone_device_ApplicationId_SourceId_SourceVersion",
                table: "trigger_phone_device",
                columns: new[] { "ApplicationId", "SourceId", "SourceVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_phone_device_CredentialVerifier",
                table: "trigger_phone_device",
                column: "CredentialVerifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_phone_device_PrincipalId",
                table: "trigger_phone_device",
                column: "PrincipalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_phone_device_current_ApplicationId_DeviceId_CurrentRevision",
                table: "trigger_phone_device_current",
                columns: new[] { "ApplicationId", "DeviceId", "CurrentRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_phone_device_structure_ApplicationId_DeviceId_StructureId_StructureVersion",
                table: "trigger_phone_device_structure",
                columns: new[] { "ApplicationId", "DeviceId", "StructureId", "StructureVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_phone_device_structure_ApplicationId_StructureId_StructureVersion",
                table: "trigger_phone_device_structure",
                columns: new[] { "ApplicationId", "StructureId", "StructureVersion" });

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_observation_structure_immutable_update;
                DROP TRIGGER IF EXISTS trigger_observation_structure_immutable_delete;
                CREATE TRIGGER trigger_observation_structure_immutable_update
                BEFORE UPDATE ON trigger_observation_structure
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_structure_immutable_delete
                BEFORE DELETE ON trigger_observation_structure
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;

                CREATE TRIGGER trigger_phone_device_scope_insert
                BEFORE INSERT ON trigger_phone_device
                WHEN NOT EXISTS (SELECT 1 FROM trigger_observation_source_current current
                    JOIN trigger_observation_source source ON source.ApplicationId = current.ApplicationId
                      AND source.Id = current.Id AND source.Version = current.CurrentVersion
                    JOIN trigger_observation_source_principal principal
                      ON principal.ApplicationId = source.ApplicationId AND principal.SourceId = source.Id
                      AND principal.SourceVersion = source.Version
                    WHERE current.ApplicationId = NEW.ApplicationId AND current.Id = NEW.SourceId
                      AND current.CurrentVersion = NEW.SourceVersion AND source.Status = 'enabled'
                      AND principal.PrincipalId = NEW.PrincipalId)
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_PHONE_DEVICE_SCOPE'); END;

                CREATE TRIGGER trigger_phone_device_structure_scope_insert
                BEFORE INSERT ON trigger_phone_device_structure
                WHEN NOT EXISTS (SELECT 1 FROM trigger_phone_device device
                    JOIN trigger_observation_source_structure allowed
                      ON allowed.ApplicationId = device.ApplicationId AND allowed.SourceId = device.SourceId
                      AND allowed.SourceVersion = device.SourceVersion
                    JOIN trigger_observation_structure_current current
                      ON current.ApplicationId = device.ApplicationId AND current.Id = allowed.StructureId
                    JOIN trigger_observation_structure structure
                      ON structure.ApplicationId = current.ApplicationId AND structure.Id = current.Id
                      AND structure.Version = current.CurrentVersion
                    WHERE device.ApplicationId = NEW.ApplicationId AND device.DeviceId = NEW.DeviceId
                      AND allowed.StructureId = NEW.StructureId
                      AND allowed.StructureVersion = NEW.StructureVersion
                      AND current.CurrentVersion = NEW.StructureVersion
                      AND structure.Status = 'active'
                      AND structure.DataClassification = 'privacy-minimized-signal'
                      AND structure.SchemaHash = NEW.StructureHash)
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_PHONE_DEVICE_STRUCTURE_SCOPE'); END;

                CREATE TRIGGER trigger_phone_device_status_insert_guard
                BEFORE INSERT ON trigger_phone_device_status
                WHEN NOT ((NEW.Revision = 1 AND NEW.Status = 'active'
                        AND NOT EXISTS (SELECT 1 FROM trigger_phone_device_current current
                            WHERE current.ApplicationId = NEW.ApplicationId AND current.DeviceId = NEW.DeviceId)
                        AND NOT EXISTS (SELECT 1 FROM trigger_phone_device_status status
                            WHERE status.ApplicationId = NEW.ApplicationId AND status.DeviceId = NEW.DeviceId))
                    OR (NEW.Revision = 2 AND NEW.Status = 'revoked'
                        AND EXISTS (SELECT 1 FROM trigger_phone_device_current current
                            JOIN trigger_phone_device_status status
                              ON status.ApplicationId = current.ApplicationId
                              AND status.DeviceId = current.DeviceId
                              AND status.Revision = current.CurrentRevision
                            WHERE current.ApplicationId = NEW.ApplicationId
                              AND current.DeviceId = NEW.DeviceId
                              AND current.CurrentRevision = 1 AND status.Status = 'active')))
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_PHONE_DEVICE_STATUS_TRANSITION'); END;

                CREATE TRIGGER trigger_phone_device_current_insert_guard
                BEFORE INSERT ON trigger_phone_device_current
                WHEN NEW.CurrentRevision <> 1 OR NOT EXISTS (
                    SELECT 1 FROM trigger_phone_device_status status
                    WHERE status.ApplicationId = NEW.ApplicationId AND status.DeviceId = NEW.DeviceId
                      AND status.Revision = 1 AND status.Status = 'active')
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_PHONE_DEVICE_CURRENT_TRANSITION'); END;
                CREATE TRIGGER trigger_phone_device_current_update_guard
                BEFORE UPDATE ON trigger_phone_device_current
                WHEN NEW.ApplicationId <> OLD.ApplicationId OR NEW.DeviceId <> OLD.DeviceId
                  OR OLD.CurrentRevision <> 1 OR NEW.CurrentRevision <> 2
                  OR NOT EXISTS (SELECT 1 FROM trigger_phone_device_status status
                    WHERE status.ApplicationId = NEW.ApplicationId AND status.DeviceId = NEW.DeviceId
                      AND status.Revision = 2 AND status.Status = 'revoked')
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_PHONE_DEVICE_CURRENT_TRANSITION'); END;
                CREATE TRIGGER trigger_phone_device_current_delete_guard
                BEFORE DELETE ON trigger_phone_device_current
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_PHONE_DEVICE_CURRENT_DELETE'); END;

                CREATE TRIGGER trigger_phone_device_update_guard BEFORE UPDATE ON trigger_phone_device
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_phone_device_delete_guard BEFORE DELETE ON trigger_phone_device
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_phone_device_structure_update_guard BEFORE UPDATE ON trigger_phone_device_structure
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_phone_device_structure_delete_guard BEFORE DELETE ON trigger_phone_device_structure
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_phone_device_status_update_guard BEFORE UPDATE ON trigger_phone_device_status
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_phone_device_status_delete_guard BEFORE DELETE ON trigger_phone_device_status
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                """);

            CreateObservationMatchDefinitionScopeTrigger(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropObservationStructureTriggers(migrationBuilder);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_phone_device_status_delete_guard;
                DROP TRIGGER IF EXISTS trigger_phone_device_status_update_guard;
                DROP TRIGGER IF EXISTS trigger_phone_device_structure_delete_guard;
                DROP TRIGGER IF EXISTS trigger_phone_device_structure_update_guard;
                DROP TRIGGER IF EXISTS trigger_phone_device_delete_guard;
                DROP TRIGGER IF EXISTS trigger_phone_device_update_guard;
                DROP TRIGGER IF EXISTS trigger_phone_device_current_delete_guard;
                DROP TRIGGER IF EXISTS trigger_phone_device_current_update_guard;
                DROP TRIGGER IF EXISTS trigger_phone_device_current_insert_guard;
                DROP TRIGGER IF EXISTS trigger_phone_device_status_insert_guard;
                DROP TRIGGER IF EXISTS trigger_phone_device_structure_scope_insert;
                DROP TRIGGER IF EXISTS trigger_phone_device_scope_insert;
                """);
            migrationBuilder.DropTable(
                name: "trigger_phone_device_current");

            migrationBuilder.DropTable(
                name: "trigger_phone_device_structure");

            migrationBuilder.DropTable(
                name: "trigger_phone_device_status");

            migrationBuilder.DropTable(
                name: "trigger_phone_device");

            migrationBuilder.Sql("ALTER TABLE trigger_observation_structure DROP COLUMN DataClassification;");

            migrationBuilder.Sql("""
                CREATE TRIGGER trigger_observation_structure_immutable_update
                BEFORE UPDATE ON trigger_observation_structure
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_structure_immutable_delete
                BEFORE DELETE ON trigger_observation_structure
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                """);

            CreateObservationMatchDefinitionScopeTrigger(migrationBuilder);
        }

        private static void DropObservationStructureTriggers(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_observation_match_definition_scope_insert;
                DROP TRIGGER IF EXISTS trigger_observation_structure_immutable_update;
                DROP TRIGGER IF EXISTS trigger_observation_structure_immutable_delete;
                """);
        }

        private static void CreateObservationMatchDefinitionScopeTrigger(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER trigger_observation_match_definition_scope_insert
                BEFORE INSERT ON trigger_observation_match_definition
                WHEN NOT ((NEW.Version = 1 AND NOT EXISTS (
                        SELECT 1 FROM trigger_observation_match_current current
                        WHERE current.ApplicationId = NEW.ApplicationId AND current.Id = NEW.Id)) OR
                    EXISTS (SELECT 1 FROM trigger_observation_match_current current
                        WHERE current.ApplicationId = NEW.ApplicationId AND current.Id = NEW.Id
                          AND NEW.Version = current.CurrentVersion + 1))
                  OR NOT EXISTS (SELECT 1 FROM trigger_observation_source_current current
                    JOIN trigger_observation_source source ON source.ApplicationId = current.ApplicationId
                      AND source.Id = current.Id AND source.Version = current.CurrentVersion
                    WHERE current.ApplicationId = NEW.ApplicationId AND current.Id = NEW.SourceId
                      AND current.CurrentVersion = NEW.SourceVersion AND source.Status = 'enabled')
                  OR NOT EXISTS (SELECT 1 FROM trigger_observation_structure_current current
                    JOIN trigger_observation_structure structure ON structure.ApplicationId = current.ApplicationId
                      AND structure.Id = current.Id AND structure.Version = current.CurrentVersion
                    WHERE current.ApplicationId = NEW.ApplicationId AND current.Id = NEW.StructureId
                      AND current.CurrentVersion = NEW.StructureVersion AND structure.Status = 'active'
                      AND structure.SchemaHash = NEW.StructureHash)
                  OR NOT EXISTS (SELECT 1 FROM trigger_observation_source_structure allowed
                    WHERE allowed.ApplicationId = NEW.ApplicationId AND allowed.SourceId = NEW.SourceId
                      AND allowed.SourceVersion = NEW.SourceVersion AND allowed.StructureId = NEW.StructureId
                      AND allowed.StructureVersion = NEW.StructureVersion)
                  OR (NEW.NotificationStateSpaceId IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM system_state_space space
                    WHERE space.Id = NEW.NotificationStateSpaceId AND space.ApplicationId = NEW.ApplicationId))
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_OBSERVATION_MATCH_SCOPE'); END;
                """);
        }
    }
}
