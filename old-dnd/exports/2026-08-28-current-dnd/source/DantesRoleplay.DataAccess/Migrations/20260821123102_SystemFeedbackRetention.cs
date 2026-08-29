using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class SystemFeedbackRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Explicit SQLite rebuild keeps the upgrade transactional. EF's generated check-
            // constraint rebuild emits a non-transactional foreign-key PRAGMA.
            migrationBuilder.Sql("PRAGMA defer_foreign_keys = ON;");
            migrationBuilder.Sql("""
                CREATE TABLE "system_feedback_report_rebuilt" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_system_feedback_report" PRIMARY KEY,
                    "RequestToken" TEXT NOT NULL, "PayloadFingerprint" TEXT NOT NULL,
                    "Category" TEXT NOT NULL, "Impact" TEXT NOT NULL, "State" TEXT NOT NULL,
                    "TriageRevision" INTEGER NOT NULL DEFAULT 0,
                    "RetentionRevision" INTEGER NOT NULL DEFAULT 0,
                    "ArchivedAt" TEXT NULL, "HoldState" TEXT NOT NULL DEFAULT 'None',
                    "Summary" TEXT NOT NULL, "Observed" TEXT NOT NULL, "Expected" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL, "SubmissionOperationId" TEXT NOT NULL,
                    CONSTRAINT "CK_system_feedback_report_id" CHECK (length("Id") = 41 AND substr("Id", 1, 9) = 'feedback.' AND substr("Id", 10) NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_system_feedback_report_token" CHECK (length("RequestToken") = 49 AND substr("RequestToken", 1, 17) = 'feedback-request.' AND substr("RequestToken", 18) NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_system_feedback_report_fingerprint" CHECK (length("PayloadFingerprint") = 64 AND "PayloadFingerprint" NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_system_feedback_report_category" CHECK ("Category" IN ('Defect', 'Friction', 'Documentation', 'Suggestion', 'Positive')),
                    CONSTRAINT "CK_system_feedback_report_impact" CHECK ("Impact" IN ('Blocked', 'Degraded', 'Minor', 'None')),
                    CONSTRAINT "CK_system_feedback_report_state" CHECK ("State" IN ('Open', 'Acknowledged', 'Resolved', 'Dismissed')),
                    CONSTRAINT "CK_system_feedback_report_triage_revision" CHECK ("TriageRevision" >= 0),
                    CONSTRAINT "CK_system_feedback_report_retention_revision" CHECK ("RetentionRevision" >= 0),
                    CONSTRAINT "CK_system_feedback_report_hold_state" CHECK ("HoldState" IN ('None', 'Held')),
                    CONSTRAINT "CK_system_feedback_report_operation" CHECK (length("SubmissionOperationId") = 32 AND "SubmissionOperationId" NOT GLOB '*[^0-9a-f]*')
                );
                INSERT INTO "system_feedback_report_rebuilt" ("Id", "RequestToken", "PayloadFingerprint", "Category", "Impact", "State", "TriageRevision", "RetentionRevision", "ArchivedAt", "HoldState", "Summary", "Observed", "Expected", "CreatedAt", "SubmissionOperationId")
                SELECT "Id", "RequestToken", "PayloadFingerprint", "Category", "Impact", "State", "TriageRevision", 0, NULL, 'None', "Summary", "Observed", "Expected", "CreatedAt", "SubmissionOperationId" FROM "system_feedback_report";
                DROP TABLE "system_feedback_report";
                ALTER TABLE "system_feedback_report_rebuilt" RENAME TO "system_feedback_report";
                CREATE UNIQUE INDEX "IX_system_feedback_report_RequestToken" ON "system_feedback_report" ("RequestToken");
                CREATE INDEX "IX_system_feedback_report_State_CreatedAt_Id" ON "system_feedback_report" ("State", "CreatedAt", "Id");
                CREATE INDEX "IX_system_feedback_report_Category_CreatedAt_Id" ON "system_feedback_report" ("Category", "CreatedAt", "Id");
                CREATE INDEX "IX_system_feedback_report_Impact_CreatedAt_Id" ON "system_feedback_report" ("Impact", "CreatedAt", "Id");
                CREATE INDEX "IX_system_feedback_report_ArchivedAt_State_Category_CreatedAt_Id" ON "system_feedback_report" ("ArchivedAt", "State", "Category", "CreatedAt", "Id");
                CREATE INDEX "IX_system_feedback_report_HoldState_State_CreatedAt_Id" ON "system_feedback_report" ("HoldState", "State", "CreatedAt", "Id");
                """);

            migrationBuilder.CreateTable(
                name: "system_feedback_retention_action",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 51, nullable: false),
                    ReportId = table.Column<string>(type: "TEXT", maxLength: 41, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FromArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    ToArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    FromHoldState = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ToHoldState = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    EffectiveAsOf = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_feedback_retention_action", x => x.Id);
                    table.CheckConstraint("CK_system_feedback_retention_action_action", "\"Action\" IN ('Archive', 'Restore', 'PlaceHold', 'ReleaseHold')");
                    table.CheckConstraint("CK_system_feedback_retention_action_changed", "(\"FromArchived\" <> \"ToArchived\") <> (\"FromHoldState\" <> \"ToHoldState\")");
                    table.CheckConstraint("CK_system_feedback_retention_action_effective_as_of", "(\"Action\" = 'Archive' AND \"EffectiveAsOf\" IS NOT NULL) OR (\"Action\" <> 'Archive' AND \"EffectiveAsOf\" IS NULL)");
                    table.CheckConstraint("CK_system_feedback_retention_action_from_hold", "\"FromHoldState\" IN ('None', 'Held')");
                    table.CheckConstraint("CK_system_feedback_retention_action_id", "length(\"Id\") = 51 AND substr(\"Id\", 1, 19) = 'feedback-retention.' AND substr(\"Id\", 20) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_system_feedback_retention_action_reference", "(\"Action\" IN ('PlaceHold', 'ReleaseHold') AND \"Reference\" IS NOT NULL AND length(\"Reference\") BETWEEN 1 AND 100) OR (\"Action\" IN ('Archive', 'Restore') AND \"Reference\" IS NULL)");
                    table.CheckConstraint("CK_system_feedback_retention_action_revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_system_feedback_retention_action_to_hold", "\"ToHoldState\" IN ('None', 'Held')");
                    table.ForeignKey(
                        name: "FK_system_feedback_retention_action_system_feedback_report_ReportId",
                        column: x => x.ReportId,
                        principalTable: "system_feedback_report",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_retention_action_Action_CreatedAt_Id",
                table: "system_feedback_retention_action",
                columns: new[] { "Action", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_retention_action_ReportId_Revision",
                table: "system_feedback_retention_action",
                columns: new[] { "ReportId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_feedback_retention_action");

            migrationBuilder.Sql("PRAGMA defer_foreign_keys = ON;");
            migrationBuilder.Sql("""
                CREATE TABLE "system_feedback_report_rebuilt" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_system_feedback_report" PRIMARY KEY,
                    "RequestToken" TEXT NOT NULL, "PayloadFingerprint" TEXT NOT NULL,
                    "Category" TEXT NOT NULL, "Impact" TEXT NOT NULL, "State" TEXT NOT NULL,
                    "TriageRevision" INTEGER NOT NULL DEFAULT 0,
                    "Summary" TEXT NOT NULL, "Observed" TEXT NOT NULL, "Expected" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL, "SubmissionOperationId" TEXT NOT NULL,
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
                SELECT "Id", "RequestToken", "PayloadFingerprint", "Category", "Impact", "State", "TriageRevision", "Summary", "Observed", "Expected", "CreatedAt", "SubmissionOperationId" FROM "system_feedback_report";
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
