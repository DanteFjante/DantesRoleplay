using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InteractionReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "interaction_resolution_receipt",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 52, nullable: false),
                    PrincipalReference = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    ApplicationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SessionContextId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StateRevision = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EffectiveSetFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RoleProfile = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ParentDelegationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AuthorizationEvidenceReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EnvelopeFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    QueryFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProposalFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SafeSummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interaction_resolution_receipt", x => x.Id);
                    table.CheckConstraint("CK_interaction_resolution_receipt_bounds", "\"ApplicationRevision\" > 0 AND length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"SessionContextId\") BETWEEN 1 AND 200 AND length(\"StateRevision\") BETWEEN 1 AND 200 AND length(\"RoleProfile\") BETWEEN 1 AND 300 AND (\"ConversationId\" IS NULL OR length(\"ConversationId\") BETWEEN 1 AND 200) AND (\"ParentDelegationId\" IS NULL OR length(\"ParentDelegationId\") BETWEEN 1 AND 200) AND length(\"AuthorizationEvidenceReference\") BETWEEN 1 AND 200 AND length(\"IdempotencyKey\") BETWEEN 1 AND 128 AND length(\"Code\") BETWEEN 1 AND 200 AND length(\"SafeSummary\") <= 1000");
                    table.CheckConstraint("CK_interaction_resolution_receipt_evidence", "length(\"EvidenceJson\") <= 16384 AND json_valid(\"EvidenceJson\") AND json_type(\"EvidenceJson\") = 'array' AND json_array_length(\"EvidenceJson\") BETWEEN 0 AND 16");
                    table.CheckConstraint("CK_interaction_resolution_receipt_hashes", "length(\"ApplicationFingerprint\") = 64 AND \"ApplicationFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"EffectiveSetFingerprint\") = 64 AND \"EffectiveSetFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"EnvelopeFingerprint\") = 64 AND \"EnvelopeFingerprint\" NOT GLOB '*[^0-9A-F]*' AND (\"QueryFingerprint\" IS NULL OR (length(\"QueryFingerprint\") = 64 AND \"QueryFingerprint\" NOT GLOB '*[^0-9A-F]*')) AND (\"ProposalFingerprint\" IS NULL OR (length(\"ProposalFingerprint\") = 64 AND \"ProposalFingerprint\" NOT GLOB '*[^0-9A-F]*'))");
                    table.CheckConstraint("CK_interaction_resolution_receipt_id", "length(\"Id\") = 52 AND substr(\"Id\", 1, 20) = 'interaction-receipt.' AND substr(\"Id\", 21) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_interaction_resolution_receipt_principal", "length(\"PrincipalReference\") = 74 AND substr(\"PrincipalReference\", 1, 10) = 'principal.' AND substr(\"PrincipalReference\", 11) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_interaction_resolution_receipt_proposal", "(\"Status\" = 'resolved' AND \"ProposalFingerprint\" IS NOT NULL) OR (\"Status\" <> 'resolved' AND \"ProposalFingerprint\" IS NULL)");
                    table.CheckConstraint("CK_interaction_resolution_receipt_status", "\"Status\" IN ('resolved', 'needs-input', 'ambiguous', 'unknown', 'unsupported', 'unavailable', 'unsafe', 'stale')");
                });

            migrationBuilder.CreateTable(
                name: "interaction_execution_receipt",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 52, nullable: false),
                    ResolutionReceiptId = table.Column<string>(type: "TEXT", maxLength: 52, nullable: false),
                    PrincipalReference = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExecutionRequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProposalFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Disposition = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SafeSummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interaction_execution_receipt", x => x.Id);
                    table.CheckConstraint("CK_interaction_execution_receipt_bounds", "length(\"ResolutionReceiptId\") = 52 AND length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"IdempotencyKey\") BETWEEN 1 AND 128 AND length(\"SafeSummary\") <= 1000");
                    table.CheckConstraint("CK_interaction_execution_receipt_disposition", "\"Disposition\" IN ('succeeded', 'failed', 'partial', 'skipped', 'stale', 'unauthorized', 'cancelled', 'timed-out')");
                    table.CheckConstraint("CK_interaction_execution_receipt_evidence", "length(\"EvidenceJson\") <= 16384 AND json_valid(\"EvidenceJson\") AND json_type(\"EvidenceJson\") = 'array' AND json_array_length(\"EvidenceJson\") BETWEEN 0 AND 16");
                    table.CheckConstraint("CK_interaction_execution_receipt_hashes", "length(\"ExecutionRequestFingerprint\") = 64 AND \"ExecutionRequestFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ProposalFingerprint\") = 64 AND \"ProposalFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_interaction_execution_receipt_id", "length(\"Id\") = 52 AND substr(\"Id\", 1, 20) = 'interaction-receipt.' AND substr(\"Id\", 21) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_interaction_execution_receipt_principal", "length(\"PrincipalReference\") = 74 AND substr(\"PrincipalReference\", 1, 10) = 'principal.' AND substr(\"PrincipalReference\", 11) NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_interaction_execution_receipt_interaction_resolution_receipt_ResolutionReceiptId",
                        column: x => x.ResolutionReceiptId,
                        principalTable: "interaction_resolution_receipt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "interaction_execution_receipt_step",
                columns: table => new
                {
                    ExecutionReceiptId = table.Column<string>(type: "TEXT", maxLength: 52, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    ProposalStepId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Disposition = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interaction_execution_receipt_step", x => new { x.ExecutionReceiptId, x.Ordinal });
                    table.CheckConstraint("CK_interaction_execution_receipt_step_bounds", "length(\"ProposalStepId\") BETWEEN 1 AND 200 AND (\"OperationId\" IS NULL OR length(\"OperationId\") BETWEEN 1 AND 40)");
                    table.CheckConstraint("CK_interaction_execution_receipt_step_disposition", "\"Disposition\" IN ('succeeded', 'failed', 'skipped')");
                    table.CheckConstraint("CK_interaction_execution_receipt_step_ordinal", "\"Ordinal\" BETWEEN 1 AND 16");
                    table.ForeignKey(
                        name: "FK_interaction_execution_receipt_step_interaction_execution_receipt_ExecutionReceiptId",
                        column: x => x.ExecutionReceiptId,
                        principalTable: "interaction_execution_receipt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_interaction_execution_receipt_step_operation_OperationId",
                        column: x => x.OperationId,
                        principalTable: "operation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_interaction_execution_receipt_PrincipalReference_ApplicationId_StateSpaceId_ResolutionReceiptId_IdempotencyKey",
                table: "interaction_execution_receipt",
                columns: new[] { "PrincipalReference", "ApplicationId", "StateSpaceId", "ResolutionReceiptId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_interaction_execution_receipt_ResolutionReceiptId",
                table: "interaction_execution_receipt",
                column: "ResolutionReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_interaction_execution_receipt_step_ExecutionReceiptId_ProposalStepId",
                table: "interaction_execution_receipt_step",
                columns: new[] { "ExecutionReceiptId", "ProposalStepId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_interaction_execution_receipt_step_OperationId",
                table: "interaction_execution_receipt_step",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_interaction_resolution_receipt_ApplicationId_StateSpaceId_CreatedAtUtc_Id",
                table: "interaction_resolution_receipt",
                columns: new[] { "ApplicationId", "StateSpaceId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_interaction_resolution_receipt_PrincipalReference_ApplicationId_StateSpaceId_IdempotencyKey",
                table: "interaction_resolution_receipt",
                columns: new[] { "PrincipalReference", "ApplicationId", "StateSpaceId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new InvalidOperationException(
                "Interaction receipts are durable append-only audit evidence. Restore a database backup rather than downgrading this migration.");
    }
}
