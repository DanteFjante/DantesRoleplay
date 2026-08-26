using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TriggerSchedulingNotificationTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Lifecycle",
                table: "trigger_one_time_definition",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "active");

            migrationBuilder.AddColumn<string>(
                name: "NotificationBody",
                table: "trigger_one_time_definition",
                type: "TEXT",
                maxLength: 16384,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NotificationStateSpaceId",
                table: "trigger_one_time_definition",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotificationSubject",
                table: "trigger_one_time_definition",
                type: "TEXT",
                maxLength: 400,
                nullable: false,
                defaultValue: "Scheduled reminder");

            migrationBuilder.AddColumn<string>(
                name: "NotificationTopic",
                table: "trigger_one_time_definition",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "scheduled.reminder");

            migrationBuilder.CreateTable(
                name: "trigger_notification_link",
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
                    table.PrimaryKey("PK_trigger_notification_link", x => x.FireId);
                    table.CheckConstraint("CK_trigger_notification_link_fire", "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_notification_link_notification", "length(\"NotificationId\") = 32 AND \"NotificationId\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_notification_link_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_trigger_notification_link_notification_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "notification",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_notification_link_trigger_fire_receipt_FireId",
                        column: x => x.FireId,
                        principalTable: "trigger_fire_receipt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_notification_link_trigger_one_time_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_one_time_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_one_time_notification_entity",
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
                    table.PrimaryKey("PK_trigger_one_time_notification_entity", x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion, x.Ordinal });
                    table.CheckConstraint("CK_trigger_one_time_notification_entity_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 31 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "FK_trigger_one_time_notification_entity_trigger_one_time_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_one_time_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_notification_link_ApplicationId_TriggerId_TriggerVersion_OccurrenceAtUtc",
                table: "trigger_notification_link",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "OccurrenceAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_notification_link_NotificationId",
                table: "trigger_notification_link",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_one_time_notification_entity_ApplicationId_TriggerId_TriggerVersion_EntityId",
                table: "trigger_one_time_notification_entity",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "EntityId" },
                unique: true);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_one_time_definition_immutable_update;
                DROP TRIGGER IF EXISTS trigger_one_time_definition_immutable_delete;
                UPDATE trigger_one_time_definition
                SET NotificationBody = Id
                WHERE NotificationBody = '';
                CREATE TRIGGER trigger_one_time_definition_immutable_update
                BEFORE UPDATE ON trigger_one_time_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_one_time_definition_immutable_delete
                BEFORE DELETE ON trigger_one_time_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;

                CREATE TRIGGER trigger_one_time_notification_entity_update_denied
                BEFORE UPDATE ON trigger_one_time_notification_entity
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_one_time_notification_entity_delete_denied
                BEFORE DELETE ON trigger_one_time_notification_entity
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;

                CREATE TRIGGER trigger_notification_link_insert_guard
                BEFORE INSERT ON trigger_notification_link
                WHEN NOT EXISTS (
                    SELECT 1 FROM trigger_fire_receipt receipt
                    WHERE receipt.Id = NEW.FireId
                      AND receipt.ApplicationId = NEW.ApplicationId
                      AND receipt.TriggerId = NEW.TriggerId
                      AND receipt.TriggerVersion = NEW.TriggerVersion
                      AND receipt.OccurrenceAtUtc = NEW.OccurrenceAtUtc)
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_NOTIFICATION_FIRE_MISMATCH'); END;
                CREATE TRIGGER trigger_notification_link_update_denied
                BEFORE UPDATE ON trigger_notification_link
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_notification_link_delete_denied
                BEFORE DELETE ON trigger_notification_link
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;

                CREATE TRIGGER notification_content_update_guard
                BEFORE UPDATE ON notification
                WHEN OLD.Id <> NEW.Id OR OLD.Topic <> NEW.Topic OR OLD.Subject <> NEW.Subject
                  OR OLD.Body <> NEW.Body OR OLD.CorrelationId <> NEW.CorrelationId
                  OR OLD.EventId <> NEW.EventId OR OLD.ExecutionId <> NEW.ExecutionId
                  OR OLD.RootOperationId <> NEW.RootOperationId OR OLD.Ordinal <> NEW.Ordinal
                  OR OLD.CreatedAt <> NEW.CreatedAt
                BEGIN SELECT RAISE(ABORT, 'NOTIFICATION_CONTENT_IMMUTABLE'); END;
                CREATE TRIGGER notification_delete_denied
                BEFORE DELETE ON notification
                BEGIN SELECT RAISE(ABORT, 'NOTIFICATION_CONTENT_IMMUTABLE'); END;
                CREATE TRIGGER notification_entity_update_denied
                BEFORE UPDATE ON notification_entity
                BEGIN SELECT RAISE(ABORT, 'NOTIFICATION_CONTENT_IMMUTABLE'); END;
                CREATE TRIGGER notification_entity_delete_denied
                BEFORE DELETE ON notification_entity
                BEGIN SELECT RAISE(ABORT, 'NOTIFICATION_CONTENT_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_one_time_notification_entity_update_denied;
                DROP TRIGGER IF EXISTS trigger_one_time_notification_entity_delete_denied;
                DROP TRIGGER IF EXISTS trigger_notification_link_insert_guard;
                DROP TRIGGER IF EXISTS trigger_notification_link_update_denied;
                DROP TRIGGER IF EXISTS trigger_notification_link_delete_denied;
                DROP TRIGGER IF EXISTS notification_content_update_guard;
                DROP TRIGGER IF EXISTS notification_delete_denied;
                DROP TRIGGER IF EXISTS notification_entity_update_denied;
                DROP TRIGGER IF EXISTS notification_entity_delete_denied;
                """);
            migrationBuilder.DropTable(
                name: "trigger_notification_link");

            migrationBuilder.DropTable(
                name: "trigger_one_time_notification_entity");

            migrationBuilder.DropColumn(
                name: "Lifecycle",
                table: "trigger_one_time_definition");

            migrationBuilder.DropColumn(
                name: "NotificationBody",
                table: "trigger_one_time_definition");

            migrationBuilder.DropColumn(
                name: "NotificationStateSpaceId",
                table: "trigger_one_time_definition");

            migrationBuilder.DropColumn(
                name: "NotificationSubject",
                table: "trigger_one_time_definition");

            migrationBuilder.DropColumn(
                name: "NotificationTopic",
                table: "trigger_one_time_definition");

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_one_time_definition_immutable_update;
                DROP TRIGGER IF EXISTS trigger_one_time_definition_immutable_delete;
                CREATE TRIGGER trigger_one_time_definition_immutable_update
                BEFORE UPDATE ON trigger_one_time_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_one_time_definition_immutable_delete
                BEFORE DELETE ON trigger_one_time_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                """);
        }
    }
}
