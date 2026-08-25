using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class CodexConversationBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalStatus",
                table: "assistant_turn",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalTurnId",
                table: "assistant_turn",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalThreadId",
                table: "assistant_conversation",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "assistant_turn_activity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 41, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    TurnId = table.Column<string>(type: "TEXT", maxLength: 37, nullable: false),
                    ExternalItemId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assistant_turn_activity", x => x.Id);
                    table.CheckConstraint("CK_assistant_turn_activity_content", "length(\"ExternalItemId\") BETWEEN 1 AND 200 AND length(\"Summary\") BETWEEN 1 AND 500");
                    table.CheckConstraint("CK_assistant_turn_activity_id", "length(\"Id\") = 41 AND substr(\"Id\", 1, 9) = 'activity.' AND substr(\"Id\", 10) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_assistant_turn_activity_kind", "\"Kind\" IN ('command', 'file-change', 'mcp-tool', 'dynamic-tool', 'web-search', 'warning', 'error')");
                    table.CheckConstraint("CK_assistant_turn_activity_sequence", "\"Sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_assistant_turn_activity_assistant_conversation_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "assistant_conversation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_assistant_turn_activity_assistant_turn_TurnId",
                        column: x => x.TurnId,
                        principalTable: "assistant_turn",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turn_ExternalTurnId",
                table: "assistant_turn",
                column: "ExternalTurnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assistant_conversation_ExternalThreadId",
                table: "assistant_conversation",
                column: "ExternalThreadId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turn_activity_ConversationId",
                table: "assistant_turn_activity",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turn_activity_TurnId_ExternalItemId",
                table: "assistant_turn_activity",
                columns: new[] { "TurnId", "ExternalItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turn_activity_TurnId_Sequence",
                table: "assistant_turn_activity",
                columns: new[] { "TurnId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_turn_activity");

            migrationBuilder.DropIndex(
                name: "IX_assistant_turn_ExternalTurnId",
                table: "assistant_turn");

            migrationBuilder.DropIndex(
                name: "IX_assistant_conversation_ExternalThreadId",
                table: "assistant_conversation");

            migrationBuilder.DropColumn(
                name: "ExternalStatus",
                table: "assistant_turn");

            migrationBuilder.DropColumn(
                name: "ExternalTurnId",
                table: "assistant_turn");

            migrationBuilder.DropColumn(
                name: "ExternalThreadId",
                table: "assistant_conversation");
        }
    }
}
