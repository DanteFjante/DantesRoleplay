using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class DurableProcedureWorkflowResultSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE trigger_one_time_workflow_binding
                    ADD COLUMN ResultSchemaFingerprint TEXT NULL;
                ALTER TABLE trigger_one_time_workflow_binding
                    ADD COLUMN ResultSchemaJson TEXT NULL;
                ALTER TABLE trigger_observation_match_workflow_binding
                    ADD COLUMN ResultSchemaFingerprint TEXT NULL;
                ALTER TABLE trigger_observation_match_workflow_binding
                    ADD COLUMN ResultSchemaJson TEXT NULL;

                PRAGMA writable_schema = ON;
                UPDATE sqlite_schema SET sql = substr(rtrim(sql, char(9) || char(10) || char(13) || ' '),
                    1, length(rtrim(sql, char(9) || char(10) || char(13) || ' ')) - 1) ||
                    ', CONSTRAINT "CK_trigger_one_time_workflow_binding_result_schema" CHECK (("ResultSchemaJson" IS NULL AND "ResultSchemaFingerprint" IS NULL) OR (length("ResultSchemaJson") BETWEEN 2 AND 65536 AND json_valid("ResultSchemaJson") AND json_type("ResultSchemaJson") = ''object'' AND length("ResultSchemaFingerprint") = 64 AND "ResultSchemaFingerprint" NOT GLOB ''*[^0-9A-F]*'')))'
                    WHERE type = 'table' AND name = 'trigger_one_time_workflow_binding';
                UPDATE sqlite_schema SET sql = substr(rtrim(sql, char(9) || char(10) || char(13) || ' '),
                    1, length(rtrim(sql, char(9) || char(10) || char(13) || ' ')) - 1) ||
                    ', CONSTRAINT "CK_trigger_observation_match_workflow_binding_result_schema" CHECK (("ResultSchemaJson" IS NULL AND "ResultSchemaFingerprint" IS NULL) OR (length("ResultSchemaJson") BETWEEN 2 AND 65536 AND json_valid("ResultSchemaJson") AND json_type("ResultSchemaJson") = ''object'' AND length("ResultSchemaFingerprint") = 64 AND "ResultSchemaFingerprint" NOT GLOB ''*[^0-9A-F]*'')))'
                    WHERE type = 'table' AND name = 'trigger_observation_match_workflow_binding';
                PRAGMA writable_schema = RESET;

                CREATE TRIGGER enforce_one_time_workflow_result_schema_insert
                BEFORE INSERT ON trigger_one_time_workflow_binding
                WHEN (NEW.ResultSchemaJson IS NULL) <> (NEW.ResultSchemaFingerprint IS NULL)
                    OR (NEW.ResultSchemaJson IS NOT NULL AND NOT (
                        length(NEW.ResultSchemaJson) BETWEEN 2 AND 65536
                        AND json_valid(NEW.ResultSchemaJson) AND json_type(NEW.ResultSchemaJson) = 'object'
                        AND length(NEW.ResultSchemaFingerprint) = 64
                        AND NEW.ResultSchemaFingerprint NOT GLOB '*[^0-9A-F]*'))
                BEGIN SELECT RAISE(ABORT, 'workflow result schema fields must be present together'); END;
                CREATE TRIGGER enforce_one_time_workflow_result_schema_update
                BEFORE UPDATE OF ResultSchemaJson, ResultSchemaFingerprint ON trigger_one_time_workflow_binding
                WHEN (NEW.ResultSchemaJson IS NULL) <> (NEW.ResultSchemaFingerprint IS NULL)
                    OR (NEW.ResultSchemaJson IS NOT NULL AND NOT (
                        length(NEW.ResultSchemaJson) BETWEEN 2 AND 65536
                        AND json_valid(NEW.ResultSchemaJson) AND json_type(NEW.ResultSchemaJson) = 'object'
                        AND length(NEW.ResultSchemaFingerprint) = 64
                        AND NEW.ResultSchemaFingerprint NOT GLOB '*[^0-9A-F]*'))
                BEGIN SELECT RAISE(ABORT, 'workflow result schema fields must be present together'); END;
                CREATE TRIGGER enforce_observation_workflow_result_schema_insert
                BEFORE INSERT ON trigger_observation_match_workflow_binding
                WHEN (NEW.ResultSchemaJson IS NULL) <> (NEW.ResultSchemaFingerprint IS NULL)
                    OR (NEW.ResultSchemaJson IS NOT NULL AND NOT (
                        length(NEW.ResultSchemaJson) BETWEEN 2 AND 65536
                        AND json_valid(NEW.ResultSchemaJson) AND json_type(NEW.ResultSchemaJson) = 'object'
                        AND length(NEW.ResultSchemaFingerprint) = 64
                        AND NEW.ResultSchemaFingerprint NOT GLOB '*[^0-9A-F]*'))
                BEGIN SELECT RAISE(ABORT, 'workflow result schema fields must be present together'); END;
                CREATE TRIGGER enforce_observation_workflow_result_schema_update
                BEFORE UPDATE OF ResultSchemaJson, ResultSchemaFingerprint ON trigger_observation_match_workflow_binding
                WHEN (NEW.ResultSchemaJson IS NULL) <> (NEW.ResultSchemaFingerprint IS NULL)
                    OR (NEW.ResultSchemaJson IS NOT NULL AND NOT (
                        length(NEW.ResultSchemaJson) BETWEEN 2 AND 65536
                        AND json_valid(NEW.ResultSchemaJson) AND json_type(NEW.ResultSchemaJson) = 'object'
                        AND length(NEW.ResultSchemaFingerprint) = 64
                        AND NEW.ResultSchemaFingerprint NOT GLOB '*[^0-9A-F]*'))
                BEGIN SELECT RAISE(ABORT, 'workflow result schema fields must be present together'); END;

                CREATE TEMP TABLE __workflow_result_schema_upgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT workflow_result_schema_upgrade_invalid CHECK (blocked = 0));
                INSERT INTO __workflow_result_schema_upgrade_guard (blocked)
                SELECT 1 WHERE
                    (SELECT COUNT(*) FROM pragma_table_info('trigger_one_time_workflow_binding')
                        WHERE name IN ('ResultSchemaJson', 'ResultSchemaFingerprint')) <> 2
                    OR (SELECT COUNT(*) FROM pragma_table_info('trigger_observation_match_workflow_binding')
                        WHERE name IN ('ResultSchemaJson', 'ResultSchemaFingerprint')) <> 2
                    OR instr((SELECT sql FROM sqlite_schema WHERE type = 'table'
                        AND name = 'trigger_one_time_workflow_binding'),
                        'CK_trigger_one_time_workflow_binding_result_schema') = 0
                    OR instr((SELECT sql FROM sqlite_schema WHERE type = 'table'
                        AND name = 'trigger_observation_match_workflow_binding'),
                        'CK_trigger_observation_match_workflow_binding_result_schema') = 0
                    OR (SELECT COUNT(*) FROM sqlite_schema WHERE type = 'trigger' AND name IN
                        ('enforce_one_time_workflow_result_schema_insert',
                         'enforce_one_time_workflow_result_schema_update',
                         'enforce_observation_workflow_result_schema_insert',
                         'enforce_observation_workflow_result_schema_update')) <> 4
                    OR EXISTS (SELECT 1 FROM pragma_foreign_key_check);
                DROP TABLE __workflow_result_schema_upgrade_guard;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE __workflow_result_schema_downgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT retained_workflow_result_schema_prevents_downgrade CHECK (blocked = 0));
                INSERT INTO __workflow_result_schema_downgrade_guard (blocked)
                SELECT 1 WHERE EXISTS (
                    SELECT 1 FROM trigger_one_time_workflow_binding
                    WHERE ResultSchemaJson IS NOT NULL OR ResultSchemaFingerprint IS NOT NULL)
                    OR EXISTS (
                    SELECT 1 FROM trigger_observation_match_workflow_binding
                    WHERE ResultSchemaJson IS NOT NULL OR ResultSchemaFingerprint IS NOT NULL);
                DROP TABLE __workflow_result_schema_downgrade_guard;

                DROP TRIGGER enforce_one_time_workflow_result_schema_insert;
                DROP TRIGGER enforce_one_time_workflow_result_schema_update;
                DROP TRIGGER enforce_observation_workflow_result_schema_insert;
                DROP TRIGGER enforce_observation_workflow_result_schema_update;

                PRAGMA writable_schema = ON;
                UPDATE sqlite_schema SET sql = replace(sql,
                    ', CONSTRAINT "CK_trigger_one_time_workflow_binding_result_schema" CHECK (("ResultSchemaJson" IS NULL AND "ResultSchemaFingerprint" IS NULL) OR (length("ResultSchemaJson") BETWEEN 2 AND 65536 AND json_valid("ResultSchemaJson") AND json_type("ResultSchemaJson") = ''object'' AND length("ResultSchemaFingerprint") = 64 AND "ResultSchemaFingerprint" NOT GLOB ''*[^0-9A-F]*'')))', ')')
                    WHERE type = 'table' AND name = 'trigger_one_time_workflow_binding';
                UPDATE sqlite_schema SET sql = replace(sql,
                    ', CONSTRAINT "CK_trigger_observation_match_workflow_binding_result_schema" CHECK (("ResultSchemaJson" IS NULL AND "ResultSchemaFingerprint" IS NULL) OR (length("ResultSchemaJson") BETWEEN 2 AND 65536 AND json_valid("ResultSchemaJson") AND json_type("ResultSchemaJson") = ''object'' AND length("ResultSchemaFingerprint") = 64 AND "ResultSchemaFingerprint" NOT GLOB ''*[^0-9A-F]*'')))', ')')
                    WHERE type = 'table' AND name = 'trigger_observation_match_workflow_binding';
                PRAGMA writable_schema = RESET;

                ALTER TABLE trigger_one_time_workflow_binding DROP COLUMN ResultSchemaJson;
                ALTER TABLE trigger_one_time_workflow_binding DROP COLUMN ResultSchemaFingerprint;
                ALTER TABLE trigger_observation_match_workflow_binding DROP COLUMN ResultSchemaJson;
                ALTER TABLE trigger_observation_match_workflow_binding DROP COLUMN ResultSchemaFingerprint;

                CREATE TEMP TABLE __workflow_result_schema_downgrade_check (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT workflow_result_schema_downgrade_invalid CHECK (blocked = 0));
                INSERT INTO __workflow_result_schema_downgrade_check (blocked)
                SELECT 1 WHERE
                    EXISTS (SELECT 1 FROM pragma_table_info('trigger_one_time_workflow_binding')
                        WHERE name IN ('ResultSchemaJson', 'ResultSchemaFingerprint'))
                    OR EXISTS (SELECT 1 FROM pragma_table_info('trigger_observation_match_workflow_binding')
                        WHERE name IN ('ResultSchemaJson', 'ResultSchemaFingerprint'))
                    OR EXISTS (SELECT 1 FROM pragma_foreign_key_check);
                DROP TABLE __workflow_result_schema_downgrade_check;
                """);
        }
    }
}
