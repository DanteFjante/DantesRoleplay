using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Owns the relational model for application activation evidence and receipts.</summary>
internal static class ApplicationActivationModelConfiguration
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationActivationRevisionRecord>(entity =>
        {
            entity.ToTable("system_application_activation_revision", table =>
            {
                table.HasCheckConstraint("CK_system_application_activation_revision_number", "\"ActivationRevision\" > 0 AND \"ApplicationRevision\" > 0");
                table.HasCheckConstraint("CK_system_application_activation_revision_hashes", "length(\"ApplicationFingerprint\") = 64 AND \"ApplicationFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"PreviewFingerprint\") = 64 AND \"PreviewFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ScannedDocumentsFingerprint\") = 64 AND \"ScannedDocumentsFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"CandidateManifestFingerprint\") = 64 AND \"CandidateManifestFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"DependencyGraphFingerprint\") = 64 AND \"DependencyGraphFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ResolutionFingerprint\") = 64 AND \"ResolutionFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ActivationFingerprint\") = 64 AND \"ActivationFingerprint\" NOT GLOB '*[^0-9A-F]*'");
            });
            entity.HasKey(x => new { x.ApplicationId, x.ActivationRevision });
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.ApplicationFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PreviewFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ScannedDocumentsFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CandidateManifestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DependencyGraphFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ResolutionFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ActivationFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DependencyCoverageVersion).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ActivatedByOperationId).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.ActivationFingerprint });
            entity.HasOne<ApplicationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, Revision = x.ApplicationRevision })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Operation>()
                .WithMany()
                .HasForeignKey(x => x.ActivatedByOperationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationActivationCurrentRecord>(entity =>
        {
            entity.ToTable("system_application_activation_current", table =>
                table.HasCheckConstraint("CK_system_application_activation_current_revision", "\"ActivationRevision\" > 0"));
            entity.HasKey(x => x.ApplicationId);
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.HasOne<ApplicationActivationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.ActivationRevision })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationActivationSourceRecord>(entity =>
        {
            entity.ToTable("system_application_activation_source", table =>
            {
                table.HasCheckConstraint("CK_system_application_activation_source_counts", "\"Ordinal\" >= 0 AND \"DocumentCount\" >= 0 AND \"ProblemCount\" >= 0");
                table.HasCheckConstraint("CK_system_application_activation_source_hash", "length(\"RegistrationFingerprint\") = 64 AND \"RegistrationFingerprint\" NOT GLOB '*[^0-9A-F]*'");
            });
            entity.HasKey(x => new { x.ApplicationId, x.ActivationRevision, x.Ordinal });
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.SourceId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RegistrationFingerprint).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.ActivationRevision, x.SourceId }).IsUnique();
            entity.HasOne<ApplicationActivationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.ActivationRevision })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationActivationExtensionRecord>(entity =>
        {
            entity.ToTable("system_application_activation_extension", table =>
            {
                table.HasCheckConstraint("CK_system_application_activation_extension_values", "\"Ordinal\" >= 0 AND length(\"SourceIdsJson\") >= 2 AND json_valid(\"SourceIdsJson\") AND length(\"NamespaceIdsJson\") >= 2 AND json_valid(\"NamespaceIdsJson\") AND length(\"HigherPriorityThanJson\") >= 2 AND json_valid(\"HigherPriorityThanJson\")");
                table.HasCheckConstraint("CK_system_application_activation_extension_hash", "length(\"RegistrationFingerprint\") = 64 AND \"RegistrationFingerprint\" NOT GLOB '*[^0-9A-F]*'");
            });
            entity.HasKey(x => new { x.ApplicationId, x.ActivationRevision, x.Ordinal });
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.ExtensionId).HasMaxLength(63).IsRequired();
            entity.Property(x => x.RegistrationFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.SourceIdsJson).IsRequired();
            entity.Property(x => x.NamespaceIdsJson).IsRequired();
            entity.Property(x => x.HigherPriorityThanJson).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.ActivationRevision, x.ExtensionId }).IsUnique();
            entity.HasOne<ApplicationActivationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.ActivationRevision })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationActivationDocumentIdentityRecord>(entity =>
        {
            entity.ToTable("system_application_activation_document_identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.ApplicationId).HasMaxLength(63).IsRequired();
            entity.Property(x => x.LogicalIdentity).HasMaxLength(1200).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.LogicalIdentity }).IsUnique();
            entity.HasOne<ApplicationRegistryRecord>()
                .WithMany()
                .HasForeignKey(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationActivationDocumentEvidenceRecord>(entity =>
        {
            entity.ToTable("system_application_activation_document_evidence", table =>
            {
                table.HasCheckConstraint("CK_system_application_activation_document_evidence_values", "\"EvidenceVersion\" > 0 AND \"Trust\" IN (0, 1) AND \"Length\" >= 0");
                table.HasCheckConstraint("CK_system_application_activation_document_evidence_hash", "length(\"ContentFingerprint\") = 64 AND \"ContentFingerprint\" NOT GLOB '*[^0-9A-F]*'");
            });
            entity.HasKey(x => new { x.IdentityId, x.EvidenceVersion });
            entity.Property(x => x.SourceId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RelativePath).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.MediaType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ContentFingerprint).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new
            {
                x.IdentityId,
                x.SourceId,
                x.Trust,
                x.Precedence,
                x.RelativePath,
                x.MediaType,
                x.ContentFingerprint,
                x.Length,
                x.IsText
            }).IsUnique();
            entity.HasOne<ApplicationActivationDocumentIdentityRecord>()
                .WithMany()
                .HasForeignKey(x => x.IdentityId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationActivationDocumentRecord>(entity =>
        {
            entity.ToTable("system_application_activation_document", table =>
                table.HasCheckConstraint("CK_system_application_activation_document_values", "\"Ordinal\" >= 0 AND \"EvidenceVersion\" > 0"));
            entity.HasKey(x => new { x.ApplicationId, x.ActivationRevision, x.Ordinal });
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.HasIndex(x => new { x.ApplicationId, x.ActivationRevision, x.IdentityId }).IsUnique();
            entity.HasOne<ApplicationActivationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.ActivationRevision })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationActivationDocumentIdentityRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.IdentityId })
                .HasPrincipalKey(x => new { x.ApplicationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationActivationDocumentEvidenceRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.IdentityId, x.EvidenceVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationActivationReceiptRecord>(entity =>
        {
            entity.ToTable("system_application_activation_receipt", table =>
            {
                table.HasCheckConstraint("CK_system_application_activation_receipt_hash", "length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                table.HasCheckConstraint("CK_system_application_activation_receipt_outcome", "\"Outcome\" IN ('activated', 'unchanged')");
            });
            entity.HasKey(x => x.OperationId);
            entity.Property(x => x.OperationId).HasMaxLength(200);
            entity.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ApplicationId).HasMaxLength(63);
            entity.Property(x => x.Outcome).HasMaxLength(20).IsRequired();
            entity.HasOne<Operation>()
                .WithMany()
                .HasForeignKey(x => x.OperationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationActivationRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.ApplicationId, x.ActivationRevision })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
