using DantesRoleplay.Applications;
using DantesRoleplay.Information;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

// Frozen owner model. The coordinator owns DbContext registration and migrations.
internal static class InformationSourceOwnershipModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InformationSourceTargetIdentityRecord>(entity =>
        {
            entity.ToTable("system_information_source_target_identity");
            entity.HasKey(x => x.QualifiedTargetId);
            entity.Property(x => x.QualifiedTargetId).HasMaxLength(200);
            entity.Property(x => x.SourceId).HasMaxLength(200);
            entity.Property(x => x.CreatedByOperationId).HasMaxLength(200);
            entity.HasAlternateKey(x => new { x.QualifiedTargetId, x.SourceId });
            entity.HasOne<InformationSource>().WithMany().HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Operation>().WithMany().HasForeignKey(x => x.CreatedByOperationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InformationSourceOwnerRevisionRecord>(entity =>
        {
            entity.ToTable("system_information_source_owner_revision", table =>
            {
                table.HasCheckConstraint("CK_system_information_source_owner_revision_positive", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_system_information_source_owner_revision_hashes",
                    Hash("ContentFingerprint") + " AND (\"PreviousFingerprint\" IS NULL OR " + Hash("PreviousFingerprint") + ")");
                table.HasCheckConstraint("CK_system_information_source_owner_revision_previous",
                    "(\"Revision\" = 1 AND \"PreviousFingerprint\" IS NULL) OR (\"Revision\" > 1 AND \"PreviousFingerprint\" IS NOT NULL)");
            });
            entity.HasKey(x => new { x.SourceId, x.Revision });
            entity.Property(x => x.SourceId).HasMaxLength(200);
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.QualifiedTargetId).HasMaxLength(200);
            entity.Property(x => x.ContentFingerprint).HasMaxLength(64);
            entity.Property(x => x.PreviousFingerprint).HasMaxLength(64);
            entity.Property(x => x.BoundByOperationId).HasMaxLength(200);
            entity.HasAlternateKey(x => new { x.SourceId, x.Revision, x.QualifiedTargetId });
            entity.HasOne<InformationSource>().WithMany().HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationRegistryRecord>().WithMany().HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Operation>().WithMany().HasForeignKey(x => x.BoundByOperationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InformationSourceTargetIdentityRecord>().WithMany()
                .HasForeignKey(x => new { x.QualifiedTargetId, x.SourceId })
                .HasPrincipalKey(x => new { x.QualifiedTargetId, x.SourceId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InformationSourceOwnerCurrentRecord>(entity =>
        {
            entity.ToTable("system_information_source_owner_current", table =>
                table.HasCheckConstraint("CK_system_information_source_owner_current_revision", "\"Revision\" > 0"));
            entity.HasKey(x => x.SourceId);
            entity.Property(x => x.SourceId).HasMaxLength(200);
            entity.Property(x => x.QualifiedTargetId).HasMaxLength(200);
            entity.HasIndex(x => x.QualifiedTargetId).IsUnique();
            entity.HasOne<InformationSource>().WithMany().HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InformationSourceOwnerRevisionRecord>().WithMany()
                .HasForeignKey(x => new { x.SourceId, x.Revision, x.QualifiedTargetId })
                .HasPrincipalKey(x => new { x.SourceId, x.Revision, x.QualifiedTargetId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static string Hash(string column) => "length(\"" + column + "\") = 64 AND \"" + column + "\" NOT GLOB '*[^0-9A-F]*'";
}

internal sealed class InformationSourceTargetIdentityRecord
{
    public required string QualifiedTargetId { get; set; }
    public required string SourceId { get; set; }
    public required string CreatedByOperationId { get; set; }
}

internal sealed class InformationSourceOwnerRevisionRecord
{
    public required string SourceId { get; set; }
    public int Revision { get; set; }
    public required string ApplicationId { get; set; }
    public required string QualifiedTargetId { get; set; }
    public required string ContentFingerprint { get; set; }
    public string? PreviousFingerprint { get; set; }
    public required string BoundByOperationId { get; set; }
}

internal sealed class InformationSourceOwnerCurrentRecord
{
    public required string SourceId { get; set; }
    public int Revision { get; set; }
    public required string QualifiedTargetId { get; set; }
}
