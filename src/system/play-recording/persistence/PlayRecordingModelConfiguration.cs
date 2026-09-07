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
    }
}
