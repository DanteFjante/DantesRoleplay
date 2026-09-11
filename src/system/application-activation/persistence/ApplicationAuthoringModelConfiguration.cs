using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

// Frozen owner model. The coordinator owns DbContext registration and migrations.
internal static class ApplicationAuthoringModelConfiguration
{
    internal static void Configure(ModelBuilder b)
    {
        b.Entity<ApplicationCandidateRevisionRecord>(e =>
        {
            e.ToTable("system_application_candidate_revision", t =>
            {
                t.HasCheckConstraint("CK_system_candidate_revision", "\"Revision\" > 0 AND \"ApplicationRevision\" > 0");
                t.HasCheckConstraint("CK_system_candidate_hashes", Hash("ContentFingerprint") + " AND " + Hash("CanonicalCommandFingerprint") + " AND (\"ExpectedActiveFingerprint\" IS NULL OR " + Hash("ExpectedActiveFingerprint") + ")");
            });
            e.HasKey(x => new { x.ApplicationId, x.CandidateId, x.Revision });
            e.Property(x => x.ApplicationId).HasMaxLength(63); e.Property(x => x.CandidateId).HasMaxLength(32);
            e.Property(x => x.ContentFingerprint).HasMaxLength(64); e.Property(x => x.ExpectedActiveFingerprint).HasMaxLength(64);
            e.Property(x => x.Origin).HasMaxLength(32); e.Property(x => x.SynchronizationEvidenceReference).HasMaxLength(200);
            e.Property(x => x.NewImplementationReason).HasMaxLength(2000); e.Property(x => x.AuthorGrantReference).HasMaxLength(200);
            e.Property(x => x.SourceOperationId).HasMaxLength(200); e.Property(x => x.CanonicalCommandFingerprint).HasMaxLength(64);
            e.HasIndex(x => x.SourceOperationId).IsUnique();
            e.HasOne<ApplicationRevisionRecord>().WithMany().HasForeignKey(x => new { x.ApplicationId, Revision = x.ApplicationRevision }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Operation>().WithMany().HasForeignKey(x => x.SourceOperationId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ApplicationCandidateDocumentRecord>(e =>
        {
            e.ToTable("system_application_candidate_document", t => t.HasCheckConstraint("CK_system_candidate_document_ordinal", "\"Ordinal\" >= 0"));
            e.HasKey(x => new { x.ApplicationId, x.CandidateId, x.Revision, x.Ordinal }); e.Property(x => x.ApplicationId).HasMaxLength(63); e.Property(x => x.CandidateId).HasMaxLength(32);
            e.HasIndex(x => new { x.ApplicationId, x.CandidateId, x.Revision, x.IdentityId }).IsUnique();
            e.HasOne<ApplicationCandidateRevisionRecord>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.CandidateId, x.Revision }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationActivationDocumentIdentityRecord>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.IdentityId }).HasPrincipalKey(x => new { x.ApplicationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationActivationDocumentEvidenceRecord>().WithMany().HasForeignKey(x => new { x.IdentityId, x.EvidenceVersion }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ApplicationCandidateValidationRecord>(e =>
        {
            e.ToTable("system_application_candidate_validation", t =>
            {
                t.HasCheckConstraint("CK_system_candidate_validation_outcome", "\"Outcome\" IN ('valid','invalid','unavailable')");
                t.HasCheckConstraint("CK_system_candidate_validation_valid_evidence", """
                    "Outcome" <> 'valid' OR COALESCE(
                        length(trim("PreparationVersion")) BETWEEN 1 AND 100
                        AND length(trim("PreparedEvidenceReference")) BETWEEN 1 AND 200
                        AND length("ManualPacketResultFingerprint") = 64
                        AND length(trim("ReuseEvidenceReference")) BETWEEN 1 AND 200
                        AND length(trim("DependencyEvidenceReference")) BETWEEN 1 AND 200
                        AND "DependenciesComplete" = 1, 0)
                    """);
                t.HasCheckConstraint("CK_system_candidate_validation_json", "json_valid(\"DiagnosticsJson\") AND json_valid(\"AlternativesJson\") AND length(CAST(\"DiagnosticsJson\" AS BLOB)) <= 16000 AND length(CAST(\"AlternativesJson\" AS BLOB)) <= 16000");
                t.HasCheckConstraint("CK_system_candidate_validation_revision", "\"Revision\" > 0");
                t.HasCheckConstraint("CK_system_candidate_validation_expected_hash", "(\"ExpectedActiveFingerprint\" IS NULL OR " + Hash("ExpectedActiveFingerprint") + ") AND " + Hash("CandidateFingerprint") + " AND " + Hash("DependencyFingerprint") + " AND (\"ManualPacketResultFingerprint\" IS NULL OR " + Hash("ManualPacketResultFingerprint") + ") AND " + Hash("CanonicalCommandFingerprint"));
                t.HasCheckConstraint("CK_system_candidate_validation_dependencies", """
                    "DependenciesComplete" IN (0,1) AND
                    CASE WHEN json_valid("DependenciesJson") THEN COALESCE(
                        json_type("DependenciesJson") = 'array'
                        AND length(CAST("DependenciesJson" AS BLOB)) <= 65536
                        AND json_array_length("DependenciesJson") <= 64, 0)
                    ELSE 0 END
                    """);
            });
            e.HasKey(x => x.OperationId); e.Property(x => x.OperationId).HasMaxLength(200); e.Property(x => x.ApplicationId).HasMaxLength(63); e.Property(x => x.CandidateId).HasMaxLength(32);
            e.Property(x => x.CandidateFingerprint).HasMaxLength(64); e.Property(x => x.GrantReference).HasMaxLength(200); e.Property(x => x.ExpectedActiveFingerprint).HasMaxLength(64); e.Property(x => x.DependencyFingerprint).HasMaxLength(64);
            e.Property(x => x.PreparationVersion).HasMaxLength(100); e.Property(x => x.ManualPacketResultFingerprint).HasMaxLength(64); e.Property(x => x.CanonicalCommandFingerprint).HasMaxLength(64); e.Property(x => x.DependenciesJson).HasMaxLength(65536); e.Property(x => x.DependencyEvidenceReference).HasMaxLength(200); e.Property(x => x.PreparedEvidenceReference).HasMaxLength(200); e.Property(x => x.ReuseEvidenceReference).HasMaxLength(200); e.Property(x => x.DiagnosticsJson).HasMaxLength(16000); e.Property(x => x.AlternativesJson).HasMaxLength(16000);
            e.HasOne<Operation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
            e.HasAlternateKey(x => new { x.OperationId, x.ApplicationId, x.CandidateId, x.Revision });
            e.HasOne<ApplicationCandidateRevisionRecord>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.CandidateId, x.Revision }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ApplicationCandidatePublicationRecord>(e =>
        {
            e.ToTable("system_application_candidate_publication"); e.HasKey(x => x.ActivationOperationId); e.Property(x => x.ActivationOperationId).HasMaxLength(200); e.Property(x => x.ValidationOperationId).HasMaxLength(200); e.Property(x => x.ApplicationId).HasMaxLength(63); e.Property(x => x.CandidateId).HasMaxLength(32);
            e.HasOne<ApplicationActivationReceiptRecord>().WithMany().HasForeignKey(x => x.ActivationOperationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationCandidateValidationRecord>().WithMany().HasForeignKey(x => new { x.ValidationOperationId, x.ApplicationId, x.CandidateId, x.Revision }).HasPrincipalKey(x => new { x.OperationId, x.ApplicationId, x.CandidateId, x.Revision }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationCandidateRevisionRecord>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.CandidateId, x.Revision }).OnDelete(DeleteBehavior.Restrict);
        });
    }
    private static string Hash(string column) => "\"" + column + "\" IS NOT NULL AND length(\"" + column + "\") = 64 AND \"" + column + "\" NOT GLOB '*[^0-9A-F]*'";
}

internal sealed class ApplicationCandidateRevisionRecord
{
    public required string ApplicationId { get; set; }
    public required string CandidateId { get; set; }
    public int Revision { get; set; }
    public int ApplicationRevision { get; set; }
    public required string ContentFingerprint { get; set; }
    public string? ExpectedActiveFingerprint { get; set; }
    public required string Origin { get; set; }
    public string? SynchronizationEvidenceReference { get; set; }
    public required string NewImplementationReason { get; set; }
    public required string AuthorGrantReference { get; set; }
    public required string SourceOperationId { get; set; }
    public required string CanonicalCommandFingerprint { get; set; }
}
internal sealed class ApplicationCandidateDocumentRecord
{
    public required string ApplicationId { get; set; }
    public required string CandidateId { get; set; }
    public int Revision { get; set; }
    public int Ordinal { get; set; }
    public long IdentityId { get; set; }
    public int EvidenceVersion { get; set; }
}
internal sealed class ApplicationCandidateValidationRecord
{
    public required string OperationId { get; set; }
    public required string ApplicationId { get; set; }
    public required string CandidateId { get; set; }
    public int Revision { get; set; }
    public required string CandidateFingerprint { get; set; }
    public required string GrantReference { get; set; }
    public string? ExpectedActiveFingerprint { get; set; }
    public required string DependencyFingerprint { get; set; }
    public string? PreparationVersion { get; set; }
    public string? ManualPacketResultFingerprint { get; set; }
    public required string CanonicalCommandFingerprint { get; set; }
    /// <summary>
    /// Canonical exact dependency pins, recomputed by the activation owner from retained effective
    /// documents; never accepted from caller JSON or inferred solely from an evidence-reference name.
    /// A future service must persist this collection and its fingerprint before recording success,
    /// and rehydrate/recompute both against the same retained bytes before publication. This model
    /// describes storage only; it does not implement that extraction or dependency preparation.
    /// </summary>
    public required string DependenciesJson { get; set; }
    public bool DependenciesComplete { get; set; }
    public string? DependencyEvidenceReference { get; set; }
    public string? PreparedEvidenceReference { get; set; }
    public string? ReuseEvidenceReference { get; set; }
    public required string Outcome { get; set; }
    public required string DiagnosticsJson { get; set; }
    public required string AlternativesJson { get; set; }
}
internal sealed class ApplicationCandidatePublicationRecord
{
    public required string ActivationOperationId { get; set; }
    public required string ValidationOperationId { get; set; }
    public required string ApplicationId { get; set; }
    public required string CandidateId { get; set; }
    public int Revision { get; set; }
}
