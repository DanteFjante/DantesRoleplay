using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class CodexTurnApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_turn_approval",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 41, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    TurnId = table.Column<string>(type: "TEXT", maxLength: 37, nullable: false),
                    OperatorId = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    ExternalRequestId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExternalItemId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExternalApprovalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DetailsJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    CanAccept = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DispatchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assistant_turn_approval", x => x.Id);
                    table.CheckConstraint("CK_assistant_turn_approval_content", "length(\"ExternalRequestId\") BETWEEN 1 AND 200 AND length(\"ExternalItemId\") BETWEEN 1 AND 200 AND length(\"Summary\") BETWEEN 1 AND 500 AND length(\"DetailsJson\") BETWEEN 2 AND 8192");
                    table.CheckConstraint("CK_assistant_turn_approval_decision", "\"Decision\" IS NULL OR \"Decision\" IN ('accept', 'decline', 'cancel')");
                    table.CheckConstraint("CK_assistant_turn_approval_hash", "length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_assistant_turn_approval_id", "length(\"Id\") = 41 AND substr(\"Id\", 1, 9) = 'approval.' AND substr(\"Id\", 10) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_assistant_turn_approval_kind", "\"Kind\" IN ('command', 'file-change', 'network', 'permissions')");
                    table.CheckConstraint("CK_assistant_turn_approval_revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_assistant_turn_approval_status", "\"Status\" IN ('pending', 'decided', 'dispatched', 'resolved', 'expired', 'cancelled', 'failed')");
                    table.ForeignKey(
                        name: "FK_assistant_turn_approval_assistant_conversation_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "assistant_conversation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_assistant_turn_approval_assistant_turn_TurnId",
                        column: x => x.TurnId,
                        principalTable: "assistant_turn",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turn_approval_ConversationId",
                table: "assistant_turn_approval",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turn_approval_OperatorId_TurnId_Status",
                table: "assistant_turn_approval",
                columns: new[] { "OperatorId", "TurnId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turn_approval_TurnId_ExternalRequestId",
                table: "assistant_turn_approval",
                columns: new[] { "TurnId", "ExternalRequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_turn_approval");
        }
    }
}
