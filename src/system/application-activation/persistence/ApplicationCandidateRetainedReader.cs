using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Rehydrates a candidate only from its linked immutable retained evidence.</summary>
internal sealed class ApplicationCandidateRetainedReader(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications)
{
    internal async Task<ApplicationCandidateRetainedReadback?> ReadAsync(
        ApplicationIdentifier applicationId,
        string candidateId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var revisionRow = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.ApplicationId == applicationId.Value
                && value.CandidateId == candidateId && value.Revision == revision, cancellationToken);
        if (revisionRow is null) return null;
        ValidateCandidateRow(revisionRow, applicationId);
        var applicationRevision = applications.Get(applicationId, revisionRow.ApplicationRevision)
            ?? throw Invalid("APPLICATION_CANDIDATE_INCONSISTENT",
                "Candidate evidence does not have its pinned application revision.");
        var stored = await (from link in db.Set<ApplicationCandidateDocumentRecord>().AsNoTracking()
                            join identity in db.Set<ApplicationActivationDocumentIdentityRecord>().AsNoTracking()
                                on new { link.ApplicationId, link.IdentityId }
                                equals new { identity.ApplicationId, IdentityId = identity.Id }
                            join evidence in db.Set<ApplicationActivationDocumentEvidenceRecord>().AsNoTracking()
                                on new { link.IdentityId, link.EvidenceVersion }
                                equals new { evidence.IdentityId, evidence.EvidenceVersion }
                            where link.ApplicationId == applicationId.Value
                                  && link.CandidateId == candidateId
                                  && link.Revision == revision
                            orderby link.Ordinal
                            select new
                            {
                                link.Ordinal,
                                identity.LogicalIdentity,
                                evidence.IdentityId,
                                evidence.EvidenceVersion,
                                evidence.SourceId,
                                evidence.Trust,
                                evidence.Precedence,
                                evidence.RelativePath,
                                evidence.MediaType,
                                evidence.ContentFingerprint,
                                evidence.Length,
                                evidence.IsText,
                                RetainedBytesLength = evidence.RetainedBytes == null
                                    ? (int?)null
                                    : evidence.RetainedBytes.Length
                            })
            .Take(129)
            .ToArrayAsync(cancellationToken);
        if (stored.Length > 128)
            throw Invalid("APPLICATION_CANDIDATE_INCONSISTENT", "Candidate document evidence exceeds its item bound.");
        if (stored.Length == 0)
            throw Invalid("APPLICATION_CANDIDATE_INCONSISTENT", "Candidate document evidence is required.");

