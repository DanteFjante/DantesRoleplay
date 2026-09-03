using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InteractionMechanicSandbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "interaction_mechanic_sandbox_draft",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 55, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OpportunityProposalFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    QuotaSlot = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevisedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewPrincipalReference = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    ReviewAuthorizationEvidence = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PromotionPrincipalReference = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    PromotionAuthorizationEvidence = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PromotedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PromotionIdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PromotionRequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PromotionOperationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interaction_mechanic_sandbox_draft", x => x.Id);
                    table.CheckConstraint("CK_interaction_mechanic_sandbox_draft_bounds", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"ReviewPrincipalReference\") = 74 AND length(\"ReviewAuthorizationEvidence\") BETWEEN 1 AND 200 AND length(\"PromotionPrincipalReference\") <= 74 AND length(\"PromotionAuthorizationEvidence\") <= 200 AND length(\"PromotionIdempotencyKey\") <= 128 AND length(\"PromotionOperationId\") <= 200");
                    table.CheckConstraint("CK_interaction_mechanic_sandbox_draft_hashes", "length(\"OpportunityProposalFingerprint\") = 64 AND \"OpportunityProposalFingerprint\" NOT GLOB '*[^0-9A-F]*' AND (\"PromotionRequestFingerprint\" = '' OR length(\"PromotionRequestFingerprint\") = 64 AND \"PromotionRequestFingerprint\" NOT GLOB '*[^0-9A-F]*')");
                    table.CheckConstraint("CK_interaction_mechanic_sandbox_draft_id", "length(\"Id\") = 55 AND \"Id\" GLOB 'mechanic-sandbox-draft.[0-9a-f]*'");
                    table.CheckConstraint("CK_interaction_mechanic_sandbox_draft_quota_slot", "\"QuotaSlot\" BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_interaction_mechanic_sandbox_draft_revision", "\"CurrentRevision\" BETWEEN 1 AND 8");
                    table.CheckConstraint("CK_interaction_mechanic_sandbox_draft_status", "\"Status\" IN ('draft', 'validated', 'approved-for-export', 'expired')");
                });

            migrationBuilder.CreateTable(
                name: "interaction_mechanic_sandbox_draft_revision",
                columns: table => new
                {
                    DraftId = table.Column<string>(type: "TEXT", maxLength: 55, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    CandidateFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CandidateJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: false),
                    ValidationJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interaction_mechanic_sandbox_draft_revision", x => new { x.DraftId, x.Revision });
                    table.CheckConstraint("CK_interaction_mechanic_sandbox_revision_bounds", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND length(\"IdempotencyKey\") BETWEEN 1 AND 128 AND length(\"OperationId\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_interaction_mechanic_sandbox_revision_hashes", "length(\"CandidateFingerprint\") = 64 AND \"CandidateFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_interaction_mechanic_sandbox_revision_json", "length(\"CandidateJson\") BETWEEN 2 AND 262144 AND json_valid(\"CandidateJson\") AND json_type(\"CandidateJson\") = 'object' AND length(\"ValidationJson\") BETWEEN 2 AND 262144 AND json_valid(\"ValidationJson\") AND json_type(\"ValidationJson\") = 'object'");
                    table.CheckConstraint("CK_interaction_mechanic_sandbox_revision_number", "\"Revision\" BETWEEN 1 AND 8");
                    table.ForeignKey(
                        name: "FK_interaction_mechanic_sandbox_draft_revision_interaction_mechanic_sandbox_draft_DraftId",
                        column: x => x.DraftId,
                        principalTable: "interaction_mechanic_sandbox_draft",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_interaction_mechanic_sandbox_draft_ApplicationId_QuotaSlot",
                table: "interaction_mechanic_sandbox_draft",
                columns: new[] { "ApplicationId", "QuotaSlot" },
                unique: true,
                filter: "\"Status\" IN ('draft', 'validated')");

            migrationBuilder.CreateIndex(
                name: "IX_interaction_mechanic_sandbox_draft_ApplicationId_Status_ExpiresAtUtc",
                table: "interaction_mechanic_sandbox_draft",
                columns: new[] { "ApplicationId", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_interaction_mechanic_sandbox_draft_revision_ApplicationId_IdempotencyKey",
                table: "interaction_mechanic_sandbox_draft_revision",
                columns: new[] { "ApplicationId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "interaction_mechanic_sandbox_draft_revision");

            migrationBuilder.DropTable(
                name: "interaction_mechanic_sandbox_draft");
        }
    }
}
