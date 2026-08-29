using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TriggerSchedulingSecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trigger_observation_source_current",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation_source_current", x => new { x.ApplicationId, x.Id });
                    table.CheckConstraint("CK_trigger_observation_source_current_version", "\"CurrentVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_trigger_observation_source_current_trigger_observation_source_ApplicationId_Id_CurrentVersion",
                        columns: x => new { x.ApplicationId, x.Id, x.CurrentVersion },
                        principalTable: "trigger_observation_source",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_observation_structure_current",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation_structure_current", x => new { x.ApplicationId, x.Id });
                    table.CheckConstraint("CK_trigger_observation_structure_current_version", "\"CurrentVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_trigger_observation_structure_current_trigger_observation_structure_ApplicationId_Id_CurrentVersion",
                        columns: x => new { x.ApplicationId, x.Id, x.CurrentVersion },
                        principalTable: "trigger_observation_structure",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_one_time_current",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_one_time_current", x => new { x.ApplicationId, x.Id });
                    table.CheckConstraint("CK_trigger_one_time_current_version", "\"CurrentVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_trigger_one_time_current_trigger_one_time_definition_ApplicationId_Id_CurrentVersion",
                        columns: x => new { x.ApplicationId, x.Id, x.CurrentVersion },
                        principalTable: "trigger_one_time_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                INSERT INTO trigger_observation_source_current (ApplicationId, Id, CurrentVersion)
                SELECT ApplicationId, Id, MAX(Version)
                FROM trigger_observation_source
                GROUP BY ApplicationId, Id;

                INSERT INTO trigger_observation_structure_current (ApplicationId, Id, CurrentVersion)
                SELECT ApplicationId, Id, MAX(Version)
                FROM trigger_observation_structure
                GROUP BY ApplicationId, Id;

                INSERT INTO trigger_one_time_current (ApplicationId, Id, CurrentVersion)
                SELECT ApplicationId, Id, MAX(Version)
                FROM trigger_one_time_definition
                GROUP BY ApplicationId, Id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_source_current_ApplicationId_Id_CurrentVersion",
                table: "trigger_observation_source_current",
                columns: new[] { "ApplicationId", "Id", "CurrentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_observation_structure_current_ApplicationId_Id_CurrentVersion",
                table: "trigger_observation_structure_current",
                columns: new[] { "ApplicationId", "Id", "CurrentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_one_time_current_ApplicationId_Id_CurrentVersion",
                table: "trigger_one_time_current",
                columns: new[] { "ApplicationId", "Id", "CurrentVersion" });

            // SQLite cannot add a foreign key with ALTER TABLE. EF's generated rebuild disables
            // foreign keys outside the migration transaction, so perform the child-table rebuild
            // explicitly while foreign-key enforcement remains enabled and recreate every index.
            migrationBuilder.Sql("""
                CREATE TABLE "__trigger_observation_hardened" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_trigger_observation" PRIMARY KEY,
                    "ApplicationId" TEXT NOT NULL,
                    "RequestId" TEXT NOT NULL,
                    "SourceId" TEXT NOT NULL,
                    "SourceVersion" INTEGER NOT NULL,
                    "SourceInstanceId" TEXT NOT NULL,
                    "OccurrenceId" TEXT NOT NULL,
                    "StructureId" TEXT NOT NULL,
                    "StructureVersion" INTEGER NOT NULL,
                    "StructureHash" TEXT NOT NULL,
                    "ObservedAtUtc" TEXT NOT NULL,
                    "ReceivedAtUtc" TEXT NOT NULL,
                    "DataJson" TEXT NOT NULL,
                    "DataHash" TEXT NOT NULL,
                    "RequestFingerprint" TEXT NOT NULL,
                    CONSTRAINT "CK_trigger_observation_hashes" CHECK (length("StructureHash") = 64 AND "StructureHash" NOT GLOB '*[^0-9A-F]*' AND length("DataHash") = 64 AND "DataHash" NOT GLOB '*[^0-9A-F]*' AND length("RequestFingerprint") = 64 AND "RequestFingerprint" NOT GLOB '*[^0-9A-F]*'),
                    CONSTRAINT "CK_trigger_observation_id" CHECK (length("Id") = 44 AND substr("Id", 1, 12) = 'observation.' AND substr("Id", 13) NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_trigger_observation_request" CHECK (length("RequestId") = 52 AND substr("RequestId", 1, 20) = 'observation-request.' AND substr("RequestId", 21) NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_trigger_observation_values" CHECK (length("ApplicationId") BETWEEN 1 AND 63 AND "ApplicationId" <> 'system' AND length("SourceId") BETWEEN 3 AND 200 AND "SourceVersion" > 0 AND length("SourceInstanceId") BETWEEN 1 AND 128 AND length("OccurrenceId") BETWEEN 1 AND 200 AND length("StructureId") BETWEEN 3 AND 200 AND "StructureVersion" > 0 AND length("DataJson") BETWEEN 2 AND 65536 AND json_valid("DataJson") AND json_type("DataJson") = 'object'),
                    CONSTRAINT "FK_trigger_observation_trigger_observation_source_ApplicationId_SourceId_SourceVersion" FOREIGN KEY ("ApplicationId", "SourceId", "SourceVersion") REFERENCES "trigger_observation_source" ("ApplicationId", "Id", "Version") ON DELETE RESTRICT,
                    CONSTRAINT "FK_trigger_observation_trigger_observation_structure_ApplicationId_StructureId_StructureVersion" FOREIGN KEY ("ApplicationId", "StructureId", "StructureVersion") REFERENCES "trigger_observation_structure" ("ApplicationId", "Id", "Version") ON DELETE RESTRICT,
                    CONSTRAINT "FK_trigger_observation_trigger_observation_source_structure_ApplicationId_SourceId_SourceVersion_StructureId_StructureVersion" FOREIGN KEY ("ApplicationId", "SourceId", "SourceVersion", "StructureId", "StructureVersion") REFERENCES "trigger_observation_source_structure" ("ApplicationId", "SourceId", "SourceVersion", "StructureId", "StructureVersion") ON DELETE RESTRICT
                );
                INSERT INTO "__trigger_observation_hardened" ("Id", "ApplicationId", "RequestId", "SourceId", "SourceVersion", "SourceInstanceId", "OccurrenceId", "StructureId", "StructureVersion", "StructureHash", "ObservedAtUtc", "ReceivedAtUtc", "DataJson", "DataHash", "RequestFingerprint")
                SELECT "Id", "ApplicationId", "RequestId", "SourceId", "SourceVersion", "SourceInstanceId", "OccurrenceId", "StructureId", "StructureVersion", "StructureHash", "ObservedAtUtc", "ReceivedAtUtc", "DataJson", "DataHash", "RequestFingerprint"
                FROM "trigger_observation";
                DROP TABLE "trigger_observation";
                ALTER TABLE "__trigger_observation_hardened" RENAME TO "trigger_observation";
                CREATE INDEX "IX_trigger_observation_ApplicationId_ReceivedAtUtc_Id" ON "trigger_observation" ("ApplicationId", "ReceivedAtUtc", "Id");
                CREATE UNIQUE INDEX "IX_trigger_observation_ApplicationId_RequestId" ON "trigger_observation" ("ApplicationId", "RequestId");
                CREATE UNIQUE INDEX "IX_trigger_observation_ApplicationId_SourceId_SourceVersion_SourceInstanceId_OccurrenceId" ON "trigger_observation" ("ApplicationId", "SourceId", "SourceVersion", "SourceInstanceId", "OccurrenceId");
                CREATE INDEX "IX_trigger_observation_ApplicationId_StructureId_StructureVersion" ON "trigger_observation" ("ApplicationId", "StructureId", "StructureVersion");
                CREATE INDEX "IX_trigger_observation_ApplicationId_SourceId_SourceVersion_StructureId_StructureVersion" ON "trigger_observation" ("ApplicationId", "SourceId", "SourceVersion", "StructureId", "StructureVersion");
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trigger_observation_structure_immutable_update
                BEFORE UPDATE ON trigger_observation_structure
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_structure_immutable_delete
                BEFORE DELETE ON trigger_observation_structure
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;

                CREATE TRIGGER trigger_observation_source_immutable_update
                BEFORE UPDATE ON trigger_observation_source
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_source_immutable_delete
                BEFORE DELETE ON trigger_observation_source
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;

                CREATE TRIGGER trigger_observation_source_structure_immutable_update
                BEFORE UPDATE ON trigger_observation_source_structure
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_source_structure_immutable_delete
                BEFORE DELETE ON trigger_observation_source_structure
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;

                CREATE TRIGGER trigger_one_time_definition_immutable_update
                BEFORE UPDATE ON trigger_one_time_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_one_time_definition_immutable_delete
                BEFORE DELETE ON trigger_one_time_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;

                CREATE TRIGGER trigger_fire_receipt_immutable_update
                BEFORE UPDATE ON trigger_fire_receipt
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_fire_receipt_immutable_delete
                BEFORE DELETE ON trigger_fire_receipt
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_observation_structure_immutable_update;
                DROP TRIGGER IF EXISTS trigger_observation_structure_immutable_delete;
                DROP TRIGGER IF EXISTS trigger_observation_source_immutable_update;
                DROP TRIGGER IF EXISTS trigger_observation_source_immutable_delete;
                DROP TRIGGER IF EXISTS trigger_observation_source_structure_immutable_update;
                DROP TRIGGER IF EXISTS trigger_observation_source_structure_immutable_delete;
                DROP TRIGGER IF EXISTS trigger_one_time_definition_immutable_update;
                DROP TRIGGER IF EXISTS trigger_one_time_definition_immutable_delete;
                DROP TRIGGER IF EXISTS trigger_fire_receipt_immutable_update;
                DROP TRIGGER IF EXISTS trigger_fire_receipt_immutable_delete;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE "__trigger_observation_unhardened" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_trigger_observation" PRIMARY KEY,
                    "ApplicationId" TEXT NOT NULL,
                    "RequestId" TEXT NOT NULL,
                    "SourceId" TEXT NOT NULL,
                    "SourceVersion" INTEGER NOT NULL,
                    "SourceInstanceId" TEXT NOT NULL,
                    "OccurrenceId" TEXT NOT NULL,
                    "StructureId" TEXT NOT NULL,
                    "StructureVersion" INTEGER NOT NULL,
                    "StructureHash" TEXT NOT NULL,
                    "ObservedAtUtc" TEXT NOT NULL,
                    "ReceivedAtUtc" TEXT NOT NULL,
                    "DataJson" TEXT NOT NULL,
                    "DataHash" TEXT NOT NULL,
                    "RequestFingerprint" TEXT NOT NULL,
                    CONSTRAINT "CK_trigger_observation_hashes" CHECK (length("StructureHash") = 64 AND "StructureHash" NOT GLOB '*[^0-9A-F]*' AND length("DataHash") = 64 AND "DataHash" NOT GLOB '*[^0-9A-F]*' AND length("RequestFingerprint") = 64 AND "RequestFingerprint" NOT GLOB '*[^0-9A-F]*'),
                    CONSTRAINT "CK_trigger_observation_id" CHECK (length("Id") = 44 AND substr("Id", 1, 12) = 'observation.' AND substr("Id", 13) NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_trigger_observation_request" CHECK (length("RequestId") = 52 AND substr("RequestId", 1, 20) = 'observation-request.' AND substr("RequestId", 21) NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_trigger_observation_values" CHECK (length("ApplicationId") BETWEEN 1 AND 63 AND "ApplicationId" <> 'system' AND length("SourceId") BETWEEN 3 AND 200 AND "SourceVersion" > 0 AND length("SourceInstanceId") BETWEEN 1 AND 128 AND length("OccurrenceId") BETWEEN 1 AND 200 AND length("StructureId") BETWEEN 3 AND 200 AND "StructureVersion" > 0 AND length("DataJson") BETWEEN 2 AND 65536 AND json_valid("DataJson") AND json_type("DataJson") = 'object'),
                    CONSTRAINT "FK_trigger_observation_trigger_observation_source_ApplicationId_SourceId_SourceVersion" FOREIGN KEY ("ApplicationId", "SourceId", "SourceVersion") REFERENCES "trigger_observation_source" ("ApplicationId", "Id", "Version") ON DELETE RESTRICT,
                    CONSTRAINT "FK_trigger_observation_trigger_observation_structure_ApplicationId_StructureId_StructureVersion" FOREIGN KEY ("ApplicationId", "StructureId", "StructureVersion") REFERENCES "trigger_observation_structure" ("ApplicationId", "Id", "Version") ON DELETE RESTRICT
                );
                INSERT INTO "__trigger_observation_unhardened" ("Id", "ApplicationId", "RequestId", "SourceId", "SourceVersion", "SourceInstanceId", "OccurrenceId", "StructureId", "StructureVersion", "StructureHash", "ObservedAtUtc", "ReceivedAtUtc", "DataJson", "DataHash", "RequestFingerprint")
                SELECT "Id", "ApplicationId", "RequestId", "SourceId", "SourceVersion", "SourceInstanceId", "OccurrenceId", "StructureId", "StructureVersion", "StructureHash", "ObservedAtUtc", "ReceivedAtUtc", "DataJson", "DataHash", "RequestFingerprint"
                FROM "trigger_observation";
                DROP TABLE "trigger_observation";
                ALTER TABLE "__trigger_observation_unhardened" RENAME TO "trigger_observation";
                CREATE INDEX "IX_trigger_observation_ApplicationId_ReceivedAtUtc_Id" ON "trigger_observation" ("ApplicationId", "ReceivedAtUtc", "Id");
                CREATE UNIQUE INDEX "IX_trigger_observation_ApplicationId_RequestId" ON "trigger_observation" ("ApplicationId", "RequestId");
                CREATE UNIQUE INDEX "IX_trigger_observation_ApplicationId_SourceId_SourceVersion_SourceInstanceId_OccurrenceId" ON "trigger_observation" ("ApplicationId", "SourceId", "SourceVersion", "SourceInstanceId", "OccurrenceId");
                CREATE INDEX "IX_trigger_observation_ApplicationId_StructureId_StructureVersion" ON "trigger_observation" ("ApplicationId", "StructureId", "StructureVersion");
                """);

            migrationBuilder.DropTable(
                name: "trigger_observation_source_current");

            migrationBuilder.DropTable(
                name: "trigger_observation_structure_current");

            migrationBuilder.DropTable(
                name: "trigger_one_time_current");

        }
    }
}
