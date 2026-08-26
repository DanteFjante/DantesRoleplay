using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TriggerObservationIngestionSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trigger_observation_source_principal",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    PrincipalId = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation_source_principal", x => new { x.ApplicationId, x.SourceId, x.SourceVersion, x.PrincipalId });
                    table.CheckConstraint("CK_trigger_observation_source_principal_id", "length(\"PrincipalId\") = 74 AND substr(\"PrincipalId\", 1, 10) = 'principal.' AND substr(\"PrincipalId\", 11) NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_trigger_observation_source_principal_trigger_observation_source_ApplicationId_SourceId_SourceVersion",
                        columns: x => new { x.ApplicationId, x.SourceId, x.SourceVersion },
                        principalTable: "trigger_observation_source",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            // Keep foreign keys enabled and the migration transactional. Provider-generated
            // AddCheckConstraint/AddForeignKey operations rebuild this table via a non-atomic
            // PRAGMA foreign_keys toggle.
            migrationBuilder.Sql("""
                CREATE TABLE "__trigger_observation_ingress" (
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
                    "PrincipalId" TEXT NULL,
                    CONSTRAINT "CK_trigger_observation_hashes" CHECK (length("StructureHash") = 64 AND "StructureHash" NOT GLOB '*[^0-9A-F]*' AND length("DataHash") = 64 AND "DataHash" NOT GLOB '*[^0-9A-F]*' AND length("RequestFingerprint") = 64 AND "RequestFingerprint" NOT GLOB '*[^0-9A-F]*'),
                    CONSTRAINT "CK_trigger_observation_id" CHECK (length("Id") = 44 AND substr("Id", 1, 12) = 'observation.' AND substr("Id", 13) NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_trigger_observation_principal" CHECK ("PrincipalId" IS NULL OR (length("PrincipalId") = 74 AND substr("PrincipalId", 1, 10) = 'principal.' AND substr("PrincipalId", 11) NOT GLOB '*[^0-9a-f]*')),
                    CONSTRAINT "CK_trigger_observation_request" CHECK (length("RequestId") = 52 AND substr("RequestId", 1, 20) = 'observation-request.' AND substr("RequestId", 21) NOT GLOB '*[^0-9a-f]*'),
                    CONSTRAINT "CK_trigger_observation_values" CHECK (length("ApplicationId") BETWEEN 1 AND 63 AND "ApplicationId" <> 'system' AND length("SourceId") BETWEEN 3 AND 200 AND "SourceVersion" > 0 AND length("SourceInstanceId") BETWEEN 1 AND 128 AND length("OccurrenceId") BETWEEN 1 AND 200 AND length("StructureId") BETWEEN 3 AND 200 AND "StructureVersion" > 0 AND length("DataJson") BETWEEN 2 AND 65536 AND json_valid("DataJson") AND json_type("DataJson") = 'object'),
                    CONSTRAINT "FK_trigger_observation_trigger_observation_source_ApplicationId_SourceId_SourceVersion" FOREIGN KEY ("ApplicationId", "SourceId", "SourceVersion") REFERENCES "trigger_observation_source" ("ApplicationId", "Id", "Version") ON DELETE RESTRICT,
                    CONSTRAINT "FK_trigger_observation_trigger_observation_structure_ApplicationId_StructureId_StructureVersion" FOREIGN KEY ("ApplicationId", "StructureId", "StructureVersion") REFERENCES "trigger_observation_structure" ("ApplicationId", "Id", "Version") ON DELETE RESTRICT,
                    CONSTRAINT "FK_trigger_observation_trigger_observation_source_structure_ApplicationId_SourceId_SourceVersion_StructureId_StructureVersion" FOREIGN KEY ("ApplicationId", "SourceId", "SourceVersion", "StructureId", "StructureVersion") REFERENCES "trigger_observation_source_structure" ("ApplicationId", "SourceId", "SourceVersion", "StructureId", "StructureVersion") ON DELETE RESTRICT,
                    CONSTRAINT "FK_trigger_observation_trigger_observation_source_principal_ApplicationId_SourceId_SourceVersion_PrincipalId" FOREIGN KEY ("ApplicationId", "SourceId", "SourceVersion", "PrincipalId") REFERENCES "trigger_observation_source_principal" ("ApplicationId", "SourceId", "SourceVersion", "PrincipalId") ON DELETE RESTRICT
                );
                INSERT INTO "__trigger_observation_ingress" ("Id", "ApplicationId", "RequestId", "SourceId", "SourceVersion", "SourceInstanceId", "OccurrenceId", "StructureId", "StructureVersion", "StructureHash", "ObservedAtUtc", "ReceivedAtUtc", "DataJson", "DataHash", "RequestFingerprint", "PrincipalId")
                SELECT "Id", "ApplicationId", "RequestId", "SourceId", "SourceVersion", "SourceInstanceId", "OccurrenceId", "StructureId", "StructureVersion", "StructureHash", "ObservedAtUtc", "ReceivedAtUtc", "DataJson", "DataHash", "RequestFingerprint", NULL
                FROM "trigger_observation";
                DROP TABLE "trigger_observation";
                ALTER TABLE "__trigger_observation_ingress" RENAME TO "trigger_observation";
                CREATE INDEX "IX_trigger_observation_ApplicationId_ReceivedAtUtc_Id" ON "trigger_observation" ("ApplicationId", "ReceivedAtUtc", "Id");
                CREATE UNIQUE INDEX "IX_trigger_observation_ApplicationId_RequestId" ON "trigger_observation" ("ApplicationId", "RequestId");
                CREATE UNIQUE INDEX "IX_trigger_observation_ApplicationId_SourceId_SourceVersion_SourceInstanceId_OccurrenceId" ON "trigger_observation" ("ApplicationId", "SourceId", "SourceVersion", "SourceInstanceId", "OccurrenceId");
                CREATE INDEX "IX_trigger_observation_ApplicationId_StructureId_StructureVersion" ON "trigger_observation" ("ApplicationId", "StructureId", "StructureVersion");
                CREATE INDEX "IX_trigger_observation_ApplicationId_SourceId_SourceVersion_StructureId_StructureVersion" ON "trigger_observation" ("ApplicationId", "SourceId", "SourceVersion", "StructureId", "StructureVersion");
                CREATE INDEX "IX_trigger_observation_ApplicationId_SourceId_SourceVersion_PrincipalId" ON "trigger_observation" ("ApplicationId", "SourceId", "SourceVersion", "PrincipalId");

                CREATE TRIGGER trigger_observation_immutable_update
                BEFORE UPDATE ON trigger_observation
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_immutable_delete
                BEFORE DELETE ON trigger_observation
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_principal_required_insert
                BEFORE INSERT ON trigger_observation WHEN NEW.PrincipalId IS NULL
                BEGIN SELECT RAISE(ABORT, 'OBSERVATION_PRINCIPAL_REQUIRED'); END;
                CREATE TRIGGER trigger_observation_source_principal_immutable_update
                BEFORE UPDATE ON trigger_observation_source_principal
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_source_principal_immutable_delete
                BEFORE DELETE ON trigger_observation_source_principal
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_observation_immutable_update;
                DROP TRIGGER IF EXISTS trigger_observation_immutable_delete;
                DROP TRIGGER IF EXISTS trigger_observation_principal_required_insert;
                DROP TRIGGER IF EXISTS trigger_observation_source_principal_immutable_update;
                DROP TRIGGER IF EXISTS trigger_observation_source_principal_immutable_delete;

                CREATE TABLE "__trigger_observation_before_ingress" (
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
                INSERT INTO "__trigger_observation_before_ingress" ("Id", "ApplicationId", "RequestId", "SourceId", "SourceVersion", "SourceInstanceId", "OccurrenceId", "StructureId", "StructureVersion", "StructureHash", "ObservedAtUtc", "ReceivedAtUtc", "DataJson", "DataHash", "RequestFingerprint")
                SELECT "Id", "ApplicationId", "RequestId", "SourceId", "SourceVersion", "SourceInstanceId", "OccurrenceId", "StructureId", "StructureVersion", "StructureHash", "ObservedAtUtc", "ReceivedAtUtc", "DataJson", "DataHash", "RequestFingerprint"
                FROM "trigger_observation";
                DROP TABLE "trigger_observation";
                ALTER TABLE "__trigger_observation_before_ingress" RENAME TO "trigger_observation";
                CREATE INDEX "IX_trigger_observation_ApplicationId_ReceivedAtUtc_Id" ON "trigger_observation" ("ApplicationId", "ReceivedAtUtc", "Id");
                CREATE UNIQUE INDEX "IX_trigger_observation_ApplicationId_RequestId" ON "trigger_observation" ("ApplicationId", "RequestId");
                CREATE UNIQUE INDEX "IX_trigger_observation_ApplicationId_SourceId_SourceVersion_SourceInstanceId_OccurrenceId" ON "trigger_observation" ("ApplicationId", "SourceId", "SourceVersion", "SourceInstanceId", "OccurrenceId");
                CREATE INDEX "IX_trigger_observation_ApplicationId_StructureId_StructureVersion" ON "trigger_observation" ("ApplicationId", "StructureId", "StructureVersion");
                CREATE INDEX "IX_trigger_observation_ApplicationId_SourceId_SourceVersion_StructureId_StructureVersion" ON "trigger_observation" ("ApplicationId", "SourceId", "SourceVersion", "StructureId", "StructureVersion");
                CREATE TRIGGER trigger_observation_immutable_update
                BEFORE UPDATE ON trigger_observation
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_immutable_delete
                BEFORE DELETE ON trigger_observation
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                """);

            migrationBuilder.DropTable(
                name: "trigger_observation_source_principal");

        }
    }
}
