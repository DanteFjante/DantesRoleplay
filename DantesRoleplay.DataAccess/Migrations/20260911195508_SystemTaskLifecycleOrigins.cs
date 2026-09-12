using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class SystemTaskLifecycleOrigins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These are native SQLite column additions. The remaining changes affect only the
            // declared schema (nullability, checks, and foreign keys), so changing sqlite_schema
            // avoids EF's non-transactional table rebuild and preserves every referencing table.
            migrationBuilder.Sql("""
                ALTER TABLE "system_task_lifecycle" ADD COLUMN "activation_application_fingerprint" TEXT NULL;
                ALTER TABLE "system_task_lifecycle" ADD COLUMN "activation_application_revision" INTEGER NULL;
                ALTER TABLE "system_task_lifecycle" ADD COLUMN "activation_fingerprint" TEXT NULL;
                ALTER TABLE "system_task_lifecycle" ADD COLUMN "activation_revision" INTEGER NULL;
                ALTER TABLE "system_task_lifecycle" ADD COLUMN "admission_payload_json" TEXT NULL;
                ALTER TABLE "system_task_lifecycle" ADD COLUMN "candidate_fingerprint" TEXT NULL;
                ALTER TABLE "system_task_lifecycle" ADD COLUMN "candidate_id" TEXT NULL;
                ALTER TABLE "system_task_lifecycle" ADD COLUMN "candidate_revision" INTEGER NULL;
                ALTER TABLE "system_task_lifecycle" ADD COLUMN "causation_operation_id" TEXT NULL;
                ALTER TABLE "system_task_lifecycle" ADD COLUMN "purpose" TEXT NOT NULL DEFAULT 'procedure-workflow';
                ALTER TABLE "system_task_ai_ceiling" ADD COLUMN "task_purpose" TEXT NOT NULL DEFAULT 'procedure-workflow';

                CREATE UNIQUE INDEX "AK_system_task_lifecycle_task_id_purpose"
                    ON "system_task_lifecycle" ("task_id", "purpose");

                PRAGMA writable_schema = ON;

                UPDATE sqlite_schema SET sql = replace(sql,
                    '"state_space_id" TEXT NOT NULL', '"state_space_id" TEXT NULL')
                    WHERE type = 'table' AND name = 'system_task_lifecycle';
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"state_revision" TEXT NOT NULL', '"state_revision" TEXT NULL')
                    WHERE type = 'table' AND name = 'system_task_lifecycle';
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"definition_id" TEXT NOT NULL', '"definition_id" TEXT NULL')
                    WHERE type = 'table' AND name = 'system_task_lifecycle';
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"definition_version" INTEGER NOT NULL', '"definition_version" INTEGER NULL')
                    WHERE type = 'table' AND name = 'system_task_lifecycle';
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"definition_fingerprint" TEXT NOT NULL', '"definition_fingerprint" TEXT NULL')
                    WHERE type = 'table' AND name = 'system_task_lifecycle';
                UPDATE sqlite_schema SET sql = substr(sql, 1, length(sql) - 1) || ', /* lifecycle-origins-v1 */
                    CONSTRAINT "CK_system_task_lifecycle_activation_origin" CHECK ((("activation_revision" IS NULL AND "activation_fingerprint" IS NULL AND "activation_application_revision" IS NULL AND "activation_application_fingerprint" IS NULL) OR ("activation_revision" IS NOT NULL AND "activation_revision" > 0 AND "activation_fingerprint" IS NOT NULL AND length("activation_fingerprint") = 64 AND "activation_application_revision" IS NOT NULL AND "activation_application_revision" > 0 AND "activation_application_fingerprint" IS NOT NULL AND length("activation_application_fingerprint") = 64))),
                    CONSTRAINT "CK_system_task_lifecycle_admission_payload" CHECK (("admission_payload_json" IS NULL OR (json_valid("admission_payload_json") = 1 AND json_type("admission_payload_json") = ''object'' AND length(CAST("admission_payload_json" AS BLOB)) <= 65536))),
                    CONSTRAINT "CK_system_task_lifecycle_admission_payload_shape" CHECK ((("purpose" = ''procedure-workflow'' AND (("activation_revision" IS NULL AND "admission_payload_json" IS NULL) OR ("activation_revision" IS NOT NULL AND "admission_payload_json" IS NOT NULL))) OR ("purpose" = ''application-validation'' AND "activation_revision" IS NULL AND "admission_payload_json" IS NOT NULL))),
                    CONSTRAINT "CK_system_task_lifecycle_purpose" CHECK ("purpose" IN (''procedure-workflow'',''application-validation'')),
                    CONSTRAINT "CK_system_task_lifecycle_purpose_shape" CHECK ((("purpose" = ''procedure-workflow'' AND "state_space_id" IS NOT NULL AND length(trim("state_space_id")) BETWEEN 1 AND 200 AND "state_revision" IS NOT NULL AND length(trim("state_revision")) BETWEEN 1 AND 200 AND "definition_id" IS NOT NULL AND length("definition_id") BETWEEN 1 AND 200 AND "definition_version" IS NOT NULL AND "definition_version" > 0 AND "definition_fingerprint" IS NOT NULL AND length("definition_fingerprint") = 64 AND "definition_fingerprint" NOT GLOB ''*[^0-9A-F]*'' AND "candidate_id" IS NULL AND "candidate_revision" IS NULL AND "candidate_fingerprint" IS NULL AND "causation_operation_id" IS NULL) OR ("purpose" = ''application-validation'' AND "state_space_id" IS NULL AND "state_revision" IS NULL AND "definition_id" IS NULL AND "definition_version" IS NULL AND "definition_fingerprint" IS NULL AND "activation_revision" IS NULL AND "activation_fingerprint" IS NULL AND "activation_application_revision" IS NULL AND "activation_application_fingerprint" IS NULL AND "candidate_id" IS NOT NULL AND length("candidate_id") = 32 AND "candidate_id" NOT GLOB ''*[^0-9a-f]*'' AND "candidate_revision" IS NOT NULL AND "candidate_revision" > 0 AND "candidate_fingerprint" IS NOT NULL AND length("candidate_fingerprint") = 64 AND "candidate_fingerprint" NOT GLOB ''*[^0-9A-F]*'' AND ("causation_operation_id" IS NULL OR (length("causation_operation_id") = 32 AND "causation_operation_id" NOT GLOB ''*[^0-9a-f]*''))))),
                    CONSTRAINT "FK_system_task_lifecycle_operation_causation_operation_id" FOREIGN KEY ("causation_operation_id") REFERENCES "operation" ("Id") ON DELETE RESTRICT
                )'
                    WHERE type = 'table' AND name = 'system_task_lifecycle';

                UPDATE sqlite_schema SET sql = replace(sql,
                    '"definition_id" TEXT NOT NULL', '"definition_id" TEXT NULL')
                    WHERE type = 'table' AND name = 'system_task_ai_ceiling';
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"definition_version" INTEGER NOT NULL', '"definition_version" INTEGER NULL')
                    WHERE type = 'table' AND name = 'system_task_ai_ceiling';
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"definition_fingerprint" TEXT NOT NULL', '"definition_fingerprint" TEXT NULL')
                    WHERE type = 'table' AND name = 'system_task_ai_ceiling';
                UPDATE sqlite_schema SET sql = replace(sql,
                    'CONSTRAINT "FK_system_task_ai_ceiling_system_task_lifecycle_task_id" FOREIGN KEY ("task_id") REFERENCES "system_task_lifecycle" ("task_id") ON DELETE RESTRICT',
                    'CONSTRAINT "FK_system_task_ai_ceiling_system_task_lifecycle_task_id_task_purpose" FOREIGN KEY ("task_id", "task_purpose") REFERENCES "system_task_lifecycle" ("task_id", "purpose") ON DELETE RESTRICT')
                    WHERE type = 'table' AND name = 'system_task_ai_ceiling';
                UPDATE sqlite_schema SET sql = substr(sql, 1, length(sql) - 1) || ', /* lifecycle-origins-v1 */
                    CONSTRAINT "CK_system_task_ai_ceiling_purpose_shape" CHECK ((("task_purpose" = ''procedure-workflow'' AND "definition_id" IS NOT NULL AND "definition_version" IS NOT NULL AND "definition_fingerprint" IS NOT NULL AND "definition_fingerprint" NOT GLOB ''*[^0-9A-F]*'') OR ("task_purpose" = ''application-validation'' AND "definition_id" IS NULL AND "definition_version" IS NULL AND "definition_fingerprint" IS NULL))),
                    CONSTRAINT "CK_system_task_ai_ceiling_task_purpose" CHECK ("task_purpose" IN (''procedure-workflow'',''application-validation''))
                )'
                    WHERE type = 'table' AND name = 'system_task_ai_ceiling';

                PRAGMA writable_schema = RESET;

                CREATE INDEX "ix_system_task_lifecycle_causation_operation"
                    ON "system_task_lifecycle" ("causation_operation_id");
                CREATE INDEX "ix_system_task_ai_ceiling_task_purpose"
                    ON "system_task_ai_ceiling" ("task_id", "task_purpose");

                CREATE TEMP TABLE __system_task_lifecycle_origins_upgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT system_task_lifecycle_origins_upgrade_invalid CHECK (blocked = 0)
                );
                INSERT INTO __system_task_lifecycle_origins_upgrade_guard (blocked)
                SELECT 1
                WHERE (SELECT COUNT(*) FROM pragma_table_info('system_task_lifecycle')
                       WHERE name IN ('state_space_id','state_revision','definition_id',
                                      'definition_version','definition_fingerprint')
                         AND "notnull" = 0) <> 5
                   OR (SELECT COUNT(*) FROM pragma_table_info('system_task_ai_ceiling')
                       WHERE name IN ('definition_id','definition_version','definition_fingerprint')
                         AND "notnull" = 0) <> 3
                   OR (SELECT COUNT(*) FROM pragma_foreign_key_list('system_task_ai_ceiling')
                       WHERE "table" = 'system_task_lifecycle') <> 2
                   OR (SELECT COUNT(*) FROM pragma_foreign_key_list('system_task_lifecycle')
                       WHERE "table" = 'operation' AND "from" = 'causation_operation_id') <> 1
                   OR EXISTS (SELECT 1 FROM pragma_foreign_key_check)
                   OR (SELECT COUNT(*) FROM sqlite_schema
                       WHERE type = 'table' AND name = 'system_task_lifecycle'
                         AND instr(sql, 'CK_system_task_lifecycle_purpose_shape') > 0
                         AND instr(sql, 'lifecycle-origins-v1') > 0) <> 1
                   OR (SELECT COUNT(*) FROM sqlite_schema
                       WHERE type = 'table' AND name = 'system_task_ai_ceiling'
                         AND instr(sql, 'CK_system_task_ai_ceiling_purpose_shape') > 0
                         AND instr(sql, 'lifecycle-origins-v1') > 0) <> 1;
                DROP TABLE __system_task_lifecycle_origins_upgrade_guard;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE __system_task_lifecycle_origins_downgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT retained_task_lifecycle_origins_prevent_downgrade CHECK (blocked = 0)
                );
                INSERT INTO __system_task_lifecycle_origins_downgrade_guard (blocked)
                SELECT 1 WHERE EXISTS (
                    SELECT 1 FROM system_task_lifecycle
                    WHERE purpose <> 'procedure-workflow'
                       OR admission_payload_json IS NOT NULL
                       OR activation_revision IS NOT NULL OR activation_fingerprint IS NOT NULL
                       OR activation_application_revision IS NOT NULL OR activation_application_fingerprint IS NOT NULL
                       OR candidate_id IS NOT NULL OR candidate_revision IS NOT NULL OR candidate_fingerprint IS NOT NULL
                       OR causation_operation_id IS NOT NULL
                       OR state_space_id IS NULL OR state_revision IS NULL
                       OR definition_id IS NULL OR definition_version IS NULL OR definition_fingerprint IS NULL
                ) OR EXISTS (
                    SELECT 1 FROM system_task_ai_ceiling
                    WHERE task_purpose <> 'procedure-workflow'
                       OR definition_id IS NULL OR definition_version IS NULL OR definition_fingerprint IS NULL
                );
                DROP TABLE __system_task_lifecycle_origins_downgrade_guard;

                PRAGMA writable_schema = ON;

                UPDATE sqlite_schema SET sql =
                    substr(sql, 1, instr(sql, ', /* lifecycle-origins-v1 */') - 1) || char(10) || ')'
                    WHERE type = 'table' AND name = 'system_task_lifecycle'
                      AND instr(sql, ', /* lifecycle-origins-v1 */') > 0;
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"state_space_id" TEXT NULL', '"state_space_id" TEXT NOT NULL')
                    WHERE type = 'table' AND name = 'system_task_lifecycle';
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"state_revision" TEXT NULL', '"state_revision" TEXT NOT NULL')
                    WHERE type = 'table' AND name = 'system_task_lifecycle';
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"definition_id" TEXT NULL', '"definition_id" TEXT NOT NULL')
                    WHERE type = 'table' AND name = 'system_task_lifecycle';
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"definition_version" INTEGER NULL', '"definition_version" INTEGER NOT NULL')
                    WHERE type = 'table' AND name = 'system_task_lifecycle';
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"definition_fingerprint" TEXT NULL', '"definition_fingerprint" TEXT NOT NULL')
                    WHERE type = 'table' AND name = 'system_task_lifecycle';

                UPDATE sqlite_schema SET sql =
                    substr(sql, 1, instr(sql, ', /* lifecycle-origins-v1 */') - 1) || char(10) || ')'
                    WHERE type = 'table' AND name = 'system_task_ai_ceiling'
                      AND instr(sql, ', /* lifecycle-origins-v1 */') > 0;
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"definition_id" TEXT NULL', '"definition_id" TEXT NOT NULL')
                    WHERE type = 'table' AND name = 'system_task_ai_ceiling';
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"definition_version" INTEGER NULL', '"definition_version" INTEGER NOT NULL')
                    WHERE type = 'table' AND name = 'system_task_ai_ceiling';
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"definition_fingerprint" TEXT NULL', '"definition_fingerprint" TEXT NOT NULL')
                    WHERE type = 'table' AND name = 'system_task_ai_ceiling';
                UPDATE sqlite_schema SET sql = replace(sql,
                    'CONSTRAINT "FK_system_task_ai_ceiling_system_task_lifecycle_task_id_task_purpose" FOREIGN KEY ("task_id", "task_purpose") REFERENCES "system_task_lifecycle" ("task_id", "purpose") ON DELETE RESTRICT',
                    'CONSTRAINT "FK_system_task_ai_ceiling_system_task_lifecycle_task_id" FOREIGN KEY ("task_id") REFERENCES "system_task_lifecycle" ("task_id") ON DELETE RESTRICT')
                    WHERE type = 'table' AND name = 'system_task_ai_ceiling';

                PRAGMA writable_schema = RESET;

                DROP INDEX "ix_system_task_lifecycle_causation_operation";
                DROP INDEX "ix_system_task_ai_ceiling_task_purpose";
                DROP INDEX "AK_system_task_lifecycle_task_id_purpose";

                ALTER TABLE "system_task_lifecycle" DROP COLUMN "activation_application_fingerprint";
                ALTER TABLE "system_task_lifecycle" DROP COLUMN "activation_application_revision";
                ALTER TABLE "system_task_lifecycle" DROP COLUMN "activation_fingerprint";
                ALTER TABLE "system_task_lifecycle" DROP COLUMN "activation_revision";
                ALTER TABLE "system_task_lifecycle" DROP COLUMN "admission_payload_json";
                ALTER TABLE "system_task_lifecycle" DROP COLUMN "candidate_fingerprint";
                ALTER TABLE "system_task_lifecycle" DROP COLUMN "candidate_id";
                ALTER TABLE "system_task_lifecycle" DROP COLUMN "candidate_revision";
                ALTER TABLE "system_task_lifecycle" DROP COLUMN "causation_operation_id";
                ALTER TABLE "system_task_lifecycle" DROP COLUMN "purpose";
                ALTER TABLE "system_task_ai_ceiling" DROP COLUMN "task_purpose";

                CREATE TEMP TABLE __system_task_lifecycle_origins_downgrade_schema_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT system_task_lifecycle_origins_downgrade_invalid CHECK (blocked = 0)
                );
                INSERT INTO __system_task_lifecycle_origins_downgrade_schema_guard (blocked)
                SELECT 1
                WHERE (SELECT COUNT(*) FROM pragma_table_info('system_task_lifecycle')
                       WHERE name IN ('state_space_id','state_revision','definition_id',
                                      'definition_version','definition_fingerprint')
                         AND "notnull" = 1) <> 5
                   OR (SELECT COUNT(*) FROM pragma_table_info('system_task_lifecycle')
                       WHERE name IN ('purpose','activation_application_fingerprint',
                                      'activation_application_revision','activation_fingerprint',
                                      'activation_revision','admission_payload_json','candidate_fingerprint',
                                      'candidate_id','candidate_revision','causation_operation_id')) <> 0
                   OR (SELECT COUNT(*) FROM pragma_table_info('system_task_ai_ceiling')
                       WHERE name IN ('definition_id','definition_version','definition_fingerprint')
                         AND "notnull" = 1) <> 3
                   OR (SELECT COUNT(*) FROM pragma_table_info('system_task_ai_ceiling')
                       WHERE name = 'task_purpose') <> 0
                   OR (SELECT COUNT(*) FROM pragma_foreign_key_list('system_task_ai_ceiling')
                       WHERE "table" = 'system_task_lifecycle') <> 1
                   OR EXISTS (SELECT 1 FROM pragma_foreign_key_check);
                DROP TABLE __system_task_lifecycle_origins_downgrade_schema_guard;
                """);
        }
    }
}
