using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TriggerSchedulingObservationMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trigger_observation_match_definition",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Lifecycle = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    StructureId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StructureVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    StructureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AdapterId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AdapterVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    AdapterConfigurationJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    AdapterConfigurationHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    NotificationTopic = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NotificationSubject = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    NotificationBody = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: false),
                    NotificationStateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation_match_definition", x => new { x.ApplicationId, x.Id, x.Version });
                    table.CheckConstraint("CK_trigger_observation_match_definition_config", "length(\"AdapterConfigurationJson\") BETWEEN 2 AND 65536 AND json_valid(\"AdapterConfigurationJson\") AND json_type(\"AdapterConfigurationJson\") = 'object'");
                    table.CheckConstraint("CK_trigger_observation_match_definition_hashes", "length(\"StructureHash\") = 64 AND \"StructureHash\" NOT GLOB '*[^0-9A-F]*' AND length(\"AdapterConfigurationHash\") = 64 AND \"AdapterConfigurationHash\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_trigger_observation_match_definition_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"Id\") BETWEEN 3 AND 200 AND \"Version\" > 0 AND \"Lifecycle\" IN ('active', 'paused', 'cancelled') AND length(\"SourceId\") BETWEEN 3 AND 200 AND \"SourceVersion\" > 0 AND length(\"StructureId\") BETWEEN 3 AND 200 AND \"StructureVersion\" > 0 AND length(\"AdapterId\") BETWEEN 3 AND 200 AND \"AdapterVersion\" > 0 AND \"Target\" = 'notification-only'");
                    table.CheckConstraint("CK_trigger_observation_match_notification_values", "length(\"NotificationTopic\") BETWEEN 1 AND 200 AND length(\"NotificationSubject\") BETWEEN 1 AND 400 AND length(CAST(\"NotificationBody\" AS BLOB)) <= 16384 AND (\"NotificationStateSpaceId\" IS NULL OR length(\"NotificationStateSpaceId\") BETWEEN 1 AND 200)");
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_definition_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_definition_trigger_observation_source_ApplicationId_SourceId_SourceVersion",
                        columns: x => new { x.ApplicationId, x.SourceId, x.SourceVersion },
                        principalTable: "trigger_observation_source",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_definition_trigger_observation_structure_ApplicationId_StructureId_StructureVersion",
                        columns: x => new { x.ApplicationId, x.StructureId, x.StructureVersion },
                        principalTable: "trigger_observation_structure",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_observation_match_current",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation_match_current", x => new { x.ApplicationId, x.Id });
                    table.CheckConstraint("CK_trigger_observation_match_current_version", "\"CurrentVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_current_trigger_observation_match_definition_ApplicationId_Id_CurrentVersion",
                        columns: x => new { x.ApplicationId, x.Id, x.CurrentVersion },
                        principalTable: "trigger_observation_match_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_observation_match_notification_entity",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation_match_notification_entity", x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion, x.Ordinal });
                    table.CheckConstraint("CK_trigger_observation_match_notification_entity_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 31 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_notification_entity_system_ecs_entity_StateSpaceId_EntityId",
                        columns: x => new { x.StateSpaceId, x.EntityId },
                        principalTable: "system_ecs_entity",
                        principalColumns: new[] { "StateSpaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_notification_entity_trigger_observation_match_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_observation_match_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_observation_match_receipt",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ObservationId = table.Column<string>(type: "TEXT", maxLength: 44, nullable: false),
                    Disposition = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation_match_receipt", x => x.Id);
                    table.CheckConstraint("CK_trigger_observation_match_receipt_id", "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'trigger-fire.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_observation_match_receipt_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ObservationId\") = 44 AND substr(\"ObservationId\", 1, 12) = 'observation.' AND \"Disposition\" IN ('matched', 'not-matched')");
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_receipt_trigger_observation_ObservationId",
                        column: x => x.ObservationId,
                        principalTable: "trigger_observation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_receipt_trigger_observation_match_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_observation_match_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_observation_match_work",
                columns: table => new
                {
                    FireId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ObservationId = table.Column<string>(type: "TEXT", maxLength: 44, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LeaseToken = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureKind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation_match_work", x => x.FireId);
                    table.CheckConstraint("CK_trigger_observation_match_work_id", "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_observation_match_work_lease", "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                    table.CheckConstraint("CK_trigger_observation_match_work_state", "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'failed') AND ((\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR (\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL) OR (\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('handler-unavailable', 'transient-database')) OR (\"State\" = 'completed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR (\"State\" = 'failed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted'))) ");
                    table.CheckConstraint("CK_trigger_observation_match_work_token", "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
                    table.CheckConstraint("CK_trigger_observation_match_work_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ObservationId\") = 44 AND substr(\"ObservationId\", 1, 12) = 'observation.' AND substr(\"ObservationId\", 13) NOT GLOB '*[^0-9a-f]*' AND \"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_work_trigger_observation_ObservationId",
                        column: x => x.ObservationId,
                        principalTable: "trigger_observation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_work_trigger_observation_match_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_observation_match_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_observation_match_notification_link",
                columns: table => new
                {
                    FireId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    NotificationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ObservationId = table.Column<string>(type: "TEXT", maxLength: 44, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation_match_notification_link", x => x.FireId);
                    table.CheckConstraint("CK_trigger_observation_match_notification_link_fire", "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_observation_match_notification_link_notification", "length(\"NotificationId\") = 32 AND \"NotificationId\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_observation_match_notification_link_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ObservationId\") = 44 AND substr(\"ObservationId\", 1, 12) = 'observation.'");
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_notification_link_notification_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "notification",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_notification_link_trigger_observation_ObservationId",
                        column: x => x.ObservationId,
                        principalTable: "trigger_observation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_notification_link_trigger_observation_match_receipt_FireId",
                        column: x => x.FireId,
                        principalTable: "trigger_observation_match_receipt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_match_current_ApplicationId_Id_CurrentVersion",
                table: "trigger_observation_match_current",
                columns: new[] { "ApplicationId", "Id", "CurrentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_match_definition_ApplicationId_SourceId_SourceVersion_StructureId_StructureVersion_StructureHash_Lifecycle",
                table: "trigger_observation_match_definition",
                columns: new[] { "ApplicationId", "SourceId", "SourceVersion", "StructureId", "StructureVersion", "StructureHash", "Lifecycle" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_match_definition_ApplicationId_StructureId_StructureVersion",
                table: "trigger_observation_match_definition",
                columns: new[] { "ApplicationId", "StructureId", "StructureVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_match_notification_entity_ApplicationId_TriggerId_TriggerVersion_EntityId",
                table: "trigger_observation_match_notification_entity",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_match_notification_entity_StateSpaceId_EntityId",
                table: "trigger_observation_match_notification_entity",
                columns: new[] { "StateSpaceId", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_match_notification_link_NotificationId",
                table: "trigger_observation_match_notification_link",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_match_notification_link_ObservationId",
                table: "trigger_observation_match_notification_link",
                column: "ObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_match_receipt_ApplicationId_TriggerId_TriggerVersion_ObservationId",
                table: "trigger_observation_match_receipt",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "ObservationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_match_receipt_ObservationId",
                table: "trigger_observation_match_receipt",
                column: "ObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_match_work_ApplicationId_TriggerId_TriggerVersion_ObservationId",
                table: "trigger_observation_match_work",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "ObservationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_match_work_ObservationId",
                table: "trigger_observation_match_work",
                column: "ObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_match_work_State_NextAttemptAtUtc_LeaseExpiresAtUtc",
                table: "trigger_observation_match_work",
                columns: new[] { "State", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });

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

                CREATE TRIGGER trigger_observation_match_notification_entity_scope_insert
                BEFORE INSERT ON trigger_observation_match_notification_entity
                WHEN NOT EXISTS (SELECT 1 FROM trigger_observation_match_definition definition
                    WHERE definition.ApplicationId = NEW.ApplicationId AND definition.Id = NEW.TriggerId
                      AND definition.Version = NEW.TriggerVersion
                      AND definition.NotificationStateSpaceId = NEW.StateSpaceId)
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_OBSERVATION_MATCH_NOTIFICATION_SCOPE'); END;

                CREATE TRIGGER trigger_observation_match_current_insert_guard
                BEFORE INSERT ON trigger_observation_match_current WHEN NEW.CurrentVersion <> 1
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_OBSERVATION_MATCH_CURRENT_TRANSITION'); END;
                CREATE TRIGGER trigger_observation_match_current_update_guard
                BEFORE UPDATE ON trigger_observation_match_current
                WHEN NEW.ApplicationId <> OLD.ApplicationId OR NEW.Id <> OLD.Id
                  OR NEW.CurrentVersion <> OLD.CurrentVersion + 1
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_OBSERVATION_MATCH_CURRENT_TRANSITION'); END;
                CREATE TRIGGER trigger_observation_match_current_delete_guard
                BEFORE DELETE ON trigger_observation_match_current
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_OBSERVATION_MATCH_CURRENT_DELETE'); END;

                CREATE TRIGGER trigger_observation_match_work_insert_guard
                BEFORE INSERT ON trigger_observation_match_work
                WHEN NOT EXISTS (SELECT 1 FROM trigger_observation observation
                    JOIN trigger_observation_match_current current ON current.ApplicationId = observation.ApplicationId
                    JOIN trigger_observation_match_definition definition
                      ON definition.ApplicationId = current.ApplicationId AND definition.Id = current.Id
                      AND definition.Version = current.CurrentVersion
                    WHERE observation.Id = NEW.ObservationId AND observation.ApplicationId = NEW.ApplicationId
                      AND current.Id = NEW.TriggerId AND current.CurrentVersion = NEW.TriggerVersion
                      AND definition.Lifecycle = 'active'
                      AND definition.SourceId = observation.SourceId
                      AND definition.SourceVersion = observation.SourceVersion
                      AND definition.StructureId = observation.StructureId
                      AND definition.StructureVersion = observation.StructureVersion
                      AND definition.StructureHash = observation.StructureHash)
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_OBSERVATION_MATCH_WORK_PROVENANCE'); END;

                CREATE TRIGGER trigger_observation_match_work_update_guard
                BEFORE UPDATE ON trigger_observation_match_work
                WHEN NEW.FireId <> OLD.FireId OR NEW.ApplicationId <> OLD.ApplicationId
                  OR NEW.TriggerId <> OLD.TriggerId OR NEW.TriggerVersion <> OLD.TriggerVersion
                  OR NEW.ObservationId <> OLD.ObservationId OR NEW.CreatedAtUtc <> OLD.CreatedAtUtc
                  OR NEW.Revision <> OLD.Revision + 1 OR NOT (
                    (OLD.State = 'ready' AND NEW.State = 'leased' AND NEW.AttemptCount = 1) OR
                    (OLD.State = 'retry' AND NEW.State = 'leased' AND NEW.AttemptCount = OLD.AttemptCount + 1) OR
                    (OLD.State = 'leased' AND NEW.State = 'leased' AND NEW.AttemptCount = OLD.AttemptCount + 1
                        AND OLD.LeaseExpiresAtUtc <= NEW.UpdatedAtUtc) OR
                    (OLD.State = 'leased' AND NEW.State IN ('retry', 'completed')
                        AND NEW.AttemptCount = OLD.AttemptCount AND OLD.LeaseExpiresAtUtc > NEW.UpdatedAtUtc) OR
                    (OLD.State = 'leased' AND NEW.State = 'failed' AND NEW.AttemptCount = OLD.AttemptCount) OR
                    (OLD.State IN ('ready', 'retry') AND NEW.State = 'failed'
                        AND NEW.AttemptCount = OLD.AttemptCount))
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_OBSERVATION_MATCH_WORK_TRANSITION'); END;
                CREATE TRIGGER trigger_observation_match_work_delete_guard
                BEFORE DELETE ON trigger_observation_match_work
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_OBSERVATION_MATCH_WORK_DELETE'); END;

                CREATE TRIGGER trigger_observation_match_receipt_insert_guard
                BEFORE INSERT ON trigger_observation_match_receipt
                WHEN NOT EXISTS (SELECT 1 FROM trigger_observation_match_work work
                    WHERE work.FireId = NEW.Id AND work.ApplicationId = NEW.ApplicationId
                      AND work.TriggerId = NEW.TriggerId AND work.TriggerVersion = NEW.TriggerVersion
                      AND work.ObservationId = NEW.ObservationId AND work.State = 'completed')
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_OBSERVATION_MATCH_RECEIPT_PROVENANCE'); END;
                CREATE TRIGGER trigger_observation_match_link_insert_guard
                BEFORE INSERT ON trigger_observation_match_notification_link
                WHEN NOT EXISTS (SELECT 1 FROM trigger_observation_match_receipt receipt
                    WHERE receipt.Id = NEW.FireId AND receipt.ApplicationId = NEW.ApplicationId
                      AND receipt.TriggerId = NEW.TriggerId AND receipt.TriggerVersion = NEW.TriggerVersion
                      AND receipt.ObservationId = NEW.ObservationId AND receipt.Disposition = 'matched')
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_OBSERVATION_MATCH_LINK_PROVENANCE'); END;

                CREATE TRIGGER trigger_observation_match_definition_update_guard BEFORE UPDATE ON trigger_observation_match_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_match_definition_delete_guard BEFORE DELETE ON trigger_observation_match_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_match_notification_entity_update_guard BEFORE UPDATE ON trigger_observation_match_notification_entity
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_match_notification_entity_delete_guard BEFORE DELETE ON trigger_observation_match_notification_entity
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_match_receipt_update_guard BEFORE UPDATE ON trigger_observation_match_receipt
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_match_receipt_delete_guard BEFORE DELETE ON trigger_observation_match_receipt
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_match_link_update_guard BEFORE UPDATE ON trigger_observation_match_notification_link
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_match_link_delete_guard BEFORE DELETE ON trigger_observation_match_notification_link
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_observation_match_link_delete_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_link_update_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_receipt_delete_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_receipt_update_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_notification_entity_delete_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_notification_entity_update_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_definition_delete_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_definition_update_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_link_insert_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_receipt_insert_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_work_delete_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_work_update_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_work_insert_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_current_delete_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_current_update_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_current_insert_guard;
                DROP TRIGGER IF EXISTS trigger_observation_match_notification_entity_scope_insert;
                DROP TRIGGER IF EXISTS trigger_observation_match_definition_scope_insert;
                """);
            migrationBuilder.DropTable(
                name: "trigger_observation_match_current");

            migrationBuilder.DropTable(
                name: "trigger_observation_match_notification_entity");

            migrationBuilder.DropTable(
                name: "trigger_observation_match_notification_link");

            migrationBuilder.DropTable(
                name: "trigger_observation_match_work");

            migrationBuilder.DropTable(
                name: "trigger_observation_match_receipt");

            migrationBuilder.DropTable(
                name: "trigger_observation_match_definition");
        }
    }
}
