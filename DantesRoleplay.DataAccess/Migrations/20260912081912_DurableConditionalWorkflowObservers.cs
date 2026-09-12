using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class DurableConditionalWorkflowObservers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE trigger_conditional_fire_work
                    ADD COLUMN CausalAllowanceId TEXT NULL;
                ALTER TABLE trigger_conditional_fire_work
                    ADD COLUMN PredicateCaptureFingerprint TEXT NULL;
                ALTER TABLE trigger_conditional_fire_work
                    ADD COLUMN PredicateCaptureJson TEXT NULL;
                ALTER TABLE trigger_conditional_fire_work
                    ADD COLUMN PredicatePriorArmed INTEGER NULL;
                ALTER TABLE trigger_conditional_fire_work
                    ADD COLUMN PredicatePriorTruth INTEGER NULL;
                """);

            migrationBuilder.CreateTable(
                name: "trigger_causal_allowance",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 39, nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MaximumOperations = table.Column<int>(type: "INTEGER", nullable: false),
                    ReservedOperations = table.Column<int>(type: "INTEGER", nullable: false),
                    IdentityFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_causal_allowance", x => x.Id);
                    table.CheckConstraint("CK_trigger_causal_allowance_hash", "length(\"IdentityFingerprint\") = 64 AND \"IdentityFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_trigger_causal_allowance_id", "length(\"Id\") = 39 AND substr(\"Id\", 1, 7) = 'causal.' AND substr(\"Id\", 8) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_causal_allowance_values", "\"SourceKind\" IN ('ecs-operation', 'observation') AND length(\"SourceId\") BETWEEN 1 AND 64 AND \"MaximumOperations\" = 64 AND \"ReservedOperations\" BETWEEN 0 AND \"MaximumOperations\"");
                });

            migrationBuilder.CreateTable(
                name: "trigger_conditional_predicate_binding",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    MechanicId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    MechanicVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    MechanicFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActivationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    ActivationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActivationApplicationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    ActivationApplicationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceRegistrationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RequirementsJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    RequirementsFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RoleEntityIdsJson = table.Column<string>(type: "TEXT", maxLength: 32768, nullable: false),
                    RoleEntityIdsFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Coalescing = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    MaximumOperationsPerFire = table.Column<int>(type: "INTEGER", nullable: false),
                    BindingFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_conditional_predicate_binding", x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion });
                    table.CheckConstraint("CK_trigger_conditional_predicate_binding_hashes", "length(\"MechanicFingerprint\") = 64 AND \"MechanicFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ActivationFingerprint\") = 64 AND \"ActivationFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ActivationApplicationFingerprint\") = 64 AND \"ActivationApplicationFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"SourceRegistrationFingerprint\") = 64 AND \"SourceRegistrationFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"RequirementsFingerprint\") = 64 AND \"RequirementsFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"RoleEntityIdsFingerprint\") = 64 AND \"RoleEntityIdsFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"BindingFingerprint\") = 64 AND \"BindingFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_trigger_conditional_predicate_binding_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"MechanicId\") BETWEEN 3 AND 200 AND \"MechanicVersion\" > 0 AND \"ActivationRevision\" > 0 AND \"ActivationApplicationRevision\" > 0 AND length(\"RequirementsJson\") BETWEEN 2 AND 65536 AND json_valid(\"RequirementsJson\") AND json_type(\"RequirementsJson\") = 'object' AND length(\"RoleEntityIdsJson\") BETWEEN 2 AND 32768 AND json_valid(\"RoleEntityIdsJson\") AND json_type(\"RoleEntityIdsJson\") = 'object' AND \"Coalescing\" = 'per-operation' AND \"MaximumOperationsPerFire\" BETWEEN 1 AND 16");
                    table.ForeignKey(
                        name: "FK_trigger_conditional_predicate_binding_trigger_conditional_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_conditional_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_conditional_relationship_dependency",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TriggerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TriggerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    QualifiedKind = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AnchorEntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Incoming = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_conditional_relationship_dependency", x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion, x.Ordinal });
                    table.CheckConstraint("CK_trigger_conditional_relationship_dependency_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system' AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 15 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"QualifiedKind\") BETWEEN 3 AND 200 AND length(\"AnchorEntityId\") BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "FK_trigger_conditional_relationship_dependency_system_ecs_entity_StateSpaceId_AnchorEntityId",
                        columns: x => new { x.StateSpaceId, x.AnchorEntityId },
                        principalTable: "system_ecs_entity",
                        principalColumns: new[] { "StateSpaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_conditional_relationship_dependency_trigger_conditional_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_conditional_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_conditional_workflow_binding",
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
                    table.PrimaryKey("PK_trigger_conditional_workflow_binding", x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion });
                    table.CheckConstraint("CK_trigger_conditional_workflow_binding_hashes", "length(\"ApplicationFingerprint\") = 64 AND \"ApplicationFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"DefinitionFingerprint\") = 64 AND \"DefinitionFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"BindingFingerprint\") = 64 AND \"BindingFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_trigger_conditional_workflow_binding_principal", "length(\"PrincipalReference\") = 74 AND substr(\"PrincipalReference\", 1, 10) = 'principal.' AND substr(\"PrincipalReference\", 11) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_trigger_conditional_workflow_binding_result_schema", "((\"ResultSchemaJson\" IS NULL AND \"ResultSchemaFingerprint\" IS NULL) OR (length(\"ResultSchemaJson\") BETWEEN 2 AND 65536 AND json_valid(\"ResultSchemaJson\") AND json_type(\"ResultSchemaJson\") = 'object' AND length(\"ResultSchemaFingerprint\") = 64 AND \"ResultSchemaFingerprint\" NOT GLOB '*[^0-9A-F]*'))");
                    table.CheckConstraint("CK_trigger_conditional_workflow_binding_values", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"AuthenticationMethod\") BETWEEN 1 AND 64 AND \"ApplicationRevision\" > 0 AND length(\"BaseApplicationsJson\") BETWEEN 2 AND 4096 AND json_valid(\"BaseApplicationsJson\") AND json_type(\"BaseApplicationsJson\") = 'array' AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"GrantReference\") BETWEEN 1 AND 200 AND length(\"StateRevision\") BETWEEN 1 AND 200 AND length(\"DefinitionId\") BETWEEN 1 AND 200 AND \"DefinitionVersion\" > 0 AND length(\"ExecutionRequestJson\") BETWEEN 2 AND 98304 AND json_valid(\"ExecutionRequestJson\") AND json_type(\"ExecutionRequestJson\") = 'object' AND \"MaximumOperations\" BETWEEN 2 AND 16 AND \"RuntimeWindowSeconds\" BETWEEN 5 AND 600");
                    table.ForeignKey(
                        name: "FK_trigger_conditional_workflow_binding_trigger_conditional_definition_ApplicationId_TriggerId_TriggerVersion",
                        columns: x => new { x.ApplicationId, x.TriggerId, x.TriggerVersion },
                        principalTable: "trigger_conditional_definition",
                        principalColumns: new[] { "ApplicationId", "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trigger_causal_reservation",
                columns: table => new
                {
                    CausalAllowanceId = table.Column<string>(type: "TEXT", maxLength: 39, nullable: false),
                    FireId = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    Operations = table.Column<int>(type: "INTEGER", nullable: false),
                    CommandId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_causal_reservation", x => new { x.CausalAllowanceId, x.FireId });
                    table.CheckConstraint("CK_trigger_causal_reservation_hash", "length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_trigger_causal_reservation_values", "length(\"CausalAllowanceId\") = 39 AND length(\"FireId\") = 45 AND \"Operations\" BETWEEN 1 AND 16 AND length(\"CommandId\") BETWEEN 1 AND 128");
                    table.ForeignKey(
                        name: "FK_trigger_causal_reservation_trigger_causal_allowance_CausalAllowanceId",
                        column: x => x.CausalAllowanceId,
                        principalTable: "trigger_causal_allowance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trigger_causal_reservation_trigger_conditional_fire_work_FireId",
                        column: x => x.FireId,
                        principalTable: "trigger_conditional_fire_work",
                        principalColumn: "FireId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_fire_work_CausalAllowanceId",
                table: "trigger_conditional_fire_work",
                column: "CausalAllowanceId");

            migrationBuilder.CreateIndex(
                name: "IX_trigger_causal_allowance_SourceKind_SourceId",
                table: "trigger_causal_allowance",
                columns: new[] { "SourceKind", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_causal_reservation_FireId",
                table: "trigger_causal_reservation",
                column: "FireId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_relationship_dependency_ApplicationId_TriggerId_TriggerVersion_QualifiedKind_AnchorEntityId_Incoming",
                table: "trigger_conditional_relationship_dependency",
                columns: new[] { "ApplicationId", "TriggerId", "TriggerVersion", "QualifiedKind", "AnchorEntityId", "Incoming" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_relationship_dependency_StateSpaceId_AnchorEntityId",
                table: "trigger_conditional_relationship_dependency",
                columns: new[] { "StateSpaceId", "AnchorEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_conditional_relationship_dependency_StateSpaceId_QualifiedKind_AnchorEntityId_Incoming",
                table: "trigger_conditional_relationship_dependency",
                columns: new[] { "StateSpaceId", "QualifiedKind", "AnchorEntityId", "Incoming" });

            migrationBuilder.Sql("""
                PRAGMA writable_schema = ON;
                UPDATE sqlite_schema
                SET sql = replace(sql,
                    '"Target" = ''notification-only''',
                    '"Target" IN (''notification-only'', ''procedure-workflow'')')
                WHERE type = 'table' AND name = 'trigger_conditional_definition'
                  AND instr(sql, '"Target" = ''notification-only''') > 0;
                UPDATE sqlite_schema
                SET sql = replace(sql,
                    '"Disposition" = ''due''',
                    '"Disposition" IN (''due'', ''not-matched'')')
                WHERE type = 'table' AND name = 'trigger_conditional_fire_receipt'
                  AND instr(sql, '"Disposition" = ''due''') > 0;
                UPDATE sqlite_schema SET sql = substr(rtrim(sql, char(9) || char(10) || char(13) || ' '),
                    1, length(rtrim(sql, char(9) || char(10) || char(13) || ' ')) - 1) ||
                    ', CONSTRAINT "CK_trigger_conditional_fire_work_predicate" CHECK (("PredicateCaptureJson" IS NULL AND "PredicateCaptureFingerprint" IS NULL AND "CausalAllowanceId" IS NULL AND "PredicatePriorTruth" IS NULL AND "PredicatePriorArmed" IS NULL) OR (length("PredicateCaptureJson") BETWEEN 2 AND 2000000 AND json_valid("PredicateCaptureJson") AND json_type("PredicateCaptureJson") = ''object'' AND length("PredicateCaptureFingerprint") = 64 AND "PredicateCaptureFingerprint" NOT GLOB ''*[^0-9A-F]*'' AND length("CausalAllowanceId") = 39 AND substr("CausalAllowanceId", 1, 7) = ''causal.'' AND "PredicatePriorArmed" IS NOT NULL)), CONSTRAINT "FK_trigger_conditional_fire_work_trigger_causal_allowance_CausalAllowanceId" FOREIGN KEY ("CausalAllowanceId") REFERENCES "trigger_causal_allowance" ("Id") ON DELETE RESTRICT)'
                WHERE type = 'table' AND name = 'trigger_conditional_fire_work';
                PRAGMA writable_schema = RESET;

                DROP TRIGGER trigger_conditional_work_insert_guard;
                CREATE TRIGGER trigger_conditional_work_insert_guard
                BEFORE INSERT ON trigger_conditional_fire_work
                WHEN NOT EXISTS (SELECT 1 FROM trigger_conditional_current current
                    JOIN trigger_conditional_definition definition ON definition.ApplicationId = current.ApplicationId
                      AND definition.Id = current.Id AND definition.Version = current.CurrentVersion
                    JOIN trigger_conditional_state state ON state.ApplicationId = current.ApplicationId
                      AND state.TriggerId = current.Id AND state.CurrentVersion = current.CurrentVersion
                    WHERE current.ApplicationId = NEW.ApplicationId AND current.Id = NEW.TriggerId
                      AND current.CurrentVersion = NEW.TriggerVersion AND definition.Lifecycle = 'active'
                      AND ((state.LastFiredOperationId = NEW.ChangeOperationId
                            AND NEW.PredicateCaptureJson IS NULL)
                           OR (state.LastOperationId = NEW.ChangeOperationId
                            AND NEW.PredicateCaptureJson IS NOT NULL)))
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_WORK_PROVENANCE'); END;

                CREATE TEMP TABLE __conditional_workflow_observer_upgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT conditional_workflow_observer_upgrade_incomplete CHECK (blocked = 0));
                INSERT INTO __conditional_workflow_observer_upgrade_guard (blocked)
                SELECT 1 WHERE instr((SELECT sql FROM sqlite_schema
                        WHERE type = 'table' AND name = 'trigger_conditional_definition'),
                        '"Target" IN (''notification-only'', ''procedure-workflow'')') = 0
                    OR instr((SELECT sql FROM sqlite_schema
                        WHERE type = 'table' AND name = 'trigger_conditional_fire_receipt'),
                        '"Disposition" IN (''due'', ''not-matched'')') = 0
                    OR instr((SELECT sql FROM sqlite_schema
                        WHERE type = 'table' AND name = 'trigger_conditional_fire_work'),
                        'CK_trigger_conditional_fire_work_predicate') = 0
                    OR instr((SELECT sql FROM sqlite_schema
                        WHERE type = 'table' AND name = 'trigger_conditional_fire_work'),
                        'FK_trigger_conditional_fire_work_trigger_causal_allowance_CausalAllowanceId') = 0
                    OR instr((SELECT sql FROM sqlite_schema
                        WHERE type = 'trigger' AND name = 'trigger_conditional_work_insert_guard'),
                        'NEW.PredicateCaptureJson IS NOT NULL') = 0
                    OR (SELECT COUNT(*) FROM pragma_table_info('trigger_conditional_fire_work')
                        WHERE name IN ('CausalAllowanceId', 'PredicateCaptureJson',
                            'PredicateCaptureFingerprint', 'PredicatePriorTruth', 'PredicatePriorArmed')) <> 5
                    OR (SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name IN
                        ('trigger_conditional_relationship_dependency',
                         'trigger_conditional_workflow_binding',
                         'trigger_conditional_predicate_binding',
                         'trigger_causal_allowance',
                         'trigger_causal_reservation')) <> 5
                    OR EXISTS (SELECT 1 FROM pragma_foreign_key_check);
                DROP TABLE __conditional_workflow_observer_upgrade_guard;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE __conditional_workflow_observer_downgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT retained_conditional_workflow_observers_prevent_downgrade CHECK (blocked = 0));
                INSERT INTO __conditional_workflow_observer_downgrade_guard (blocked)
                SELECT 1 WHERE EXISTS (SELECT 1 FROM trigger_conditional_definition
                        WHERE "Target" = 'procedure-workflow')
                    OR EXISTS (SELECT 1 FROM trigger_conditional_relationship_dependency)
                    OR EXISTS (SELECT 1 FROM trigger_conditional_workflow_binding)
                    OR EXISTS (SELECT 1 FROM trigger_conditional_predicate_binding)
                    OR EXISTS (SELECT 1 FROM trigger_causal_allowance)
                    OR EXISTS (SELECT 1 FROM trigger_causal_reservation)
                    OR EXISTS (SELECT 1 FROM trigger_conditional_fire_work
                        WHERE "CausalAllowanceId" IS NOT NULL
                           OR "PredicateCaptureJson" IS NOT NULL
                           OR "PredicateCaptureFingerprint" IS NOT NULL
                           OR "PredicatePriorTruth" IS NOT NULL
                           OR "PredicatePriorArmed" IS NOT NULL);
                DROP TABLE __conditional_workflow_observer_downgrade_guard;
                """);

            migrationBuilder.DropTable(
                name: "trigger_causal_reservation");

            migrationBuilder.DropTable(
                name: "trigger_conditional_predicate_binding");

            migrationBuilder.DropTable(
                name: "trigger_conditional_relationship_dependency");

            migrationBuilder.DropTable(
                name: "trigger_conditional_workflow_binding");

            migrationBuilder.DropIndex(
                name: "IX_trigger_conditional_fire_work_CausalAllowanceId",
                table: "trigger_conditional_fire_work");

            migrationBuilder.Sql("""
                PRAGMA writable_schema = ON;
                UPDATE sqlite_schema SET sql = replace(sql,
                    ', CONSTRAINT "CK_trigger_conditional_fire_work_predicate" CHECK (("PredicateCaptureJson" IS NULL AND "PredicateCaptureFingerprint" IS NULL AND "CausalAllowanceId" IS NULL AND "PredicatePriorTruth" IS NULL AND "PredicatePriorArmed" IS NULL) OR (length("PredicateCaptureJson") BETWEEN 2 AND 2000000 AND json_valid("PredicateCaptureJson") AND json_type("PredicateCaptureJson") = ''object'' AND length("PredicateCaptureFingerprint") = 64 AND "PredicateCaptureFingerprint" NOT GLOB ''*[^0-9A-F]*'' AND length("CausalAllowanceId") = 39 AND substr("CausalAllowanceId", 1, 7) = ''causal.'' AND "PredicatePriorArmed" IS NOT NULL)), CONSTRAINT "FK_trigger_conditional_fire_work_trigger_causal_allowance_CausalAllowanceId" FOREIGN KEY ("CausalAllowanceId") REFERENCES "trigger_causal_allowance" ("Id") ON DELETE RESTRICT)', ')')
                WHERE type = 'table' AND name = 'trigger_conditional_fire_work';
                UPDATE sqlite_schema
                SET sql = replace(sql,
                    '"Disposition" IN (''due'', ''not-matched'')',
                    '"Disposition" = ''due''')
                WHERE type = 'table' AND name = 'trigger_conditional_fire_receipt'
                  AND instr(sql, '"Disposition" IN (''due'', ''not-matched'')') > 0;
                UPDATE sqlite_schema
                SET sql = replace(sql,
                    '"Target" IN (''notification-only'', ''procedure-workflow'')',
                    '"Target" = ''notification-only''')
                WHERE type = 'table' AND name = 'trigger_conditional_definition'
                  AND instr(sql, '"Target" IN (''notification-only'', ''procedure-workflow'')') > 0;
                PRAGMA writable_schema = RESET;

                DROP TRIGGER trigger_conditional_work_insert_guard;
                CREATE TRIGGER trigger_conditional_work_insert_guard
                BEFORE INSERT ON trigger_conditional_fire_work
                WHEN NOT EXISTS (SELECT 1 FROM trigger_conditional_current current
                    JOIN trigger_conditional_definition definition ON definition.ApplicationId = current.ApplicationId
                      AND definition.Id = current.Id AND definition.Version = current.CurrentVersion
                    JOIN trigger_conditional_state state ON state.ApplicationId = current.ApplicationId
                      AND state.TriggerId = current.Id AND state.CurrentVersion = current.CurrentVersion
                    WHERE current.ApplicationId = NEW.ApplicationId AND current.Id = NEW.TriggerId
                      AND current.CurrentVersion = NEW.TriggerVersion AND definition.Lifecycle = 'active'
                      AND state.LastFiredOperationId = NEW.ChangeOperationId)
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_CONDITIONAL_WORK_PROVENANCE'); END;
                """);

            migrationBuilder.DropTable(
                name: "trigger_causal_allowance");

            migrationBuilder.Sql("""
                ALTER TABLE trigger_conditional_fire_work DROP COLUMN CausalAllowanceId;
                ALTER TABLE trigger_conditional_fire_work DROP COLUMN PredicateCaptureFingerprint;
                ALTER TABLE trigger_conditional_fire_work DROP COLUMN PredicateCaptureJson;
                ALTER TABLE trigger_conditional_fire_work DROP COLUMN PredicatePriorArmed;
                ALTER TABLE trigger_conditional_fire_work DROP COLUMN PredicatePriorTruth;
                """);

            migrationBuilder.Sql("""
                CREATE TEMP TABLE __conditional_workflow_observer_downgrade_complete_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT conditional_workflow_observer_downgrade_incomplete CHECK (blocked = 0));
                INSERT INTO __conditional_workflow_observer_downgrade_complete_guard (blocked)
                SELECT 1 WHERE instr((SELECT sql FROM sqlite_schema
                        WHERE type = 'table' AND name = 'trigger_conditional_definition'),
                        '"Target" = ''notification-only''') = 0
                    OR instr((SELECT sql FROM sqlite_schema
                        WHERE type = 'table' AND name = 'trigger_conditional_fire_receipt'),
                        '"Disposition" = ''due''') = 0
                    OR EXISTS (SELECT 1 FROM pragma_table_info('trigger_conditional_fire_work')
                        WHERE name IN ('CausalAllowanceId', 'PredicateCaptureJson',
                            'PredicateCaptureFingerprint', 'PredicatePriorTruth', 'PredicatePriorArmed'))
                    OR EXISTS (SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name IN
                        ('trigger_conditional_relationship_dependency',
                         'trigger_conditional_workflow_binding',
                         'trigger_conditional_predicate_binding',
                         'trigger_causal_allowance',
                         'trigger_causal_reservation'))
                    OR instr((SELECT sql FROM sqlite_schema
                        WHERE type = 'trigger' AND name = 'trigger_conditional_work_insert_guard'),
                        'PredicateCaptureJson') > 0
                    OR EXISTS (SELECT 1 FROM pragma_foreign_key_check);
                DROP TABLE __conditional_workflow_observer_downgrade_complete_guard;
                """);
        }
    }
}
