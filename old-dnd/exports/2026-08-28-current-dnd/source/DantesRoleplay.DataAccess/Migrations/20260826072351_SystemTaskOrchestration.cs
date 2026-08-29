using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class SystemTaskOrchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_task",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 44, nullable: false),
                    PrincipalReference = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Intent = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SafeSummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    PlanFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContextProfile = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ContextFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContextSourceReferencesJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task", x => x.Id);
                    table.CheckConstraint("CK_system_task_bounds", "length(\"Intent\") BETWEEN 1 AND 8000 AND length(\"IdempotencyKey\") BETWEEN 1 AND 100 AND length(\"SafeSummary\") <= 1000 AND length(\"ContextSourceReferencesJson\") <= 16000 AND length(\"ErrorCode\") <= 100 AND length(\"ErrorMessage\") <= 500");
                    table.CheckConstraint("CK_system_task_hashes", "length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*' AND (\"PlanFingerprint\" = '' OR (length(\"PlanFingerprint\") = 64 AND \"PlanFingerprint\" NOT GLOB '*[^0-9A-F]*')) AND (\"ContextFingerprint\" = '' OR (length(\"ContextFingerprint\") = 64 AND \"ContextFingerprint\" NOT GLOB '*[^0-9A-F]*'))");
                    table.CheckConstraint("CK_system_task_id", "length(\"Id\") = 44 AND substr(\"Id\", 1, 12) = 'system-task.' AND substr(\"Id\", 13) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_system_task_operation", "\"Operation\" IN ('resolve', 'submit')");
                    table.CheckConstraint("CK_system_task_principal", "length(\"PrincipalReference\") = 74 AND substr(\"PrincipalReference\", 1, 10) = 'principal.' AND substr(\"PrincipalReference\", 11) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_system_task_status", "\"Status\" IN ('planning', 'prepared', 'completed', 'needs-input', 'unknown', 'unsupported', 'unavailable', 'failed')");
                    table.ForeignKey(
                        name: "FK_system_task_assistant_conversation_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "assistant_conversation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_task_confirmation",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 57, nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 44, nullable: false),
                    PrincipalReference = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    PlanFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AuthorizationEvidenceJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_confirmation", x => x.Id);
                    table.CheckConstraint("CK_system_task_confirmation_bounds", "length(\"IdempotencyKey\") BETWEEN 1 AND 100 AND length(\"AuthorizationEvidenceJson\") BETWEEN 2 AND 4000 AND \"ExpiresAtUtc\" > \"ConfirmedAtUtc\"");
                    table.CheckConstraint("CK_system_task_confirmation_hashes", "length(\"PlanFingerprint\") = 64 AND \"PlanFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_task_confirmation_id", "length(\"Id\") = 57 AND substr(\"Id\", 1, 25) = 'system-task-confirmation.' AND substr(\"Id\", 26) NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_system_task_confirmation_system_task_TaskId",
                        column: x => x.TaskId,
                        principalTable: "system_task",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_task_round",
                columns: table => new
                {
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 44, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Disposition = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ContextFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ResponseFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ModelProvider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ModelRevision = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ModelProfile = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    OutputJson = table.Column<string>(type: "TEXT", maxLength: 524288, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_round", x => new { x.TaskId, x.Ordinal });
                    table.CheckConstraint("CK_system_task_round_bounds", "length(\"Summary\") BETWEEN 1 AND 1000 AND length(\"EvidenceJson\") <= 16000 AND length(\"OutputJson\") <= 524288");
                    table.CheckConstraint("CK_system_task_round_disposition", "\"Disposition\" IN ('continue', 'prepared', 'completed', 'needs-input', 'unknown', 'unsupported', 'unavailable')");
                    table.CheckConstraint("CK_system_task_round_hashes", "length(\"ContextFingerprint\") = 64 AND \"ContextFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ResponseFingerprint\") = 64 AND \"ResponseFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_task_round_ordinal", "\"Ordinal\" BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_system_task_round_system_task_TaskId",
                        column: x => x.TaskId,
                        principalTable: "system_task",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_task_step",
                columns: table => new
                {
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 44, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    StepId = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    CapabilityId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CapabilityVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    DescriptorFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    InputJson = table.Column<string>(type: "TEXT", maxLength: 98304, nullable: false),
                    InputFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreflightStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PreconditionFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SafeSummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AffectedReferencesJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    DeferredStepIdsJson = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", maxLength: 1048576, nullable: false),
                    ResultFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_step", x => new { x.TaskId, x.Ordinal });
                    table.CheckConstraint("CK_system_task_step_bounds", "\"CapabilityVersion\" > 0 AND length(\"InputJson\") BETWEEN 2 AND 98304 AND length(\"SafeSummary\") BETWEEN 1 AND 1000 AND length(\"AffectedReferencesJson\") <= 16000 AND length(\"DeferredStepIdsJson\") <= 1024 AND length(\"ResultJson\") <= 1048576");
                    table.CheckConstraint("CK_system_task_step_hashes", "length(\"DescriptorFingerprint\") = 64 AND \"DescriptorFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"InputFingerprint\") = 64 AND \"InputFingerprint\" NOT GLOB '*[^0-9A-F]*' AND (\"PreconditionFingerprint\" = '' OR (length(\"PreconditionFingerprint\") = 64 AND \"PreconditionFingerprint\" NOT GLOB '*[^0-9A-F]*')) AND (\"ResultFingerprint\" = '' OR (length(\"ResultFingerprint\") = 64 AND \"ResultFingerprint\" NOT GLOB '*[^0-9A-F]*'))");
                    table.CheckConstraint("CK_system_task_step_id", "length(\"StepId\") = 8 AND substr(\"StepId\", 1, 5) = 'step-' AND substr(\"StepId\", 6) NOT GLOB '*[^0-9]*'");
                    table.CheckConstraint("CK_system_task_step_mode", "\"Mode\" IN ('read', 'write')");
                    table.CheckConstraint("CK_system_task_step_ordinal", "\"Ordinal\" BETWEEN 1 AND 12");
                    table.CheckConstraint("CK_system_task_step_preflight", "\"PreflightStatus\" IN ('read', 'ready', 'deferred')");
                    table.ForeignKey(
                        name: "FK_system_task_step_system_task_TaskId",
                        column: x => x.TaskId,
                        principalTable: "system_task",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_task_execution",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 52, nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 44, nullable: false),
                    ConfirmationId = table.Column<string>(type: "TEXT", maxLength: 57, nullable: false),
                    PrincipalReference = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PlanFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SafeSummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AuthorizationEvidenceJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_execution", x => x.Id);
                    table.CheckConstraint("CK_system_task_execution_bounds", "length(\"IdempotencyKey\") BETWEEN 1 AND 100 AND length(\"SafeSummary\") <= 1000 AND length(\"ErrorCode\") <= 100 AND length(\"ErrorMessage\") <= 500 AND length(\"AuthorizationEvidenceJson\") BETWEEN 2 AND 4000");
                    table.CheckConstraint("CK_system_task_execution_hashes", "length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"PlanFingerprint\") = 64 AND \"PlanFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_task_execution_id", "length(\"Id\") = 52 AND substr(\"Id\", 1, 20) = 'system-task-receipt.' AND substr(\"Id\", 21) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_system_task_execution_status", "\"Status\" IN ('running', 'succeeded', 'partial', 'failed', 'stale', 'unauthorized', 'cancelled', 'timed-out', 'indeterminate')");
                    table.ForeignKey(
                        name: "FK_system_task_execution_system_task_TaskId",
                        column: x => x.TaskId,
                        principalTable: "system_task",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_system_task_execution_system_task_confirmation_ConfirmationId",
                        column: x => x.ConfirmationId,
                        principalTable: "system_task_confirmation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_task_execution_step",
                columns: table => new
                {
                    ExecutionId = table.Column<string>(type: "TEXT", maxLength: 52, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskStepId = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ExecutionEvidenceJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OutputJson = table.Column<string>(type: "TEXT", maxLength: 1048576, nullable: false),
                    OutputFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReadBackFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_execution_step", x => new { x.ExecutionId, x.Ordinal });
                    table.CheckConstraint("CK_system_task_execution_step_bounds", "length(\"TaskStepId\") = 8 AND length(\"ExecutionEvidenceJson\") BETWEEN 2 AND 16000 AND length(\"OperationId\") <= 100 AND length(\"OutputJson\") <= 1048576 AND length(\"ErrorCode\") <= 100 AND length(\"ErrorMessage\") <= 500");
                    table.CheckConstraint("CK_system_task_execution_step_completion", "(\"Status\" = 'running' AND \"CompletedAtUtc\" IS NULL) OR (\"Status\" <> 'running' AND \"CompletedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_system_task_execution_step_hashes", "(\"OutputFingerprint\" = '' OR (length(\"OutputFingerprint\") = 64 AND \"OutputFingerprint\" NOT GLOB '*[^0-9A-F]*')) AND (\"ReadBackFingerprint\" = '' OR (length(\"ReadBackFingerprint\") = 64 AND \"ReadBackFingerprint\" NOT GLOB '*[^0-9A-F]*'))");
                    table.CheckConstraint("CK_system_task_execution_step_ordinal", "\"Ordinal\" BETWEEN 1 AND 12");
                    table.CheckConstraint("CK_system_task_execution_step_status", "\"Status\" IN ('running', 'succeeded', 'failed', 'stale', 'unauthorized', 'cancelled', 'timed-out', 'indeterminate', 'skipped')");
                    table.ForeignKey(
                        name: "FK_system_task_execution_step_system_task_execution_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "system_task_execution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_task_ConversationId",
                table: "system_task",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_system_task_PrincipalReference_ConversationId_CreatedAtUtc_Id",
                table: "system_task",
                columns: new[] { "PrincipalReference", "ConversationId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_system_task_PrincipalReference_ConversationId_IdempotencyKey",
                table: "system_task",
                columns: new[] { "PrincipalReference", "ConversationId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_task_confirmation_PrincipalReference_TaskId_IdempotencyKey",
                table: "system_task_confirmation",
                columns: new[] { "PrincipalReference", "TaskId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_task_confirmation_TaskId",
                table: "system_task_confirmation",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_system_task_execution_ConfirmationId",
                table: "system_task_execution",
                column: "ConfirmationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_task_execution_PrincipalReference_TaskId_IdempotencyKey",
                table: "system_task_execution",
                columns: new[] { "PrincipalReference", "TaskId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_task_execution_TaskId",
                table: "system_task_execution",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_system_task_step_TaskId_StepId",
                table: "system_task_step",
                columns: new[] { "TaskId", "StepId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_task_execution_step");

            migrationBuilder.DropTable(
                name: "system_task_round");

            migrationBuilder.DropTable(
                name: "system_task_step");

            migrationBuilder.DropTable(
                name: "system_task_execution");

            migrationBuilder.DropTable(
                name: "system_task_confirmation");

            migrationBuilder.DropTable(
                name: "system_task");
        }
    }
}
