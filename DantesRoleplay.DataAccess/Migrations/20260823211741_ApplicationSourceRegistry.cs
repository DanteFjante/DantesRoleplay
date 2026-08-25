using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationSourceRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_application",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application", x => x.Id);
                    table.CheckConstraint("CK_system_application_id", "\"Id\" <> 'system'");
                });

            migrationBuilder.CreateTable(
                name: "system_application_revision",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_revision", x => new { x.ApplicationId, x.Revision });
                    table.CheckConstraint("CK_system_application_revision_fingerprint", "length(\"Fingerprint\") = 64 AND \"Fingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_application_revision_number", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_system_application_revision_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_application_source",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedRootId = table.Column<string>(type: "TEXT", nullable: false),
                    RelativePathOrGlob = table.Column<string>(type: "TEXT", nullable: false),
                    Trust = table.Column<int>(type: "INTEGER", nullable: false),
                    Precedence = table.Column<int>(type: "INTEGER", nullable: false),
                    LogicalIdentity = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_source", x => new { x.ApplicationId, x.SourceId });
                    table.CheckConstraint("CK_system_application_source_trust", "\"Trust\" IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_system_application_source_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_application_revision_base",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    BaseApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_revision_base", x => new { x.ApplicationId, x.Revision, x.Ordinal });
                    table.CheckConstraint("CK_system_application_revision_base_ordinal", "\"Ordinal\" >= 0");
                    table.ForeignKey(
                        name: "FK_system_application_revision_base_system_application_BaseApplicationId",
                        column: x => x.BaseApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_application_revision_base_system_application_revision_ApplicationId_Revision",
                        columns: x => new { x.ApplicationId, x.Revision },
                        principalTable: "system_application_revision",
                        principalColumns: new[] { "ApplicationId", "Revision" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_application_source_scan",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    Generation = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_source_scan", x => new { x.ApplicationId, x.SourceId, x.Generation });
                    table.CheckConstraint("CK_system_application_source_scan_fingerprint", "length(\"ContentFingerprint\") = 64 AND \"ContentFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_application_source_scan_generation", "\"Generation\" > 0");
                    table.CheckConstraint("CK_system_application_source_scan_status", "\"Status\" IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_system_application_source_scan_system_application_source_ApplicationId_SourceId",
                        columns: x => new { x.ApplicationId, x.SourceId },
                        principalTable: "system_application_source",
                        principalColumns: new[] { "ApplicationId", "SourceId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_revision_base_ApplicationId_Revision_BaseApplicationId",
                table: "system_application_revision_base",
                columns: new[] { "ApplicationId", "Revision", "BaseApplicationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_application_revision_base_BaseApplicationId",
                table: "system_application_revision_base",
                column: "BaseApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_system_application_source_ApplicationId_LogicalIdentity_Precedence",
                table: "system_application_source",
                columns: new[] { "ApplicationId", "LogicalIdentity", "Precedence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new InvalidOperationException(
                "Application/source registry rows are immutable evidence. Restore a database backup rather than "
                + "downgrading this migration and deleting registrations.");
    }
}
