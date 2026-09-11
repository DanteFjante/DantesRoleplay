using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

// Review-only schema proposal. Production DantesRoleplayDbContext deliberately does not call this.
internal static class ProposedApplicationAuthoringModel
{
    internal static void Configure(ModelBuilder b)
    {
        b.Entity<ApplicationCandidateRevisionRecord>(e =>
        {
            e.ToTable("proposed_application_candidate_revision", t =>
            {
                t.HasCheckConstraint("CK_proposed_candidate_revision", "\"Revision\" > 0 AND \"ApplicationRevision\" > 0");
                t.HasCheckConstraint("CK_proposed_candidate_hashes", Hash("ContentFingerprint") + " AND " + Hash("CanonicalCommandFingerprint") + " AND (\"ExpectedActiveFingerprint\" IS NULL OR " + Hash("ExpectedActiveFingerprint") + ")");
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
            e.ToTable("proposed_application_candidate_document", t => t.HasCheckConstraint("CK_proposed_candidate_document_ordinal", "\"Ordinal\" >= 0"));
            e.HasKey(x => new { x.ApplicationId, x.CandidateId, x.Revision, x.Ordinal }); e.Property(x => x.ApplicationId).HasMaxLength(63); e.Property(x => x.CandidateId).HasMaxLength(32);
            e.HasIndex(x => new { x.ApplicationId, x.CandidateId, x.Revision, x.IdentityId }).IsUnique();
            e.HasOne<ApplicationCandidateRevisionRecord>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.CandidateId, x.Revision }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationActivationDocumentIdentityRecord>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.IdentityId }).HasPrincipalKey(x => new { x.ApplicationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationActivationDocumentEvidenceRecord>().WithMany().HasForeignKey(x => new { x.IdentityId, x.EvidenceVersion }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ApplicationCandidateValidationRecord>(e =>
        {
            e.ToTable("proposed_application_candidate_validation", t =>
            {
                t.HasCheckConstraint("CK_proposed_candidate_validation_outcome", "\"Outcome\" IN ('valid','invalid','unavailable')");
                t.HasCheckConstraint("CK_proposed_candidate_validation_valid_evidence", "\"Outcome\" <> 'valid' OR (\"PreparedEvidenceReference\" IS NOT NULL AND length(\"PreparedEvidenceReference\") > 0 AND \"ReuseEvidenceReference\" IS NOT NULL AND length(\"ReuseEvidenceReference\") > 0)");
                t.HasCheckConstraint("CK_proposed_candidate_validation_json", "json_valid(\"DiagnosticsJson\") AND json_valid(\"AlternativesJson\") AND length(CAST(\"DiagnosticsJson\" AS BLOB)) <= 16000 AND length(CAST(\"AlternativesJson\" AS BLOB)) <= 16000");
                t.HasCheckConstraint("CK_proposed_candidate_validation_revision", "\"Revision\" > 0");
                t.HasCheckConstraint("CK_proposed_candidate_validation_expected_hash", "\"ExpectedActiveFingerprint\" IS NULL OR " + Hash("ExpectedActiveFingerprint"));
            });
            e.HasKey(x => x.OperationId); e.Property(x => x.OperationId).HasMaxLength(200); e.Property(x => x.ApplicationId).HasMaxLength(63); e.Property(x => x.CandidateId).HasMaxLength(32);
            e.Property(x => x.CandidateFingerprint).HasMaxLength(64); e.Property(x => x.GrantReference).HasMaxLength(200); e.Property(x => x.ExpectedActiveFingerprint).HasMaxLength(64); e.Property(x => x.DependencyFingerprint).HasMaxLength(64);
            e.Property(x => x.PreparedEvidenceReference).HasMaxLength(200); e.Property(x => x.ReuseEvidenceReference).HasMaxLength(200); e.Property(x => x.DiagnosticsJson).HasMaxLength(16000); e.Property(x => x.AlternativesJson).HasMaxLength(16000);
            e.HasOne<Operation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
            e.HasAlternateKey(x => new { x.OperationId, x.ApplicationId, x.CandidateId, x.Revision });
            e.HasOne<ApplicationCandidateRevisionRecord>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.CandidateId, x.Revision }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ApplicationCandidatePublicationRecord>(e =>
        {
            e.ToTable("proposed_application_candidate_publication"); e.HasKey(x => x.ActivationOperationId); e.Property(x => x.ActivationOperationId).HasMaxLength(200); e.Property(x => x.ValidationOperationId).HasMaxLength(200); e.Property(x => x.ApplicationId).HasMaxLength(63); e.Property(x => x.CandidateId).HasMaxLength(32);
            e.HasOne<ApplicationActivationReceiptRecord>().WithMany().HasForeignKey(x => x.ActivationOperationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationCandidateValidationRecord>().WithMany().HasForeignKey(x => new { x.ValidationOperationId, x.ApplicationId, x.CandidateId, x.Revision }).HasPrincipalKey(x => new { x.OperationId, x.ApplicationId, x.CandidateId, x.Revision }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationCandidateRevisionRecord>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.CandidateId, x.Revision }).OnDelete(DeleteBehavior.Restrict);
        });
    }
    private static string Hash(string column) => "length(\"" + column + "\") = 64 AND \"" + column + "\" NOT GLOB '*[^0-9A-F]*'";
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
