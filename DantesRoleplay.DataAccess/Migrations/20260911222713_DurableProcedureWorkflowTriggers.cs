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
            migrationBuilder.DropCheckConstraint(
                name: "CK_trigger_one_time_definition_values",
                table: "trigger_one_time_definition");

            migrationBuilder.DropCheckConstraint(
                name: "CK_trigger_observation_match_definition_values",
                table: "trigger_observation_match_definition");

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

            migrationBuilder.AddCheckConstraint(
                name: "CK_trigger_one_time_definition_values",
                table: "trigger_one_time_definition",
                sql: "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"Id\") BETWEEN 3 AND 200 AND \"Version\" > 0 AND \"MisfirePolicy\" IN ('skip', 'fire-once') AND \"Target\" IN ('notification-only', 'procedure-workflow') AND \"Lifecycle\" IN ('active', 'cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_trigger_observation_match_definition_values",
                table: "trigger_observation_match_definition",
                sql: "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"Id\") BETWEEN 3 AND 200 AND \"Version\" > 0 AND \"Lifecycle\" IN ('active', 'paused', 'cancelled') AND length(\"SourceId\") BETWEEN 3 AND 200 AND \"SourceVersion\" > 0 AND length(\"StructureId\") BETWEEN 3 AND 200 AND \"StructureVersion\" > 0 AND length(\"AdapterId\") BETWEEN 3 AND 200 AND \"AdapterVersion\" > 0 AND \"Target\" IN ('notification-only', 'procedure-workflow')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trigger_observation_match_workflow_binding");

            migrationBuilder.DropTable(
                name: "trigger_one_time_workflow_binding");

            migrationBuilder.DropCheckConstraint(
                name: "CK_trigger_one_time_definition_values",
                table: "trigger_one_time_definition");

            migrationBuilder.DropCheckConstraint(
                name: "CK_trigger_observation_match_definition_values",
                table: "trigger_observation_match_definition");

            migrationBuilder.AddCheckConstraint(
                name: "CK_trigger_one_time_definition_values",
                table: "trigger_one_time_definition",
                sql: "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"Id\") BETWEEN 3 AND 200 AND \"Version\" > 0 AND \"MisfirePolicy\" IN ('skip', 'fire-once') AND \"Target\" = 'notification-only' AND \"Lifecycle\" IN ('active', 'cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_trigger_observation_match_definition_values",
                table: "trigger_observation_match_definition",
                sql: "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"Id\") BETWEEN 3 AND 200 AND \"Version\" > 0 AND \"Lifecycle\" IN ('active', 'paused', 'cancelled') AND length(\"SourceId\") BETWEEN 3 AND 200 AND \"SourceVersion\" > 0 AND length(\"StructureId\") BETWEEN 3 AND 200 AND \"StructureVersion\" > 0 AND length(\"AdapterId\") BETWEEN 3 AND 200 AND \"AdapterVersion\" > 0 AND \"Target\" = 'notification-only'");
        }
    }
}
