using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class StoryPlanRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "story_plan_run",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 43, nullable: false),
                    RequestToken = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CampaignId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Objective = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    PlanJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    PrincipalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PolicyRevision = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    NextStepIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedStepCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CancelRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    StopCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StopMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    HandoffJson = table.Column<string>(type: "TEXT", maxLength: 32000, nullable: true),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_plan_run", x => x.Id);
                    table.CheckConstraint("CK_story_plan_run_id", "length(\"Id\") = 43 AND substr(\"Id\", 1, 11) = 'story-plan.' AND substr(\"Id\", 12) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_story_plan_run_revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_story_plan_run_status", "\"Status\" IN ('pending', 'running', 'completed', 'blocked', 'failed', 'cancelled')");
                    table.CheckConstraint("CK_story_plan_run_step_counts", "\"NextStepIndex\" >= 0 AND \"CompletedStepCount\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "story_plan_step_run",
                columns: table => new
                {
                    StoryPlanId = table.Column<string>(type: "TEXT", maxLength: 43, nullable: false),
                    StepIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    StepId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Intent = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RoleEntityIdsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    InputJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ProcedureEvidenceJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    MechanicId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    MechanicVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    ActionOperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", maxLength: 32000, nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_plan_step_run", x => new { x.StoryPlanId, x.StepIndex });
                    table.CheckConstraint("CK_story_plan_step_run_index", "\"StepIndex\" BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_story_plan_step_run_kind", "\"Kind\" IN ('campaign-context', 'knowledge', 'action')");
                    table.CheckConstraint("CK_story_plan_step_run_status", "\"Status\" IN ('pending', 'running', 'completed', 'blocked', 'failed', 'skipped')");
                    table.ForeignKey(
                        name: "FK_story_plan_step_run_story_plan_run_StoryPlanId",
                        column: x => x.StoryPlanId,
                        principalTable: "story_plan_run",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_story_plan_run_RequestToken",
                table: "story_plan_run",
                column: "RequestToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_plan_run_Status_LeaseUntilUtc_UpdatedAtUtc",
                table: "story_plan_run",
                columns: new[] { "Status", "LeaseUntilUtc", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "story_plan_step_run");

            migrationBuilder.DropTable(
                name: "story_plan_run");
        }
    }
}
