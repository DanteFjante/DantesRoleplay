using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TriggerSchedulingOneTimeWorker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trigger_fire_work",
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
                    table.PrimaryKey("PK_trigger_fire_work", x => x.FireId);
                    table.CheckConstraint("CK_trigger_fire_work_id", "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_fire_work_lease", "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                    table.CheckConstraint("CK_trigger_fire_work_state", "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'missed', 'failed') AND ((\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR (\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL) OR (\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('handler-unavailable', 'transient-database')) OR (\"State\" IN ('completed', 'missed') AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR (\"State\" = 'failed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted'))) ");
                    table.CheckConstraint("CK_trigger_fire_work_token", "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
                    table.CheckConstraint("CK_trigger_fire_work_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                    table.ForeignKey(
                        name: "FK_trigger_fire_work_trigger_one_time_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_one_time_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_fire_work_ApplicationId_TriggerId_TriggerVersion_OccurrenceAtUtc",
                table: "trigger_fire_work",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "OccurrenceAtUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_fire_work_State_NextAttemptAtUtc_LeaseExpiresAtUtc",
                table: "trigger_fire_work",
                columns: new[] { "State", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.Sql("""
                CREATE TRIGGER trigger_fire_work_transition_update
                BEFORE UPDATE ON trigger_fire_work
                WHEN NOT (
                    OLD.State IN ('ready', 'leased', 'retry')
                    AND NEW.Revision = OLD.Revision + 1
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
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_FIRE_WORK_TRANSITION_DENIED'); END;

                CREATE TRIGGER trigger_fire_work_delete_denied
                BEFORE DELETE ON trigger_fire_work
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_FIRE_WORK_DELETE_DENIED'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_fire_work_transition_update;
                DROP TRIGGER IF EXISTS trigger_fire_work_delete_denied;
                """);
            migrationBuilder.DropTable(
                name: "trigger_fire_work");
        }
    }
}
