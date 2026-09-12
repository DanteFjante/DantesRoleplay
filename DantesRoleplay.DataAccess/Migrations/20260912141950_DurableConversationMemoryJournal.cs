using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class DurableConversationMemoryJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_conversation_memory_journal",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PrincipalId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SessionContextId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceClient = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SourceProjectId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RepositoryRoot = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    SourceThreadId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    NextOrdinal = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSourceTurnId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_conversation_memory_journal", x => x.Id);
                    table.CheckConstraint("CK_application_conversation_memory_journal_next_ordinal", "\"NextOrdinal\" > 0");
                    table.CheckConstraint("CK_application_conversation_memory_journal_revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_application_conversation_memory_journal_status", "\"Status\" IN ('connected', 'retry-pending', 'disconnected', 'archived', 'deleted')");
                    table.ForeignKey(
                        name: "FK_application_conversation_memory_journal_system_state_space_StateSpaceId",
                        column: x => x.StateSpaceId,
                        principalTable: "system_state_space",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "application_conversation_memory_delivery",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    JournalId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    RequestToken = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SourceTurnId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MessageIdsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_conversation_memory_delivery", x => x.Id);
                    table.CheckConstraint("CK_application_conversation_memory_delivery_fingerprint", "length(\"PayloadFingerprint\") = 64 AND \"PayloadFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.ForeignKey(
                        name: "FK_application_conversation_memory_delivery_application_conversation_memory_journal_JournalId",
                        column: x => x.JournalId,
                        principalTable: "application_conversation_memory_journal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_conversation_memory_derived",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    JournalId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CommandId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SourceRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CandidateJson = table.Column<string>(type: "TEXT", maxLength: 64000, nullable: false),
                    ResultSchemaFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CompletionEvidenceReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Audience = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_conversation_memory_derived", x => x.Id);
                    table.CheckConstraint("CK_application_conversation_memory_derived_audience", "\"Audience\" = 'private'");
                    table.CheckConstraint("CK_application_conversation_memory_derived_fingerprints", "length(\"SourceFingerprint\") = 64 AND \"SourceFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ResultSchemaFingerprint\") = 64 AND \"ResultSchemaFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_application_conversation_memory_derived_json", "json_valid(\"CandidateJson\")");
                    table.CheckConstraint("CK_application_conversation_memory_derived_source_revision", "\"SourceRevision\" > 0");
                    table.CheckConstraint("CK_application_conversation_memory_derived_status", "\"Status\" IN ('candidate', 'archived')");
                    table.ForeignKey(
                        name: "FK_application_conversation_memory_derived_application_conversation_memory_journal_JournalId",
                        column: x => x.JournalId,
                        principalTable: "application_conversation_memory_journal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_conversation_memory_message",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    JournalId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceTurnId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceMessageId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    TextFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CaptureProvenance = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SourceAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_conversation_memory_message", x => x.Id);
                    table.CheckConstraint("CK_application_conversation_memory_message_fingerprint", "length(\"TextFingerprint\") = 64 AND \"TextFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_application_conversation_memory_message_kind", "\"SourceKind\" IN ('user-prompt', 'assistant-commentary', 'assistant-final')");
                    table.CheckConstraint("CK_application_conversation_memory_message_ordinal", "\"Ordinal\" > 0");
                    table.CheckConstraint("CK_application_conversation_memory_message_role", "\"Role\" IN ('user', 'assistant')");
                    table.CheckConstraint("CK_application_conversation_memory_message_status", "\"Status\" IN ('captured', 'deleted')");
                    table.CheckConstraint("CK_application_conversation_memory_message_text", "(\"Status\" = 'captured' AND length(\"Text\") BETWEEN 1 AND 16000) OR (\"Status\" = 'deleted' AND \"Text\" = '')");
                    table.ForeignKey(
                        name: "FK_application_conversation_memory_message_application_conversation_memory_journal_JournalId",
                        column: x => x.JournalId,
                        principalTable: "application_conversation_memory_journal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_conversation_memory_derived_source",
                columns: table => new
                {
                    DerivedId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_conversation_memory_derived_source", x => new { x.DerivedId, x.MessageId });
                    table.ForeignKey(
                        name: "FK_application_conversation_memory_derived_source_application_conversation_memory_derived_DerivedId",
                        column: x => x.DerivedId,
                        principalTable: "application_conversation_memory_derived",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_application_conversation_memory_derived_source_application_conversation_memory_message_MessageId",
                        column: x => x.MessageId,
                        principalTable: "application_conversation_memory_message",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_conversation_memory_delivery_JournalId_RequestToken",
                table: "application_conversation_memory_delivery",
                columns: new[] { "JournalId", "RequestToken" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_conversation_memory_delivery_JournalId_SourceTurnId",
                table: "application_conversation_memory_delivery",
                columns: new[] { "JournalId", "SourceTurnId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_conversation_memory_derived_JournalId_TaskId_CommandId",
                table: "application_conversation_memory_derived",
                columns: new[] { "JournalId", "TaskId", "CommandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_conversation_memory_derived_source_MessageId",
                table: "application_conversation_memory_derived_source",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_application_conversation_memory_journal_PrincipalId_ApplicationId_StateSpaceId_SessionContextId",
                table: "application_conversation_memory_journal",
                columns: new[] { "PrincipalId", "ApplicationId", "StateSpaceId", "SessionContextId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_conversation_memory_journal_SourceClient_SourceProjectId_SourceThreadId",
                table: "application_conversation_memory_journal",
                columns: new[] { "SourceClient", "SourceProjectId", "SourceThreadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_conversation_memory_journal_StateSpaceId",
                table: "application_conversation_memory_journal",
                column: "StateSpaceId");

            migrationBuilder.CreateIndex(
                name: "IX_application_conversation_memory_message_JournalId_Ordinal",
                table: "application_conversation_memory_message",
                columns: new[] { "JournalId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_conversation_memory_message_JournalId_SourceMessageId",
                table: "application_conversation_memory_message",
                columns: new[] { "JournalId", "SourceMessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_conversation_memory_delivery");

            migrationBuilder.DropTable(
                name: "application_conversation_memory_derived_source");

            migrationBuilder.DropTable(
                name: "application_conversation_memory_derived");

            migrationBuilder.DropTable(
                name: "application_conversation_memory_message");

            migrationBuilder.DropTable(
                name: "application_conversation_memory_journal");
        }
    }
}
