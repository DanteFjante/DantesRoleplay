using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class SystemFeedbackTriage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF's SQLite table-rebuild operation disables foreign keys with a PRAGMA that cannot
            // run in a transaction. Rebuild explicitly instead: deferred checking keeps the child
            // feedback-reference rows valid at transaction commit, so upgrade remains atomic.
            migrationBuilder.Sql("PRAGMA defer_foreign_keys = ON;");
            migrationBuilder.Sql("""
                CREATE TABLE "system_feedback_report_rebuilt" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_system_feedback_report" PRIMARY KEY,
                    "RequestToken" TEXT NOT NULL,
                    "PayloadFingerprint" TEXT NOT NULL,
                    "Category" TEXT NOT NULL,
                    "Impact" TEXT NOT NULL,
                    "State" TEXT NOT NULL,
                    "TriageRevision" INTEGER NOT NULL DEFAULT 0,
                    "Summary" TEXT NOT NULL,
                    "Observed" TEXT NOT NULL,
                    "Expected" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "SubmissionOperationId" TEXT NOT NULL,
                    CONSTRAINT "CK_system_feedback_report_id" CHECK (length("Id") = 41 AND substr("Id", 1, 9) = 'feedback.' AND substr("Id", 10) NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_system_feedback_report_token" CHECK (length("RequestToken") = 49 AND substr("RequestToken", 1, 17) = 'feedback-request.' AND substr("RequestToken", 18) NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_system_feedback_report_fingerprint" CHECK (length("PayloadFingerprint") = 64 AND "PayloadFingerprint" NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_system_feedback_report_category" CHECK ("Category" IN ('Defect', 'Friction', 'Documentation', 'Suggestion', 'Positive')),
                    CONSTRAINT "CK_system_feedback_report_impact" CHECK ("Impact" IN ('Blocked', 'Degraded', 'Minor', 'None')),
                    CONSTRAINT "CK_system_feedback_report_state" CHECK ("State" IN ('Open', 'Acknowledged', 'Resolved', 'Dismissed')),
                    CONSTRAINT "CK_system_feedback_report_triage_revision" CHECK ("TriageRevision" >= 0),
                    CONSTRAINT "CK_system_feedback_report_operation" CHECK (length("SubmissionOperationId") = 32 AND "SubmissionOperationId" NOT GLOB '*[^0-9a-f]*')
                );
                INSERT INTO "system_feedback_report_rebuilt" ("Id", "RequestToken", "PayloadFingerprint", "Category", "Impact", "State", "TriageRevision", "Summary", "Observed", "Expected", "CreatedAt", "SubmissionOperationId")
                SELECT "Id", "RequestToken", "PayloadFingerprint", "Category", "Impact", "State", 0, "Summary", "Observed", "Expected", "CreatedAt", "SubmissionOperationId" FROM "system_feedback_report";
                DROP TABLE "system_feedback_report";
                ALTER TABLE "system_feedback_report_rebuilt" RENAME TO "system_feedback_report";
                CREATE UNIQUE INDEX "IX_system_feedback_report_RequestToken" ON "system_feedback_report" ("RequestToken");
                CREATE INDEX "IX_system_feedback_report_State_CreatedAt_Id" ON "system_feedback_report" ("State", "CreatedAt", "Id");
                CREATE INDEX "IX_system_feedback_report_Category_CreatedAt_Id" ON "system_feedback_report" ("Category", "CreatedAt", "Id");
                CREATE INDEX "IX_system_feedback_report_Impact_CreatedAt_Id" ON "system_feedback_report" ("Impact", "CreatedAt", "Id");
                """);

            migrationBuilder.CreateTable(
                name: "system_feedback_disposition",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 53, nullable: false),
                    ReportId = table.Column<string>(type: "TEXT", maxLength: 41, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    FromState = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ToState = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_feedback_disposition", x => x.Id);
                    table.CheckConstraint("CK_system_feedback_disposition_changed", "\"FromState\" <> \"ToState\"");
                    table.CheckConstraint("CK_system_feedback_disposition_from", "\"FromState\" IN ('Open', 'Acknowledged', 'Resolved', 'Dismissed')");
                    table.CheckConstraint("CK_system_feedback_disposition_id", "length(\"Id\") = 53 AND substr(\"Id\", 1, 21) = 'feedback-disposition.' AND substr(\"Id\", 22) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_system_feedback_disposition_revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_system_feedback_disposition_to", "\"ToState\" IN ('Open', 'Acknowledged', 'Resolved', 'Dismissed')");
                    table.ForeignKey(
                        name: "FK_system_feedback_disposition_system_feedback_report_ReportId",
                        column: x => x.ReportId,
                        principalTable: "system_feedback_report",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_disposition_ReportId_Revision",
                table: "system_feedback_disposition",
                columns: new[] { "ReportId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_disposition_ToState_CreatedAt_Id",
                table: "system_feedback_disposition",
                columns: new[] { "ToState", "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_feedback_disposition");

            migrationBuilder.Sql("PRAGMA defer_foreign_keys = ON;");
            migrationBuilder.Sql("""
                CREATE TABLE "system_feedback_report_rebuilt" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_system_feedback_report" PRIMARY KEY,
                    "RequestToken" TEXT NOT NULL, "PayloadFingerprint" TEXT NOT NULL,
                    "Category" TEXT NOT NULL, "Impact" TEXT NOT NULL, "State" TEXT NOT NULL,
                    "Summary" TEXT NOT NULL, "Observed" TEXT NOT NULL, "Expected" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL, "SubmissionOperationId" TEXT NOT NULL,
                    CONSTRAINT "CK_system_feedback_report_id" CHECK (length("Id") = 41 AND substr("Id", 1, 9) = 'feedback.' AND substr("Id", 10) NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_system_feedback_report_token" CHECK (length("RequestToken") = 49 AND substr("RequestToken", 1, 17) = 'feedback-request.' AND substr("RequestToken", 18) NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_system_feedback_report_fingerprint" CHECK (length("PayloadFingerprint") = 64 AND "PayloadFingerprint" NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_system_feedback_report_category" CHECK ("Category" IN ('Defect', 'Friction', 'Documentation', 'Suggestion', 'Positive')),
                    CONSTRAINT "CK_system_feedback_report_impact" CHECK ("Impact" IN ('Blocked', 'Degraded', 'Minor', 'None')),
                    CONSTRAINT "CK_system_feedback_report_state" CHECK ("State" = 'Open'),
                    CONSTRAINT "CK_system_feedback_report_operation" CHECK (length("SubmissionOperationId") = 32 AND "SubmissionOperationId" NOT GLOB '*[^0-9a-f]*')
                );
                INSERT INTO "system_feedback_report_rebuilt" ("Id", "RequestToken", "PayloadFingerprint", "Category", "Impact", "State", "Summary", "Observed", "Expected", "CreatedAt", "SubmissionOperationId")
                SELECT "Id", "RequestToken", "PayloadFingerprint", "Category", "Impact", "State", "Summary", "Observed", "Expected", "CreatedAt", "SubmissionOperationId" FROM "system_feedback_report";
                DROP TABLE "system_feedback_report";
                ALTER TABLE "system_feedback_report_rebuilt" RENAME TO "system_feedback_report";
                CREATE UNIQUE INDEX "IX_system_feedback_report_RequestToken" ON "system_feedback_report" ("RequestToken");
                CREATE INDEX "IX_system_feedback_report_State_CreatedAt_Id" ON "system_feedback_report" ("State", "CreatedAt", "Id");
                CREATE INDEX "IX_system_feedback_report_Category_CreatedAt_Id" ON "system_feedback_report" ("Category", "CreatedAt", "Id");
                CREATE INDEX "IX_system_feedback_report_Impact_CreatedAt_Id" ON "system_feedback_report" ("Impact", "CreatedAt", "Id");
                """);
        }
    }
}
