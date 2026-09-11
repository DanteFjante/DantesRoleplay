using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class SystemTaskAiAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_task_ai_ceiling",
                columns: table => new
                {
                    task_id = table.Column<string>(type: "TEXT", nullable: false),
                    enrollment_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    profile_id = table.Column<string>(type: "TEXT", nullable: false),
                    profile_version = table.Column<int>(type: "INTEGER", nullable: false),
                    profile_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    grant_reference = table.Column<string>(type: "TEXT", nullable: false),
                    grant_revision = table.Column<string>(type: "TEXT", nullable: false),
                    grant_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    definition_id = table.Column<string>(type: "TEXT", nullable: false),
                    definition_version = table.Column<int>(type: "INTEGER", nullable: false),
                    definition_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    output_schema_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    mode = table.Column<string>(type: "TEXT", nullable: false),
                    maximum_provider_tokens = table.Column<int>(type: "INTEGER", nullable: false),
                    maximum_tool_calls = table.Column<int>(type: "INTEGER", nullable: false),
                    maximum_concurrent_provider_requests = table.Column<int>(type: "INTEGER", nullable: false),
                    deadline_utc = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_ai_ceiling", x => x.task_id);
                    table.CheckConstraint("CK_system_task_ai_ceiling_concurrency", "\"maximum_concurrent_provider_requests\" BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_system_task_ai_ceiling_created_at", "length(\"created_at_utc\") BETWEEN 1 AND 40");
                    table.CheckConstraint("CK_system_task_ai_ceiling_deadline", "length(\"deadline_utc\") BETWEEN 1 AND 40");
                    table.CheckConstraint("CK_system_task_ai_ceiling_definition_fingerprint", "length(\"definition_fingerprint\") = 64");
                    table.CheckConstraint("CK_system_task_ai_ceiling_definition_id", "length(\"definition_id\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_system_task_ai_ceiling_definition_version", "\"definition_version\" > 0");
                    table.CheckConstraint("CK_system_task_ai_ceiling_enrollment_fingerprint", "length(\"enrollment_fingerprint\") = 64");
                    table.CheckConstraint("CK_system_task_ai_ceiling_grant_fingerprint", "length(\"grant_fingerprint\") = 64");
                    table.CheckConstraint("CK_system_task_ai_ceiling_grant_reference", "length(\"grant_reference\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_system_task_ai_ceiling_grant_revision", "length(\"grant_revision\") BETWEEN 1 AND 1024");
                    table.CheckConstraint("CK_system_task_ai_ceiling_mode", "\"mode\" IN ('measured-stop','hard-cap')");
                    table.CheckConstraint("CK_system_task_ai_ceiling_profile_fingerprint", "length(\"profile_fingerprint\") = 64");
                    table.CheckConstraint("CK_system_task_ai_ceiling_profile_id", "length(\"profile_id\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_system_task_ai_ceiling_profile_version", "\"profile_version\" > 0");
                    table.CheckConstraint("CK_system_task_ai_ceiling_provider_tokens", "\"maximum_provider_tokens\" BETWEEN 1 AND 131072");
                    table.CheckConstraint("CK_system_task_ai_ceiling_schema_fingerprint", "length(\"output_schema_fingerprint\") = 64");
                    table.CheckConstraint("CK_system_task_ai_ceiling_task_id", "length(\"task_id\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_system_task_ai_ceiling_tool_calls", "\"maximum_tool_calls\" BETWEEN 0 AND 16");
                    table.ForeignKey(
                        name: "FK_system_task_ai_ceiling_system_task_lifecycle_task_id",
                        column: x => x.task_id,
                        principalTable: "system_task_lifecycle",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_task_ai_reservation",
                columns: table => new
                {
                    record_reference = table.Column<string>(type: "TEXT", nullable: false),
                    task_id = table.Column<string>(type: "TEXT", nullable: false),
                    reservation_id = table.Column<string>(type: "TEXT", nullable: false),
                    request_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    attempt_id = table.Column<string>(type: "TEXT", nullable: false),
                    fencing_counter = table.Column<long>(type: "INTEGER", nullable: false),
                    lease_token = table.Column<string>(type: "TEXT", nullable: false),
                    lease_expires_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    deadline_utc = table.Column<string>(type: "TEXT", nullable: false),
                    requested_provider_tokens = table.Column<int>(type: "INTEGER", nullable: false),
                    reserved_provider_tokens = table.Column<int>(type: "INTEGER", nullable: false),
                    reserved_tool_calls = table.Column<int>(type: "INTEGER", nullable: false),
                    mode = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    charged_provider_tokens = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    charged_tool_calls = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    settled_evidence_sequence = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_ai_reservation", x => x.record_reference);
                    table.UniqueConstraint("AK_system_task_ai_reservation_task_id_reservation_id", x => new { x.task_id, x.reservation_id });
                    table.CheckConstraint("CK_system_task_ai_reservation_attempt_id", "length(\"attempt_id\") BETWEEN 1 AND 128");
                    table.CheckConstraint("CK_system_task_ai_reservation_charged_tokens", "\"charged_provider_tokens\" >= 0");
                    table.CheckConstraint("CK_system_task_ai_reservation_charged_tools", "\"charged_tool_calls\" >= 0");
                    table.CheckConstraint("CK_system_task_ai_reservation_created_at", "length(\"created_at_utc\") BETWEEN 1 AND 40");
                    table.CheckConstraint("CK_system_task_ai_reservation_deadline", "length(\"deadline_utc\") BETWEEN 1 AND 40");
                    table.CheckConstraint("CK_system_task_ai_reservation_dispatch_shape", "((\"requested_provider_tokens\" > 0 AND \"reserved_provider_tokens\" > 0 AND \"reserved_tool_calls\" = 0) OR (\"requested_provider_tokens\" = 0 AND \"reserved_provider_tokens\" = 0 AND \"reserved_tool_calls\" = 1))");
                    table.CheckConstraint("CK_system_task_ai_reservation_fence", "\"fencing_counter\" >= 1");
                    table.CheckConstraint("CK_system_task_ai_reservation_lease_expiry", "length(\"lease_expires_at_utc\") BETWEEN 1 AND 40");
                    table.CheckConstraint("CK_system_task_ai_reservation_lease_token", "length(\"lease_token\") BETWEEN 1 AND 128");
                    table.CheckConstraint("CK_system_task_ai_reservation_mode", "\"mode\" IN ('measured-stop','hard-cap')");
                    table.CheckConstraint("CK_system_task_ai_reservation_record_reference", "length(\"record_reference\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_system_task_ai_reservation_request_fingerprint", "length(\"request_fingerprint\") = 64");
                    table.CheckConstraint("CK_system_task_ai_reservation_requested", "\"requested_provider_tokens\" BETWEEN 0 AND 131072");
                    table.CheckConstraint("CK_system_task_ai_reservation_reservation_id", "length(\"reservation_id\") BETWEEN 1 AND 128");
                    table.CheckConstraint("CK_system_task_ai_reservation_reserved_tokens", "\"reserved_provider_tokens\" BETWEEN 0 AND \"requested_provider_tokens\"");
                    table.CheckConstraint("CK_system_task_ai_reservation_reserved_tools", "\"reserved_tool_calls\" BETWEEN 0 AND 16");
                    table.CheckConstraint("CK_system_task_ai_reservation_settled_sequence", "\"settled_evidence_sequence\" BETWEEN 1 AND 16");
                    table.CheckConstraint("CK_system_task_ai_reservation_status", "\"status\" IN ('reserved','unknown','settled','exceeded')");
                    table.CheckConstraint("CK_system_task_ai_reservation_task_id", "length(\"task_id\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_system_task_ai_reservation_updated_at", "length(\"updated_at_utc\") BETWEEN 1 AND 40");
                    table.ForeignKey(
                        name: "FK_system_task_ai_reservation_system_task_ai_ceiling_task_id",
                        column: x => x.task_id,
                        principalTable: "system_task_ai_ceiling",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_task_ai_reservation_system_task_attempt_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "system_task_attempt",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_task_ai_dispatch_evidence",
                columns: table => new
                {
                    record_reference = table.Column<string>(type: "TEXT", nullable: false),
                    sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    event_reference = table.Column<string>(type: "TEXT", nullable: false),
                    payload_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    dispatch_kind = table.Column<string>(type: "TEXT", nullable: false),
                    request_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    provider_id = table.Column<string>(type: "TEXT", nullable: true),
                    model_id = table.Column<string>(type: "TEXT", nullable: true),
                    profile_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    schema_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    request_json = table.Column<string>(type: "TEXT", nullable: true),
                    response_fingerprint = table.Column<string>(type: "TEXT", nullable: true),
                    input_tokens = table.Column<long>(type: "INTEGER", nullable: true),
                    output_tokens = table.Column<long>(type: "INTEGER", nullable: true),
                    total_tokens = table.Column<long>(type: "INTEGER", nullable: true),
                    observed_tool_calls = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    is_complete = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    completion_kind = table.Column<string>(type: "TEXT", nullable: true),
                    observed_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_ai_dispatch_evidence", x => new { x.record_reference, x.sequence });
                    table.UniqueConstraint("AK_system_task_ai_dispatch_evidence_event_reference", x => x.event_reference);
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_complete", "\"is_complete\" IN (0, 1)");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_completion_kind", "\"completion_kind\" IS NULL OR \"completion_kind\" IN ('returned','threw','cancelled','not-started')");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_dispatch_kind", "\"dispatch_kind\" IN ('provider','tool')");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_event", "length(\"event_reference\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_input_tokens", "\"input_tokens\" IS NULL OR \"input_tokens\" >= 0");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_kind", "\"kind\" IN ('dispatch','usage')");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_model", "\"model_id\" IS NULL OR length(\"model_id\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_observed_at", "length(\"observed_at_utc\") BETWEEN 1 AND 40");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_output_tokens", "\"output_tokens\" IS NULL OR \"output_tokens\" >= 0");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_payload_fingerprint", "length(\"payload_fingerprint\") = 64");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_profile_fingerprint", "length(\"profile_fingerprint\") = 64");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_provider", "\"provider_id\" IS NULL OR length(\"provider_id\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_record", "length(\"record_reference\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_request_fingerprint", "length(\"request_fingerprint\") = 64");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_response_fingerprint", "\"response_fingerprint\" IS NULL OR length(\"response_fingerprint\") = 64");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_schema_fingerprint", "length(\"schema_fingerprint\") = 64");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_sequence", "\"sequence\" BETWEEN 0 AND 16");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_shape", "((\"kind\" = 'dispatch' AND \"sequence\" = 0 AND \"request_json\" IS NOT NULL AND length(CAST(\"request_json\" AS BLOB)) <= 65536 AND \"response_fingerprint\" IS NULL AND \"input_tokens\" IS NULL AND \"output_tokens\" IS NULL AND \"total_tokens\" IS NULL AND \"observed_tool_calls\" = 0 AND \"is_complete\" = 0 AND \"completion_kind\" IS NULL) OR (\"kind\" = 'usage' AND \"sequence\" BETWEEN 1 AND 16 AND \"request_json\" IS NULL AND \"response_fingerprint\" IS NOT NULL AND \"completion_kind\" IS NOT NULL))");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_sum", "(\"input_tokens\" IS NULL OR \"output_tokens\" IS NULL OR \"input_tokens\" <= 9223372036854775807 - \"output_tokens\")");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_target", "((\"dispatch_kind\" = 'provider' AND \"provider_id\" IS NOT NULL AND \"model_id\" IS NOT NULL) OR (\"dispatch_kind\" = 'tool' AND \"provider_id\" IS NULL AND \"model_id\" IS NULL))");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_tool_calls", "\"observed_tool_calls\" >= 0");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_total", "(\"total_tokens\" IS NULL OR (\"total_tokens\" >= COALESCE(\"output_tokens\", 0) AND COALESCE(\"input_tokens\", 0) <= \"total_tokens\" - COALESCE(\"output_tokens\", 0)))");
                    table.CheckConstraint("CK_system_task_ai_dispatch_evidence_total_tokens", "\"total_tokens\" IS NULL OR \"total_tokens\" >= 0");
                    table.ForeignKey(
                        name: "FK_system_task_ai_dispatch_evidence_system_task_ai_reservation_record_reference",
                        column: x => x.record_reference,
                        principalTable: "system_task_ai_reservation",
                        principalColumn: "record_reference",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_task_ai_reservation_ancestor",
                columns: table => new
                {
                    record_reference = table.Column<string>(type: "TEXT", nullable: false),
                    ancestor_task_id = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_ai_reservation_ancestor", x => new { x.record_reference, x.ancestor_task_id });
                    table.CheckConstraint("CK_system_task_ai_reservation_ancestor_record", "length(\"record_reference\") BETWEEN 1 AND 200");
                    table.CheckConstraint("CK_system_task_ai_reservation_ancestor_task", "length(\"ancestor_task_id\") BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "FK_system_task_ai_reservation_ancestor_system_task_ai_ceiling_ancestor_task_id",
                        column: x => x.ancestor_task_id,
                        principalTable: "system_task_ai_ceiling",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_task_ai_reservation_ancestor_system_task_ai_reservation_record_reference",
                        column: x => x.record_reference,
                        principalTable: "system_task_ai_reservation",
                        principalColumn: "record_reference",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_system_task_ai_reservation_attempt",
                table: "system_task_ai_reservation",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_system_task_ai_reservation_ancestor_task",
                table: "system_task_ai_reservation_ancestor",
                column: "ancestor_task_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An older host cannot represent AI admission, reservation, or observed-usage
            // evidence. Refuse to delete it during rollback.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE __system_task_ai_accounting_downgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT retained_task_ai_accounting_prevents_downgrade CHECK (blocked = 0)
                );
                INSERT INTO __system_task_ai_accounting_downgrade_guard (blocked)
                SELECT 1 WHERE EXISTS (SELECT 1 FROM system_task_ai_ceiling)
                    OR EXISTS (SELECT 1 FROM system_task_ai_reservation)
                    OR EXISTS (SELECT 1 FROM system_task_ai_reservation_ancestor)
                    OR EXISTS (SELECT 1 FROM system_task_ai_dispatch_evidence);
                DROP TABLE __system_task_ai_accounting_downgrade_guard;
                """);

            migrationBuilder.DropTable(
                name: "system_task_ai_dispatch_evidence");

            migrationBuilder.DropTable(
                name: "system_task_ai_reservation_ancestor");

            migrationBuilder.DropTable(
                name: "system_task_ai_reservation");

            migrationBuilder.DropTable(
                name: "system_task_ai_ceiling");
        }
    }
}
