using DantesRoleplay.Play;
using DantesRoleplay.Ecs;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Owns the relational model for persisted application play records.</summary>
internal static class PlayRecordingModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        const string conversationStatuses = "'ready', 'planning', 'awaiting-confirmation', 'needs-attention', 'unavailable'";
        const string situationKinds = "'out-of-character', 'conversation', 'combat', 'exploration', 'investigation', 'travel', 'rest', 'downtime', 'other'";
        modelBuilder.Entity<ApplicationPlayConversationRecord>(entity =>
        {
            entity.ToTable("application_play_conversation", table =>
            {
                table.HasCheckConstraint("CK_application_play_conversation_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_application_play_conversation_status", $"\"Status\" IN ({conversationStatuses})");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.PrincipalId).HasMaxLength(100).IsRequired();
            entity.Property(value => value.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(value => value.StateSpaceId).HasMaxLength(200).IsRequired();
            entity.Property(value => value.SessionContextId).HasMaxLength(200).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(30).IsRequired();
            entity.Property(value => value.Revision).IsConcurrencyToken();
            entity.Property(value => value.CurrentSituationId).HasMaxLength(80);
            entity.HasIndex(value => new
            {
                value.PrincipalId,
                value.ApplicationId,
                value.StateSpaceId,
                value.SessionContextId
            }).IsUnique();
            entity.HasIndex(value => new { value.PrincipalId, value.ApplicationId, value.UpdatedAtUtc, value.Id });
            entity.HasOne<ApplicationStateSpaceRecord>().WithMany().HasForeignKey(value => value.StateSpaceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationPlayMessageRecord>(entity =>
        {
            entity.ToTable("application_play_message", table =>
            {
                table.HasCheckConstraint("CK_application_play_message_ordinal", "\"Ordinal\" > 0");
                table.HasCheckConstraint("CK_application_play_message_role", "\"Role\" IN ('player', 'assistant')");
                table.HasCheckConstraint("CK_application_play_message_text", "length(\"Text\") BETWEEN 1 AND 8000");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.ConversationId).HasMaxLength(80).IsRequired();
            entity.Property(value => value.Role).HasMaxLength(20).IsRequired();
            entity.Property(value => value.Text).HasMaxLength(8_000).IsRequired();
            entity.Property(value => value.Code).HasMaxLength(100).IsRequired();
            entity.Property(value => value.SituationId).HasMaxLength(80);
            entity.HasIndex(value => new { value.ConversationId, value.Ordinal }).IsUnique();
            entity.HasIndex(value => new { value.ConversationId, value.CreatedAtUtc, value.Id });
            entity.HasOne(value => value.Conversation).WithMany(value => value.Messages)
                .HasForeignKey(value => value.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationPlaySituationRecord>(entity =>
        {
            entity.ToTable("application_play_situation", table =>
            {
                table.HasCheckConstraint("CK_application_play_situation_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_application_play_situation_kind", $"\"Kind\" IN ({situationKinds})");
                table.HasCheckConstraint("CK_application_play_situation_status", "\"Status\" IN ('active', 'completed')");
                table.HasCheckConstraint("CK_application_play_situation_json", "json_valid(\"ParticipantsJson\") AND (\"LocationJson\" = '' OR json_valid(\"LocationJson\"))");
                table.HasCheckConstraint("CK_application_play_situation_summary", "length(\"Summary\") BETWEEN 1 AND 1000");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.ConversationId).HasMaxLength(80).IsRequired();
            entity.Property(value => value.Kind).HasMaxLength(30).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.Property(value => value.Summary).HasMaxLength(1_000).IsRequired();
            entity.Property(value => value.ParticipantsJson).HasMaxLength(16_000).IsRequired();
            entity.Property(value => value.LocationJson).HasMaxLength(1_000).IsRequired();
            entity.HasIndex(value => new { value.ConversationId, value.StartedAtUtc, value.Id });
            entity.HasOne(value => value.Conversation).WithMany(value => value.Situations)
                .HasForeignKey(value => value.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationPlayTruthRecord>(entity =>
        {
            entity.ToTable("application_play_truth", table =>
            {
                table.HasCheckConstraint("CK_application_play_truth_ordinal", "\"Ordinal\" > 0");
                table.HasCheckConstraint("CK_application_play_truth_statement", "length(\"Statement\") BETWEEN 1 AND 1000");
                table.HasCheckConstraint("CK_application_play_truth_hash", "length(\"NormalizedHash\") = 64 AND \"NormalizedHash\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_application_play_truth_subjects", "json_valid(\"SubjectEntityIdsJson\") AND json_type(\"SubjectEntityIdsJson\") = 'array'");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.ConversationId).HasMaxLength(80).IsRequired();
            entity.Property(value => value.Statement).HasMaxLength(1_000).IsRequired();
            entity.Property(value => value.NormalizedHash).HasMaxLength(64).IsRequired();
            entity.Property(value => value.SubjectEntityIdsJson).HasMaxLength(8_000).IsRequired();
            entity.Property(value => value.SourceMessageId).HasMaxLength(80).IsRequired();
            entity.Property(value => value.SituationId).HasMaxLength(80);
            entity.HasIndex(value => new { value.ConversationId, value.Ordinal }).IsUnique();
            entity.HasIndex(value => new { value.ConversationId, value.NormalizedHash }).IsUnique();
            entity.HasOne(value => value.Conversation).WithMany(value => value.Truths)
                .HasForeignKey(value => value.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationConversationMemoryJournalRecord>(entity =>
        {
            entity.ToTable("application_conversation_memory_journal", table =>
            {
                table.HasCheckConstraint("CK_application_conversation_memory_journal_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_application_conversation_memory_journal_next_ordinal", "\"NextOrdinal\" > 0");
                table.HasCheckConstraint("CK_application_conversation_memory_journal_status",
                    "\"Status\" IN ('connected', 'retry-pending', 'disconnected', 'archived', 'deleted')");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.PrincipalId).HasMaxLength(100).IsRequired();
            entity.Property(value => value.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(value => value.StateSpaceId).HasMaxLength(200).IsRequired();
            entity.Property(value => value.SessionContextId).HasMaxLength(200).IsRequired();
            entity.Property(value => value.SourceClient).HasMaxLength(40).IsRequired();
            entity.Property(value => value.SourceProjectId).HasMaxLength(200).IsRequired();
            entity.Property(value => value.RepositoryRoot).HasMaxLength(1_024).IsRequired();
            entity.Property(value => value.SourceThreadId).HasMaxLength(200).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(30).IsRequired();
            entity.Property(value => value.Revision).IsConcurrencyToken();
            entity.Property(value => value.LastSourceTurnId).HasMaxLength(200);
            entity.Property(value => value.FailureCode).HasMaxLength(100);
            entity.HasIndex(value => new
            {
                value.PrincipalId, value.ApplicationId, value.StateSpaceId, value.SessionContextId
            }).IsUnique();
            entity.HasIndex(value => new
            {
                value.SourceClient, value.SourceProjectId, value.SourceThreadId
            }).IsUnique();
            entity.HasOne<ApplicationStateSpaceRecord>().WithMany().HasForeignKey(value => value.StateSpaceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationConversationMemoryMessageRecord>(entity =>
        {
            entity.ToTable("application_conversation_memory_message", table =>
            {
                table.HasCheckConstraint("CK_application_conversation_memory_message_ordinal", "\"Ordinal\" > 0");
                table.HasCheckConstraint("CK_application_conversation_memory_message_role", "\"Role\" IN ('user', 'assistant')");
                table.HasCheckConstraint("CK_application_conversation_memory_message_kind",
                    "\"SourceKind\" IN ('user-prompt', 'assistant-commentary', 'assistant-final')");
                table.HasCheckConstraint("CK_application_conversation_memory_message_status", "\"Status\" IN ('captured', 'deleted')");
                table.HasCheckConstraint("CK_application_conversation_memory_message_text",
                    "(\"Status\" = 'captured' AND length(\"Text\") BETWEEN 1 AND 16000) OR (\"Status\" = 'deleted' AND \"Text\" = '')");
                table.HasCheckConstraint("CK_application_conversation_memory_message_fingerprint",
                    "length(\"TextFingerprint\") = 64 AND \"TextFingerprint\" NOT GLOB '*[^0-9A-F]*'");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.JournalId).HasMaxLength(80).IsRequired();
            entity.Property(value => value.SourceTurnId).HasMaxLength(200).IsRequired();
            entity.Property(value => value.SourceMessageId).HasMaxLength(200).IsRequired();
            entity.Property(value => value.Role).HasMaxLength(20).IsRequired();
            entity.Property(value => value.SourceKind).HasMaxLength(30).IsRequired();
            entity.Property(value => value.Text).HasMaxLength(16_000).IsRequired();
            entity.Property(value => value.TextFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.CaptureProvenance).HasMaxLength(1_000).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(value => new { value.JournalId, value.Ordinal }).IsUnique();
            entity.HasIndex(value => new { value.JournalId, value.SourceMessageId }).IsUnique();
            entity.HasOne(value => value.Journal).WithMany(value => value.Messages)
                .HasForeignKey(value => value.JournalId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationConversationMemoryDeliveryRecord>(entity =>
        {
            entity.ToTable("application_conversation_memory_delivery", table =>
                table.HasCheckConstraint("CK_application_conversation_memory_delivery_fingerprint",
                    "length(\"PayloadFingerprint\") = 64 AND \"PayloadFingerprint\" NOT GLOB '*[^0-9A-F]*'"));
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.JournalId).HasMaxLength(80).IsRequired();
            entity.Property(value => value.RequestToken).HasMaxLength(128).IsRequired();
            entity.Property(value => value.SourceTurnId).HasMaxLength(200).IsRequired();
            entity.Property(value => value.PayloadFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.MessageIdsJson).HasMaxLength(4_000).IsRequired();
            entity.HasIndex(value => new { value.JournalId, value.RequestToken }).IsUnique();
            entity.HasIndex(value => new { value.JournalId, value.SourceTurnId }).IsUnique();
            entity.HasOne(value => value.Journal).WithMany(value => value.Deliveries)
                .HasForeignKey(value => value.JournalId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationConversationMemoryDerivedRecord>(entity =>
        {
            entity.ToTable("application_conversation_memory_derived", table =>
            {
                table.HasCheckConstraint("CK_application_conversation_memory_derived_source_revision", "\"SourceRevision\" > 0");
                table.HasCheckConstraint("CK_application_conversation_memory_derived_fingerprints",
                    "length(\"SourceFingerprint\") = 64 AND \"SourceFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ResultSchemaFingerprint\") = 64 AND \"ResultSchemaFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_application_conversation_memory_derived_json", "json_valid(\"CandidateJson\")");
                table.HasCheckConstraint("CK_application_conversation_memory_derived_audience", "\"Audience\" = 'private'");
                table.HasCheckConstraint("CK_application_conversation_memory_derived_status", "\"Status\" IN ('candidate', 'archived')");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasMaxLength(80);
            entity.Property(value => value.JournalId).HasMaxLength(80).IsRequired();
            entity.Property(value => value.TaskId).HasMaxLength(200).IsRequired();
            entity.Property(value => value.CommandId).HasMaxLength(128).IsRequired();
            entity.Property(value => value.SourceFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.CandidateJson).HasMaxLength(64_000).IsRequired();
            entity.Property(value => value.ResultSchemaFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(value => value.CompletionEvidenceReference).HasMaxLength(200).IsRequired();
            entity.Property(value => value.Audience).HasMaxLength(20).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(value => new { value.JournalId, value.TaskId, value.CommandId }).IsUnique();
            entity.HasOne(value => value.Journal).WithMany(value => value.DerivedCandidates)
                .HasForeignKey(value => value.JournalId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationConversationMemoryDerivedSourceRecord>(entity =>
        {
            entity.ToTable("application_conversation_memory_derived_source");
            entity.HasKey(value => new { value.DerivedId, value.MessageId });
            entity.Property(value => value.DerivedId).HasMaxLength(80);
            entity.Property(value => value.MessageId).HasMaxLength(80);
            entity.HasOne(value => value.Derived).WithMany(value => value.Sources)
                .HasForeignKey(value => value.DerivedId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(value => value.Message).WithMany(value => value.DerivedSources)
                .HasForeignKey(value => value.MessageId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
