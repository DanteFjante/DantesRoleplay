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
            migrationBuilder.DropForeignKey(
                name: "FK_system_task_ai_ceiling_system_task_lifecycle_task_id",
                table: "system_task_ai_ceiling");

            migrationBuilder.AlterColumn<string>(
                name: "state_space_id",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "state_revision",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<int>(
                name: "definition_version",
                table: "system_task_lifecycle",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "definition_id",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "definition_fingerprint",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "activation_application_fingerprint",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "activation_application_revision",
                table: "system_task_lifecycle",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "activation_fingerprint",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "activation_revision",
                table: "system_task_lifecycle",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "admission_payload_json",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "candidate_fingerprint",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "candidate_id",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "candidate_revision",
                table: "system_task_lifecycle",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "causation_operation_id",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: false,
                defaultValue: "procedure-workflow");

            // SQLite rebuilds the AI ceiling before it rebuilds the lifecycle table. The new
            // composite foreign key is therefore validated against the still-current lifecycle
            // table while rows are copied into EF's temporary ceiling table. Give that table the
            // required parent key for the duration of the coordinated rebuild; dropping the old
            // lifecycle table also drops this temporary index, while the rebuilt table retains
            // the declared alternate key below.
            migrationBuilder.CreateIndex(
                name: "__ux_system_task_lifecycle_task_id_purpose_migration",
                table: "system_task_lifecycle",
                columns: new[] { "task_id", "purpose" },
                unique: true);

            migrationBuilder.AlterColumn<int>(
                name: "definition_version",
                table: "system_task_ai_ceiling",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "definition_id",
                table: "system_task_ai_ceiling",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "definition_fingerprint",
                table: "system_task_ai_ceiling",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "task_purpose",
                table: "system_task_ai_ceiling",
                type: "TEXT",
                nullable: false,
                defaultValue: "procedure-workflow");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_system_task_lifecycle_task_id_purpose",
                table: "system_task_lifecycle",
                columns: new[] { "task_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_system_task_lifecycle_causation_operation",
                table: "system_task_lifecycle",
                column: "causation_operation_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_system_task_lifecycle_activation_origin",
                table: "system_task_lifecycle",
                sql: "((\"activation_revision\" IS NULL AND \"activation_fingerprint\" IS NULL AND \"activation_application_revision\" IS NULL AND \"activation_application_fingerprint\" IS NULL) OR (\"activation_revision\" IS NOT NULL AND \"activation_revision\" > 0 AND \"activation_fingerprint\" IS NOT NULL AND length(\"activation_fingerprint\") = 64 AND \"activation_application_revision\" IS NOT NULL AND \"activation_application_revision\" > 0 AND \"activation_application_fingerprint\" IS NOT NULL AND length(\"activation_application_fingerprint\") = 64))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_system_task_lifecycle_admission_payload",
                table: "system_task_lifecycle",
                sql: "(\"admission_payload_json\" IS NULL OR (json_valid(\"admission_payload_json\") = 1 AND json_type(\"admission_payload_json\") = 'object' AND length(CAST(\"admission_payload_json\" AS BLOB)) <= 65536))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_system_task_lifecycle_admission_payload_shape",
                table: "system_task_lifecycle",
                sql: "((\"purpose\" = 'procedure-workflow' AND ((\"activation_revision\" IS NULL AND \"admission_payload_json\" IS NULL) OR (\"activation_revision\" IS NOT NULL AND \"admission_payload_json\" IS NOT NULL))) OR (\"purpose\" = 'application-validation' AND \"activation_revision\" IS NULL AND \"admission_payload_json\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_system_task_lifecycle_purpose",
                table: "system_task_lifecycle",
                sql: "\"purpose\" IN ('procedure-workflow','application-validation')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_system_task_lifecycle_purpose_shape",
                table: "system_task_lifecycle",
                sql: "((\"purpose\" = 'procedure-workflow' AND \"state_space_id\" IS NOT NULL AND length(trim(\"state_space_id\")) BETWEEN 1 AND 200 AND \"state_revision\" IS NOT NULL AND length(trim(\"state_revision\")) BETWEEN 1 AND 200 AND \"definition_id\" IS NOT NULL AND length(\"definition_id\") BETWEEN 1 AND 200 AND \"definition_version\" IS NOT NULL AND \"definition_version\" > 0 AND \"definition_fingerprint\" IS NOT NULL AND length(\"definition_fingerprint\") = 64 AND \"definition_fingerprint\" NOT GLOB '*[^0-9A-F]*' AND \"candidate_id\" IS NULL AND \"candidate_revision\" IS NULL AND \"candidate_fingerprint\" IS NULL AND \"causation_operation_id\" IS NULL) OR (\"purpose\" = 'application-validation' AND \"state_space_id\" IS NULL AND \"state_revision\" IS NULL AND \"definition_id\" IS NULL AND \"definition_version\" IS NULL AND \"definition_fingerprint\" IS NULL AND \"activation_revision\" IS NULL AND \"activation_fingerprint\" IS NULL AND \"activation_application_revision\" IS NULL AND \"activation_application_fingerprint\" IS NULL AND \"candidate_id\" IS NOT NULL AND length(\"candidate_id\") = 32 AND \"candidate_id\" NOT GLOB '*[^0-9a-f]*' AND \"candidate_revision\" IS NOT NULL AND \"candidate_revision\" > 0 AND \"candidate_fingerprint\" IS NOT NULL AND length(\"candidate_fingerprint\") = 64 AND \"candidate_fingerprint\" NOT GLOB '*[^0-9A-F]*' AND (\"causation_operation_id\" IS NULL OR (length(\"causation_operation_id\") = 32 AND \"causation_operation_id\" NOT GLOB '*[^0-9a-f]*'))))");

            migrationBuilder.CreateIndex(
                name: "ix_system_task_ai_ceiling_task_purpose",
                table: "system_task_ai_ceiling",
                columns: new[] { "task_id", "task_purpose" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_system_task_ai_ceiling_purpose_shape",
                table: "system_task_ai_ceiling",
                sql: "((\"task_purpose\" = 'procedure-workflow' AND \"definition_id\" IS NOT NULL AND \"definition_version\" IS NOT NULL AND \"definition_fingerprint\" IS NOT NULL AND \"definition_fingerprint\" NOT GLOB '*[^0-9A-F]*') OR (\"task_purpose\" = 'application-validation' AND \"definition_id\" IS NULL AND \"definition_version\" IS NULL AND \"definition_fingerprint\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_system_task_ai_ceiling_task_purpose",
                table: "system_task_ai_ceiling",
                sql: "\"task_purpose\" IN ('procedure-workflow','application-validation')");

            migrationBuilder.AddForeignKey(
                name: "FK_system_task_ai_ceiling_system_task_lifecycle_task_id_task_purpose",
                table: "system_task_ai_ceiling",
                columns: new[] { "task_id", "task_purpose" },
                principalTable: "system_task_lifecycle",
                principalColumns: new[] { "task_id", "purpose" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_system_task_lifecycle_operation_causation_operation_id",
                table: "system_task_lifecycle",
                column: "causation_operation_id",
                principalTable: "operation",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
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
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_system_task_ai_ceiling_system_task_lifecycle_task_id_task_purpose",
                table: "system_task_ai_ceiling");

            migrationBuilder.DropForeignKey(
                name: "FK_system_task_lifecycle_operation_causation_operation_id",
                table: "system_task_lifecycle");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_system_task_lifecycle_task_id_purpose",
                table: "system_task_lifecycle");

            migrationBuilder.DropIndex(
                name: "ix_system_task_lifecycle_causation_operation",
                table: "system_task_lifecycle");

            migrationBuilder.DropCheckConstraint(
                name: "CK_system_task_lifecycle_activation_origin",
                table: "system_task_lifecycle");

            migrationBuilder.DropCheckConstraint(
                name: "CK_system_task_lifecycle_admission_payload",
                table: "system_task_lifecycle");

            migrationBuilder.DropCheckConstraint(
                name: "CK_system_task_lifecycle_admission_payload_shape",
                table: "system_task_lifecycle");

            migrationBuilder.DropCheckConstraint(
                name: "CK_system_task_lifecycle_purpose",
                table: "system_task_lifecycle");

            migrationBuilder.DropCheckConstraint(
                name: "CK_system_task_lifecycle_purpose_shape",
                table: "system_task_lifecycle");

            migrationBuilder.DropIndex(
                name: "ix_system_task_ai_ceiling_task_purpose",
                table: "system_task_ai_ceiling");

            migrationBuilder.DropCheckConstraint(
                name: "CK_system_task_ai_ceiling_purpose_shape",
                table: "system_task_ai_ceiling");

            migrationBuilder.DropCheckConstraint(
                name: "CK_system_task_ai_ceiling_task_purpose",
                table: "system_task_ai_ceiling");

            migrationBuilder.DropColumn(
                name: "activation_application_fingerprint",
                table: "system_task_lifecycle");

            migrationBuilder.DropColumn(
                name: "activation_application_revision",
                table: "system_task_lifecycle");

            migrationBuilder.DropColumn(
                name: "activation_fingerprint",
                table: "system_task_lifecycle");

            migrationBuilder.DropColumn(
                name: "activation_revision",
                table: "system_task_lifecycle");

            migrationBuilder.DropColumn(
                name: "admission_payload_json",
                table: "system_task_lifecycle");

            migrationBuilder.DropColumn(
                name: "candidate_fingerprint",
                table: "system_task_lifecycle");

            migrationBuilder.DropColumn(
                name: "candidate_id",
                table: "system_task_lifecycle");

            migrationBuilder.DropColumn(
                name: "candidate_revision",
                table: "system_task_lifecycle");

            migrationBuilder.DropColumn(
                name: "causation_operation_id",
                table: "system_task_lifecycle");

            migrationBuilder.DropColumn(
                name: "purpose",
                table: "system_task_lifecycle");

            migrationBuilder.DropColumn(
                name: "task_purpose",
                table: "system_task_ai_ceiling");

            migrationBuilder.AlterColumn<string>(
                name: "state_space_id",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "state_revision",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "definition_version",
                table: "system_task_lifecycle",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "definition_id",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "definition_fingerprint",
                table: "system_task_lifecycle",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "definition_version",
                table: "system_task_ai_ceiling",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "definition_id",
                table: "system_task_ai_ceiling",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "definition_fingerprint",
                table: "system_task_ai_ceiling",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_system_task_ai_ceiling_system_task_lifecycle_task_id",
                table: "system_task_ai_ceiling",
                column: "task_id",
                principalTable: "system_task_lifecycle",
                principalColumn: "task_id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
