using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class StateSpaceBindingHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BindingRevision",
                table: "system_state_space",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "system_state_space",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "system_state_space_binding_revision",
                columns: table => new
                {
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BindingRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    ApplicationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActiveFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BindingFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreviousBindingFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CompatibilityCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    EntityCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ComponentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DependencyCoverageVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DependencyCoverageComplete = table.Column<bool>(type: "INTEGER", nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_state_space_binding_revision", x => new { x.StateSpaceId, x.BindingRevision });
                    table.CheckConstraint("CK_system_state_space_binding_active_fingerprint", "length(\"ActiveFingerprint\") = 64 AND \"ActiveFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_state_space_binding_application_fingerprint", "length(\"ApplicationFingerprint\") = 64 AND \"ApplicationFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_state_space_binding_application_revision", "\"ApplicationRevision\" > 0");
                    table.CheckConstraint("CK_system_state_space_binding_counts", "\"EntityCount\" >= 0 AND \"ComponentCount\" >= 0");
                    table.CheckConstraint("CK_system_state_space_binding_fingerprint", "length(\"BindingFingerprint\") = 64 AND \"BindingFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_state_space_binding_previous", "\"PreviousBindingFingerprint\" IS NULL OR (length(\"PreviousBindingFingerprint\") = 64 AND \"PreviousBindingFingerprint\" NOT GLOB '*[^0-9A-F]*')");
                    table.CheckConstraint("CK_system_state_space_binding_revision", "\"BindingRevision\" > 0");
                    table.ForeignKey(
                        name: "FK_system_state_space_binding_revision_operation_OperationId",
                        column: x => x.OperationId,
                        principalTable: "operation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_state_space_binding_revision_system_application_revision_ApplicationId_ApplicationRevision",
                        columns: x => new { x.ApplicationId, x.ApplicationRevision },
                        principalTable: "system_application_revision",
                        principalColumns: new[] { "ApplicationId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_state_space_binding_revision_system_state_space_StateSpaceId",
                        column: x => x.StateSpaceId,
                        principalTable: "system_state_space",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_state_space_binding_revision_ApplicationId_ApplicationRevision",
                table: "system_state_space_binding_revision",
                columns: new[] { "ApplicationId", "ApplicationRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_system_state_space_binding_revision_OperationId",
                table: "system_state_space_binding_revision",
                column: "OperationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new InvalidOperationException(
                "State-space binding history is durable compatibility and audit evidence and cannot be removed by downgrade.");
        }
    }
}