        var documents = new List<ApplicationCandidateDocument>(stored.Length);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var (value, ordinal) in stored.Select((value, index) => (value, index)))
        {
            if (value.Ordinal != ordinal)
                throw Invalid("APPLICATION_CANDIDATE_INCONSISTENT", "Candidate document ordinals are not contiguous.");
            if (!identities.Add(value.LogicalIdentity) || !paths.Add(value.RelativePath))
                throw Invalid("APPLICATION_CANDIDATE_INCONSISTENT", "Candidate document identities and paths must be unique.");
            if (!Enum.IsDefined((SourceTrust)value.Trust)
                || !GenericSourceDocument.IsNormalizedRelativePath(value.RelativePath)
                || value.LogicalIdentity != "file:" + value.RelativePath
                || string.IsNullOrWhiteSpace(value.SourceId)
                || string.IsNullOrWhiteSpace(value.MediaType)
                || !UpperSha256(value.ContentFingerprint))
                throw Invalid("APPLICATION_CANDIDATE_INCONSISTENT", "Candidate document metadata is invalid.");
            if (value.Length < 0 || value.Length > 16L * 1024 * 1024 - totalBytes)
                throw Invalid("APPLICATION_CANDIDATE_INCONSISTENT", "Candidate document evidence exceeds its byte bound.");
            if (value.RetainedBytesLength is null)
                throw Invalid("ACTIVATION_EVIDENCE_MISSING", "Candidate document evidence is missing retained bytes.");
            if (value.RetainedBytesLength != value.Length)
                throw Invalid("ACTIVATION_EVIDENCE_CORRUPT",
                    "Candidate retained document byte length does not match its immutable evidence.");
            var bytes = await db.Set<ApplicationActivationDocumentEvidenceRecord>().AsNoTracking()
                .Where(evidence => evidence.IdentityId == value.IdentityId
                    && evidence.EvidenceVersion == value.EvidenceVersion)
                .Select(evidence => evidence.RetainedBytes)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw Invalid("ACTIVATION_EVIDENCE_MISSING", "Candidate document evidence is missing retained bytes.");
            if (bytes.LongLength != value.Length || HashBytes(bytes) != value.ContentFingerprint)
                throw Invalid("ACTIVATION_EVIDENCE_CORRUPT",
                    "Candidate retained document bytes do not match their immutable evidence.");
            totalBytes += bytes.LongLength;
            documents.Add(new(new(value.LogicalIdentity, value.SourceId, (SourceTrust)value.Trust,
                value.Precedence, value.RelativePath, value.MediaType, value.ContentFingerprint,
                value.Length, value.IsText), bytes.ToArray()));
        }
        var result = new ApplicationCandidateRetainedReadback(revisionRow, applicationRevision,
            Array.AsReadOnly(documents.ToArray()));
        if (!string.Equals(revisionRow.ContentFingerprint,
                ContentFingerprint(result.RevisionRow,
                    result.ApplicationRevision, result.Documents), StringComparison.Ordinal))
            throw Invalid("APPLICATION_CANDIDATE_FINGERPRINT_MISMATCH",
                "Candidate metadata does not match its immutable content fingerprint.");
        return result;
    }

    private static string HashBytes(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static bool UpperSha256(string value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');
    private static ApplicationActivationException Invalid(string code, string message) => new(code, message);

    private static void ValidateCandidateRow(ApplicationCandidateRevisionRecord row, ApplicationIdentifier applicationId)
    {
        if (row.ApplicationId != applicationId.Value || row.Revision < 1 || row.ApplicationRevision < 1
            || row.CandidateId is not { Length: 32 }
            || row.CandidateId.Any(value => !(char.IsAsciiDigit(value) || value is >= 'a' and <= 'f'))
            || !UpperSha256(row.ContentFingerprint)
            || row.ExpectedActiveFingerprint is not null && !UpperSha256(row.ExpectedActiveFingerprint)
            || row.Origin is not ("runtime" or "catalog-sync")
            || row.SynchronizationEvidenceReference is { Length: 0 }
            || string.IsNullOrWhiteSpace(row.NewImplementationReason)
            || string.IsNullOrWhiteSpace(row.AuthorGrantReference)
            || string.IsNullOrWhiteSpace(row.SourceOperationId)
            || !UpperSha256(row.CanonicalCommandFingerprint))
            throw Invalid("APPLICATION_CANDIDATE_INCONSISTENT", "Candidate revision metadata is invalid.");
    }

    internal static string ContentFingerprint(
        ApplicationCandidateRevisionRecord row,
        ApplicationRevision applicationRevision,
        IReadOnlyList<ApplicationCandidateDocument> documents) =>
        InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-revision/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                row.ApplicationId,
                row.CandidateId,
                row.Revision,
                row.ApplicationRevision,
                applicationFingerprint = applicationRevision.Fingerprint,
                row.ExpectedActiveFingerprint,
                row.Origin,
                row.SynchronizationEvidenceReference,
                row.NewImplementationReason,
                row.AuthorGrantReference,
                row.SourceOperationId,
                row.CanonicalCommandFingerprint,
                documents = documents.Select(value => new
                {
                    value.Document.LogicalIdentity,
                    value.Document.SourceId,
                    value.Document.Trust,
                    value.Document.Precedence,
                    value.Document.RelativePath,
                    value.Document.MediaType,
                    value.Document.ContentFingerprint,
                    value.Document.Length,
                    value.Document.IsText
                })
            })));
}

internal sealed record ApplicationCandidateRetainedReadback(
    ApplicationCandidateRevisionRecord RevisionRow,
    ApplicationRevision ApplicationRevision,
    IReadOnlyList<ApplicationCandidateDocument> Documents);
