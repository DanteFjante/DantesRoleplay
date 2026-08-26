using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TriggerSchedulingPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trigger_observation_source",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ReplayWindowSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestsPerMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation_source", x => new { x.ApplicationId, x.Id, x.Version });
                    table.CheckConstraint("CK_trigger_observation_source_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"Id\") BETWEEN 3 AND 200 AND \"Version\" > 0 AND \"Status\" IN ('enabled', 'disabled') AND \"ReplayWindowSeconds\" BETWEEN 1 AND 604800 AND \"RequestsPerMinute\" BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "FK_trigger_observation_source_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_observation_structure",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    SchemaProfileId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedSchema = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    SchemaHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation_structure", x => new { x.ApplicationId, x.Id, x.Version });
                    table.CheckConstraint("CK_trigger_observation_structure_hash", "length(\"SchemaHash\") = 64 AND \"SchemaHash\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_trigger_observation_structure_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"Id\") BETWEEN 3 AND 200 AND \"Version\" > 0 AND \"Status\" IN ('active', 'retired') AND length(\"SchemaProfileId\") BETWEEN 1 AND 200 AND length(\"NormalizedSchema\") BETWEEN 2 AND 65536 AND json_valid(\"NormalizedSchema\") AND json_type(\"NormalizedSchema\") = 'object' AND length(\"Description\") BETWEEN 1 AND 1024");
                    table.ForeignKey(
                        name: "FK_trigger_observation_structure_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_one_time_definition",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MisfirePolicy = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_one_time_definition", x => new { x.ApplicationId, x.Id, x.Version });
                    table.CheckConstraint("CK_trigger_one_time_definition_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"Id\") BETWEEN 3 AND 200 AND \"Version\" > 0 AND \"MisfirePolicy\" IN ('skip', 'fire-once') AND \"Target\" = 'notification-only'");
                    table.ForeignKey(
                        name: "FK_trigger_one_time_definition_system_application_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "system_application",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_observation",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 44, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    RequestId = table.Column<string>(type: "TEXT", maxLength: 52, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceInstanceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OccurrenceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StructureId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StructureVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    StructureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    DataHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation", x => x.Id);
                    table.CheckConstraint("CK_trigger_observation_hashes", "length(\"StructureHash\") = 64 AND \"StructureHash\" NOT GLOB '*[^0-9A-F]*' AND length(\"DataHash\") = 64 AND \"DataHash\" NOT GLOB '*[^0-9A-F]*' AND length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_trigger_observation_id", "length(\"Id\") = 44 AND substr(\"Id\", 1, 12) = 'observation.' AND substr(\"Id\", 13) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_observation_request", "length(\"RequestId\") = 52 AND substr(\"RequestId\", 1, 20) = 'observation-request.' AND substr(\"RequestId\", 21) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_observation_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"SourceId\") BETWEEN 3 AND 200 AND \"SourceVersion\" > 0 AND length(\"SourceInstanceId\") BETWEEN 1 AND 128 AND length(\"OccurrenceId\") BETWEEN 1 AND 200 AND length(\"StructureId\") BETWEEN 3 AND 200 AND \"StructureVersion\" > 0 AND length(\"DataJson\") BETWEEN 2 AND 65536 AND json_valid(\"DataJson\") AND json_type(\"DataJson\") = 'object'");
                    table.ForeignKey(
                        name: "FK_trigger_observation_trigger_observation_source_ApplicationId_SourceId_SourceVersion",
                        columns: x => new { x.ApplicationId, x.SourceId, x.SourceVersion },
                        principalTable: "trigger_observation_source",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_observation_trigger_observation_structure_ApplicationId_StructureId_StructureVersion",
                        columns: x => new { x.ApplicationId, x.StructureId, x.StructureVersion },
                        principalTable: "trigger_observation_structure",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_observation_source_structure",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    StructureId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StructureVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation_source_structure", x => new { x.ApplicationId, x.SourceId, x.SourceVersion, x.StructureId, x.StructureVersion });
                    table.CheckConstraint("CK_trigger_observation_source_structure_ids", "length(\"SourceId\") BETWEEN 3 AND 200 AND length(\"StructureId\") BETWEEN 3 AND 200");
                    table.CheckConstraint("CK_trigger_observation_source_structure_versions", "\"SourceVersion\" > 0 AND \"StructureVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_trigger_observation_source_structure_trigger_observation_source_ApplicationId_SourceId_SourceVersion",
                        columns: x => new { x.ApplicationId, x.SourceId, x.SourceVersion },
                        principalTable: "trigger_observation_source",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_observation_source_structure_trigger_observation_structure_ApplicationId_StructureId_StructureVersion",
                        columns: x => new { x.ApplicationId, x.StructureId, x.StructureVersion },
                        principalTable: "trigger_observation_structure",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_fire_receipt",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurrenceAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Disposition = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_fire_receipt", x => x.Id);
                    table.CheckConstraint("CK_trigger_fire_receipt_id", "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'trigger-fire.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_fire_receipt_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Disposition\" IN ('due', 'missed')");
                    table.ForeignKey(
                        name: "FK_trigger_fire_receipt_trigger_one_time_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_one_time_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_fire_receipt_ApplicationId_TriggerId_TriggerVersion_OccurrenceAtUtc",
                table: "trigger_fire_receipt",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "OccurrenceAtUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_ApplicationId_ReceivedAtUtc_Id",
                table: "trigger_observation",
                columns: new[] { "ApplicationId", "ReceivedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_ApplicationId_RequestId",
                table: "trigger_observation",
                columns: new[] { "ApplicationId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_ApplicationId_SourceId_SourceVersion_SourceInstanceId_OccurrenceId",
                table: "trigger_observation",
                columns: new[] { "ApplicationId", "SourceId", "SourceVersion", "SourceInstanceId", "OccurrenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_ApplicationId_StructureId_StructureVersion",
                table: "trigger_observation",
                columns: new[] { "ApplicationId", "StructureId", "StructureVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_source_structure_ApplicationId_StructureId_StructureVersion",
                table: "trigger_observation_source_structure",
                columns: new[] { "ApplicationId", "StructureId", "StructureVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trigger_fire_receipt");

            migrationBuilder.DropTable(
                name: "trigger_observation");

            migrationBuilder.DropTable(
                name: "trigger_observation_source_structure");

            migrationBuilder.DropTable(
                name: "trigger_one_time_definition");

            migrationBuilder.DropTable(
                name: "trigger_observation_source");

            migrationBuilder.DropTable(
                name: "trigger_observation_structure");
        }
    }
}
