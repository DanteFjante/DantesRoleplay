using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TriggerSchedulingConditional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trigger_conditional_definition",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Lifecycle = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Activation = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Rearm = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
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
                    table.PrimaryKey("PK_trigger_conditional_definition", x => new { x.ApplicationId, x.Id, x.Version });
                    table.CheckConstraint("CK_trigger_conditional_definition_clock_policy", "\"Kind\" <> 'world-clock-threshold' OR (\"Activation\" = 'rising-edge' AND \"Rearm\" = 'manual')");
                    table.CheckConstraint("CK_trigger_conditional_definition_config", "length(\"AdapterConfigurationJson\") BETWEEN 2 AND 65536 AND json_valid(\"AdapterConfigurationJson\") AND json_type(\"AdapterConfigurationJson\") = 'object'");
                    table.CheckConstraint("CK_trigger_conditional_definition_config_hash", "length(\"AdapterConfigurationHash\") = 64 AND \"AdapterConfigurationHash\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_trigger_conditional_definition_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"Id\") BETWEEN 3 AND 200 AND \"Version\" > 0 AND \"Lifecycle\" IN ('active', 'paused', 'cancelled') AND \"Kind\" IN ('world-clock-threshold', 'state-condition') AND \"Activation\" IN ('rising-edge', 'level') AND \"Rearm\" IN ('on-false', 'manual') AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"AdapterId\") BETWEEN 3 AND 200 AND \"AdapterVersion\" > 0 AND \"Target\" = 'notification-only'");
                    table.CheckConstraint("CK_trigger_conditional_notification_values", "length(\"NotificationTopic\") BETWEEN 1 AND 200 AND length(\"NotificationSubject\") BETWEEN 1 AND 400 AND length(CAST(\"NotificationBody\" AS BLOB)) <= 16384 AND (\"NotificationStateSpaceId\" IS NULL OR length(\"NotificationStateSpaceId\") BETWEEN 1 AND 200)");
                    table.ForeignKey(
                        name: "FK_trigger_conditional_definition_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_conditional_definition_system_state_space_StateSpaceId",
                        column: x => x.StateSpaceId,
                        principalTable: "system_state_space",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_conditional_current",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_conditional_current", x => new { x.ApplicationId, x.Id });
                    table.CheckConstraint("CK_trigger_conditional_current_version", "\"CurrentVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_trigger_conditional_current_trigger_conditional_definition_ApplicationId_Id_CurrentVersion",
                        columns: x => new { x.ApplicationId, x.Id, x.CurrentVersion },
                        principalTable: "trigger_conditional_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_conditional_dependency",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    QualifiedTypeId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TypeVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SchemaHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_conditional_dependency", x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion, x.Ordinal });
                    table.CheckConstraint("CK_trigger_conditional_dependency_hash", "length(\"SchemaHash\") = 64 AND \"SchemaHash\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_trigger_conditional_dependency_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 15 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200 AND length(\"QualifiedTypeId\") BETWEEN 3 AND 200 AND \"TypeVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_trigger_conditional_dependency_system_ecs_entity_StateSpaceId_EntityId",
                        columns: x => new { x.StateSpaceId, x.EntityId },
                        principalTable: "system_ecs_entity",
                        principalColumns: new[] { "StateSpaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_conditional_dependency_trigger_conditional_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_conditional_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_conditional_fire_receipt",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangeOperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Disposition = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_conditional_fire_receipt", x => x.Id);
                    table.CheckConstraint("CK_trigger_conditional_fire_receipt_id", "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'trigger-fire.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_conditional_fire_receipt_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ChangeOperationId\") = 32 AND \"ChangeOperationId\" NOT GLOB '*[^0-9a-f]*' AND \"Disposition\" = 'due'");
                    table.ForeignKey(
                        name: "FK_trigger_conditional_fire_receipt_trigger_conditional_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_conditional_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_conditional_fire_work",
                columns: table => new
                {
                    FireId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangeOperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
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
                    table.PrimaryKey("PK_trigger_conditional_fire_work", x => x.FireId);
                    table.CheckConstraint("CK_trigger_conditional_fire_work_id", "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_conditional_fire_work_lease", "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                    table.CheckConstraint("CK_trigger_conditional_fire_work_state", "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'failed') AND ((\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR (\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL) OR (\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('handler-unavailable', 'transient-database')) OR (\"State\" = 'completed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR (\"State\" = 'failed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted'))) ");
                    table.CheckConstraint("CK_trigger_conditional_fire_work_token", "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
                    table.CheckConstraint("CK_trigger_conditional_fire_work_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ChangeOperationId\") = 32 AND \"ChangeOperationId\" NOT GLOB '*[^0-9a-f]*' AND \"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                    table.ForeignKey(
                        name: "FK_trigger_conditional_fire_work_trigger_conditional_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_conditional_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_conditional_notification_entity",
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
                    table.PrimaryKey("PK_trigger_conditional_notification_entity", x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion, x.Ordinal });
                    table.CheckConstraint("CK_trigger_conditional_notification_entity_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 31 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "FK_trigger_conditional_notification_entity_system_ecs_entity_StateSpaceId_EntityId",
                        columns: x => new { x.StateSpaceId, x.EntityId },
                        principalTable: "system_ecs_entity",
                        principalColumns: new[] { "StateSpaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_conditional_notification_entity_trigger_conditional_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_conditional_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_conditional_state",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentTruth = table.Column<bool>(type: "INTEGER", nullable: true),
                    Armed = table.Column<bool>(type: "INTEGER", nullable: false),
                    EvaluationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    LastOperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LastFiredOperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_conditional_state", x => new { x.ApplicationId, x.TriggerId });
                    table.CheckConstraint("CK_trigger_conditional_state_operations", "(\"LastOperationId\" IS NULL OR (length(\"LastOperationId\") = 32 AND \"LastOperationId\" NOT GLOB '*[^0-9a-f]*')) AND (\"LastFiredOperationId\" IS NULL OR (length(\"LastFiredOperationId\") = 32 AND \"LastFiredOperationId\" NOT GLOB '*[^0-9a-f]*'))");
                    table.CheckConstraint("CK_trigger_conditional_state_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"CurrentVersion\" > 0 AND \"EvaluationRevision\" >= 0");
                    table.ForeignKey(
                        name: "FK_trigger_conditional_state_trigger_conditional_definition_ApplicationId_TriggerId_CurrentVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.CurrentVersion },
                        principalTable: "trigger_conditional_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_conditional_notification_link",
                columns: table => new
                {
                    FireId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    NotificationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangeOperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_conditional_notification_link", x => x.FireId);
                    table.CheckConstraint("CK_trigger_conditional_notification_link_fire", "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_conditional_notification_link_notification", "length(\"NotificationId\") = 32 AND \"NotificationId\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_conditional_notification_link_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ChangeOperationId\") = 32 AND \"ChangeOperationId\" NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_trigger_conditional_notification_link_notification_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "notification",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_conditional_notification_link_trigger_conditional_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_conditional_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_conditional_notification_link_trigger_conditional_fire_receipt_FireId",
                        column: x => x.FireId,
                        principalTable: "trigger_conditional_fire_receipt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_current_ApplicationId_Id_CurrentVersion",
                table: "trigger_conditional_current",
                columns: new[] { "ApplicationId", "Id", "CurrentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_definition_StateSpaceId",
                table: "trigger_conditional_definition",
                column: "StateSpaceId");

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_dependency_ApplicationId_TriggerId_TriggerVersion_EntityId_QualifiedTypeId",
                table: "trigger_conditional_dependency",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "EntityId", "QualifiedTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_dependency_StateSpaceId_EntityId_QualifiedTypeId",
                table: "trigger_conditional_dependency",
                columns: new[] { "StateSpaceId", "EntityId", "QualifiedTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_fire_receipt_ApplicationId_TriggerId_TriggerVersion_ChangeOperationId",
                table: "trigger_conditional_fire_receipt",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "ChangeOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_fire_work_ApplicationId_TriggerId_TriggerVersion_ChangeOperationId",
                table: "trigger_conditional_fire_work",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "ChangeOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_fire_work_State_NextAttemptAtUtc_LeaseExpiresAtUtc",
                table: "trigger_conditional_fire_work",
                columns: new[] { "State", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_notification_entity_ApplicationId_TriggerId_TriggerVersion_EntityId",
                table: "trigger_conditional_notification_entity",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_notification_entity_StateSpaceId_EntityId",
                table: "trigger_conditional_notification_entity",
                columns: new[] { "StateSpaceId", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_notification_link_ApplicationId_TriggerId_TriggerVersion",
                table: "trigger_conditional_notification_link",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_notification_link_NotificationId",
                table: "trigger_conditional_notification_link",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_state_ApplicationId_TriggerId_CurrentVersion",
                table: "trigger_conditional_state",
                columns: new[] { "ApplicationId", "TriggerId", "CurrentVersion" });

            migrationBuilder.Sql("""
                CREATE TRIGGER trigger_conditional_definition_scope_insert
                BEFORE INSERT ON trigger_conditional_definition
                WHEN NOT ((NEW.Version = 1 AND NOT EXISTS (SELECT 1 FROM trigger_conditional_current current
                            WHERE current.ApplicationId = NEW.ApplicationId AND current.Id = NEW.Id)) OR
                          EXISTS (SELECT 1 FROM trigger_conditional_current current
                            WHERE current.ApplicationId = NEW.ApplicationId AND current.Id = NEW.Id
                              AND NEW.Version = current.CurrentVersion + 1))
                  OR NOT EXISTS (SELECT 1 FROM system_state_space space
                    WHERE space.Id = NEW.StateSpaceId AND space.ApplicationId = NEW.ApplicationId)
                  OR (NEW.NotificationStateSpaceId IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM system_state_space space
                    WHERE space.Id = NEW.NotificationStateSpaceId AND space.ApplicationId = NEW.ApplicationId))
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_SCOPE'); END;

                CREATE TRIGGER trigger_conditional_dependency_scope_insert
                BEFORE INSERT ON trigger_conditional_dependency
                WHEN NOT EXISTS (SELECT 1 FROM trigger_conditional_definition definition
                    WHERE definition.ApplicationId = NEW.ApplicationId AND definition.Id = NEW.TriggerId
                      AND definition.Version = NEW.TriggerVersion AND definition.StateSpaceId = NEW.StateSpaceId)
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_DEPENDENCY_SCOPE'); END;

                CREATE TRIGGER trigger_conditional_notification_entity_scope_insert
                BEFORE INSERT ON trigger_conditional_notification_entity
                WHEN NOT EXISTS (SELECT 1 FROM trigger_conditional_definition definition
                    WHERE definition.ApplicationId = NEW.ApplicationId AND definition.Id = NEW.TriggerId
                      AND definition.Version = NEW.TriggerVersion
                      AND definition.NotificationStateSpaceId = NEW.StateSpaceId)
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_NOTIFICATION_SCOPE'); END;

                CREATE TRIGGER trigger_conditional_current_update_guard
                BEFORE UPDATE ON trigger_conditional_current
                WHEN NEW.ApplicationId <> OLD.ApplicationId OR NEW.Id <> OLD.Id
                  OR NEW.CurrentVersion <> OLD.CurrentVersion + 1
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_CURRENT_TRANSITION'); END;

                CREATE TRIGGER trigger_conditional_current_insert_guard
                BEFORE INSERT ON trigger_conditional_current
                WHEN NEW.CurrentVersion <> 1
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_CURRENT_TRANSITION'); END;

                CREATE TRIGGER trigger_conditional_current_delete_guard
                BEFORE DELETE ON trigger_conditional_current
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_CURRENT_DELETE'); END;

                CREATE TRIGGER trigger_conditional_state_update_guard
                BEFORE UPDATE ON trigger_conditional_state
                WHEN NEW.ApplicationId <> OLD.ApplicationId OR NEW.TriggerId <> OLD.TriggerId OR
                  NOT ((NEW.CurrentVersion = OLD.CurrentVersion AND
                        NEW.EvaluationRevision = OLD.EvaluationRevision + 1 AND
                        NEW.LastOperationId IS NOT NULL AND
                        (NEW.LastFiredOperationId IS OLD.LastFiredOperationId OR
                         (NEW.LastFiredOperationId = NEW.LastOperationId AND NEW.Armed = 0 AND NEW.CurrentTruth = 1))) OR
                       (NEW.CurrentVersion = OLD.CurrentVersion + 1 AND
                        NEW.EvaluationRevision = OLD.EvaluationRevision + 1 AND
                        NEW.LastOperationId IS NULL AND NEW.LastFiredOperationId IS NULL))
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_STATE_TRANSITION'); END;

                CREATE TRIGGER trigger_conditional_state_insert_guard
                BEFORE INSERT ON trigger_conditional_state
                WHEN NEW.CurrentVersion <> 1 OR NEW.EvaluationRevision <> 0
                  OR NEW.LastOperationId IS NOT NULL OR NEW.LastFiredOperationId IS NOT NULL
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_STATE_TRANSITION'); END;

                CREATE TRIGGER trigger_conditional_state_delete_guard
                BEFORE DELETE ON trigger_conditional_state
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_STATE_DELETE'); END;

                CREATE TRIGGER trigger_conditional_work_insert_guard
                BEFORE INSERT ON trigger_conditional_fire_work
                WHEN NOT EXISTS (SELECT 1 FROM trigger_conditional_current current
                    JOIN trigger_conditional_definition definition ON definition.ApplicationId = current.ApplicationId
                      AND definition.Id = current.Id AND definition.Version = current.CurrentVersion
                    JOIN trigger_conditional_state state ON state.ApplicationId = current.ApplicationId
                      AND state.TriggerId = current.Id AND state.CurrentVersion = current.CurrentVersion
                    WHERE current.ApplicationId = NEW.ApplicationId AND current.Id = NEW.TriggerId
                      AND current.CurrentVersion = NEW.TriggerVersion AND definition.Lifecycle = 'active'
                      AND state.LastFiredOperationId = NEW.ChangeOperationId)
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_WORK_PROVENANCE'); END;

                CREATE TRIGGER trigger_conditional_work_update_guard
                BEFORE UPDATE ON trigger_conditional_fire_work
                WHEN NEW.FireId <> OLD.FireId OR NEW.ApplicationId <> OLD.ApplicationId
                  OR NEW.TriggerId <> OLD.TriggerId OR NEW.TriggerVersion <> OLD.TriggerVersion
                  OR NEW.ChangeOperationId <> OLD.ChangeOperationId OR NEW.CreatedAtUtc <> OLD.CreatedAtUtc
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
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_WORK_TRANSITION'); END;

                CREATE TRIGGER trigger_conditional_work_delete_guard
                BEFORE DELETE ON trigger_conditional_fire_work
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_WORK_DELETE'); END;

                CREATE TRIGGER trigger_conditional_receipt_insert_guard
                BEFORE INSERT ON trigger_conditional_fire_receipt
                WHEN NOT EXISTS (SELECT 1 FROM trigger_conditional_fire_work work
                    WHERE work.FireId = NEW.Id AND work.ApplicationId = NEW.ApplicationId
                      AND work.TriggerId = NEW.TriggerId AND work.TriggerVersion = NEW.TriggerVersion
                      AND work.ChangeOperationId = NEW.ChangeOperationId AND work.State = 'completed')
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_RECEIPT_PROVENANCE'); END;

                CREATE TRIGGER trigger_conditional_link_insert_guard
                BEFORE INSERT ON trigger_conditional_notification_link
                WHEN NOT EXISTS (SELECT 1 FROM trigger_conditional_fire_receipt receipt
                    WHERE receipt.Id = NEW.FireId AND receipt.ApplicationId = NEW.ApplicationId
                      AND receipt.TriggerId = NEW.TriggerId AND receipt.TriggerVersion = NEW.TriggerVersion
                      AND receipt.ChangeOperationId = NEW.ChangeOperationId AND receipt.Disposition = 'due')
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_LINK_PROVENANCE'); END;

                CREATE TRIGGER trigger_conditional_definition_update_guard BEFORE UPDATE ON trigger_conditional_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_conditional_definition_delete_guard BEFORE DELETE ON trigger_conditional_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_conditional_dependency_update_guard BEFORE UPDATE ON trigger_conditional_dependency
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_conditional_dependency_delete_guard BEFORE DELETE ON trigger_conditional_dependency
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_conditional_notification_entity_update_guard BEFORE UPDATE ON trigger_conditional_notification_entity
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_conditional_notification_entity_delete_guard BEFORE DELETE ON trigger_conditional_notification_entity
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_conditional_receipt_update_guard BEFORE UPDATE ON trigger_conditional_fire_receipt
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_conditional_receipt_delete_guard BEFORE DELETE ON trigger_conditional_fire_receipt
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_conditional_link_update_guard BEFORE UPDATE ON trigger_conditional_notification_link
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_conditional_link_delete_guard BEFORE DELETE ON trigger_conditional_notification_link
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_conditional_link_delete_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_link_update_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_receipt_delete_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_receipt_update_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_notification_entity_delete_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_notification_entity_update_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_dependency_delete_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_dependency_update_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_definition_delete_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_definition_update_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_link_insert_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_receipt_insert_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_work_delete_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_work_update_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_work_insert_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_state_delete_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_state_insert_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_state_update_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_current_delete_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_current_insert_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_current_update_guard;
                DROP TRIGGER IF EXISTS trigger_conditional_notification_entity_scope_insert;
                DROP TRIGGER IF EXISTS trigger_conditional_dependency_scope_insert;
                DROP TRIGGER IF EXISTS trigger_conditional_definition_scope_insert;
                """);
            migrationBuilder.DropTable(
                name: "trigger_conditional_current");

            migrationBuilder.DropTable(
                name: "trigger_conditional_dependency");

            migrationBuilder.DropTable(
                name: "trigger_conditional_fire_work");

            migrationBuilder.DropTable(
                name: "trigger_conditional_notification_entity");

            migrationBuilder.DropTable(
                name: "trigger_conditional_notification_link");

            migrationBuilder.DropTable(
                name: "trigger_conditional_state");

            migrationBuilder.DropTable(
                name: "trigger_conditional_fire_receipt");

            migrationBuilder.DropTable(
                name: "trigger_conditional_definition");
        }
    }
}
