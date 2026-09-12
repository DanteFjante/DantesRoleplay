using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class DurableProcedureWorkflowTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trigger_observation_match_workflow_binding",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    PrincipalReference = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    AuthenticationMethod = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ApplicationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BaseApplicationsJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    GrantReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StateRevision = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DefinitionVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    DefinitionFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExecutionRequestJson = table.Column<string>(type: "TEXT", maxLength: 98304, nullable: false),
                    MaximumOperations = table.Column<int>(type: "INTEGER", nullable: false),
                    RuntimeWindowSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    BindingFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_observation_match_workflow_binding", x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion });
                    table.CheckConstraint("CK_trigger_observation_match_workflow_binding_hashes", "length(\"ApplicationFingerprint\") = 64 AND \"ApplicationFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"DefinitionFingerprint\") = 64 AND \"DefinitionFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"BindingFingerprint\") = 64 AND \"BindingFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_trigger_observation_match_workflow_binding_principal", "length(\"PrincipalReference\") = 74 AND substr(\"PrincipalReference\", 1, 10) = 'principal.' AND substr(\"PrincipalReference\", 11) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_observation_match_workflow_binding_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"AuthenticationMethod\") BETWEEN 1 AND 64 AND \"ApplicationRevision\" > 0 AND length(\"BaseApplicationsJson\") BETWEEN 2 AND 4096 AND json_valid(\"BaseApplicationsJson\") AND json_type(\"BaseApplicationsJson\") = 'array' AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"GrantReference\") BETWEEN 1 AND 200 AND length(\"StateRevision\") BETWEEN 1 AND 200 AND length(\"DefinitionId\") BETWEEN 1 AND 200 AND \"DefinitionVersion\" > 0 AND length(\"ExecutionRequestJson\") BETWEEN 2 AND 98304 AND json_valid(\"ExecutionRequestJson\") AND json_type(\"ExecutionRequestJson\") = 'object' AND \"MaximumOperations\" BETWEEN 2 AND 16 AND \"RuntimeWindowSeconds\" BETWEEN 5 AND 600");
                    table.ForeignKey(
                        name: "FK_trigger_observation_match_workflow_binding_trigger_observation_match_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_observation_match_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_one_time_workflow_binding",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    PrincipalReference = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    AuthenticationMethod = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ApplicationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BaseApplicationsJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    GrantReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StateRevision = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DefinitionVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    DefinitionFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExecutionRequestJson = table.Column<string>(type: "TEXT", maxLength: 98304, nullable: false),
                    MaximumOperations = table.Column<int>(type: "INTEGER", nullable: false),
                    RuntimeWindowSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    BindingFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_one_time_workflow_binding", x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion });
                    table.CheckConstraint("CK_trigger_one_time_workflow_binding_hashes", "length(\"ApplicationFingerprint\") = 64 AND \"ApplicationFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"DefinitionFingerprint\") = 64 AND \"DefinitionFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"BindingFingerprint\") = 64 AND \"BindingFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_trigger_one_time_workflow_binding_principal", "length(\"PrincipalReference\") = 74 AND substr(\"PrincipalReference\", 1, 10) = 'principal.' AND substr(\"PrincipalReference\", 11) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_one_time_workflow_binding_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"AuthenticationMethod\") BETWEEN 1 AND 64 AND \"ApplicationRevision\" > 0 AND length(\"BaseApplicationsJson\") BETWEEN 2 AND 4096 AND json_valid(\"BaseApplicationsJson\") AND json_type(\"BaseApplicationsJson\") = 'array' AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"GrantReference\") BETWEEN 1 AND 200 AND length(\"StateRevision\") BETWEEN 1 AND 200 AND length(\"DefinitionId\") BETWEEN 1 AND 200 AND \"DefinitionVersion\" > 0 AND length(\"ExecutionRequestJson\") BETWEEN 2 AND 98304 AND json_valid(\"ExecutionRequestJson\") AND json_type(\"ExecutionRequestJson\") = 'object' AND \"MaximumOperations\" BETWEEN 2 AND 16 AND \"RuntimeWindowSeconds\" BETWEEN 5 AND 600");
                    table.ForeignKey(
                        name: "FK_trigger_one_time_workflow_binding_trigger_one_time_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_one_time_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            // Editing only the declared CHECK expression avoids EF's SQLite parent-table rebuild,
            // which can split the migration transaction and discard dependent rows or custom triggers.
            migrationBuilder.Sql("""
                PRAGMA writable_schema = ON;
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"Target" = ''notification-only''',
                    '"Target" IN (''notification-only'', ''procedure-workflow'')')
                    WHERE type = 'table' AND name IN
                        ('trigger_one_time_definition', 'trigger_observation_match_definition');
                PRAGMA writable_schema = RESET;

                CREATE TEMP TABLE __durable_workflow_trigger_upgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT durable_workflow_trigger_upgrade_invalid CHECK (blocked = 0));
                INSERT INTO __durable_workflow_trigger_upgrade_guard (blocked)
                SELECT 1 WHERE
                    (SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table'
                        AND name IN ('trigger_one_time_definition', 'trigger_observation_match_definition')
                        AND instr(sql, '"Target" IN (''notification-only'', ''procedure-workflow'')') > 0) <> 2
                    OR (SELECT COUNT(*) FROM pragma_foreign_key_list('trigger_one_time_workflow_binding')
                        WHERE "table" = 'trigger_one_time_definition') <> 3
                    OR (SELECT COUNT(*) FROM pragma_foreign_key_list('trigger_observation_match_workflow_binding')
                        WHERE "table" = 'trigger_observation_match_definition') <> 3
                    OR EXISTS (SELECT 1 FROM pragma_foreign_key_check);
                DROP TABLE __durable_workflow_trigger_upgrade_guard;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE __durable_workflow_trigger_downgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT retained_durable_workflow_triggers_prevent_downgrade CHECK (blocked = 0));
                INSERT INTO __durable_workflow_trigger_downgrade_guard (blocked)
                SELECT 1 WHERE
                    EXISTS (SELECT 1 FROM trigger_one_time_definition WHERE "Target" = 'procedure-workflow')
                    OR EXISTS (SELECT 1 FROM trigger_observation_match_definition WHERE "Target" = 'procedure-workflow')
                    OR EXISTS (SELECT 1 FROM trigger_one_time_workflow_binding)
                    OR EXISTS (SELECT 1 FROM trigger_observation_match_workflow_binding);
                DROP TABLE __durable_workflow_trigger_downgrade_guard;
                """);

            migrationBuilder.DropTable(
                name: "trigger_observation_match_workflow_binding");

            migrationBuilder.DropTable(
                name: "trigger_one_time_workflow_binding");

            migrationBuilder.Sql("""
                PRAGMA writable_schema = ON;
                UPDATE sqlite_schema SET sql = replace(sql,
                    '"Target" IN (''notification-only'', ''procedure-workflow'')',
                    '"Target" = ''notification-only''')
                    WHERE type = 'table' AND name IN
                        ('trigger_one_time_definition', 'trigger_observation_match_definition');
                PRAGMA writable_schema = RESET;

                CREATE TEMP TABLE __durable_workflow_trigger_downgrade_schema_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT durable_workflow_trigger_downgrade_invalid CHECK (blocked = 0));
                INSERT INTO __durable_workflow_trigger_downgrade_schema_guard (blocked)
                SELECT 1 WHERE
                    (SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table'
                        AND name IN ('trigger_one_time_definition', 'trigger_observation_match_definition')
                        AND instr(sql, '"Target" = ''notification-only''') > 0) <> 2
                    OR EXISTS (SELECT 1 FROM pragma_foreign_key_check);
                DROP TABLE __durable_workflow_trigger_downgrade_schema_guard;
                """);
        }
    }
}
