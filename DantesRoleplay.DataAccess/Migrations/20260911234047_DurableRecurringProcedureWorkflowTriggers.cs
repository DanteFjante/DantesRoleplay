using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class DurableRecurringProcedureWorkflowTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                PRAGMA writable_schema = ON;
                UPDATE sqlite_schema
                SET sql = replace(sql,
                    '"Target" = ''notification-only''',
                    '"Target" IN (''notification-only'', ''procedure-workflow'')')
                WHERE type = 'table' AND name = 'trigger_recurring_definition'
                  AND instr(sql, '"Target" = ''notification-only''') > 0;
                PRAGMA writable_schema = RESET;
                """);

            migrationBuilder.CreateTable(
                name: "trigger_recurring_workflow_binding",
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
                    ResultSchemaJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: true),
                    ResultSchemaFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MaximumOperations = table.Column<int>(type: "INTEGER", nullable: false),
                    RuntimeWindowSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    BindingFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_recurring_workflow_binding", x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion });
                    table.CheckConstraint("CK_trigger_recurring_workflow_binding_hashes", "length(\"ApplicationFingerprint\") = 64 AND \"ApplicationFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"DefinitionFingerprint\") = 64 AND \"DefinitionFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"BindingFingerprint\") = 64 AND \"BindingFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_trigger_recurring_workflow_binding_principal", "length(\"PrincipalReference\") = 74 AND substr(\"PrincipalReference\", 1, 10) = 'principal.' AND substr(\"PrincipalReference\", 11) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_recurring_workflow_binding_result_schema", "((\"ResultSchemaJson\" IS NULL AND \"ResultSchemaFingerprint\" IS NULL) OR (length(\"ResultSchemaJson\") BETWEEN 2 AND 65536 AND json_valid(\"ResultSchemaJson\") AND json_type(\"ResultSchemaJson\") = 'object' AND length(\"ResultSchemaFingerprint\") = 64 AND \"ResultSchemaFingerprint\" NOT GLOB '*[^0-9A-F]*'))");
                    table.CheckConstraint("CK_trigger_recurring_workflow_binding_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"AuthenticationMethod\") BETWEEN 1 AND 64 AND \"ApplicationRevision\" > 0 AND length(\"BaseApplicationsJson\") BETWEEN 2 AND 4096 AND json_valid(\"BaseApplicationsJson\") AND json_type(\"BaseApplicationsJson\") = 'array' AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"GrantReference\") BETWEEN 1 AND 200 AND length(\"StateRevision\") BETWEEN 1 AND 200 AND length(\"DefinitionId\") BETWEEN 1 AND 200 AND \"DefinitionVersion\" > 0 AND length(\"ExecutionRequestJson\") BETWEEN 2 AND 98304 AND json_valid(\"ExecutionRequestJson\") AND json_type(\"ExecutionRequestJson\") = 'object' AND \"MaximumOperations\" BETWEEN 2 AND 16 AND \"RuntimeWindowSeconds\" BETWEEN 5 AND 600");
                    table.ForeignKey(
                        name: "FK_trigger_recurring_workflow_binding_trigger_recurring_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_recurring_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                CREATE TEMP TABLE __recurring_workflow_upgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT recurring_workflow_upgrade_incomplete CHECK (blocked = 0));
                INSERT INTO __recurring_workflow_upgrade_guard (blocked)
                SELECT 1 WHERE instr((SELECT sql FROM sqlite_schema
                    WHERE type = 'table' AND name = 'trigger_recurring_definition'),
                    '"Target" IN (''notification-only'', ''procedure-workflow'')') = 0
                    OR NOT EXISTS (SELECT 1 FROM sqlite_schema WHERE type = 'table'
                        AND name = 'trigger_recurring_workflow_binding')
                    OR EXISTS (SELECT 1 FROM pragma_foreign_key_check);
                DROP TABLE __recurring_workflow_upgrade_guard;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE __recurring_workflow_downgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT retained_recurring_workflow_prevents_downgrade CHECK (blocked = 0));
                INSERT INTO __recurring_workflow_downgrade_guard (blocked)
                SELECT 1 WHERE EXISTS (SELECT 1 FROM trigger_recurring_workflow_binding)
                    OR EXISTS (SELECT 1 FROM trigger_recurring_definition
                        WHERE "Target" = 'procedure-workflow');
                DROP TABLE __recurring_workflow_downgrade_guard;
                """);

            migrationBuilder.DropTable(
                name: "trigger_recurring_workflow_binding");

            migrationBuilder.Sql("""
                PRAGMA writable_schema = ON;
                UPDATE sqlite_schema
                SET sql = replace(sql,
                    '"Target" IN (''notification-only'', ''procedure-workflow'')',
                    '"Target" = ''notification-only''')
                WHERE type = 'table' AND name = 'trigger_recurring_definition'
                  AND instr(sql, '"Target" IN (''notification-only'', ''procedure-workflow'')') > 0;
                PRAGMA writable_schema = RESET;

                CREATE TEMP TABLE __recurring_workflow_downgrade_complete_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT recurring_workflow_downgrade_incomplete CHECK (blocked = 0));
                INSERT INTO __recurring_workflow_downgrade_complete_guard (blocked)
                SELECT 1 WHERE instr((SELECT sql FROM sqlite_schema
                    WHERE type = 'table' AND name = 'trigger_recurring_definition'),
                    '"Target" = ''notification-only''') = 0
                    OR EXISTS (SELECT 1 FROM pragma_foreign_key_check);
                DROP TABLE __recurring_workflow_downgrade_complete_guard;
                """);
        }
    }
}
