using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TriggerSchedulingRecurring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trigger_recurring_definition",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Lifecycle = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Interval = table.Column<int>(type: "INTEGER", nullable: false),
                    LocalTimeSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    WeekdaysMask = table.Column<int>(type: "INTEGER", nullable: false),
                    DayOfMonth = table.Column<int>(type: "INTEGER", nullable: true),
                    GapPolicy = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OverlapPolicy = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MisfirePolicy = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    NotificationTopic = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NotificationSubject = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    NotificationBody = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: false),
                    NotificationStateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_recurring_definition", x => new { x.ApplicationId, x.Id, x.Version });
                    table.CheckConstraint("CK_trigger_recurring_definition_dates", "\"StartDate\" IS NULL OR \"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                    table.CheckConstraint("CK_trigger_recurring_definition_shape", "(\"Kind\" = 'daily' AND \"WeekdaysMask\" = 0 AND \"DayOfMonth\" IS NULL) OR (\"Kind\" = 'weekly' AND \"WeekdaysMask\" BETWEEN 1 AND 127 AND \"DayOfMonth\" IS NULL) OR (\"Kind\" = 'monthly' AND \"WeekdaysMask\" = 0 AND \"DayOfMonth\" BETWEEN 1 AND 31)");
                    table.CheckConstraint("CK_trigger_recurring_definition_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"Id\") BETWEEN 3 AND 200 AND \"Version\" > 0 AND \"Lifecycle\" IN ('active', 'paused', 'cancelled') AND \"Kind\" IN ('daily', 'weekly', 'monthly') AND \"Interval\" BETWEEN 1 AND 365 AND \"LocalTimeSeconds\" BETWEEN 0 AND 86399 AND length(\"TimeZoneId\") BETWEEN 3 AND 100 AND \"GapPolicy\" IN ('skip', 'next-valid') AND \"OverlapPolicy\" IN ('earlier', 'later') AND \"MisfirePolicy\" IN ('skip', 'fire-once') AND \"Target\" = 'notification-only'");
                    table.CheckConstraint("CK_trigger_recurring_notification_values", "length(\"NotificationTopic\") BETWEEN 1 AND 200 AND length(\"NotificationSubject\") BETWEEN 1 AND 400 AND length(CAST(\"NotificationBody\" AS BLOB)) <= 16384 AND (\"NotificationStateSpaceId\" IS NULL OR length(\"NotificationStateSpaceId\") BETWEEN 1 AND 200)");
                    table.ForeignKey(
                        name: "FK_trigger_recurring_definition_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_recurring_current",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_recurring_current", x => new { x.ApplicationId, x.Id });
                    table.CheckConstraint("CK_trigger_recurring_current_version", "\"CurrentVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_trigger_recurring_current_trigger_recurring_definition_ApplicationId_Id_CurrentVersion",
                        columns: x => new { x.ApplicationId, x.Id, x.CurrentVersion },
                        principalTable: "trigger_recurring_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_recurring_fire_receipt",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurrenceAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Disposition = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_recurring_fire_receipt", x => x.Id);
                    table.CheckConstraint("CK_trigger_recurring_fire_receipt_id", "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'trigger-fire.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_recurring_fire_receipt_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Disposition\" IN ('due', 'missed')");
                    table.ForeignKey(
                        name: "FK_trigger_recurring_fire_receipt_trigger_recurring_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_recurring_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_recurring_fire_work",
                columns: table => new
                {
                    FireId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurrenceAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_trigger_recurring_fire_work", x => x.FireId);
                    table.CheckConstraint("CK_trigger_recurring_fire_work_id", "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_recurring_fire_work_lease", "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                    table.CheckConstraint("CK_trigger_recurring_fire_work_state", "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'missed', 'failed') AND ((\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR (\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL) OR (\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('handler-unavailable', 'transient-database')) OR (\"State\" IN ('completed', 'missed') AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR (\"State\" = 'failed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted'))) ");
                    table.CheckConstraint("CK_trigger_recurring_fire_work_token", "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
                    table.CheckConstraint("CK_trigger_recurring_fire_work_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                    table.ForeignKey(
                        name: "FK_trigger_recurring_fire_work_trigger_recurring_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_recurring_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_recurring_notification_entity",
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
                    table.PrimaryKey("PK_trigger_recurring_notification_entity", x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion, x.Ordinal });
                    table.CheckConstraint("CK_trigger_recurring_notification_entity_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 31 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "FK_trigger_recurring_notification_entity_trigger_recurring_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_recurring_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_recurring_state",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    NextOccurrenceAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastOccurrenceAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastDisposition = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    LastFailureKind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_recurring_state", x => new { x.ApplicationId, x.TriggerId });
                    table.CheckConstraint("CK_trigger_recurring_state_disposition", "\"LastDisposition\" IS NULL OR \"LastDisposition\" IN ('due', 'missed')");
                    table.CheckConstraint("CK_trigger_recurring_state_failure", "\"LastFailureKind\" IS NULL OR \"LastFailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted')");
                    table.CheckConstraint("CK_trigger_recurring_state_last", "(\"LastOccurrenceAtUtc\" IS NULL AND \"LastDisposition\" IS NULL AND \"LastFailureKind\" IS NULL) OR (\"LastOccurrenceAtUtc\" IS NOT NULL AND ((\"LastDisposition\" IS NULL) <> (\"LastFailureKind\" IS NULL)))");
                    table.CheckConstraint("CK_trigger_recurring_state_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"CurrentVersion\" > 0 AND \"Revision\" >= 0");
                    table.ForeignKey(
                        name: "FK_trigger_recurring_state_trigger_recurring_definition_ApplicationId_TriggerId_CurrentVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.CurrentVersion },
                        principalTable: "trigger_recurring_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_recurring_notification_link",
                columns: table => new
                {
                    FireId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    NotificationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurrenceAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_recurring_notification_link", x => x.FireId);
                    table.CheckConstraint("CK_trigger_recurring_notification_link_fire", "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_recurring_notification_link_notification", "length(\"NotificationId\") = 32 AND \"NotificationId\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_recurring_notification_link_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_trigger_recurring_notification_link_notification_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "notification",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_recurring_notification_link_trigger_recurring_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_recurring_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_recurring_notification_link_trigger_recurring_fire_receipt_FireId",
                        column: x => x.FireId,
                        principalTable: "trigger_recurring_fire_receipt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_recurring_current_ApplicationId_Id_CurrentVersion",
                table: "trigger_recurring_current",
                columns: new[] { "ApplicationId", "Id", "CurrentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_recurring_fire_receipt_ApplicationId_TriggerId_TriggerVersion_OccurrenceAtUtc",
                table: "trigger_recurring_fire_receipt",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "OccurrenceAtUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_recurring_fire_work_ApplicationId_TriggerId_TriggerVersion_OccurrenceAtUtc",
                table: "trigger_recurring_fire_work",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "OccurrenceAtUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_recurring_fire_work_State_NextAttemptAtUtc_LeaseExpiresAtUtc",
                table: "trigger_recurring_fire_work",
                columns: new[] { "State", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_recurring_notification_entity_ApplicationId_TriggerId_TriggerVersion_EntityId",
                table: "trigger_recurring_notification_entity",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_recurring_notification_link_ApplicationId_TriggerId_TriggerVersion_OccurrenceAtUtc",
                table: "trigger_recurring_notification_link",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "OccurrenceAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_recurring_notification_link_NotificationId",
                table: "trigger_recurring_notification_link",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_recurring_state_ApplicationId_TriggerId_CurrentVersion",
                table: "trigger_recurring_state",
                columns: new[] { "ApplicationId", "TriggerId", "CurrentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_recurring_state_NextOccurrenceAtUtc_ApplicationId_TriggerId",
                table: "trigger_recurring_state",
                columns: new[] { "NextOccurrenceAtUtc", "ApplicationId", "TriggerId" });

            migrationBuilder.Sql("""
                CREATE TRIGGER trigger_recurring_definition_immutable_update
                BEFORE UPDATE ON trigger_recurring_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_recurring_definition_immutable_delete
                BEFORE DELETE ON trigger_recurring_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_recurring_notification_entity_immutable_update
                BEFORE UPDATE ON trigger_recurring_notification_entity
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_recurring_notification_entity_immutable_delete
                BEFORE DELETE ON trigger_recurring_notification_entity
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_recurring_notification_entity_insert_guard
                BEFORE INSERT ON trigger_recurring_notification_entity
                WHEN NOT EXISTS (
                    SELECT 1 FROM trigger_recurring_definition definition
                    WHERE definition.ApplicationId = NEW.ApplicationId
                      AND definition.Id = NEW.TriggerId
                      AND definition.Version = NEW.TriggerVersion
                      AND definition.NotificationStateSpaceId = NEW.StateSpaceId)
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_NOTIFICATION_LINK_INVALID'); END;

                CREATE TRIGGER trigger_recurring_current_transition_update
                BEFORE UPDATE ON trigger_recurring_current
                WHEN NEW.ApplicationId <> OLD.ApplicationId OR NEW.Id <> OLD.Id
                  OR NEW.CurrentVersion <= OLD.CurrentVersion
                BEGIN SELECT RAISE(ABORT, 'RECURRING_CURRENT_TRANSITION_DENIED'); END;
                CREATE TRIGGER trigger_recurring_current_delete_denied
                BEFORE DELETE ON trigger_recurring_current
                BEGIN SELECT RAISE(ABORT, 'RECURRING_CURRENT_DELETE_DENIED'); END;

                CREATE TRIGGER trigger_recurring_state_transition_update
                BEFORE UPDATE ON trigger_recurring_state
                WHEN NOT (
                    NEW.ApplicationId = OLD.ApplicationId
                    AND NEW.TriggerId = OLD.TriggerId
                    AND NEW.Revision = OLD.Revision + 1
                    AND (
                        (NEW.CurrentVersion > OLD.CurrentVersion
                         AND NEW.LastOccurrenceAtUtc IS NULL
                         AND NEW.LastDisposition IS NULL
                         AND NEW.LastFailureKind IS NULL)
                        OR
                        (NEW.CurrentVersion = OLD.CurrentVersion
                         AND NEW.LastOccurrenceAtUtc IS NOT NULL
                         AND OLD.NextOccurrenceAtUtc IS NOT NULL
                         AND NEW.LastOccurrenceAtUtc >= OLD.NextOccurrenceAtUtc
                         AND (NEW.NextOccurrenceAtUtc IS NULL
                              OR NEW.NextOccurrenceAtUtc > NEW.LastOccurrenceAtUtc))
                    )
                )
                BEGIN SELECT RAISE(ABORT, 'RECURRING_STATE_TRANSITION_DENIED'); END;
                CREATE TRIGGER trigger_recurring_state_delete_denied
                BEFORE DELETE ON trigger_recurring_state
                BEGIN SELECT RAISE(ABORT, 'RECURRING_STATE_DELETE_DENIED'); END;

                CREATE TRIGGER trigger_recurring_fire_work_transition_update
                BEFORE UPDATE ON trigger_recurring_fire_work
                WHEN NOT (
                    OLD.State IN ('ready', 'leased', 'retry')
                    AND NEW.Revision = OLD.Revision + 1
                    AND NEW.FireId = OLD.FireId
                    AND NEW.ApplicationId = OLD.ApplicationId
                    AND NEW.TriggerId = OLD.TriggerId
                    AND NEW.TriggerVersion = OLD.TriggerVersion
                    AND NEW.OccurrenceAtUtc = OLD.OccurrenceAtUtc
                    AND (
                        (NEW.State = 'leased' AND NEW.AttemptCount = OLD.AttemptCount + 1)
                        OR (NEW.State <> 'leased' AND NEW.AttemptCount = OLD.AttemptCount)
                    )
                    AND (
                        (OLD.State = 'ready' AND NEW.State IN ('leased', 'missed', 'failed'))
                        OR (OLD.State = 'leased' AND NEW.State IN ('leased', 'retry', 'completed', 'missed', 'failed'))
                        OR (OLD.State = 'retry' AND NEW.State IN ('leased', 'missed', 'failed'))
                    )
                )
                BEGIN SELECT RAISE(ABORT, 'RECURRING_FIRE_WORK_TRANSITION_DENIED'); END;
                CREATE TRIGGER trigger_recurring_fire_work_delete_denied
                BEFORE DELETE ON trigger_recurring_fire_work
                BEGIN SELECT RAISE(ABORT, 'RECURRING_FIRE_WORK_DELETE_DENIED'); END;

                CREATE TRIGGER trigger_recurring_fire_receipt_immutable_update
                BEFORE UPDATE ON trigger_recurring_fire_receipt
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_recurring_fire_receipt_immutable_delete
                BEFORE DELETE ON trigger_recurring_fire_receipt
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;

                CREATE TRIGGER trigger_recurring_notification_link_insert_guard
                BEFORE INSERT ON trigger_recurring_notification_link
                WHEN NOT EXISTS (
                    SELECT 1 FROM trigger_recurring_fire_receipt receipt
                    WHERE receipt.Id = NEW.FireId
                      AND receipt.ApplicationId = NEW.ApplicationId
                      AND receipt.TriggerId = NEW.TriggerId
                      AND receipt.TriggerVersion = NEW.TriggerVersion
                      AND receipt.OccurrenceAtUtc = NEW.OccurrenceAtUtc
                      AND receipt.Disposition = 'due')
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_NOTIFICATION_PROVENANCE_INVALID'); END;
                CREATE TRIGGER trigger_recurring_notification_link_update_denied
                BEFORE UPDATE ON trigger_recurring_notification_link
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_recurring_notification_link_delete_denied
                BEFORE DELETE ON trigger_recurring_notification_link
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_recurring_notification_link_delete_denied;
                DROP TRIGGER IF EXISTS trigger_recurring_notification_link_update_denied;
                DROP TRIGGER IF EXISTS trigger_recurring_notification_link_insert_guard;
                DROP TRIGGER IF EXISTS trigger_recurring_fire_receipt_immutable_delete;
                DROP TRIGGER IF EXISTS trigger_recurring_fire_receipt_immutable_update;
                DROP TRIGGER IF EXISTS trigger_recurring_fire_work_delete_denied;
                DROP TRIGGER IF EXISTS trigger_recurring_fire_work_transition_update;
                DROP TRIGGER IF EXISTS trigger_recurring_state_delete_denied;
                DROP TRIGGER IF EXISTS trigger_recurring_state_transition_update;
                DROP TRIGGER IF EXISTS trigger_recurring_current_delete_denied;
                DROP TRIGGER IF EXISTS trigger_recurring_current_transition_update;
                DROP TRIGGER IF EXISTS trigger_recurring_notification_entity_insert_guard;
                DROP TRIGGER IF EXISTS trigger_recurring_notification_entity_immutable_delete;
                DROP TRIGGER IF EXISTS trigger_recurring_notification_entity_immutable_update;
                DROP TRIGGER IF EXISTS trigger_recurring_definition_immutable_delete;
                DROP TRIGGER IF EXISTS trigger_recurring_definition_immutable_update;
                """);
            migrationBuilder.DropTable(
                name: "trigger_recurring_current");

            migrationBuilder.DropTable(
                name: "trigger_recurring_fire_work");

            migrationBuilder.DropTable(
                name: "trigger_recurring_notification_entity");

            migrationBuilder.DropTable(
                name: "trigger_recurring_notification_link");

            migrationBuilder.DropTable(
                name: "trigger_recurring_state");

            migrationBuilder.DropTable(
                name: "trigger_recurring_fire_receipt");

            migrationBuilder.DropTable(
                name: "trigger_recurring_definition");
        }
    }
}
