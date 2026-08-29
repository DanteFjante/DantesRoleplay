using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class SystemFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_feedback_report",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 41, nullable: false),
                    RequestToken = table.Column<string>(type: "TEXT", maxLength: 49, nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Impact = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Observed = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Expected = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubmissionOperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_feedback_report", x => x.Id);
                    table.CheckConstraint("CK_system_feedback_report_category", "\"Category\" IN ('Defect', 'Friction', 'Documentation', 'Suggestion', 'Positive')");
                    table.CheckConstraint("CK_system_feedback_report_fingerprint", "length(\"PayloadFingerprint\") = 64 AND \"PayloadFingerprint\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_system_feedback_report_id", "length(\"Id\") = 41 AND substr(\"Id\", 1, 9) = 'feedback.' AND substr(\"Id\", 10) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_system_feedback_report_impact", "\"Impact\" IN ('Blocked', 'Degraded', 'Minor', 'None')");
                    table.CheckConstraint("CK_system_feedback_report_operation", "length(\"SubmissionOperationId\") = 32 AND \"SubmissionOperationId\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_system_feedback_report_state", "\"State\" = 'Open'");
                    table.CheckConstraint("CK_system_feedback_report_token", "length(\"RequestToken\") = 49 AND substr(\"RequestToken\", 1, 17) = 'feedback-request.' AND substr(\"RequestToken\", 18) NOT GLOB '*[^0-9a-f]*'");
                });

            migrationBuilder.CreateTable(
                name: "system_feedback_operation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<string>(type: "TEXT", maxLength: 41, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_feedback_operation", x => x.Id);
                    table.CheckConstraint("CK_system_feedback_operation_ordinal", "\"Ordinal\" BETWEEN 0 AND 7");
                    table.ForeignKey(
                        name: "FK_system_feedback_operation_system_feedback_report_ReportId",
                        column: x => x.ReportId,
                        principalTable: "system_feedback_report",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_feedback_procedure",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<string>(type: "TEXT", maxLength: 41, nullable: false),
                    ProcedureId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProcedureVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_feedback_procedure", x => x.Id);
                    table.CheckConstraint("CK_system_feedback_procedure_ordinal", "\"Ordinal\" BETWEEN 0 AND 7");
                    table.ForeignKey(
                        name: "FK_system_feedback_procedure_system_feedback_report_ReportId",
                        column: x => x.ReportId,
                        principalTable: "system_feedback_report",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_feedback_step",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<string>(type: "TEXT", maxLength: 41, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_feedback_step", x => x.Id);
                    table.CheckConstraint("CK_system_feedback_step_ordinal", "\"Ordinal\" BETWEEN 0 AND 7");
                    table.ForeignKey(
                        name: "FK_system_feedback_step_system_feedback_report_ReportId",
                        column: x => x.ReportId,
                        principalTable: "system_feedback_report",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_operation_OperationId",
                table: "system_feedback_operation",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_operation_ReportId_OperationId",
                table: "system_feedback_operation",
                columns: new[] { "ReportId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_operation_ReportId_Ordinal",
                table: "system_feedback_operation",
                columns: new[] { "ReportId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_procedure_ProcedureId_ProcedureVersion",
                table: "system_feedback_procedure",
                columns: new[] { "ProcedureId", "ProcedureVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_procedure_ReportId_Ordinal",
                table: "system_feedback_procedure",
                columns: new[] { "ReportId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_procedure_ReportId_ProcedureId",
                table: "system_feedback_procedure",
                columns: new[] { "ReportId", "ProcedureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_report_Category_CreatedAt_Id",
                table: "system_feedback_report",
                columns: new[] { "Category", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_report_Impact_CreatedAt_Id",
                table: "system_feedback_report",
                columns: new[] { "Impact", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_report_RequestToken",
                table: "system_feedback_report",
                column: "RequestToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_report_State_CreatedAt_Id",
                table: "system_feedback_report",
                columns: new[] { "State", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_system_feedback_step_ReportId_Ordinal",
                table: "system_feedback_step",
                columns: new[] { "ReportId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_feedback_operation");

            migrationBuilder.DropTable(
                name: "system_feedback_procedure");

            migrationBuilder.DropTable(
                name: "system_feedback_step");

            migrationBuilder.DropTable(
                name: "system_feedback_report");
        }
    }
}
