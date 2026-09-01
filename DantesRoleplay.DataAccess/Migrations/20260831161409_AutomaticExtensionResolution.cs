using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticExtensionResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            const string defaultFingerprint =
                "0000000000000000000000000000000000000000000000000000000000000000";
            const string fingerprintCheck =
                "CHECK (length(\"ResolutionFingerprint\") = 64 AND \"ResolutionFingerprint\" NOT GLOB '*[^0-9A-F]*')";
            migrationBuilder.Sql($"ALTER TABLE \"system_state_space_binding_revision\" ADD \"ResolutionFingerprint\" TEXT NOT NULL DEFAULT '{defaultFingerprint}' {fingerprintCheck};");
            migrationBuilder.Sql($"ALTER TABLE \"system_state_space\" ADD \"ResolutionFingerprint\" TEXT NOT NULL DEFAULT '{defaultFingerprint}' {fingerprintCheck};");
            migrationBuilder.Sql($"ALTER TABLE \"system_application_activation_revision\" ADD \"ResolutionFingerprint\" TEXT NOT NULL DEFAULT '{defaultFingerprint}' {fingerprintCheck};");
            migrationBuilder.Sql($"ALTER TABLE \"interaction_recipe_revision\" ADD \"ResolutionFingerprint\" TEXT NOT NULL DEFAULT '{defaultFingerprint}' {fingerprintCheck};");

            migrationBuilder.Sql("UPDATE system_application_activation_revision SET ResolutionFingerprint = ActivationFingerprint;");
            migrationBuilder.Sql("UPDATE system_state_space SET ResolutionFingerprint = ManifestFingerprint;");
            migrationBuilder.Sql("UPDATE system_state_space_binding_revision SET ResolutionFingerprint = ActiveFingerprint;");
            migrationBuilder.Sql("UPDATE interaction_recipe_revision SET ResolutionFingerprint = EffectiveSetFingerprint;");

            migrationBuilder.CreateTable(
                name: "system_application_activation_extension",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    ActivationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    ExtensionId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    RegistrationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    NamespaceIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    HigherPriorityThanJson = table.Column<string>(type: "TEXT", nullable: false),
                    OverridesBase = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_activation_extension", x => new { x.ApplicationId, x.ActivationRevision, x.Ordinal });
                    table.CheckConstraint("CK_system_application_activation_extension_hash", "length(\"RegistrationFingerprint\") = 64 AND \"RegistrationFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_application_activation_extension_values", "\"Ordinal\" >= 0 AND length(\"SourceIdsJson\") >= 2 AND json_valid(\"SourceIdsJson\") AND length(\"NamespaceIdsJson\") >= 2 AND json_valid(\"NamespaceIdsJson\") AND length(\"HigherPriorityThanJson\") >= 2 AND json_valid(\"HigherPriorityThanJson\")");
                    table.ForeignKey(
                        name: "FK_system_application_activation_extension_system_application_activation_revision_ApplicationId_ActivationRevision",
                        columns: x => new { x.ApplicationId, x.ActivationRevision },
                        principalTable: "system_application_activation_revision",
                        principalColumns: new[] { "ApplicationId", "ActivationRevision" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_application_extension",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    ExtensionId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SourceIdsJson = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    NamespaceIdsJson = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    DependenciesJson = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                    ConflictsWithJson = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                    HigherPriorityThanJson = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                    OverridesBase = table.Column<bool>(type: "INTEGER", nullable: false),
                    RegistrationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_extension", x => new { x.ApplicationId, x.ExtensionId });
                    table.CheckConstraint("CK_system_application_extension_fingerprint", "length(\"RegistrationFingerprint\") = 64 AND \"RegistrationFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_application_extension_id", "length(\"ExtensionId\") BETWEEN 1 AND 63 AND \"ExtensionId\" <> 'base'");
                    table.CheckConstraint("CK_system_application_extension_json", "json_valid(\"SourceIdsJson\") AND json_valid(\"NamespaceIdsJson\") AND json_valid(\"DependenciesJson\") AND json_valid(\"ConflictsWithJson\") AND json_valid(\"HigherPriorityThanJson\")");
                    table.ForeignKey(
                        name: "FK_system_application_extension_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_activation_extension_ApplicationId_ActivationRevision_ExtensionId",
                table: "system_application_activation_extension",
                columns: new[] { "ApplicationId", "ActivationRevision", "ExtensionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new InvalidOperationException(
                "Automatic extension resolution cannot be downgraded safely because doing so would discard " +
                "registered extension policy and the resolution identity bound into live state and learned work.");
    }
}
