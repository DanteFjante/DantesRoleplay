using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeAuthoringAndDurableTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_application_candidate_revision",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    CandidateId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpectedActiveFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SynchronizationEvidenceReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    NewImplementationReason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    AuthorGrantReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceOperationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CanonicalCommandFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_candidate_revision", x => new { x.ApplicationId, x.CandidateId, x.Revision });
                    table.CheckConstraint("CK_system_candidate_hashes", "\"ContentFingerprint\" IS NOT NULL AND length(\"ContentFingerprint\") = 64 AND \"ContentFingerprint\" NOT GLOB '*[^0-9A-F]*' AND \"CanonicalCommandFingerprint\" IS NOT NULL AND length(\"CanonicalCommandFingerprint\") = 64 AND \"CanonicalCommandFingerprint\" NOT GLOB '*[^0-9A-F]*' AND (\"ExpectedActiveFingerprint\" IS NULL OR \"ExpectedActiveFingerprint\" IS NOT NULL AND length(\"ExpectedActiveFingerprint\") = 64 AND \"ExpectedActiveFingerprint\" NOT GLOB '*[^0-9A-F]*')");
                    table.CheckConstraint("CK_system_candidate_revision", "\"Revision\" > 0 AND \"ApplicationRevision\" > 0");
                    table.ForeignKey(
                        name: "FK_system_application_candidate_revision_operation_SourceOperationId",
                        column: x => x.SourceOperationId,
                        principalTable: "operation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_application_candidate_revision_system_application_revision_ApplicationId_ApplicationRevision",
                        columns: x => new { x.ApplicationId, x.ApplicationRevision },
                        principalTable: "system_application_revision",
                        principalColumns: new[] { "ApplicationId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_information_content_revision",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    ContentFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RetainedByOperationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_information_content_revision", x => new { x.Kind, x.Id, x.Revision });
                    table.CheckConstraint("CK_system_information_content_hash", "length(\"ContentFingerprint\") = 64 AND \"ContentFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_information_content_json", "json_valid(\"ContentJson\") AND length(CAST(\"ContentJson\" AS BLOB)) <= 65536");
                    table.CheckConstraint("CK_system_information_content_kind", "\"Kind\" IN ('source','record')");
                    table.CheckConstraint("CK_system_information_content_origin", "\"Origin\" IN ('baseline-retained','conditional-write')");
                    table.CheckConstraint("CK_system_information_content_revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_system_information_content_revision_operation_RetainedByOperationId",
                        column: x => x.RetainedByOperationId,
                        principalTable: "operation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_standing_grant_revision",
                columns: table => new
                {
                    GrantId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    GrantReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PrincipalReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StateSpaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PermissionsJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    ContentFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MaximumOperations = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revoked = table.Column<bool>(type: "INTEGER", nullable: false),
                    IssuedByOperationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_standing_grant_revision", x => new { x.GrantId, x.Revision });
                    table.CheckConstraint("CK_system_grant_budget", "\"MaximumOperations\" BETWEEN 1 AND 16");
                    table.CheckConstraint("CK_system_grant_definition_mode", "CASE WHEN json_valid(\"PermissionsJson\") THEN COALESCE(\r\n    json_type(\"PermissionsJson\") = 'object'\r\n    AND json_type(\"PermissionsJson\", '$.capabilities') = 'array'\r\n    AND json_type(\"PermissionsJson\", '$.effectKinds') = 'array'\r\n    AND json_type(\"PermissionsJson\", '$.definitions') = 'object'\r\n    AND json_type(\"PermissionsJson\", '$.definitions.exactIds') = 'array'\r\n    AND json_type(\"PermissionsJson\", '$.definitions.applicationOwnedNamespaces') = 'array'\r\n    AND json_array_length(\"PermissionsJson\", '$.definitions.exactIds') <= 64\r\n    AND json_array_length(\"PermissionsJson\", '$.definitions.applicationOwnedNamespaces') <= 16\r\n    AND ((json_extract(\"PermissionsJson\", '$.definitions.mode') = 'exactIds'\r\n          AND json_array_length(\"PermissionsJson\", '$.definitions.applicationOwnedNamespaces') = 0)\r\n        OR (json_extract(\"PermissionsJson\", '$.definitions.mode') = 'applicationOwned'\r\n          AND json_array_length(\"PermissionsJson\", '$.definitions.exactIds') = 0\r\n          AND json_array_length(\"PermissionsJson\", '$.definitions.applicationOwnedNamespaces') > 0)), 0)\r\nELSE 0 END");
                    table.CheckConstraint("CK_system_grant_hash", "length(\"ContentFingerprint\") = 64 AND \"ContentFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_grant_permissions", "json_valid(\"PermissionsJson\") AND length(CAST(\"PermissionsJson\" AS BLOB)) <= 16000");
                    table.CheckConstraint("CK_system_grant_revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_system_grant_scope", "(\"Scope\" = 'application' AND \"StateSpaceId\" IS NULL) OR (\"Scope\" = 'stateSpace' AND \"StateSpaceId\" IS NOT NULL AND length(\"StateSpaceId\") > 0)");
                    table.ForeignKey(
                        name: "FK_system_standing_grant_revision_operation_IssuedByOperationId",
                        column: x => x.IssuedByOperationId,
                        principalTable: "operation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_task_root_budget",
                columns: table => new
                {
                    root_task_id = table.Column<string>(type: "TEXT", nullable: false),
                    maximum_operations = table.Column<int>(type: "INTEGER", nullable: false),
                    consumed_operations = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_root_budget", x => x.root_task_id);
                    table.CheckConstraint("CK_system_task_root_budget_consumed", "\"consumed_operations\" BETWEEN 0 AND \"maximum_operations\"");
                    table.CheckConstraint("CK_system_task_root_budget_maximum", "\"maximum_operations\" BETWEEN 1 AND 16");
                });

            migrationBuilder.CreateTable(
                name: "system_application_candidate_document",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    CandidateId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    IdentityId = table.Column<long>(type: "INTEGER", nullable: false),
                    EvidenceVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_candidate_document", x => new { x.ApplicationId, x.CandidateId, x.Revision, x.Ordinal });
                    table.CheckConstraint("CK_system_candidate_document_ordinal", "\"Ordinal\" >= 0");
                    table.ForeignKey(
                        name: "FK_system_application_candidate_document_system_application_activation_document_evidence_IdentityId_EvidenceVersion",
                        columns: x => new { x.IdentityId, x.EvidenceVersion },
                        principalTable: "system_application_activation_document_evidence",
                        principalColumns: new[] { "IdentityId", "EvidenceVersion" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_application_candidate_document_system_application_activation_document_identity_ApplicationId_IdentityId",
                        columns: x => new { x.ApplicationId, x.IdentityId },
                        principalTable: "system_application_activation_document_identity",
                        principalColumns: new[] { "ApplicationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_application_candidate_document_system_application_candidate_revision_ApplicationId_CandidateId_Revision",
                        columns: x => new { x.ApplicationId, x.CandidateId, x.Revision },
                        principalTable: "system_application_candidate_revision",
                        principalColumns: new[] { "ApplicationId", "CandidateId", "Revision" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_application_candidate_validation",
                columns: table => new
                {
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    CandidateId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CandidateFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    GrantReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExpectedActiveFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DependencyFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreparationVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ManualPacketResultFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CanonicalCommandFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DependenciesJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    DependenciesComplete = table.Column<bool>(type: "INTEGER", nullable: false),
                    DependencyEvidenceReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PreparedEvidenceReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ReuseEvidenceReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    DiagnosticsJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    AlternativesJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_candidate_validation", x => x.OperationId);
                    table.UniqueConstraint("AK_system_application_candidate_validation_OperationId_ApplicationId_CandidateId_Revision", x => new { x.OperationId, x.ApplicationId, x.CandidateId, x.Revision });
                    table.CheckConstraint("CK_system_candidate_validation_dependencies", "\"DependenciesComplete\" IN (0,1) AND\r\nCASE WHEN json_valid(\"DependenciesJson\") THEN COALESCE(\r\n    json_type(\"DependenciesJson\") = 'array'\r\n    AND length(CAST(\"DependenciesJson\" AS BLOB)) <= 65536\r\n    AND json_array_length(\"DependenciesJson\") <= 64, 0)\r\nELSE 0 END");
                    table.CheckConstraint("CK_system_candidate_validation_expected_hash", "(\"ExpectedActiveFingerprint\" IS NULL OR \"ExpectedActiveFingerprint\" IS NOT NULL AND length(\"ExpectedActiveFingerprint\") = 64 AND \"ExpectedActiveFingerprint\" NOT GLOB '*[^0-9A-F]*') AND \"CandidateFingerprint\" IS NOT NULL AND length(\"CandidateFingerprint\") = 64 AND \"CandidateFingerprint\" NOT GLOB '*[^0-9A-F]*' AND \"DependencyFingerprint\" IS NOT NULL AND length(\"DependencyFingerprint\") = 64 AND \"DependencyFingerprint\" NOT GLOB '*[^0-9A-F]*' AND (\"ManualPacketResultFingerprint\" IS NULL OR \"ManualPacketResultFingerprint\" IS NOT NULL AND length(\"ManualPacketResultFingerprint\") = 64 AND \"ManualPacketResultFingerprint\" NOT GLOB '*[^0-9A-F]*') AND \"CanonicalCommandFingerprint\" IS NOT NULL AND length(\"CanonicalCommandFingerprint\") = 64 AND \"CanonicalCommandFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_system_candidate_validation_json", "json_valid(\"DiagnosticsJson\") AND json_valid(\"AlternativesJson\") AND length(CAST(\"DiagnosticsJson\" AS BLOB)) <= 16000 AND length(CAST(\"AlternativesJson\" AS BLOB)) <= 16000");
                    table.CheckConstraint("CK_system_candidate_validation_outcome", "\"Outcome\" IN ('valid','invalid','unavailable')");
                    table.CheckConstraint("CK_system_candidate_validation_revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_system_candidate_validation_valid_evidence", "\"Outcome\" <> 'valid' OR COALESCE(\r\n    length(trim(\"PreparationVersion\")) BETWEEN 1 AND 100\r\n    AND length(trim(\"PreparedEvidenceReference\")) BETWEEN 1 AND 200\r\n    AND length(\"ManualPacketResultFingerprint\") = 64\r\n    AND length(trim(\"ReuseEvidenceReference\")) BETWEEN 1 AND 200\r\n    AND length(trim(\"DependencyEvidenceReference\")) BETWEEN 1 AND 200\r\n    AND \"DependenciesComplete\" = 1, 0)");
                    table.ForeignKey(
                        name: "FK_system_application_candidate_validation_operation_OperationId",
                        column: x => x.OperationId,
                        principalTable: "operation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_application_candidate_validation_system_application_candidate_revision_ApplicationId_CandidateId_Revision",
                        columns: x => new { x.ApplicationId, x.CandidateId, x.Revision },
                        principalTable: "system_application_candidate_revision",
                        principalColumns: new[] { "ApplicationId", "CandidateId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_standing_grant_current",
                columns: table => new
                {
                    GrantId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_standing_grant_current", x => x.GrantId);
                    table.ForeignKey(
                        name: "FK_system_standing_grant_current_system_standing_grant_revision_GrantId_Revision",
                        columns: x => new { x.GrantId, x.Revision },
                        principalTable: "system_standing_grant_revision",
                        principalColumns: new[] { "GrantId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_task_lifecycle",
                columns: table => new
                {
                    task_id = table.Column<string>(type: "TEXT", nullable: false),
                    command_id = table.Column<string>(type: "TEXT", nullable: false),
                    payload_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    parent_task_id = table.Column<string>(type: "TEXT", nullable: true),
                    parent_command_id = table.Column<string>(type: "TEXT", nullable: true),
                    root_task_id = table.Column<string>(type: "TEXT", nullable: false),
                    parent_depth = table.Column<int>(type: "INTEGER", nullable: false),
                    propagate_cancellation = table.Column<int>(type: "INTEGER", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    principal_reference = table.Column<string>(type: "TEXT", nullable: false),
                    authentication_method = table.Column<string>(type: "TEXT", nullable: false),
                    application_id = table.Column<string>(type: "TEXT", nullable: false),
                    application_revision = table.Column<int>(type: "INTEGER", nullable: false),
                    application_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    base_applications_json = table.Column<string>(type: "TEXT", nullable: false),
                    state_space_id = table.Column<string>(type: "TEXT", nullable: false),
                    grant_reference = table.Column<string>(type: "TEXT", nullable: false),
                    state_revision = table.Column<string>(type: "TEXT", nullable: false),
                    execution_profile = table.Column<string>(type: "TEXT", nullable: false),
                    admitted_operations = table.Column<int>(type: "INTEGER", nullable: false),
                    deadline_utc = table.Column<string>(type: "TEXT", nullable: false),
                    definition_id = table.Column<string>(type: "TEXT", nullable: false),
                    definition_version = table.Column<int>(type: "INTEGER", nullable: false),
                    definition_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    input_json = table.Column<string>(type: "TEXT", nullable: false),
                    checkpoint_name = table.Column<string>(type: "TEXT", nullable: true),
                    completion_handler = table.Column<string>(type: "TEXT", nullable: true),
                    correlation_id = table.Column<string>(type: "TEXT", nullable: true),
                    checkpoint_state_json = table.Column<string>(type: "TEXT", nullable: true),
                    wake_json = table.Column<string>(type: "TEXT", nullable: true),
                    attempt_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    consecutive_failures = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    consumed_operations = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    fencing_counter = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    lease_owner = table.Column<string>(type: "TEXT", nullable: true),
                    lease_token = table.Column<string>(type: "TEXT", nullable: true),
                    lease_expires_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    next_attempt_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    cancel_requested = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    cancel_acknowledged = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    result_json = table.Column<string>(type: "TEXT", nullable: true),
                    completion_evidence_reference = table.Column<string>(type: "TEXT", nullable: true),
                    evidence_json = table.Column<string>(type: "TEXT", nullable: true),
                    error_code = table.Column<string>(type: "TEXT", nullable: true),
                    safe_message = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    completed_at_utc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_lifecycle", x => x.task_id);
                    table.UniqueConstraint("AK_system_task_lifecycle_command_id", x => x.command_id);
                    table.CheckConstraint("CK_system_task_lifecycle_admitted", "\"admitted_operations\" BETWEEN 1 AND 16");
                    table.CheckConstraint("CK_system_task_lifecycle_attempts", "\"attempt_count\" BETWEEN 0 AND 16");
                    table.CheckConstraint("CK_system_task_lifecycle_cancel_acknowledged", "\"cancel_acknowledged\" IN (0, 1)");
                    table.CheckConstraint("CK_system_task_lifecycle_cancel_requested", "\"cancel_requested\" IN (0, 1)");
                    table.CheckConstraint("CK_system_task_lifecycle_checkpoint", "((\"checkpoint_name\" IS NULL AND \"completion_handler\" IS NULL AND \"correlation_id\" IS NULL AND \"checkpoint_state_json\" IS NULL) OR (\"checkpoint_name\" IS NOT NULL AND \"completion_handler\" IS NOT NULL AND \"correlation_id\" IS NOT NULL AND \"checkpoint_state_json\" IS NOT NULL))");
                    table.CheckConstraint("CK_system_task_lifecycle_consumed", "\"consumed_operations\" BETWEEN 0 AND \"admitted_operations\"");
                    table.CheckConstraint("CK_system_task_lifecycle_failures", "\"consecutive_failures\" BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_system_task_lifecycle_fence", "\"fencing_counter\" >= 0");
                    table.CheckConstraint("CK_system_task_lifecycle_lease", "((\"state\" = 'running' AND \"lease_owner\" IS NOT NULL AND \"lease_token\" IS NOT NULL AND \"lease_expires_at_utc\" IS NOT NULL) OR (\"state\" <> 'running' AND \"lease_owner\" IS NULL AND \"lease_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL))");
                    table.CheckConstraint("CK_system_task_lifecycle_parent", "((\"parent_task_id\" IS NULL AND \"parent_depth\" = 0 AND \"task_id\" = \"root_task_id\") OR (\"parent_task_id\" IS NOT NULL AND \"parent_depth\" > 0 AND \"task_id\" <> \"root_task_id\"))");
                    table.CheckConstraint("CK_system_task_lifecycle_parent_depth", "\"parent_depth\" BETWEEN 0 AND 16");
                    table.CheckConstraint("CK_system_task_lifecycle_profile", "\"execution_profile\" IN ('read-only','atomic','workflow')");
                    table.CheckConstraint("CK_system_task_lifecycle_propagate", "\"propagate_cancellation\" IN (0, 1)");
                    table.CheckConstraint("CK_system_task_lifecycle_state", "\"state\" IN ('queued','running','waiting','retry','completed','failed','cancelled','indeterminate')");
                    table.ForeignKey(
                        name: "FK_system_task_lifecycle_system_task_lifecycle_parent_task_id",
                        column: x => x.parent_task_id,
                        principalTable: "system_task_lifecycle",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_task_lifecycle_system_task_root_budget_root_task_id",
                        column: x => x.root_task_id,
                        principalTable: "system_task_root_budget",
                        principalColumn: "root_task_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_application_candidate_publication",
                columns: table => new
                {
                    ActivationOperationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ValidationOperationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    CandidateId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_application_candidate_publication", x => x.ActivationOperationId);
                    table.ForeignKey(
                        name: "FK_system_application_candidate_publication_system_application_activation_receipt_ActivationOperationId",
                        column: x => x.ActivationOperationId,
                        principalTable: "system_application_activation_receipt",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_application_candidate_publication_system_application_candidate_revision_ApplicationId_CandidateId_Revision",
                        columns: x => new { x.ApplicationId, x.CandidateId, x.Revision },
                        principalTable: "system_application_candidate_revision",
                        principalColumns: new[] { "ApplicationId", "CandidateId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_application_candidate_publication_system_application_candidate_validation_ValidationOperationId_ApplicationId_CandidateId_Revision",
                        columns: x => new { x.ValidationOperationId, x.ApplicationId, x.CandidateId, x.Revision },
                        principalTable: "system_application_candidate_validation",
                        principalColumns: new[] { "OperationId", "ApplicationId", "CandidateId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_task_attempt",
                columns: table => new
                {
                    task_id = table.Column<string>(type: "TEXT", nullable: false),
                    ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    attempt_id = table.Column<string>(type: "TEXT", nullable: false),
                    fencing_counter = table.Column<long>(type: "INTEGER", nullable: false),
                    lease_token = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    failure_code = table.Column<string>(type: "TEXT", nullable: true),
                    safe_message = table.Column<string>(type: "TEXT", nullable: true),
                    started_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    completed_at_utc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_attempt", x => new { x.task_id, x.ordinal });
                    table.UniqueConstraint("AK_system_task_attempt_attempt_id", x => x.attempt_id);
                    table.CheckConstraint("CK_system_task_attempt_fence", "\"fencing_counter\" >= 1");
                    table.CheckConstraint("CK_system_task_attempt_ordinal", "\"ordinal\" BETWEEN 1 AND 16");
                    table.CheckConstraint("CK_system_task_attempt_state", "\"state\" IN ('running','waiting','retry','completed','failed','cancelled','indeterminate','lease-expired')");
                    table.ForeignKey(
                        name: "FK_system_task_attempt_system_task_lifecycle_task_id",
                        column: x => x.task_id,
                        principalTable: "system_task_lifecycle",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_task_checkpoint",
                columns: table => new
                {
                    task_id = table.Column<string>(type: "TEXT", nullable: false),
                    sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    checkpoint_name = table.Column<string>(type: "TEXT", nullable: false),
                    completion_handler = table.Column<string>(type: "TEXT", nullable: false),
                    correlation_id = table.Column<string>(type: "TEXT", nullable: false),
                    state_json = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    wake_json = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    woken_at_utc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_checkpoint", x => new { x.task_id, x.sequence });
                    table.UniqueConstraint("AK_system_task_checkpoint_task_id_correlation_id", x => new { x.task_id, x.correlation_id });
                    table.CheckConstraint("CK_system_task_checkpoint_completion", "((\"status\" = 'waiting' AND \"wake_json\" IS NULL AND \"woken_at_utc\" IS NULL) OR (\"status\" = 'woken' AND \"wake_json\" IS NOT NULL AND \"woken_at_utc\" IS NOT NULL))");
                    table.CheckConstraint("CK_system_task_checkpoint_sequence", "\"sequence\" BETWEEN 1 AND 16");
                    table.CheckConstraint("CK_system_task_checkpoint_status", "\"status\" IN ('waiting','woken')");
                    table.ForeignKey(
                        name: "FK_system_task_checkpoint_system_task_lifecycle_task_id",
                        column: x => x.task_id,
                        principalTable: "system_task_lifecycle",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_task_dependency",
                columns: table => new
                {
                    task_id = table.Column<string>(type: "TEXT", nullable: false),
                    dependency_task_id = table.Column<string>(type: "TEXT", nullable: false),
                    dependency_command_id = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_dependency", x => new { x.task_id, x.dependency_task_id });
                    table.CheckConstraint("CK_system_task_dependency_self", "\"task_id\" <> \"dependency_task_id\"");
                    table.ForeignKey(
                        name: "FK_system_task_dependency_system_task_lifecycle_dependency_task_id",
                        column: x => x.dependency_task_id,
                        principalTable: "system_task_lifecycle",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_task_dependency_system_task_lifecycle_task_id",
                        column: x => x.task_id,
                        principalTable: "system_task_lifecycle",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_task_host_call",
                columns: table => new
                {
                    task_id = table.Column<string>(type: "TEXT", nullable: false),
                    operation_id = table.Column<string>(type: "TEXT", nullable: false),
                    request_fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    request_json = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    completion_json = table.Column<string>(type: "TEXT", nullable: true),
                    attempt_id = table.Column<string>(type: "TEXT", nullable: false),
                    fencing_counter = table.Column<long>(type: "INTEGER", nullable: false),
                    started_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    completed_at_utc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_task_host_call", x => new { x.task_id, x.operation_id });
                    table.CheckConstraint("CK_system_task_host_call_completion", "((\"status\" = 'pending' AND \"completion_json\" IS NULL AND \"completed_at_utc\" IS NULL) OR (\"status\" = 'completed' AND \"completion_json\" IS NOT NULL AND \"completed_at_utc\" IS NOT NULL))");
                    table.CheckConstraint("CK_system_task_host_call_fence", "\"fencing_counter\" >= 1");
                    table.CheckConstraint("CK_system_task_host_call_status", "\"status\" IN ('pending','completed')");
                    table.ForeignKey(
                        name: "FK_system_task_host_call_system_task_lifecycle_task_id",
                        column: x => x.task_id,
                        principalTable: "system_task_lifecycle",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_candidate_document_ApplicationId_CandidateId_Revision_IdentityId",
                table: "system_application_candidate_document",
                columns: new[] { "ApplicationId", "CandidateId", "Revision", "IdentityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_application_candidate_document_ApplicationId_IdentityId",
                table: "system_application_candidate_document",
                columns: new[] { "ApplicationId", "IdentityId" });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_candidate_document_IdentityId_EvidenceVersion",
                table: "system_application_candidate_document",
                columns: new[] { "IdentityId", "EvidenceVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_candidate_publication_ApplicationId_CandidateId_Revision",
                table: "system_application_candidate_publication",
                columns: new[] { "ApplicationId", "CandidateId", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_candidate_publication_ValidationOperationId_ApplicationId_CandidateId_Revision",
                table: "system_application_candidate_publication",
                columns: new[] { "ValidationOperationId", "ApplicationId", "CandidateId", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_candidate_revision_ApplicationId_ApplicationRevision",
                table: "system_application_candidate_revision",
                columns: new[] { "ApplicationId", "ApplicationRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_system_application_candidate_revision_SourceOperationId",
                table: "system_application_candidate_revision",
                column: "SourceOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_application_candidate_validation_ApplicationId_CandidateId_Revision",
                table: "system_application_candidate_validation",
                columns: new[] { "ApplicationId", "CandidateId", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_system_information_content_revision_RetainedByOperationId",
                table: "system_information_content_revision",
                column: "RetainedByOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_system_standing_grant_current_GrantId_Revision",
                table: "system_standing_grant_current",
                columns: new[] { "GrantId", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_system_standing_grant_revision_GrantReference",
                table: "system_standing_grant_revision",
                column: "GrantReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_standing_grant_revision_IssuedByOperationId",
                table: "system_standing_grant_revision",
                column: "IssuedByOperationId");

            migrationBuilder.CreateIndex(
                name: "ix_system_task_dependency_target",
                table: "system_task_dependency",
                column: "dependency_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_system_task_lifecycle_claim",
                table: "system_task_lifecycle",
                columns: new[] { "state", "next_attempt_at_utc", "lease_expires_at_utc", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_system_task_lifecycle_correlation",
                table: "system_task_lifecycle",
                columns: new[] { "correlation_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_system_task_lifecycle_parent",
                table: "system_task_lifecycle",
                column: "parent_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_system_task_lifecycle_root",
                table: "system_task_lifecycle",
                column: "root_task_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An older host cannot represent this durable work and authoring history. Never
            // turn a schema rollback into deletion of recorded grants, work or retained data.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE __runtime_authoring_downgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT retained_runtime_state_prevents_downgrade CHECK (blocked = 0)
                );
                INSERT INTO __runtime_authoring_downgrade_guard (blocked)
                SELECT 1 WHERE EXISTS (SELECT 1 FROM system_application_candidate_document)
                    OR EXISTS (SELECT 1 FROM system_application_candidate_publication)
                    OR EXISTS (SELECT 1 FROM system_application_candidate_revision)
                    OR EXISTS (SELECT 1 FROM system_application_candidate_validation)
                    OR EXISTS (SELECT 1 FROM system_information_content_revision)
                    OR EXISTS (SELECT 1 FROM system_standing_grant_current)
                    OR EXISTS (SELECT 1 FROM system_standing_grant_revision)
                    OR EXISTS (SELECT 1 FROM system_task_attempt)
                    OR EXISTS (SELECT 1 FROM system_task_checkpoint)
                    OR EXISTS (SELECT 1 FROM system_task_dependency)
                    OR EXISTS (SELECT 1 FROM system_task_host_call)
                    OR EXISTS (SELECT 1 FROM system_task_lifecycle)
                    OR EXISTS (SELECT 1 FROM system_task_root_budget);
                DROP TABLE __runtime_authoring_downgrade_guard;
                """);

            migrationBuilder.DropTable(
                name: "system_application_candidate_document");

            migrationBuilder.DropTable(
                name: "system_application_candidate_publication");

            migrationBuilder.DropTable(
                name: "system_information_content_revision");

            migrationBuilder.DropTable(
                name: "system_standing_grant_current");

            migrationBuilder.DropTable(
                name: "system_task_attempt");

            migrationBuilder.DropTable(
                name: "system_task_checkpoint");

            migrationBuilder.DropTable(
                name: "system_task_dependency");

            migrationBuilder.DropTable(
                name: "system_task_host_call");

            migrationBuilder.DropTable(
                name: "system_application_candidate_validation");

            migrationBuilder.DropTable(
                name: "system_standing_grant_revision");

            migrationBuilder.DropTable(
                name: "system_task_lifecycle");

            migrationBuilder.DropTable(
                name: "system_application_candidate_revision");

            migrationBuilder.DropTable(
                name: "system_task_root_budget");
        }
    }
}
