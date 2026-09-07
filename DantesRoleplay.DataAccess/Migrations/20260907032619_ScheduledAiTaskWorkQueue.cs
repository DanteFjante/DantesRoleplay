using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledAiTaskWorkQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scheduled_ai_task_work",
                columns: table => new
                {
                    NotificationId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LeaseToken = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureKind = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FailureMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    QueueAgeMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    ProviderDurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    EnqueuedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduled_ai_task_work", x => x.NotificationId);
                    table.CheckConstraint("CK_scheduled_ai_task_work_lease", "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                    table.CheckConstraint("CK_scheduled_ai_task_work_state", "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'failed') AND ((\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL AND \"FailureMessage\" IS NULL AND \"QueueAgeMilliseconds\" IS NULL AND \"ProviderDurationMilliseconds\" IS NULL AND \"CompletedAtUtc\" IS NULL) OR (\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL AND \"FailureMessage\" IS NULL AND \"QueueAgeMilliseconds\" IS NOT NULL AND \"ProviderDurationMilliseconds\" IS NULL AND \"CompletedAtUtc\" IS NULL) OR (\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NOT NULL AND \"FailureMessage\" IS NOT NULL AND \"QueueAgeMilliseconds\" IS NOT NULL AND \"ProviderDurationMilliseconds\" IS NOT NULL AND \"CompletedAtUtc\" IS NULL) OR (\"State\" = 'completed' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL AND \"FailureMessage\" IS NULL AND \"QueueAgeMilliseconds\" IS NOT NULL AND \"ProviderDurationMilliseconds\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL) OR (\"State\" = 'failed' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NOT NULL AND \"FailureMessage\" IS NOT NULL AND \"QueueAgeMilliseconds\" IS NOT NULL AND \"ProviderDurationMilliseconds\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL))");
                    table.CheckConstraint("CK_scheduled_ai_task_work_token", "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
                    table.CheckConstraint("CK_scheduled_ai_task_work_values", "\"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0 AND (\"QueueAgeMilliseconds\" IS NULL OR \"QueueAgeMilliseconds\" >= 0) AND (\"ProviderDurationMilliseconds\" IS NULL OR \"ProviderDurationMilliseconds\" >= 0)");
                    table.ForeignKey(
                        name: "FK_scheduled_ai_task_work_notification_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "notification",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_ai_task_work_State_NextAttemptAtUtc_LeaseExpiresAtUtc_EnqueuedAtUtc",
                table: "scheduled_ai_task_work",
                columns: new[] { "State", "NextAttemptAtUtc", "LeaseExpiresAtUtc", "EnqueuedAtUtc" });

            migrationBuilder.Sql("""
                CREATE TRIGGER scheduled_ai_task_work_insert_guard
                BEFORE INSERT ON scheduled_ai_task_work
                WHEN NEW.State <> 'ready' OR NEW.AttemptCount <> 0 OR NEW.Revision <> 0
                  OR NOT EXISTS (
                      SELECT 1 FROM notification
                      WHERE Id = NEW.NotificationId AND Topic = 'system.local-ai.task' AND State = 'Unread')
                BEGIN SELECT RAISE(ABORT, 'SCHEDULED_AI_TASK_WORK_PROVENANCE'); END;

                CREATE TRIGGER scheduled_ai_task_work_update_guard
                BEFORE UPDATE ON scheduled_ai_task_work
                WHEN NEW.NotificationId <> OLD.NotificationId OR NEW.EnqueuedAtUtc <> OLD.EnqueuedAtUtc
                  OR NEW.Revision <> OLD.Revision + 1 OR NEW.UpdatedAtUtc < OLD.UpdatedAtUtc OR NOT (
                    (OLD.State = 'ready' AND NEW.State = 'leased' AND NEW.AttemptCount = 1) OR
                    (OLD.State = 'retry' AND NEW.State = 'leased'
                        AND NEW.AttemptCount = OLD.AttemptCount + 1) OR
                    (OLD.State = 'leased' AND NEW.State = 'leased'
                        AND NEW.AttemptCount = OLD.AttemptCount + 1
                        AND OLD.LeaseExpiresAtUtc <= NEW.UpdatedAtUtc) OR
                    (OLD.State = 'leased' AND NEW.State = 'leased'
                        AND NEW.AttemptCount = OLD.AttemptCount
                        AND NEW.LeaseOwner = OLD.LeaseOwner
                        AND NEW.LeaseToken = OLD.LeaseToken
                        AND OLD.LeaseExpiresAtUtc > NEW.UpdatedAtUtc
                        AND NEW.LeaseExpiresAtUtc > OLD.LeaseExpiresAtUtc) OR
                    (OLD.State = 'leased' AND NEW.State IN ('retry', 'completed', 'failed')
                        AND NEW.AttemptCount = OLD.AttemptCount
                        AND OLD.LeaseExpiresAtUtc > NEW.UpdatedAtUtc) OR
                    (OLD.State = 'leased' AND NEW.State = 'failed'
                        AND NEW.AttemptCount = OLD.AttemptCount
                        AND OLD.AttemptCount >= 3
                        AND OLD.LeaseExpiresAtUtc <= NEW.UpdatedAtUtc))
                BEGIN SELECT RAISE(ABORT, 'SCHEDULED_AI_TASK_WORK_TRANSITION'); END;

                CREATE TRIGGER scheduled_ai_task_work_delete_guard
                BEFORE DELETE ON scheduled_ai_task_work
                BEGIN SELECT RAISE(ABORT, 'SCHEDULED_AI_TASK_WORK_DELETE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS scheduled_ai_task_work_insert_guard;
                DROP TRIGGER IF EXISTS scheduled_ai_task_work_update_guard;
                DROP TRIGGER IF EXISTS scheduled_ai_task_work_delete_guard;
                """);
            migrationBuilder.DropTable(
                name: "scheduled_ai_task_work");
        }
    }
}
