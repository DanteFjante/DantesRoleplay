using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DantesRoleplay.SystemTasks.Persistence;

internal static class SystemTaskAiAccountingModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        ConfigureCeiling(modelBuilder);
        ConfigureReservation(modelBuilder);
        ConfigureAncestor(modelBuilder);
        ConfigureEvidence(modelBuilder);
    }

    private static void ConfigureCeiling(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemTaskAiCeilingRecord>(entity =>
        {
            entity.ToTable("system_task_ai_ceiling", table =>
            {
                table.HasCheckConstraint("CK_system_task_ai_ceiling_task_id", "length(\"task_id\") BETWEEN 1 AND 200");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_enrollment_fingerprint", "length(\"enrollment_fingerprint\") = 64");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_profile_id", "length(\"profile_id\") BETWEEN 1 AND 200");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_profile_version", "\"profile_version\" > 0");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_profile_fingerprint", "length(\"profile_fingerprint\") = 64");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_grant_fingerprint", "length(\"grant_fingerprint\") = 64");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_grant_reference", "length(\"grant_reference\") BETWEEN 1 AND 200");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_grant_revision", "length(\"grant_revision\") BETWEEN 1 AND 1024");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_definition_id", "length(\"definition_id\") BETWEEN 1 AND 200");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_definition_version", "\"definition_version\" > 0");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_definition_fingerprint", "length(\"definition_fingerprint\") = 64");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_schema_fingerprint", "length(\"output_schema_fingerprint\") = 64");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_mode",
                    "\"mode\" IN ('measured-stop','hard-cap')");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_provider_tokens", "\"maximum_provider_tokens\" BETWEEN 1 AND 131072");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_tool_calls", "\"maximum_tool_calls\" BETWEEN 0 AND 16");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_concurrency", "\"maximum_concurrent_provider_requests\" BETWEEN 1 AND 4");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_deadline", "length(\"deadline_utc\") BETWEEN 1 AND 40");
                table.HasCheckConstraint("CK_system_task_ai_ceiling_created_at", "length(\"created_at_utc\") BETWEEN 1 AND 40");
            });
            entity.HasKey(value => value.TaskId);
            Text(entity.Property(value => value.TaskId), "task_id");
            Text(entity.Property(value => value.EnrollmentFingerprint), "enrollment_fingerprint");
            Text(entity.Property(value => value.ProfileId), "profile_id");
            Integer(entity.Property(value => value.ProfileVersion), "profile_version");
            Text(entity.Property(value => value.ProfileFingerprint), "profile_fingerprint");
            Text(entity.Property(value => value.GrantReference), "grant_reference");
            Text(entity.Property(value => value.GrantRevision), "grant_revision");
            Text(entity.Property(value => value.GrantFingerprint), "grant_fingerprint");
            Text(entity.Property(value => value.DefinitionId), "definition_id");
            Integer(entity.Property(value => value.DefinitionVersion), "definition_version");
            Text(entity.Property(value => value.DefinitionFingerprint), "definition_fingerprint");
            Text(entity.Property(value => value.OutputSchemaFingerprint), "output_schema_fingerprint");
            Text(entity.Property(value => value.Mode), "mode");
            Integer(entity.Property(value => value.MaximumProviderTokens), "maximum_provider_tokens");
            Integer(entity.Property(value => value.MaximumToolCalls), "maximum_tool_calls");
            Integer(entity.Property(value => value.MaximumConcurrentProviderRequests), "maximum_concurrent_provider_requests");
            Text(entity.Property(value => value.DeadlineUtc), "deadline_utc");
            Text(entity.Property(value => value.CreatedAtUtc), "created_at_utc");
            entity.HasOne<SystemTaskLifecycleRecord>().WithOne().HasForeignKey<SystemTaskAiCeilingRecord>(value => value.TaskId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureReservation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemTaskAiReservationRecord>(entity =>
        {
            entity.ToTable("system_task_ai_reservation", table =>
            {
                table.HasCheckConstraint("CK_system_task_ai_reservation_record_reference", "length(\"record_reference\") BETWEEN 1 AND 200");
                table.HasCheckConstraint("CK_system_task_ai_reservation_task_id", "length(\"task_id\") BETWEEN 1 AND 200");
                table.HasCheckConstraint("CK_system_task_ai_reservation_reservation_id", "length(\"reservation_id\") BETWEEN 1 AND 128");
                table.HasCheckConstraint("CK_system_task_ai_reservation_request_fingerprint",
                    "length(\"request_fingerprint\") = 64");
                table.HasCheckConstraint("CK_system_task_ai_reservation_attempt_id", "length(\"attempt_id\") BETWEEN 1 AND 128");
                table.HasCheckConstraint("CK_system_task_ai_reservation_fence", "\"fencing_counter\" >= 1");
                table.HasCheckConstraint("CK_system_task_ai_reservation_lease_token", "length(\"lease_token\") BETWEEN 1 AND 128");
                table.HasCheckConstraint("CK_system_task_ai_reservation_lease_expiry", "length(\"lease_expires_at_utc\") BETWEEN 1 AND 40");
                table.HasCheckConstraint("CK_system_task_ai_reservation_deadline", "length(\"deadline_utc\") BETWEEN 1 AND 40");
                table.HasCheckConstraint("CK_system_task_ai_reservation_requested",
                    "\"requested_provider_tokens\" BETWEEN 0 AND 131072");
                table.HasCheckConstraint("CK_system_task_ai_reservation_reserved_tokens",
                    "\"reserved_provider_tokens\" BETWEEN 0 AND \"requested_provider_tokens\"");
                table.HasCheckConstraint("CK_system_task_ai_reservation_reserved_tools",
                    "\"reserved_tool_calls\" BETWEEN 0 AND 16");
                table.HasCheckConstraint("CK_system_task_ai_reservation_mode",
                    "\"mode\" IN ('measured-stop','hard-cap')");
                table.HasCheckConstraint("CK_system_task_ai_reservation_status",
                    "\"status\" IN ('reserved','unknown','settled','exceeded')");
                table.HasCheckConstraint("CK_system_task_ai_reservation_charged_tokens", "\"charged_provider_tokens\" >= 0");
                table.HasCheckConstraint("CK_system_task_ai_reservation_charged_tools", "\"charged_tool_calls\" >= 0");
                table.HasCheckConstraint("CK_system_task_ai_reservation_settled_sequence",
                    "\"settled_evidence_sequence\" BETWEEN 1 AND 16");
                table.HasCheckConstraint("CK_system_task_ai_reservation_dispatch_shape",
                    "((\"requested_provider_tokens\" > 0 AND \"reserved_provider_tokens\" > 0 AND \"reserved_tool_calls\" = 0) OR (\"requested_provider_tokens\" = 0 AND \"reserved_provider_tokens\" = 0 AND \"reserved_tool_calls\" = 1))");
                table.HasCheckConstraint("CK_system_task_ai_reservation_created_at", "length(\"created_at_utc\") BETWEEN 1 AND 40");
                table.HasCheckConstraint("CK_system_task_ai_reservation_updated_at", "length(\"updated_at_utc\") BETWEEN 1 AND 40");
            });
            entity.HasKey(value => value.RecordReference);
            entity.HasAlternateKey(value => new { value.TaskId, value.ReservationId });
            Text(entity.Property(value => value.RecordReference), "record_reference");
            Text(entity.Property(value => value.TaskId), "task_id");
            Text(entity.Property(value => value.ReservationId), "reservation_id");
            Text(entity.Property(value => value.RequestFingerprint), "request_fingerprint");
            Text(entity.Property(value => value.AttemptId), "attempt_id");
            Integer(entity.Property(value => value.FencingCounter), "fencing_counter");
            Text(entity.Property(value => value.LeaseToken), "lease_token");
            Text(entity.Property(value => value.LeaseExpiresAtUtc), "lease_expires_at_utc");
            Text(entity.Property(value => value.DeadlineUtc), "deadline_utc");
            Integer(entity.Property(value => value.RequestedProviderTokens), "requested_provider_tokens");
            Integer(entity.Property(value => value.ReservedProviderTokens), "reserved_provider_tokens");
            Integer(entity.Property(value => value.ReservedToolCalls), "reserved_tool_calls");
            Text(entity.Property(value => value.Mode), "mode");
            Text(entity.Property(value => value.Status), "status");
            Integer(entity.Property(value => value.ChargedProviderTokens), "charged_provider_tokens").HasDefaultValue(0L);
            Integer(entity.Property(value => value.ChargedToolCalls), "charged_tool_calls").HasDefaultValue(0L);
            Integer(entity.Property(value => value.SettledEvidenceSequence), "settled_evidence_sequence", required: false);
            Text(entity.Property(value => value.CreatedAtUtc), "created_at_utc");
            Text(entity.Property(value => value.UpdatedAtUtc), "updated_at_utc");
            entity.HasIndex(value => value.AttemptId).HasDatabaseName("ix_system_task_ai_reservation_attempt");
            entity.HasOne<SystemTaskAiCeilingRecord>().WithMany().HasForeignKey(value => value.TaskId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SystemTaskAttemptRecord>().WithMany().HasForeignKey(value => value.AttemptId)
                .HasPrincipalKey(value => value.AttemptId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAncestor(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemTaskAiReservationAncestorRecord>(entity =>
        {
            entity.ToTable("system_task_ai_reservation_ancestor", table =>
            {
                table.HasCheckConstraint("CK_system_task_ai_reservation_ancestor_record", "length(\"record_reference\") BETWEEN 1 AND 200");
                table.HasCheckConstraint("CK_system_task_ai_reservation_ancestor_task", "length(\"ancestor_task_id\") BETWEEN 1 AND 200");
            });
            entity.HasKey(value => new { value.RecordReference, value.AncestorTaskId });
            Text(entity.Property(value => value.RecordReference), "record_reference");
            Text(entity.Property(value => value.AncestorTaskId), "ancestor_task_id");
            entity.HasIndex(value => value.AncestorTaskId).HasDatabaseName("ix_system_task_ai_reservation_ancestor_task");
            entity.HasOne<SystemTaskAiReservationRecord>().WithMany().HasForeignKey(value => value.RecordReference)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SystemTaskAiCeilingRecord>().WithMany().HasForeignKey(value => value.AncestorTaskId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureEvidence(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemTaskAiDispatchEvidenceRecord>(entity =>
        {
            entity.ToTable("system_task_ai_dispatch_evidence", table =>
            {
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_record", "length(\"record_reference\") BETWEEN 1 AND 200");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_sequence", "\"sequence\" BETWEEN 0 AND 16");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_event", "length(\"event_reference\") BETWEEN 1 AND 200");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_payload_fingerprint", "length(\"payload_fingerprint\") = 64");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_request_fingerprint", "length(\"request_fingerprint\") = 64");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_profile_fingerprint", "length(\"profile_fingerprint\") = 64");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_schema_fingerprint", "length(\"schema_fingerprint\") = 64");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_response_fingerprint", "\"response_fingerprint\" IS NULL OR length(\"response_fingerprint\") = 64");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_kind", "\"kind\" IN ('dispatch','usage')");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_dispatch_kind", "\"dispatch_kind\" IN ('provider','tool')");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_provider", "\"provider_id\" IS NULL OR length(\"provider_id\") BETWEEN 1 AND 200");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_model", "\"model_id\" IS NULL OR length(\"model_id\") BETWEEN 1 AND 200");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_input_tokens", "\"input_tokens\" IS NULL OR \"input_tokens\" >= 0");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_output_tokens", "\"output_tokens\" IS NULL OR \"output_tokens\" >= 0");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_total_tokens", "\"total_tokens\" IS NULL OR \"total_tokens\" >= 0");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_tool_calls", "\"observed_tool_calls\" >= 0");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_complete", "\"is_complete\" IN (0, 1)");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_completion_kind",
                    "\"completion_kind\" IS NULL OR \"completion_kind\" IN ('returned','threw','cancelled','not-started')");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_target",
                    "((\"dispatch_kind\" = 'provider' AND \"provider_id\" IS NOT NULL AND \"model_id\" IS NOT NULL) OR (\"dispatch_kind\" = 'tool' AND \"provider_id\" IS NULL AND \"model_id\" IS NULL))");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_shape",
                    "((\"kind\" = 'dispatch' AND \"sequence\" = 0 AND \"request_json\" IS NOT NULL AND length(CAST(\"request_json\" AS BLOB)) <= 65536 AND \"response_fingerprint\" IS NULL AND \"input_tokens\" IS NULL AND \"output_tokens\" IS NULL AND \"total_tokens\" IS NULL AND \"observed_tool_calls\" = 0 AND \"is_complete\" = 0 AND \"completion_kind\" IS NULL) OR (\"kind\" = 'usage' AND \"sequence\" BETWEEN 1 AND 16 AND \"request_json\" IS NULL AND \"response_fingerprint\" IS NOT NULL AND \"completion_kind\" IS NOT NULL))");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_total",
                    "(\"total_tokens\" IS NULL OR (\"total_tokens\" >= COALESCE(\"output_tokens\", 0) AND COALESCE(\"input_tokens\", 0) <= \"total_tokens\" - COALESCE(\"output_tokens\", 0)))");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_sum",
                    "(\"input_tokens\" IS NULL OR \"output_tokens\" IS NULL OR \"input_tokens\" <= 9223372036854775807 - \"output_tokens\")");
                table.HasCheckConstraint("CK_system_task_ai_dispatch_evidence_observed_at", "length(\"observed_at_utc\") BETWEEN 1 AND 40");
            });
            entity.HasKey(value => new { value.RecordReference, value.Sequence });
            entity.HasAlternateKey(value => value.EventReference);
            Text(entity.Property(value => value.RecordReference), "record_reference");
            Integer(entity.Property(value => value.Sequence), "sequence");
            Text(entity.Property(value => value.EventReference), "event_reference");
            Text(entity.Property(value => value.PayloadFingerprint), "payload_fingerprint");
            Text(entity.Property(value => value.Kind), "kind");
            Text(entity.Property(value => value.DispatchKind), "dispatch_kind");
            Text(entity.Property(value => value.RequestFingerprint), "request_fingerprint");
            Text(entity.Property(value => value.ProviderId), "provider_id", required: false);
            Text(entity.Property(value => value.ModelId), "model_id", required: false);
            Text(entity.Property(value => value.ProfileFingerprint), "profile_fingerprint");
            Text(entity.Property(value => value.SchemaFingerprint), "schema_fingerprint");
            Text(entity.Property(value => value.RequestJson), "request_json", required: false);
            Text(entity.Property(value => value.ResponseFingerprint), "response_fingerprint", required: false);
            Integer(entity.Property(value => value.InputTokens), "input_tokens", required: false);
            Integer(entity.Property(value => value.OutputTokens), "output_tokens", required: false);
            Integer(entity.Property(value => value.TotalTokens), "total_tokens", required: false);
            Integer(entity.Property(value => value.ObservedToolCalls), "observed_tool_calls").HasDefaultValue(0L);
            Integer(entity.Property(value => value.IsComplete), "is_complete").HasDefaultValue(0);
            Text(entity.Property(value => value.CompletionKind), "completion_kind", required: false);
            Text(entity.Property(value => value.ObservedAtUtc), "observed_at_utc");
            entity.HasOne<SystemTaskAiReservationRecord>().WithMany().HasForeignKey(value => value.RecordReference)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static PropertyBuilder<T> Text<T>(PropertyBuilder<T> property, string name, bool required = true)
    {
        property.HasColumnName(name).HasColumnType("TEXT");
        if (required) property.IsRequired();
        return property;
    }

    private static PropertyBuilder<T> Integer<T>(PropertyBuilder<T> property, string name, bool required = true)
    {
        property.HasColumnName(name).HasColumnType("INTEGER");
        if (required) property.IsRequired();
        return property;
    }
}
