using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_application_activation_revision",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    ActivationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreviewFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ScannedDocumentsFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CandidateManifestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DependencyGraphFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActivationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DependencyCoverageVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DependencyCoverageComplete = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActivatedByOperationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_activation_revision", x => new { x.ApplicationId, x.ActivationRevision });
                    table.CheckConstraint("CK_system_application_activation_revision_hashes", "length(\"ApplicationFingerprint\") = 64 AND \"ApplicationFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"PreviewFingerprint\") = 64 AND \"PreviewFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ScannedDocumentsFingerprint\") = 64 AND \"ScannedDocumentsFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"CandidateManifestFingerprint\") = 64 AND \"CandidateManifestFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"DependencyGraphFingerprint\") = 64 AND \"DependencyGraphFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ActivationFingerprint\") = 64 AND \"ActivationFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_application_activation_revision_number", "\"ActivationRevision\" > 0 AND \"ApplicationRevision\" > 0");
                    table.ForeignKey(
                        name: "FK_system_application_activation_revision_operation_ActivatedByOperationId",
                        column: x => x.ActivatedByOperationId,
                        principalTable: "operation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_application_activation_revision_system_application_revision_ApplicationId_ApplicationRevision",
                        columns: x => new { x.ApplicationId, x.ApplicationRevision },
                        principalTable: "system_application_revision",
                        principalColumns: new[] { "ApplicationId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_application_activation_current",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    ActivationRevision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_activation_current", x => x.ApplicationId);
                    table.CheckConstraint("CK_system_application_activation_current_revision", "\"ActivationRevision\" > 0");
                    table.ForeignKey(
                        name: "FK_system_application_activation_current_system_application_activation_revision_ApplicationId_ActivationRevision",
                        columns: x => new { x.ApplicationId, x.ActivationRevision },
                        principalTable: "system_application_activation_revision",
                        principalColumns: new[] { "ApplicationId", "ActivationRevision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_application_activation_document",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    ActivationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    LogicalIdentity = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Trust = table.Column<int>(type: "INTEGER", nullable: false),
                    Precedence = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContentFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Length = table.Column<long>(type: "INTEGER", nullable: false),
                    IsText = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_activation_document", x => new { x.ApplicationId, x.ActivationRevision, x.Ordinal });
                    table.CheckConstraint("CK_system_application_activation_document_hash", "length(\"ContentFingerprint\") = 64 AND \"ContentFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_application_activation_document_values", "\"Ordinal\" >= 0 AND \"Trust\" IN (0, 1) AND \"Length\" >= 0");
                    table.ForeignKey(
                        name: "FK_system_application_activation_document_system_application_activation_revision_ApplicationId_ActivationRevision",
                        columns: x => new { x.ApplicationId, x.ActivationRevision },
                        principalTable: "system_application_activation_revision",
                        principalColumns: new[] { "ApplicationId", "ActivationRevision" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_application_activation_receipt",
                columns: table => new
                {
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    ActivationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_activation_receipt", x => x.OperationId);
                    table.CheckConstraint("CK_system_application_activation_receipt_hash", "length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_application_activation_receipt_outcome", "\"Outcome\" IN ('activated', 'unchanged')");
                    table.ForeignKey(
                        name: "FK_system_application_activation_receipt_operation_OperationId",
                        column: x => x.OperationId,
                        principalTable: "operation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_application_activation_receipt_system_application_activation_revision_ApplicationId_ActivationRevision",
                        columns: x => new { x.ApplicationId, x.ActivationRevision },
                        principalTable: "system_application_activation_revision",
                        principalColumns: new[] { "ApplicationId", "ActivationRevision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_application_activation_source",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    ActivationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RegistrationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DocumentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ProblemCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_activation_source", x => new { x.ApplicationId, x.ActivationRevision, x.Ordinal });
                    table.CheckConstraint("CK_system_application_activation_source_counts", "\"Ordinal\" >= 0 AND \"DocumentCount\" >= 0 AND \"ProblemCount\" >= 0");
                    table.CheckConstraint("CK_system_application_activation_source_hash", "length(\"RegistrationFingerprint\") = 64 AND \"RegistrationFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.ForeignKey(
                        name: "FK_system_application_activation_source_system_application_activation_revision_ApplicationId_ActivationRevision",
                        columns: x => new { x.ApplicationId, x.ActivationRevision },
                        principalTable: "system_application_activation_revision",
                        principalColumns: new[] { "ApplicationId", "ActivationRevision" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_activation_current_ApplicationId_ActivationRevision",
                table: "system_application_activation_current",
                columns: new[] { "ApplicationId", "ActivationRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_activation_document_ApplicationId_ActivationRevision_LogicalIdentity",
                table: "system_application_activation_document",
                columns: new[] { "ApplicationId", "ActivationRevision", "LogicalIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_application_activation_receipt_ApplicationId_ActivationRevision",
                table: "system_application_activation_receipt",
                columns: new[] { "ApplicationId", "ActivationRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_activation_revision_ActivatedByOperationId",
                table: "system_application_activation_revision",
                column: "ActivatedByOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_system_application_activation_revision_ApplicationId_ActivationFingerprint",
                table: "system_application_activation_revision",
                columns: new[] { "ApplicationId", "ActivationFingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_activation_revision_ApplicationId_ApplicationRevision",
                table: "system_application_activation_revision",
                columns: new[] { "ApplicationId", "ApplicationRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_activation_source_ApplicationId_ActivationRevision_SourceId",
                table: "system_application_activation_source",
                columns: new[] { "ApplicationId", "ActivationRevision", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new InvalidOperationException(
                "Application activation history and replay receipts are durable audit evidence. Restore a database backup rather than downgrading this migration.");
    }
}
