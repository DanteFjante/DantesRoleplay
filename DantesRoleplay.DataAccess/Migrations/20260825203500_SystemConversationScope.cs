using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class SystemConversationScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_assistant_conversation_OperatorId_Provider_UpdatedAtUtc_Id",
                table: "assistant_conversation");

            migrationBuilder.Sql(
                "ALTER TABLE assistant_turn ADD COLUMN ContextFingerprint TEXT NOT NULL DEFAULT '' " +
                "CHECK (ContextFingerprint = '' OR (length(ContextFingerprint) = 64 AND " +
                "ContextFingerprint NOT GLOB '*[^0-9A-F]*'))");
            migrationBuilder.Sql(
                "ALTER TABLE assistant_turn ADD COLUMN ContextProfile TEXT NOT NULL DEFAULT '' " +
                "CHECK (ContextProfile IN ('', 'system-read-v1'))");
            migrationBuilder.Sql(
                "ALTER TABLE assistant_turn ADD COLUMN ContextSourceReferencesJson TEXT NOT NULL DEFAULT '' " +
                "CHECK (length(ContextSourceReferencesJson) <= 8000 AND " +
                "(ContextSourceReferencesJson = '' OR json_valid(ContextSourceReferencesJson)))");
            migrationBuilder.Sql(
                "ALTER TABLE assistant_turn ADD COLUMN ResponseDisposition TEXT NOT NULL DEFAULT '' " +
                "CHECK (ResponseDisposition IN ('', 'answered', 'unknown', 'unsupported', 'needs-input', " +
                "'needs-application', 'unavailable'))");
            migrationBuilder.Sql(
                "ALTER TABLE assistant_conversation ADD COLUMN Scope TEXT NOT NULL DEFAULT 'advisory' " +
                "CHECK (Scope IN ('advisory', 'system'))");

            migrationBuilder.CreateIndex(
                name: "IX_assistant_conversation_OperatorId_Scope_Provider_UpdatedAtUtc_Id",
                table: "assistant_conversation",
                columns: new[] { "OperatorId", "Scope", "Provider", "UpdatedAtUtc", "Id" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_assistant_conversation_OperatorId_Scope_Provider_UpdatedAtUtc_Id",
                table: "assistant_conversation");

            migrationBuilder.Sql("ALTER TABLE assistant_turn DROP COLUMN ContextFingerprint");
            migrationBuilder.Sql("ALTER TABLE assistant_turn DROP COLUMN ContextProfile");
            migrationBuilder.Sql("ALTER TABLE assistant_turn DROP COLUMN ContextSourceReferencesJson");
            migrationBuilder.Sql("ALTER TABLE assistant_turn DROP COLUMN ResponseDisposition");
            migrationBuilder.Sql("ALTER TABLE assistant_conversation DROP COLUMN Scope");

            migrationBuilder.CreateIndex(
                name: "IX_assistant_conversation_OperatorId_Provider_UpdatedAtUtc_Id",
                table: "assistant_conversation",
                columns: new[] { "OperatorId", "Provider", "UpdatedAtUtc", "Id" });
        }
    }
}
