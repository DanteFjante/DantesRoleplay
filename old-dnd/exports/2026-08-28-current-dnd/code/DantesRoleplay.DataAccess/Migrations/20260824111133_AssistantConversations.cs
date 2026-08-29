using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AssistantConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_conversation",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    OperatorId = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assistant_conversation", x => x.Id);
                    table.CheckConstraint("CK_assistant_conversation_id", "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'conversation.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_assistant_conversation_operator", "length(\"OperatorId\") = 74 AND substr(\"OperatorId\", 1, 10) = 'principal.' AND substr(\"OperatorId\", 11) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_assistant_conversation_provider", "\"Provider\" IN ('local', 'codex')");
                    table.CheckConstraint("CK_assistant_conversation_revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_assistant_conversation_status", "\"Status\" IN ('pending', 'running', 'awaiting-approval', 'completed', 'failed', 'cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "assistant_turn",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 37, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    OperatorId = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TurnNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ModelProvider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ModelRevision = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ModelProfile = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ElapsedMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assistant_turn", x => x.Id);
                    table.CheckConstraint("CK_assistant_turn_hash", "length(\"RequestHash\") = 64 AND \"RequestHash\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_assistant_turn_id", "length(\"Id\") = 37 AND substr(\"Id\", 1, 5) = 'turn.' AND substr(\"Id\", 6) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_assistant_turn_metrics", "\"ElapsedMilliseconds\" >= 0 AND \"PromptTokens\" >= 0 AND \"OutputTokens\" >= 0");
                    table.CheckConstraint("CK_assistant_turn_number", "\"TurnNumber\" > 0");
                    table.CheckConstraint("CK_assistant_turn_provider", "\"Provider\" IN ('local', 'codex')");
                    table.CheckConstraint("CK_assistant_turn_status", "\"Status\" IN ('pending', 'running', 'awaiting-approval', 'completed', 'failed', 'cancelled')");
                    table.ForeignKey(
                        name: "FK_assistant_turn_assistant_conversation_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "assistant_conversation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assistant_message",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    TurnId = table.Column<string>(type: "TEXT", maxLength: 37, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assistant_message", x => x.Id);
                    table.CheckConstraint("CK_assistant_message_content", "length(\"Content\") BETWEEN 1 AND 8000");
                    table.CheckConstraint("CK_assistant_message_id", "length(\"Id\") = 40 AND substr(\"Id\", 1, 8) = 'message.' AND substr(\"Id\", 9) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_assistant_message_ordinal", "\"Ordinal\" > 0");
                    table.CheckConstraint("CK_assistant_message_role", "\"Role\" IN ('user', 'assistant')");
                    table.ForeignKey(
                        name: "FK_assistant_message_assistant_conversation_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "assistant_conversation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_assistant_message_assistant_turn_TurnId",
                        column: x => x.TurnId,
                        principalTable: "assistant_turn",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_assistant_conversation_OperatorId_Provider_UpdatedAtUtc_Id",
                table: "assistant_conversation",
                columns: new[] { "OperatorId", "Provider", "UpdatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_assistant_message_ConversationId_Ordinal",
                table: "assistant_message",
                columns: new[] { "ConversationId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assistant_message_TurnId",
                table: "assistant_message",
                column: "TurnId");

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turn_ConversationId_TurnNumber",
                table: "assistant_turn",
                columns: new[] { "ConversationId", "TurnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turn_OperatorId_Provider_IdempotencyKey",
                table: "assistant_turn",
                columns: new[] { "OperatorId", "Provider", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_message");

            migrationBuilder.DropTable(
                name: "assistant_turn");

            migrationBuilder.DropTable(
                name: "assistant_conversation");
        }
    }
}
