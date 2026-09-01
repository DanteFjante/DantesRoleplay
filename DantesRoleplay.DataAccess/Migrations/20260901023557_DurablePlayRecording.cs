using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class DurablePlayRecording : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_play_conversation",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PrincipalId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SessionContextId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentSituationId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_play_conversation", x => x.Id);
                    table.CheckConstraint("CK_application_play_conversation_revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_application_play_conversation_status", "\"Status\" IN ('ready', 'planning', 'awaiting-confirmation', 'needs-attention', 'unavailable')");
                    table.ForeignKey(
                        name: "FK_application_play_conversation_system_state_space_StateSpaceId",
                        column: x => x.StateSpaceId,
                        principalTable: "system_state_space",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "application_play_message",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SituationId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_play_message", x => x.Id);
                    table.CheckConstraint("CK_application_play_message_ordinal", "\"Ordinal\" > 0");
                    table.CheckConstraint("CK_application_play_message_role", "\"Role\" IN ('player', 'assistant')");
                    table.CheckConstraint("CK_application_play_message_text", "length(\"Text\") BETWEEN 1 AND 8000");
                    table.ForeignKey(
                        name: "FK_application_play_message_application_play_conversation_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "application_play_conversation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_play_situation",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ParticipantsJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    LocationJson = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_play_situation", x => x.Id);
                    table.CheckConstraint("CK_application_play_situation_json", "json_valid(\"ParticipantsJson\") AND (\"LocationJson\" = '' OR json_valid(\"LocationJson\"))");
                    table.CheckConstraint("CK_application_play_situation_kind", "\"Kind\" IN ('out-of-character', 'conversation', 'combat', 'exploration', 'investigation', 'travel', 'rest', 'downtime', 'other')");
                    table.CheckConstraint("CK_application_play_situation_revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_application_play_situation_status", "\"Status\" IN ('active', 'completed')");
                    table.CheckConstraint("CK_application_play_situation_summary", "length(\"Summary\") BETWEEN 1 AND 1000");
                    table.ForeignKey(
                        name: "FK_application_play_situation_application_play_conversation_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "application_play_conversation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_play_truth",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Statement = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    NormalizedHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SubjectEntityIdsJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    SourceMessageId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SituationId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_play_truth", x => x.Id);
                    table.CheckConstraint("CK_application_play_truth_hash", "length(\"NormalizedHash\") = 64 AND \"NormalizedHash\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_application_play_truth_ordinal", "\"Ordinal\" > 0");
                    table.CheckConstraint("CK_application_play_truth_statement", "length(\"Statement\") BETWEEN 1 AND 1000");
                    table.CheckConstraint("CK_application_play_truth_subjects", "json_valid(\"SubjectEntityIdsJson\") AND json_type(\"SubjectEntityIdsJson\") = 'array'");
                    table.ForeignKey(
                        name: "FK_application_play_truth_application_play_conversation_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "application_play_conversation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_play_conversation_PrincipalId_ApplicationId_StateSpaceId_SessionContextId",
                table: "application_play_conversation",
                columns: new[] { "PrincipalId", "ApplicationId", "StateSpaceId", "SessionContextId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_play_conversation_PrincipalId_ApplicationId_UpdatedAtUtc_Id",
                table: "application_play_conversation",
                columns: new[] { "PrincipalId", "ApplicationId", "UpdatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_application_play_conversation_StateSpaceId",
                table: "application_play_conversation",
                column: "StateSpaceId");

            migrationBuilder.CreateIndex(
                name: "IX_application_play_message_ConversationId_CreatedAtUtc_Id",
                table: "application_play_message",
                columns: new[] { "ConversationId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_application_play_message_ConversationId_Ordinal",
                table: "application_play_message",
                columns: new[] { "ConversationId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_play_situation_ConversationId_StartedAtUtc_Id",
                table: "application_play_situation",
                columns: new[] { "ConversationId", "StartedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_application_play_truth_ConversationId_NormalizedHash",
                table: "application_play_truth",
                columns: new[] { "ConversationId", "NormalizedHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_play_truth_ConversationId_Ordinal",
                table: "application_play_truth",
                columns: new[] { "ConversationId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_play_message");

            migrationBuilder.DropTable(
                name: "application_play_situation");

            migrationBuilder.DropTable(
                name: "application_play_truth");

            migrationBuilder.DropTable(
                name: "application_play_conversation");
        }
    }
}
