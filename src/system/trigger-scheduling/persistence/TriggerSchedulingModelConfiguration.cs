using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Notifications;
using DantesRoleplay.TriggerScheduling;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Owns the relational model for trigger registration, execution, and evidence.</summary>
internal static class TriggerSchedulingModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        const string hash = "length(\"{0}\") = 64 AND \"{0}\" NOT GLOB '*[^0-9A-F]*'";
        const string application = "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system'";
        const string identifier = "length(\"Id\") BETWEEN 3 AND 200";

        modelBuilder.Entity<TriggerObservationStructureRecord>(entity =>
        {
            entity.ToTable("trigger_observation_structure", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_structure_values", $"{application} AND {identifier} AND \"Version\" > 0 AND \"Status\" IN ('active', 'retired') AND \"DataClassification\" IN ('general', 'privacy-minimized-signal', 'raw-location', 'third-party-notification-content') AND length(\"SchemaProfileId\") BETWEEN 1 AND 200 AND length(\"NormalizedSchema\") BETWEEN 2 AND 65536 AND json_valid(\"NormalizedSchema\") AND json_type(\"NormalizedSchema\") = 'object' AND length(\"Description\") BETWEEN 1 AND 1024");
                table.HasCheckConstraint("CK_trigger_observation_structure_hash", string.Format(hash, "SchemaHash"));
            });
            entity.HasKey(row => new { row.ApplicationId, row.Id, row.Version });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.Property(row => row.SchemaProfileId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.NormalizedSchema).HasMaxLength(65536).IsRequired();
            entity.Property(row => row.SchemaHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Description).HasMaxLength(1024).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.DataClassification).HasMaxLength(40).IsRequired();
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(row => row.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerObservationStructureCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_observation_structure_current", table =>
                table.HasCheckConstraint("CK_trigger_observation_structure_current_version", "\"CurrentVersion\" > 0"));
            entity.HasKey(row => new { row.ApplicationId, row.Id });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.HasOne<TriggerObservationStructureRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.Id, Version = row.CurrentVersion }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerObservationSourceRecord>(entity =>
        {
            entity.ToTable("trigger_observation_source", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_source_values", $"{application} AND {identifier} AND \"Version\" > 0 AND \"Status\" IN ('enabled', 'disabled') AND \"ReplayWindowSeconds\" BETWEEN 1 AND 604800 AND \"RequestsPerMinute\" BETWEEN 1 AND 10");
            });
            entity.HasKey(row => new { row.ApplicationId, row.Id, row.Version });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(row => row.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerObservationSourceCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_observation_source_current", table =>
                table.HasCheckConstraint("CK_trigger_observation_source_current_version", "\"CurrentVersion\" > 0"));
            entity.HasKey(row => new { row.ApplicationId, row.Id });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.HasOne<TriggerObservationSourceRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.Id, Version = row.CurrentVersion }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerObservationSourceStructureRecord>(entity =>
        {
            entity.ToTable("trigger_observation_source_structure", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_source_structure_versions", "\"SourceVersion\" > 0 AND \"StructureVersion\" > 0");
                table.HasCheckConstraint("CK_trigger_observation_source_structure_ids", "length(\"SourceId\") BETWEEN 3 AND 200 AND length(\"StructureId\") BETWEEN 3 AND 200");
            });
            entity.HasKey(row => new { row.ApplicationId, row.SourceId, row.SourceVersion, row.StructureId, row.StructureVersion });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.SourceId).HasMaxLength(200);
            entity.Property(row => row.StructureId).HasMaxLength(200);
            entity.HasOne(row => row.Source).WithMany(row => row.AllowedStructures)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.SourceId, Version = row.SourceVersion }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationStructureRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.StructureId, Version = row.StructureVersion }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerObservationSourcePrincipalRecord>(entity =>
        {
            entity.ToTable("trigger_observation_source_principal", table =>
                table.HasCheckConstraint("CK_trigger_observation_source_principal_id",
                    "length(\"PrincipalId\") = 74 AND substr(\"PrincipalId\", 1, 10) = 'principal.' AND substr(\"PrincipalId\", 11) NOT GLOB '*[^0-9a-f]*'"));
            entity.HasKey(row => new { row.ApplicationId, row.SourceId, row.SourceVersion, row.PrincipalId });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.SourceId).HasMaxLength(200);
            entity.Property(row => row.PrincipalId).HasMaxLength(74);
            entity.HasOne(row => row.Source).WithMany(row => row.AllowedPrincipals)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.SourceId, Version = row.SourceVersion }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PhoneCompanionDeviceRecord>(entity =>
        {
            entity.ToTable("trigger_phone_device", table =>
            {
                table.HasCheckConstraint("CK_trigger_phone_device_values",
                    $"{application} AND length(\"DeviceId\") = 45 AND substr(\"DeviceId\", 1, 13) = 'phone-device.' AND substr(\"DeviceId\", 14) NOT GLOB '*[^0-9a-f]*' AND length(\"PrincipalId\") = 74 AND substr(\"PrincipalId\", 1, 10) = 'principal.' AND substr(\"PrincipalId\", 11) NOT GLOB '*[^0-9a-f]*' AND length(\"SourceId\") BETWEEN 3 AND 200 AND \"SourceVersion\" > 0 AND \"PermissionProfile\" = 'privacy-minimized-signals'");
                table.HasCheckConstraint("CK_trigger_phone_device_verifier", string.Format(hash, "CredentialVerifier"));
            });
            entity.HasKey(row => new { row.ApplicationId, row.DeviceId });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.DeviceId).HasMaxLength(45);
            entity.Property(row => row.PrincipalId).HasMaxLength(74).IsRequired();
            entity.Property(row => row.SourceId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.CredentialVerifier).HasMaxLength(64).IsRequired();
            entity.Property(row => row.PermissionProfile).HasMaxLength(40).IsRequired();
            entity.HasIndex(row => row.PrincipalId).IsUnique();
            entity.HasIndex(row => row.CredentialVerifier).IsUnique();
            entity.HasOne<TriggerObservationSourceRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.SourceId, Version = row.SourceVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PhoneCompanionDeviceStructureRecord>(entity =>
        {
            entity.ToTable("trigger_phone_device_structure", table =>
            {
                table.HasCheckConstraint("CK_trigger_phone_device_structure_values",
                    $"{application} AND length(\"DeviceId\") = 45 AND substr(\"DeviceId\", 1, 13) = 'phone-device.' AND \"Ordinal\" BETWEEN 0 AND 7 AND length(\"StructureId\") BETWEEN 3 AND 200 AND \"StructureVersion\" > 0");
                table.HasCheckConstraint("CK_trigger_phone_device_structure_hash", string.Format(hash, "StructureHash"));
            });
            entity.HasKey(row => new { row.ApplicationId, row.DeviceId, row.Ordinal });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.DeviceId).HasMaxLength(45);
            entity.Property(row => row.StructureId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.StructureHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new { row.ApplicationId, row.DeviceId, row.StructureId, row.StructureVersion }).IsUnique();
            entity.HasOne(row => row.Device).WithMany(row => row.Structures)
                .HasForeignKey(row => new { row.ApplicationId, row.DeviceId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationStructureRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.StructureId, Version = row.StructureVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PhoneCompanionDeviceStatusRecord>(entity =>
        {
            entity.ToTable("trigger_phone_device_status", table =>
                table.HasCheckConstraint("CK_trigger_phone_device_status_values",
                    $"{application} AND length(\"DeviceId\") = 45 AND substr(\"DeviceId\", 1, 13) = 'phone-device.' AND \"Revision\" BETWEEN 1 AND 2 AND \"Status\" IN ('active', 'revoked')"));
            entity.HasKey(row => new { row.ApplicationId, row.DeviceId, row.Revision });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.DeviceId).HasMaxLength(45);
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.HasOne(row => row.Device).WithMany(row => row.StatusRevisions)
                .HasForeignKey(row => new { row.ApplicationId, row.DeviceId }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PhoneCompanionDeviceCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_phone_device_current", table =>
                table.HasCheckConstraint("CK_trigger_phone_device_current_revision",
                    "\"CurrentRevision\" BETWEEN 1 AND 2"));
            entity.HasKey(row => new { row.ApplicationId, row.DeviceId });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.DeviceId).HasMaxLength(45);
            entity.HasOne<PhoneCompanionDeviceStatusRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.DeviceId, Revision = row.CurrentRevision })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OneTimeTriggerRecord>(entity =>
        {
            entity.ToTable("trigger_one_time_definition", table =>
            {
                table.HasCheckConstraint("CK_trigger_one_time_definition_values", $"{application} AND {identifier} AND \"Version\" > 0 AND \"MisfirePolicy\" IN ('skip', 'fire-once') AND \"Target\" = 'notification-only' AND \"Lifecycle\" IN ('active', 'cancelled')");
                table.HasCheckConstraint("CK_trigger_one_time_notification_values",
                    "length(\"NotificationTopic\") BETWEEN 1 AND 200 AND length(\"NotificationSubject\") BETWEEN 1 AND 400 AND length(CAST(\"NotificationBody\" AS BLOB)) <= 16384 AND (\"NotificationStateSpaceId\" IS NULL OR length(\"NotificationStateSpaceId\") BETWEEN 1 AND 200)");
            });
            entity.HasKey(row => new { row.ApplicationId, row.Id, row.Version });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.Property(row => row.MisfirePolicy).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Target).HasMaxLength(30).IsRequired();
            entity.Property(row => row.Lifecycle).HasMaxLength(20).IsRequired();
            entity.Property(row => row.NotificationTopic).HasMaxLength(200).IsRequired();
            entity.Property(row => row.NotificationSubject).HasMaxLength(400).IsRequired();
            entity.Property(row => row.NotificationBody).HasMaxLength(16384).IsRequired();
            entity.Property(row => row.NotificationStateSpaceId).HasMaxLength(200);
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(row => row.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OneTimeTriggerNotificationEntityRecord>(entity =>
        {
            entity.ToTable("trigger_one_time_notification_entity", table =>
            {
                table.HasCheckConstraint("CK_trigger_one_time_notification_entity_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 31 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200");
            });
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.Ordinal });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.StateSpaceId).HasMaxLength(200);
            entity.Property(row => row.EntityId).HasMaxLength(200);
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.EntityId }).IsUnique();
            entity.HasOne(row => row.Trigger).WithMany(row => row.NotificationEntities)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OneTimeTriggerCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_one_time_current", table =>
                table.HasCheckConstraint("CK_trigger_one_time_current_version", "\"CurrentVersion\" > 0"));
            entity.HasKey(row => new { row.ApplicationId, row.Id });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.HasOne<OneTimeTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.Id, Version = row.CurrentVersion }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerObservationRecord>(entity =>
        {
            entity.ToTable("trigger_observation", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_id", "length(\"Id\") = 44 AND substr(\"Id\", 1, 12) = 'observation.' AND substr(\"Id\", 13) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_observation_request", "length(\"RequestId\") = 52 AND substr(\"RequestId\", 1, 20) = 'observation-request.' AND substr(\"RequestId\", 21) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_observation_values", $"{application} AND length(\"SourceId\") BETWEEN 3 AND 200 AND \"SourceVersion\" > 0 AND length(\"SourceInstanceId\") BETWEEN 1 AND 128 AND length(\"OccurrenceId\") BETWEEN 1 AND 200 AND length(\"StructureId\") BETWEEN 3 AND 200 AND \"StructureVersion\" > 0 AND length(\"DataJson\") BETWEEN 2 AND 65536 AND json_valid(\"DataJson\") AND json_type(\"DataJson\") = 'object'");
                table.HasCheckConstraint("CK_trigger_observation_hashes", string.Join(" AND ", string.Format(hash, "StructureHash"), string.Format(hash, "DataHash"), string.Format(hash, "RequestFingerprint")));
                table.HasCheckConstraint("CK_trigger_observation_principal",
                    "\"PrincipalId\" IS NULL OR (length(\"PrincipalId\") = 74 AND substr(\"PrincipalId\", 1, 10) = 'principal.' AND substr(\"PrincipalId\", 11) NOT GLOB '*[^0-9a-f]*')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(44);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.RequestId).HasMaxLength(52).IsRequired();
            entity.Property(row => row.SourceId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.SourceInstanceId).HasMaxLength(128).IsRequired();
            entity.Property(row => row.OccurrenceId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.StructureId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.StructureHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.DataJson).HasMaxLength(65536).IsRequired();
            entity.Property(row => row.DataHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.PrincipalId).HasMaxLength(74);
            entity.HasIndex(row => new { row.ApplicationId, row.RequestId }).IsUnique();
            entity.HasIndex(row => new { row.ApplicationId, row.SourceId, row.SourceVersion, row.SourceInstanceId, row.OccurrenceId }).IsUnique();
            entity.HasIndex(row => new { row.ApplicationId, row.ReceivedAtUtc, row.Id });
            entity.HasOne<TriggerObservationSourceRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, Id = row.SourceId, Version = row.SourceVersion }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationStructureRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, Id = row.StructureId, Version = row.StructureVersion }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationSourceStructureRecord>().WithMany().HasForeignKey(row => new
            {
                row.ApplicationId,
                row.SourceId,
                row.SourceVersion,
                row.StructureId,
                row.StructureVersion
            }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationSourcePrincipalRecord>().WithMany().HasForeignKey(row => new
            {
                row.ApplicationId,
                row.SourceId,
                row.SourceVersion,
                row.PrincipalId
            }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerFireReceiptRecord>(entity =>
        {
            entity.ToTable("trigger_fire_receipt", table =>
            {
                table.HasCheckConstraint("CK_trigger_fire_receipt_id", "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'trigger-fire.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_fire_receipt_values", $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Disposition\" IN ('due', 'missed')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.Disposition).HasMaxLength(20).IsRequired();
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.OccurrenceAtUtc }).IsUnique();
            entity.HasOne<OneTimeTriggerRecord>().WithMany().HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerFireWorkRecord>(entity =>
        {
            entity.ToTable("trigger_fire_work", table =>
            {
                table.HasCheckConstraint("CK_trigger_fire_work_id",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_fire_work_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                table.HasCheckConstraint("CK_trigger_fire_work_state",
                    "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'missed', 'failed') AND (" +
                    "(\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('handler-unavailable', 'transient-database')) OR " +
                    "(\"State\" IN ('completed', 'missed') AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'failed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted'))) ");
                table.HasCheckConstraint("CK_trigger_fire_work_lease",
                    "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                table.HasCheckConstraint("CK_trigger_fire_work_token",
                    "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.State).HasMaxLength(20).IsRequired();
            entity.Property(row => row.LeaseOwner).HasMaxLength(128);
            entity.Property(row => row.LeaseToken).HasMaxLength(32);
            entity.Property(row => row.FailureKind).HasMaxLength(30);
            entity.Property(row => row.Revision).IsConcurrencyToken();
            entity.HasIndex(row => new { row.State, row.NextAttemptAtUtc, row.LeaseExpiresAtUtc });
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.OccurrenceAtUtc }).IsUnique();
            entity.HasOne<OneTimeTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TriggerNotificationLinkRecord>(entity =>
        {
            entity.ToTable("trigger_notification_link", table =>
            {
                table.HasCheckConstraint("CK_trigger_notification_link_fire",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_notification_link_notification",
                    "length(\"NotificationId\") = 32 AND \"NotificationId\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_notification_link_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.NotificationId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.HasIndex(row => row.NotificationId).IsUnique();
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.OccurrenceAtUtc });
            entity.HasOne<TriggerFireReceiptRecord>().WithOne().HasForeignKey<TriggerNotificationLinkRecord>(row => row.FireId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Notification>().WithOne().HasForeignKey<TriggerNotificationLinkRecord>(row => row.NotificationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OneTimeTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_definition", table =>
            {
                table.HasCheckConstraint("CK_trigger_recurring_definition_values",
                    $"{application} AND {identifier} AND \"Version\" > 0 AND \"Lifecycle\" IN ('active', 'paused', 'cancelled') AND \"Kind\" IN ('daily', 'weekly', 'monthly') AND \"Interval\" BETWEEN 1 AND 365 AND \"LocalTimeSeconds\" BETWEEN 0 AND 86399 AND length(\"TimeZoneId\") BETWEEN 3 AND 100 AND \"GapPolicy\" IN ('skip', 'next-valid') AND \"OverlapPolicy\" IN ('earlier', 'later') AND \"MisfirePolicy\" IN ('skip', 'fire-once') AND \"Target\" = 'notification-only'");
                table.HasCheckConstraint("CK_trigger_recurring_definition_shape",
                    "(\"Kind\" = 'daily' AND \"WeekdaysMask\" = 0 AND \"DayOfMonth\" IS NULL) OR " +
                    "(\"Kind\" = 'weekly' AND \"WeekdaysMask\" BETWEEN 1 AND 127 AND \"DayOfMonth\" IS NULL) OR " +
                    "(\"Kind\" = 'monthly' AND \"WeekdaysMask\" = 0 AND \"DayOfMonth\" BETWEEN 1 AND 31)");
                table.HasCheckConstraint("CK_trigger_recurring_definition_dates",
                    "\"StartDate\" IS NULL OR \"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                table.HasCheckConstraint("CK_trigger_recurring_notification_values",
                    "length(\"NotificationTopic\") BETWEEN 1 AND 200 AND length(\"NotificationSubject\") BETWEEN 1 AND 400 AND length(CAST(\"NotificationBody\" AS BLOB)) <= 16384 AND (\"NotificationStateSpaceId\" IS NULL OR length(\"NotificationStateSpaceId\") BETWEEN 1 AND 200)");
            });
            entity.HasKey(row => new { row.ApplicationId, row.Id, row.Version });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.Property(row => row.Lifecycle).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Kind).HasMaxLength(20).IsRequired();
            entity.Property(row => row.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.Property(row => row.GapPolicy).HasMaxLength(20).IsRequired();
            entity.Property(row => row.OverlapPolicy).HasMaxLength(20).IsRequired();
            entity.Property(row => row.MisfirePolicy).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Target).HasMaxLength(30).IsRequired();
            entity.Property(row => row.NotificationTopic).HasMaxLength(200).IsRequired();
            entity.Property(row => row.NotificationSubject).HasMaxLength(400).IsRequired();
            entity.Property(row => row.NotificationBody).HasMaxLength(16384).IsRequired();
            entity.Property(row => row.NotificationStateSpaceId).HasMaxLength(200);
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(row => row.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerNotificationEntityRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_notification_entity", table =>
                table.HasCheckConstraint("CK_trigger_recurring_notification_entity_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 31 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200"));
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.Ordinal });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.StateSpaceId).HasMaxLength(200);
            entity.Property(row => row.EntityId).HasMaxLength(200);
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.EntityId }).IsUnique();
            entity.HasOne(row => row.Trigger).WithMany(row => row.NotificationEntities)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_current", table =>
                table.HasCheckConstraint("CK_trigger_recurring_current_version", "\"CurrentVersion\" > 0"));
            entity.HasKey(row => new { row.ApplicationId, row.Id });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.HasOne<RecurringTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.Id, Version = row.CurrentVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerStateRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_state", table =>
            {
                table.HasCheckConstraint("CK_trigger_recurring_state_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"CurrentVersion\" > 0 AND \"Revision\" >= 0");
                table.HasCheckConstraint("CK_trigger_recurring_state_disposition",
                    "\"LastDisposition\" IS NULL OR \"LastDisposition\" IN ('due', 'missed')");
                table.HasCheckConstraint("CK_trigger_recurring_state_failure",
                    "\"LastFailureKind\" IS NULL OR \"LastFailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted')");
                table.HasCheckConstraint("CK_trigger_recurring_state_last",
                    "(\"LastOccurrenceAtUtc\" IS NULL AND \"LastDisposition\" IS NULL AND \"LastFailureKind\" IS NULL) OR " +
                    "(\"LastOccurrenceAtUtc\" IS NOT NULL AND ((\"LastDisposition\" IS NULL) <> (\"LastFailureKind\" IS NULL)))");
            });
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.LastDisposition).HasMaxLength(20);
            entity.Property(row => row.LastFailureKind).HasMaxLength(30);
            entity.Property(row => row.Revision).IsConcurrencyToken();
            entity.HasIndex(row => new { row.NextOccurrenceAtUtc, row.ApplicationId, row.TriggerId });
            entity.HasOne<RecurringTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.CurrentVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerFireWorkRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_fire_work", table =>
            {
                table.HasCheckConstraint("CK_trigger_recurring_fire_work_id",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_recurring_fire_work_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                table.HasCheckConstraint("CK_trigger_recurring_fire_work_state",
                    "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'missed', 'failed') AND (" +
                    "(\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('handler-unavailable', 'transient-database')) OR " +
                    "(\"State\" IN ('completed', 'missed') AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'failed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted'))) ");
                table.HasCheckConstraint("CK_trigger_recurring_fire_work_lease",
                    "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                table.HasCheckConstraint("CK_trigger_recurring_fire_work_token",
                    "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.State).HasMaxLength(20).IsRequired();
            entity.Property(row => row.LeaseOwner).HasMaxLength(128);
            entity.Property(row => row.LeaseToken).HasMaxLength(32);
            entity.Property(row => row.FailureKind).HasMaxLength(30);
            entity.Property(row => row.Revision).IsConcurrencyToken();
            entity.HasIndex(row => new { row.State, row.NextAttemptAtUtc, row.LeaseExpiresAtUtc });
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.OccurrenceAtUtc }).IsUnique();
            entity.HasOne<RecurringTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerFireReceiptRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_fire_receipt", table =>
            {
                table.HasCheckConstraint("CK_trigger_recurring_fire_receipt_id",
                    "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'trigger-fire.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_recurring_fire_receipt_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Disposition\" IN ('due', 'missed')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.Disposition).HasMaxLength(20).IsRequired();
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.OccurrenceAtUtc }).IsUnique();
            entity.HasOne<RecurringTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecurringTriggerNotificationLinkRecord>(entity =>
        {
            entity.ToTable("trigger_recurring_notification_link", table =>
            {
                table.HasCheckConstraint("CK_trigger_recurring_notification_link_fire",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_recurring_notification_link_notification",
                    "length(\"NotificationId\") = 32 AND \"NotificationId\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_recurring_notification_link_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.NotificationId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.HasIndex(row => row.NotificationId).IsUnique();
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.OccurrenceAtUtc });
            entity.HasOne<RecurringTriggerFireReceiptRecord>().WithOne()
                .HasForeignKey<RecurringTriggerNotificationLinkRecord>(row => row.FireId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Notification>().WithOne()
                .HasForeignKey<RecurringTriggerNotificationLinkRecord>(row => row.NotificationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RecurringTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_definition", table =>
            {
                table.HasCheckConstraint("CK_trigger_conditional_definition_values",
                    $"{application} AND {identifier} AND \"Version\" > 0 AND \"Lifecycle\" IN ('active', 'paused', 'cancelled') AND \"Kind\" IN ('world-clock-threshold', 'state-condition') AND \"Activation\" IN ('rising-edge', 'level') AND \"Rearm\" IN ('on-false', 'manual') AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"AdapterId\") BETWEEN 3 AND 200 AND \"AdapterVersion\" > 0 AND \"Target\" = 'notification-only'");
                table.HasCheckConstraint("CK_trigger_conditional_definition_clock_policy",
                    "\"Kind\" <> 'world-clock-threshold' OR (\"Activation\" = 'rising-edge' AND \"Rearm\" = 'manual')");
                table.HasCheckConstraint("CK_trigger_conditional_definition_config",
                    "length(\"AdapterConfigurationJson\") BETWEEN 2 AND 65536 AND json_valid(\"AdapterConfigurationJson\") AND json_type(\"AdapterConfigurationJson\") = 'object'");
                table.HasCheckConstraint("CK_trigger_conditional_definition_config_hash",
                    string.Format(hash, "AdapterConfigurationHash"));
                table.HasCheckConstraint("CK_trigger_conditional_notification_values",
                    "length(\"NotificationTopic\") BETWEEN 1 AND 200 AND length(\"NotificationSubject\") BETWEEN 1 AND 400 AND length(CAST(\"NotificationBody\" AS BLOB)) <= 16384 AND (\"NotificationStateSpaceId\" IS NULL OR length(\"NotificationStateSpaceId\") BETWEEN 1 AND 200)");
            });
            entity.HasKey(row => new { row.ApplicationId, row.Id, row.Version });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.Property(row => row.Lifecycle).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Kind).HasMaxLength(30).IsRequired();
            entity.Property(row => row.Activation).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Rearm).HasMaxLength(20).IsRequired();
            entity.Property(row => row.StateSpaceId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.AdapterId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.AdapterConfigurationJson).HasMaxLength(65536).IsRequired();
            entity.Property(row => row.AdapterConfigurationHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Target).HasMaxLength(30).IsRequired();
            entity.Property(row => row.NotificationTopic).HasMaxLength(200).IsRequired();
            entity.Property(row => row.NotificationSubject).HasMaxLength(400).IsRequired();
            entity.Property(row => row.NotificationBody).HasMaxLength(16384).IsRequired();
            entity.Property(row => row.NotificationStateSpaceId).HasMaxLength(200);
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(row => row.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationStateSpaceRecord>().WithMany().HasForeignKey(row => row.StateSpaceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerDependencyRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_dependency", table =>
            {
                table.HasCheckConstraint("CK_trigger_conditional_dependency_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 15 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200 AND length(\"QualifiedTypeId\") BETWEEN 3 AND 200 AND \"TypeVersion\" > 0");
                table.HasCheckConstraint("CK_trigger_conditional_dependency_hash", string.Format(hash, "SchemaHash"));
            });
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.Ordinal });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.StateSpaceId).HasMaxLength(200);
            entity.Property(row => row.EntityId).HasMaxLength(200);
            entity.Property(row => row.QualifiedTypeId).HasMaxLength(200);
            entity.Property(row => row.SchemaHash).HasMaxLength(64);
            entity.HasIndex(row => new { row.StateSpaceId, row.EntityId, row.QualifiedTypeId });
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.EntityId, row.QualifiedTypeId }).IsUnique();
            entity.HasOne(row => row.Trigger).WithMany(row => row.Dependencies)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationEcsEntityRecord>().WithMany()
                .HasForeignKey(row => new { row.StateSpaceId, Id = row.EntityId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerNotificationEntityRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_notification_entity", table =>
                table.HasCheckConstraint("CK_trigger_conditional_notification_entity_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 31 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200"));
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.Ordinal });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.StateSpaceId).HasMaxLength(200);
            entity.Property(row => row.EntityId).HasMaxLength(200);
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.EntityId }).IsUnique();
            entity.HasOne(row => row.Trigger).WithMany(row => row.NotificationEntities)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationEcsEntityRecord>().WithMany()
                .HasForeignKey(row => new { row.StateSpaceId, Id = row.EntityId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_current", table =>
                table.HasCheckConstraint("CK_trigger_conditional_current_version", "\"CurrentVersion\" > 0"));
            entity.HasKey(row => new { row.ApplicationId, row.Id });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.HasOne<ConditionalTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.Id, Version = row.CurrentVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerStateRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_state", table =>
            {
                table.HasCheckConstraint("CK_trigger_conditional_state_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"CurrentVersion\" > 0 AND \"EvaluationRevision\" >= 0");
                table.HasCheckConstraint("CK_trigger_conditional_state_operations",
                    "(\"LastOperationId\" IS NULL OR (length(\"LastOperationId\") = 32 AND \"LastOperationId\" NOT GLOB '*[^0-9a-f]*')) AND (\"LastFiredOperationId\" IS NULL OR (length(\"LastFiredOperationId\") = 32 AND \"LastFiredOperationId\" NOT GLOB '*[^0-9a-f]*'))");
            });
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.LastOperationId).HasMaxLength(32);
            entity.Property(row => row.LastFiredOperationId).HasMaxLength(32);
            entity.Property(row => row.EvaluationRevision).IsConcurrencyToken();
            entity.HasOne<ConditionalTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.CurrentVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerFireWorkRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_fire_work", table =>
            {
                table.HasCheckConstraint("CK_trigger_conditional_fire_work_id",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_conditional_fire_work_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ChangeOperationId\") = 32 AND \"ChangeOperationId\" NOT GLOB '*[^0-9a-f]*' AND \"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                table.HasCheckConstraint("CK_trigger_conditional_fire_work_state",
                    "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'failed') AND (" +
                    "(\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('handler-unavailable', 'transient-database')) OR " +
                    "(\"State\" = 'completed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'failed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted'))) ");
                table.HasCheckConstraint("CK_trigger_conditional_fire_work_lease",
                    "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                table.HasCheckConstraint("CK_trigger_conditional_fire_work_token",
                    "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.ChangeOperationId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.State).HasMaxLength(20).IsRequired();
            entity.Property(row => row.LeaseOwner).HasMaxLength(128);
            entity.Property(row => row.LeaseToken).HasMaxLength(32);
            entity.Property(row => row.FailureKind).HasMaxLength(30);
            entity.Property(row => row.Revision).IsConcurrencyToken();
            entity.HasIndex(row => new { row.State, row.NextAttemptAtUtc, row.LeaseExpiresAtUtc });
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.ChangeOperationId }).IsUnique();
            entity.HasOne<ConditionalTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerFireReceiptRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_fire_receipt", table =>
            {
                table.HasCheckConstraint("CK_trigger_conditional_fire_receipt_id",
                    "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'trigger-fire.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_conditional_fire_receipt_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ChangeOperationId\") = 32 AND \"ChangeOperationId\" NOT GLOB '*[^0-9a-f]*' AND \"Disposition\" = 'due'");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.ChangeOperationId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.Disposition).HasMaxLength(20).IsRequired();
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.ChangeOperationId }).IsUnique();
            entity.HasOne<ConditionalTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConditionalTriggerNotificationLinkRecord>(entity =>
        {
            entity.ToTable("trigger_conditional_notification_link", table =>
            {
                table.HasCheckConstraint("CK_trigger_conditional_notification_link_fire",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_conditional_notification_link_notification",
                    "length(\"NotificationId\") = 32 AND \"NotificationId\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_conditional_notification_link_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ChangeOperationId\") = 32 AND \"ChangeOperationId\" NOT GLOB '*[^0-9a-f]*'");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.NotificationId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.ChangeOperationId).HasMaxLength(32).IsRequired();
            entity.HasIndex(row => row.NotificationId).IsUnique();
            entity.HasOne<ConditionalTriggerFireReceiptRecord>().WithOne()
                .HasForeignKey<ConditionalTriggerNotificationLinkRecord>(row => row.FireId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Notification>().WithOne()
                .HasForeignKey<ConditionalTriggerNotificationLinkRecord>(row => row.NotificationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ConditionalTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ObservationTriggerRecord>(entity =>
        {
            entity.ToTable("trigger_observation_match_definition", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_match_definition_values",
                    $"{application} AND {identifier} AND \"Version\" > 0 AND \"Lifecycle\" IN ('active', 'paused', 'cancelled') AND length(\"SourceId\") BETWEEN 3 AND 200 AND \"SourceVersion\" > 0 AND length(\"StructureId\") BETWEEN 3 AND 200 AND \"StructureVersion\" > 0 AND length(\"AdapterId\") BETWEEN 3 AND 200 AND \"AdapterVersion\" > 0 AND \"Target\" = 'notification-only'");
                table.HasCheckConstraint("CK_trigger_observation_match_definition_hashes",
                    string.Format(hash, "StructureHash") + " AND " + string.Format(hash, "AdapterConfigurationHash"));
                table.HasCheckConstraint("CK_trigger_observation_match_definition_config",
                    "length(\"AdapterConfigurationJson\") BETWEEN 2 AND 65536 AND json_valid(\"AdapterConfigurationJson\") AND json_type(\"AdapterConfigurationJson\") = 'object'");
                table.HasCheckConstraint("CK_trigger_observation_match_notification_values",
                    "length(\"NotificationTopic\") BETWEEN 1 AND 200 AND length(\"NotificationSubject\") BETWEEN 1 AND 400 AND length(CAST(\"NotificationBody\" AS BLOB)) <= 16384 AND (\"NotificationStateSpaceId\" IS NULL OR length(\"NotificationStateSpaceId\") BETWEEN 1 AND 200)");
            });
            entity.HasKey(row => new { row.ApplicationId, row.Id, row.Version });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.Property(row => row.Lifecycle).HasMaxLength(20).IsRequired();
            entity.Property(row => row.SourceId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.StructureId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.StructureHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.AdapterId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.AdapterConfigurationJson).HasMaxLength(65536).IsRequired();
            entity.Property(row => row.AdapterConfigurationHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Target).HasMaxLength(30).IsRequired();
            entity.Property(row => row.NotificationTopic).HasMaxLength(200).IsRequired();
            entity.Property(row => row.NotificationSubject).HasMaxLength(400).IsRequired();
            entity.Property(row => row.NotificationBody).HasMaxLength(16384).IsRequired();
            entity.Property(row => row.NotificationStateSpaceId).HasMaxLength(200);
            entity.HasIndex(row => new { row.ApplicationId, row.SourceId, row.SourceVersion,
                row.StructureId, row.StructureVersion, row.StructureHash, row.Lifecycle });
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(row => row.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationSourceRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.SourceId, Version = row.SourceVersion })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationStructureRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.StructureId, Version = row.StructureVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ObservationTriggerNotificationEntityRecord>(entity =>
        {
            entity.ToTable("trigger_observation_match_notification_entity", table =>
                table.HasCheckConstraint("CK_trigger_observation_match_notification_entity_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND \"Ordinal\" BETWEEN 0 AND 31 AND length(\"StateSpaceId\") BETWEEN 1 AND 200 AND length(\"EntityId\") BETWEEN 1 AND 200"));
            entity.HasKey(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.Ordinal });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.TriggerId).HasMaxLength(200);
            entity.Property(row => row.StateSpaceId).HasMaxLength(200);
            entity.Property(row => row.EntityId).HasMaxLength(200);
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.EntityId }).IsUnique();
            entity.HasOne(row => row.Trigger).WithMany(row => row.NotificationEntities)
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationEcsEntityRecord>().WithMany()
                .HasForeignKey(row => new { row.StateSpaceId, Id = row.EntityId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ObservationTriggerCurrentRecord>(entity =>
        {
            entity.ToTable("trigger_observation_match_current", table =>
                table.HasCheckConstraint("CK_trigger_observation_match_current_version", "\"CurrentVersion\" > 0"));
            entity.HasKey(row => new { row.ApplicationId, row.Id });
            entity.Property(row => row.ApplicationId).HasMaxLength(63);
            entity.Property(row => row.Id).HasMaxLength(200);
            entity.HasOne<ObservationTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, row.Id, Version = row.CurrentVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ObservationTriggerMatchWorkRecord>(entity =>
        {
            entity.ToTable("trigger_observation_match_work", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_match_work_id",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_observation_match_work_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ObservationId\") = 44 AND substr(\"ObservationId\", 1, 12) = 'observation.' AND substr(\"ObservationId\", 13) NOT GLOB '*[^0-9a-f]*' AND \"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0");
                table.HasCheckConstraint("CK_trigger_observation_match_work_state",
                    "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'failed') AND (" +
                    "(\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('handler-unavailable', 'transient-database')) OR " +
                    "(\"State\" = 'completed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL) OR " +
                    "(\"State\" = 'failed' AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IN ('permanent-handler', 'stale-trigger', 'attempts-exhausted'))) ");
                table.HasCheckConstraint("CK_trigger_observation_match_work_lease",
                    "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                table.HasCheckConstraint("CK_trigger_observation_match_work_token",
                    "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.ObservationId).HasMaxLength(44).IsRequired();
            entity.Property(row => row.State).HasMaxLength(20).IsRequired();
            entity.Property(row => row.LeaseOwner).HasMaxLength(128);
            entity.Property(row => row.LeaseToken).HasMaxLength(32);
            entity.Property(row => row.FailureKind).HasMaxLength(30);
            entity.Property(row => row.Revision).IsConcurrencyToken();
            entity.HasIndex(row => new { row.State, row.NextAttemptAtUtc, row.LeaseExpiresAtUtc });
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.ObservationId }).IsUnique();
            entity.HasOne<ObservationTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationRecord>().WithMany().HasForeignKey(row => row.ObservationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ObservationTriggerMatchReceiptRecord>(entity =>
        {
            entity.ToTable("trigger_observation_match_receipt", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_match_receipt_id",
                    "length(\"Id\") = 45 AND substr(\"Id\", 1, 13) = 'trigger-fire.' AND substr(\"Id\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_observation_match_receipt_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ObservationId\") = 44 AND substr(\"ObservationId\", 1, 12) = 'observation.' AND \"Disposition\" IN ('matched', 'not-matched')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(45);
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.ObservationId).HasMaxLength(44).IsRequired();
            entity.Property(row => row.Disposition).HasMaxLength(20).IsRequired();
            entity.HasIndex(row => new { row.ApplicationId, row.TriggerId, row.TriggerVersion, row.ObservationId }).IsUnique();
            entity.HasOne<ObservationTriggerRecord>().WithMany()
                .HasForeignKey(row => new { row.ApplicationId, Id = row.TriggerId, Version = row.TriggerVersion })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationRecord>().WithMany().HasForeignKey(row => row.ObservationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ObservationTriggerNotificationLinkRecord>(entity =>
        {
            entity.ToTable("trigger_observation_match_notification_link", table =>
            {
                table.HasCheckConstraint("CK_trigger_observation_match_notification_link_fire",
                    "length(\"FireId\") = 45 AND substr(\"FireId\", 1, 13) = 'trigger-fire.' AND substr(\"FireId\", 14) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_observation_match_notification_link_notification",
                    "length(\"NotificationId\") = 32 AND \"NotificationId\" NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_trigger_observation_match_notification_link_values",
                    $"{application} AND length(\"TriggerId\") BETWEEN 3 AND 200 AND \"TriggerVersion\" > 0 AND length(\"ObservationId\") = 44 AND substr(\"ObservationId\", 1, 12) = 'observation.'");
            });
            entity.HasKey(row => row.FireId);
            entity.Property(row => row.FireId).HasMaxLength(45);
            entity.Property(row => row.NotificationId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(row => row.TriggerId).HasMaxLength(200).IsRequired();
            entity.Property(row => row.ObservationId).HasMaxLength(44).IsRequired();
            entity.HasIndex(row => row.NotificationId).IsUnique();
            entity.HasOne<ObservationTriggerMatchReceiptRecord>().WithOne()
                .HasForeignKey<ObservationTriggerNotificationLinkRecord>(row => row.FireId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Notification>().WithOne()
                .HasForeignKey<ObservationTriggerNotificationLinkRecord>(row => row.NotificationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TriggerObservationRecord>().WithMany().HasForeignKey(row => row.ObservationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ScheduledAiTaskWorkRecord>(entity =>
        {
            entity.ToTable("scheduled_ai_task_work", table =>
            {
                table.HasCheckConstraint("CK_scheduled_ai_task_work_values",
                    "\"AttemptCount\" BETWEEN 0 AND 3 AND \"Revision\" >= 0 " +
                    "AND (\"QueueAgeMilliseconds\" IS NULL OR \"QueueAgeMilliseconds\" >= 0) " +
                    "AND (\"ProviderDurationMilliseconds\" IS NULL OR \"ProviderDurationMilliseconds\" >= 0)");
                table.HasCheckConstraint("CK_scheduled_ai_task_work_state",
                    "\"State\" IN ('ready', 'leased', 'retry', 'completed', 'failed') AND (" +
                    "(\"State\" = 'ready' AND \"AttemptCount\" = 0 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL AND \"FailureMessage\" IS NULL AND \"QueueAgeMilliseconds\" IS NULL AND \"ProviderDurationMilliseconds\" IS NULL AND \"CompletedAtUtc\" IS NULL) OR " +
                    "(\"State\" = 'leased' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL AND \"FailureKind\" IS NULL AND \"FailureMessage\" IS NULL AND \"QueueAgeMilliseconds\" IS NOT NULL AND \"ProviderDurationMilliseconds\" IS NULL AND \"CompletedAtUtc\" IS NULL) OR " +
                    "(\"State\" = 'retry' AND \"AttemptCount\" BETWEEN 1 AND 2 AND \"NextAttemptAtUtc\" IS NOT NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NOT NULL AND \"FailureMessage\" IS NOT NULL AND \"QueueAgeMilliseconds\" IS NOT NULL AND \"ProviderDurationMilliseconds\" IS NOT NULL AND \"CompletedAtUtc\" IS NULL) OR " +
                    "(\"State\" = 'completed' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NULL AND \"FailureMessage\" IS NULL AND \"QueueAgeMilliseconds\" IS NOT NULL AND \"ProviderDurationMilliseconds\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL) OR " +
                    "(\"State\" = 'failed' AND \"AttemptCount\" BETWEEN 1 AND 3 AND \"NextAttemptAtUtc\" IS NULL AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL AND \"FailureKind\" IS NOT NULL AND \"FailureMessage\" IS NOT NULL AND \"QueueAgeMilliseconds\" IS NOT NULL AND \"ProviderDurationMilliseconds\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL))");
                table.HasCheckConstraint("CK_scheduled_ai_task_work_lease",
                    "\"LeaseOwner\" IS NULL OR (length(\"LeaseOwner\") BETWEEN 1 AND 128 AND \"LeaseOwner\" NOT GLOB '*[^A-Za-z0-9._:-]*')");
                table.HasCheckConstraint("CK_scheduled_ai_task_work_token",
                    "\"LeaseToken\" IS NULL OR (length(\"LeaseToken\") = 32 AND \"LeaseToken\" NOT GLOB '*[^0-9a-f]*')");
            });
            entity.HasKey(row => row.NotificationId);
            entity.Property(row => row.NotificationId).HasMaxLength(40);
            entity.Property(row => row.State).HasMaxLength(20).IsRequired();
            entity.Property(row => row.LeaseOwner).HasMaxLength(128);
            entity.Property(row => row.LeaseToken).HasMaxLength(32);
            entity.Property(row => row.FailureKind).HasMaxLength(100);
            entity.Property(row => row.FailureMessage).HasMaxLength(500);
            entity.Property(row => row.Revision).IsConcurrencyToken();
            entity.HasIndex(row => new { row.State, row.NextAttemptAtUtc, row.LeaseExpiresAtUtc, row.EnqueuedAtUtc });
            entity.HasOne<Notification>().WithOne().HasForeignKey<ScheduledAiTaskWorkRecord>(row => row.NotificationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
