using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.SystemTasks.Persistence;

/// <summary>EF model proposal for the reviewed durable lifecycle tables.</summary>
internal static class SystemTaskLifecycleModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        ConfigureRootBudget(modelBuilder);
        ConfigureLifecycle(modelBuilder);
        ConfigureDependency(modelBuilder);
        ConfigureAttempt(modelBuilder);
        ConfigureCheckpoint(modelBuilder);
        ConfigureHostCall(modelBuilder);
    }

    private static void ConfigureRootBudget(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemTaskRootBudgetRecord>(entity =>
        {
            entity.ToTable("system_task_root_budget", table =>
            {
                table.HasCheckConstraint("CK_system_task_root_budget_maximum",
                    "\"maximum_operations\" BETWEEN 1 AND 16");
                table.HasCheckConstraint("CK_system_task_root_budget_consumed",
                    "\"consumed_operations\" BETWEEN 0 AND \"maximum_operations\"");
            });
            entity.HasKey(value => value.RootTaskId);
            Text(entity.Property(value => value.RootTaskId), "root_task_id");
            Integer(entity.Property(value => value.MaximumOperations), "maximum_operations");
            Integer(entity.Property(value => value.ConsumedOperations), "consumed_operations").HasDefaultValue(0);
        });
    }

    private static void ConfigureLifecycle(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemTaskLifecycleRecord>(entity =>
        {
            entity.ToTable("system_task_lifecycle", table =>
            {
                table.HasCheckConstraint("CK_system_task_lifecycle_parent_depth", "\"parent_depth\" BETWEEN 0 AND 16");
                table.HasCheckConstraint("CK_system_task_lifecycle_propagate", "\"propagate_cancellation\" IN (0, 1)");
                table.HasCheckConstraint("CK_system_task_lifecycle_state", "\"state\" IN ('queued','running','waiting','retry','completed','failed','cancelled','indeterminate')");
                table.HasCheckConstraint("CK_system_task_lifecycle_profile", "\"execution_profile\" IN ('read-only','atomic','workflow')");
                table.HasCheckConstraint("CK_system_task_lifecycle_admitted", "\"admitted_operations\" BETWEEN 1 AND 16");
                table.HasCheckConstraint("CK_system_task_lifecycle_attempts", "\"attempt_count\" BETWEEN 0 AND 16");
                table.HasCheckConstraint("CK_system_task_lifecycle_failures", "\"consecutive_failures\" BETWEEN 0 AND 3");
                table.HasCheckConstraint("CK_system_task_lifecycle_consumed", "\"consumed_operations\" BETWEEN 0 AND \"admitted_operations\"");
                table.HasCheckConstraint("CK_system_task_lifecycle_fence", "\"fencing_counter\" >= 0");
                table.HasCheckConstraint("CK_system_task_lifecycle_cancel_requested", "\"cancel_requested\" IN (0, 1)");
                table.HasCheckConstraint("CK_system_task_lifecycle_cancel_acknowledged", "\"cancel_acknowledged\" IN (0, 1)");
                table.HasCheckConstraint("CK_system_task_lifecycle_activation_origin", "((\"activation_revision\" IS NULL AND \"activation_fingerprint\" IS NULL AND \"activation_application_revision\" IS NULL AND \"activation_application_fingerprint\" IS NULL) OR (\"activation_revision\" IS NOT NULL AND \"activation_revision\" > 0 AND \"activation_fingerprint\" IS NOT NULL AND length(\"activation_fingerprint\") = 64 AND \"activation_application_revision\" IS NOT NULL AND \"activation_application_revision\" > 0 AND \"activation_application_fingerprint\" IS NOT NULL AND length(\"activation_application_fingerprint\") = 64))");
                table.HasCheckConstraint("CK_system_task_lifecycle_parent", "((\"parent_task_id\" IS NULL AND \"parent_depth\" = 0 AND \"task_id\" = \"root_task_id\") OR (\"parent_task_id\" IS NOT NULL AND \"parent_depth\" > 0 AND \"task_id\" <> \"root_task_id\"))");
                table.HasCheckConstraint("CK_system_task_lifecycle_checkpoint", "((\"checkpoint_name\" IS NULL AND \"completion_handler\" IS NULL AND \"correlation_id\" IS NULL AND \"checkpoint_state_json\" IS NULL) OR (\"checkpoint_name\" IS NOT NULL AND \"completion_handler\" IS NOT NULL AND \"correlation_id\" IS NOT NULL AND \"checkpoint_state_json\" IS NOT NULL))");
                table.HasCheckConstraint("CK_system_task_lifecycle_lease", "((\"state\" = 'running' AND \"lease_owner\" IS NOT NULL AND \"lease_token\" IS NOT NULL AND \"lease_expires_at_utc\" IS NOT NULL) OR (\"state\" <> 'running' AND \"lease_owner\" IS NULL AND \"lease_token\" IS NULL AND \"lease_expires_at_utc\" IS NULL))");
            });
            entity.HasKey(value => value.TaskId);
            entity.HasAlternateKey(value => value.CommandId);
            Text(entity.Property(value => value.TaskId), "task_id");
            Text(entity.Property(value => value.CommandId), "command_id");
            Text(entity.Property(value => value.PayloadFingerprint), "payload_fingerprint");
            Text(entity.Property(value => value.ParentTaskId), "parent_task_id", required: false);
            Text(entity.Property(value => value.ParentCommandId), "parent_command_id", required: false);
            Text(entity.Property(value => value.RootTaskId), "root_task_id");
            Integer(entity.Property(value => value.ParentDepth), "parent_depth");
            Integer(entity.Property(value => value.PropagateCancellation), "propagate_cancellation");
            Text(entity.Property(value => value.State), "state");
            Text(entity.Property(value => value.PrincipalReference), "principal_reference");
            Text(entity.Property(value => value.AuthenticationMethod), "authentication_method");
            Text(entity.Property(value => value.ApplicationId), "application_id");
            Integer(entity.Property(value => value.ApplicationRevision), "application_revision");
            Text(entity.Property(value => value.ApplicationFingerprint), "application_fingerprint");
            entity.Property(value => value.ActivationRevision).HasColumnName("activation_revision").HasColumnType("INTEGER").IsRequired(false);
            Text(entity.Property(value => value.ActivationFingerprint), "activation_fingerprint", required: false);
            entity.Property(value => value.ActivationApplicationRevision).HasColumnName("activation_application_revision").HasColumnType("INTEGER").IsRequired(false);
            Text(entity.Property(value => value.ActivationApplicationFingerprint), "activation_application_fingerprint", required: false);
            Text(entity.Property(value => value.BaseApplicationsJson), "base_applications_json");
            Text(entity.Property(value => value.StateSpaceId), "state_space_id");
            Text(entity.Property(value => value.GrantReference), "grant_reference");
            Text(entity.Property(value => value.StateRevision), "state_revision");
            Text(entity.Property(value => value.ExecutionProfile), "execution_profile");
            Integer(entity.Property(value => value.AdmittedOperations), "admitted_operations");
            Text(entity.Property(value => value.DeadlineUtc), "deadline_utc");
            Text(entity.Property(value => value.DefinitionId), "definition_id");
            Integer(entity.Property(value => value.DefinitionVersion), "definition_version");
            Text(entity.Property(value => value.DefinitionFingerprint), "definition_fingerprint");
            Text(entity.Property(value => value.InputJson), "input_json");
            Text(entity.Property(value => value.CheckpointName), "checkpoint_name", required: false);
            Text(entity.Property(value => value.CompletionHandler), "completion_handler", required: false);
            Text(entity.Property(value => value.CorrelationId), "correlation_id", required: false);
            Text(entity.Property(value => value.CheckpointStateJson), "checkpoint_state_json", required: false);
            Text(entity.Property(value => value.WakeJson), "wake_json", required: false);
            Integer(entity.Property(value => value.AttemptCount), "attempt_count").HasDefaultValue(0);
            Integer(entity.Property(value => value.ConsecutiveFailures), "consecutive_failures").HasDefaultValue(0);
            Integer(entity.Property(value => value.ConsumedOperations), "consumed_operations").HasDefaultValue(0);
            Integer(entity.Property(value => value.FencingCounter), "fencing_counter").HasDefaultValue(0L);
            Text(entity.Property(value => value.LeaseOwner), "lease_owner", required: false);
            Text(entity.Property(value => value.LeaseToken), "lease_token", required: false);
            Text(entity.Property(value => value.LeaseExpiresAtUtc), "lease_expires_at_utc", required: false);
            Text(entity.Property(value => value.NextAttemptAtUtc), "next_attempt_at_utc", required: false);
            Integer(entity.Property(value => value.CancelRequested), "cancel_requested").HasDefaultValue(0);
            Integer(entity.Property(value => value.CancelAcknowledged), "cancel_acknowledged").HasDefaultValue(0);
            Text(entity.Property(value => value.ResultJson), "result_json", required: false);
            Text(entity.Property(value => value.CompletionEvidenceReference), "completion_evidence_reference", required: false);
            Text(entity.Property(value => value.EvidenceJson), "evidence_json", required: false);
            Text(entity.Property(value => value.ErrorCode), "error_code", required: false);
            Text(entity.Property(value => value.SafeMessage), "safe_message", required: false);
            Text(entity.Property(value => value.CreatedAtUtc), "created_at_utc");
            Text(entity.Property(value => value.UpdatedAtUtc), "updated_at_utc");
            Text(entity.Property(value => value.CompletedAtUtc), "completed_at_utc", required: false);

            entity.HasIndex(value => new { value.State, value.NextAttemptAtUtc, value.LeaseExpiresAtUtc, value.CreatedAtUtc })
                .HasDatabaseName("ix_system_task_lifecycle_claim");
            entity.HasIndex(value => value.ParentTaskId).HasDatabaseName("ix_system_task_lifecycle_parent");
            entity.HasIndex(value => value.RootTaskId).HasDatabaseName("ix_system_task_lifecycle_root");
            entity.HasIndex(value => new { value.CorrelationId, value.State }).HasDatabaseName("ix_system_task_lifecycle_correlation");
            entity.HasOne<SystemTaskLifecycleRecord>().WithMany().HasForeignKey(value => value.ParentTaskId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SystemTaskRootBudgetRecord>().WithMany().HasForeignKey(value => value.RootTaskId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureDependency(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemTaskDependencyRecord>(entity =>
        {
            entity.ToTable("system_task_dependency", table =>
                table.HasCheckConstraint("CK_system_task_dependency_self", "\"task_id\" <> \"dependency_task_id\""));
            entity.HasKey(value => new { value.TaskId, value.DependencyTaskId });
            Text(entity.Property(value => value.TaskId), "task_id");
            Text(entity.Property(value => value.DependencyTaskId), "dependency_task_id");
            Text(entity.Property(value => value.DependencyCommandId), "dependency_command_id");
            entity.HasOne<SystemTaskLifecycleRecord>().WithMany().HasForeignKey(value => value.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<SystemTaskLifecycleRecord>().WithMany().HasForeignKey(value => value.DependencyTaskId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(value => value.DependencyTaskId)
                .HasDatabaseName("ix_system_task_dependency_target");
        });
    }

    private static void ConfigureAttempt(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemTaskAttemptRecord>(entity =>
        {
            entity.ToTable("system_task_attempt", table =>
            {
                table.HasCheckConstraint("CK_system_task_attempt_ordinal", "\"ordinal\" BETWEEN 1 AND 16");
                table.HasCheckConstraint("CK_system_task_attempt_fence", "\"fencing_counter\" >= 1");
                table.HasCheckConstraint("CK_system_task_attempt_state", "\"state\" IN ('running','waiting','retry','completed','failed','cancelled','indeterminate','lease-expired')");
            });
            entity.HasKey(value => new { value.TaskId, value.Ordinal });
            entity.HasAlternateKey(value => value.AttemptId);
            Text(entity.Property(value => value.TaskId), "task_id");
            Text(entity.Property(value => value.AttemptId), "attempt_id");
            Integer(entity.Property(value => value.Ordinal), "ordinal");
            Integer(entity.Property(value => value.FencingCounter), "fencing_counter");
            Text(entity.Property(value => value.LeaseToken), "lease_token");
            Text(entity.Property(value => value.State), "state");
            Text(entity.Property(value => value.FailureCode), "failure_code", required: false);
            Text(entity.Property(value => value.SafeMessage), "safe_message", required: false);
            Text(entity.Property(value => value.StartedAtUtc), "started_at_utc");
            Text(entity.Property(value => value.CompletedAtUtc), "completed_at_utc", required: false);
            entity.HasOne<SystemTaskLifecycleRecord>().WithMany().HasForeignKey(value => value.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureCheckpoint(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemTaskCheckpointRecord>(entity =>
        {
            entity.ToTable("system_task_checkpoint", table =>
            {
                table.HasCheckConstraint("CK_system_task_checkpoint_sequence", "\"sequence\" BETWEEN 1 AND 16");
                table.HasCheckConstraint("CK_system_task_checkpoint_status", "\"status\" IN ('waiting','woken')");
                table.HasCheckConstraint("CK_system_task_checkpoint_completion", "((\"status\" = 'waiting' AND \"wake_json\" IS NULL AND \"woken_at_utc\" IS NULL) OR (\"status\" = 'woken' AND \"wake_json\" IS NOT NULL AND \"woken_at_utc\" IS NOT NULL))");
            });
            entity.HasKey(value => new { value.TaskId, value.Sequence });
            entity.HasAlternateKey(value => new { value.TaskId, value.CorrelationId });
            Text(entity.Property(value => value.TaskId), "task_id");
            Integer(entity.Property(value => value.Sequence), "sequence");
            Text(entity.Property(value => value.CheckpointName), "checkpoint_name");
            Text(entity.Property(value => value.CompletionHandler), "completion_handler");
            Text(entity.Property(value => value.CorrelationId), "correlation_id");
            Text(entity.Property(value => value.StateJson), "state_json");
            Text(entity.Property(value => value.Status), "status");
            Text(entity.Property(value => value.WakeJson), "wake_json", required: false);
            Text(entity.Property(value => value.CreatedAtUtc), "created_at_utc");
            Text(entity.Property(value => value.WokenAtUtc), "woken_at_utc", required: false);
            entity.HasOne<SystemTaskLifecycleRecord>().WithMany().HasForeignKey(value => value.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureHostCall(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemTaskHostCallRecord>(entity =>
        {
            entity.ToTable("system_task_host_call", table =>
            {
                table.HasCheckConstraint("CK_system_task_host_call_status", "\"status\" IN ('pending','completed')");
                table.HasCheckConstraint("CK_system_task_host_call_fence", "\"fencing_counter\" >= 1");
                table.HasCheckConstraint("CK_system_task_host_call_completion", "((\"status\" = 'pending' AND \"completion_json\" IS NULL AND \"completed_at_utc\" IS NULL) OR (\"status\" = 'completed' AND \"completion_json\" IS NOT NULL AND \"completed_at_utc\" IS NOT NULL))");
            });
            entity.HasKey(value => new { value.TaskId, value.OperationId });
            Text(entity.Property(value => value.TaskId), "task_id");
            Text(entity.Property(value => value.OperationId), "operation_id");
            Text(entity.Property(value => value.RequestFingerprint), "request_fingerprint");
            Text(entity.Property(value => value.RequestJson), "request_json");
            Text(entity.Property(value => value.Status), "status");
            Text(entity.Property(value => value.CompletionJson), "completion_json", required: false);
            Text(entity.Property(value => value.AttemptId), "attempt_id");
            Integer(entity.Property(value => value.FencingCounter), "fencing_counter");
            Text(entity.Property(value => value.StartedAtUtc), "started_at_utc");
            Text(entity.Property(value => value.CompletedAtUtc), "completed_at_utc", required: false);
            entity.HasOne<SystemTaskLifecycleRecord>().WithMany().HasForeignKey(value => value.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> Text<T>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> property, string name,
        bool required = true)
    {
        property.HasColumnName(name).HasColumnType("TEXT");
        if (required) property.IsRequired();
        return property;
    }

    private static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> Integer<T>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> property, string name)
    {
        property.HasColumnName(name).HasColumnType("INTEGER").IsRequired();
        return property;
    }

}
