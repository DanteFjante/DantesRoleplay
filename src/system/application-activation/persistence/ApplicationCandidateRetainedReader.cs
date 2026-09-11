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
    internal async Task<ApplicationCandidateRetainedMetadata?> ReadMetadataAsync(ApplicationIdentifier applicationId,
        string candidateId, int revision, CancellationToken cancellationToken = default)
    {
        var row = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == applicationId.Value && value.CandidateId == candidateId && value.Revision == revision, cancellationToken);
        if (row is null) return null;
        ValidateCandidateRow(row, applicationId);
        var applicationRevision = applications.Get(applicationId, row.ApplicationRevision)
            ?? throw Invalid("APPLICATION_CANDIDATE_INCONSISTENT", "Candidate evidence does not have its pinned application revision.");
        var values = await (from link in db.Set<ApplicationCandidateDocumentRecord>().AsNoTracking()
                            join identity in db.Set<ApplicationActivationDocumentIdentityRecord>().AsNoTracking()
                                on new { link.ApplicationId, link.IdentityId } equals new { identity.ApplicationId, IdentityId = identity.Id }
                            join evidence in db.Set<ApplicationActivationDocumentEvidenceRecord>().AsNoTracking()
                                on new { link.IdentityId, link.EvidenceVersion } equals new { evidence.IdentityId, evidence.EvidenceVersion }
                            where link.ApplicationId == applicationId.Value && link.CandidateId == candidateId && link.Revision == revision
                            orderby link.Ordinal
                            select new { link.Ordinal, identity.LogicalIdentity, evidence.SourceId, evidence.Trust, evidence.Precedence,
                                evidence.RelativePath, evidence.MediaType, evidence.ContentFingerprint, evidence.Length, evidence.IsText,
                                RetainedBytesLength = evidence.RetainedBytes == null ? (long?)null : evidence.RetainedBytes.LongLength })
            .Take(80001).ToArrayAsync(cancellationToken);
        if (values.Length is 0 or > 80000) throw Invalid("APPLICATION_CANDIDATE_INCONSISTENT", "Candidate document evidence exceeds its item bound.");
        var documents = new List<ActivatedApplicationDocument>(values.Length); var identities = new HashSet<string>(StringComparer.Ordinal); var paths = new HashSet<string>(StringComparer.Ordinal); long total = 0;
        foreach (var (value, ordinal) in values.Select((value, index) => (value, index)))
        {
            if (value.RetainedBytesLength is null)
                throw Invalid("ACTIVATION_EVIDENCE_MISSING", "Candidate document evidence is missing retained bytes.");
            if (value.RetainedBytesLength != value.Length)
                throw Invalid("ACTIVATION_EVIDENCE_CORRUPT", "Candidate retained document byte length does not match immutable evidence.");
            if (value.Ordinal != ordinal || !identities.Add(value.LogicalIdentity) || !paths.Add(value.RelativePath)
                || !Enum.IsDefined((SourceTrust)value.Trust) || !GenericSourceDocument.IsNormalizedRelativePath(value.RelativePath)
                || value.LogicalIdentity != "file:" + value.RelativePath || string.IsNullOrWhiteSpace(value.SourceId)
                || string.IsNullOrWhiteSpace(value.MediaType) || !UpperSha256(value.ContentFingerprint)
                || value.Length < 0 || value.Length > 10L * 1024 * 1024 || value.Length > 256L * 1024 * 1024 - total)
                throw Invalid("APPLICATION_CANDIDATE_INCONSISTENT", "Candidate document metadata is invalid.");
            total += value.Length;
            documents.Add(new(value.LogicalIdentity, value.SourceId, (SourceTrust)value.Trust, value.Precedence, value.RelativePath,
                value.MediaType, value.ContentFingerprint, value.Length, value.IsText));
        }
        var metadata = new ApplicationCandidateRetainedMetadata(row, applicationRevision, Array.AsReadOnly(documents.ToArray()));
        if (row.ContentFingerprint != ContentFingerprint(row, applicationRevision, metadata.Documents))
            throw Invalid("APPLICATION_CANDIDATE_FINGERPRINT_MISMATCH", "Candidate metadata does not match its immutable content fingerprint.");
        return metadata;
    }

    internal Task<IReadOnlyList<ApplicationCandidateDocument>> ReadSelectedAsync(ApplicationCandidateRetainedMetadata metadata,
        IReadOnlyCollection<string> exactPaths, CancellationToken cancellationToken = default) =>
        ReadDocumentsAsync(metadata, exactPaths, true, cancellationToken);

    private async Task<IReadOnlyList<ApplicationCandidateDocument>> ReadDocumentsAsync(ApplicationCandidateRetainedMetadata metadata,
        IReadOnlyCollection<string> exactPaths, bool selectedBounds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(exactPaths);
        var requested = exactPaths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if ((selectedBounds && requested.Length > 128) || requested.Any(path => !GenericSourceDocument.IsNormalizedRelativePath(path)))
            throw Invalid("APPLICATION_CANDIDATE_INCONSISTENT", "Candidate selected documents exceed their bound.");
        var selected = metadata.Documents.Where(value => requested.Contains(value.RelativePath, StringComparer.Ordinal)).ToArray();
        if (selected.Length != requested.Length || (selectedBounds && selected.Sum(value => value.Length) > 16L * 1024 * 1024))
            throw Invalid("APPLICATION_CANDIDATE_INCONSISTENT", "Candidate selected documents are unavailable.");
        var values = new List<ApplicationCandidateDocument>(selected.Length);
        foreach (var document in selected)
        {
            var bytes = await (from link in db.Set<ApplicationCandidateDocumentRecord>().AsNoTracking()
                               join identity in db.Set<ApplicationActivationDocumentIdentityRecord>().AsNoTracking() on new { link.ApplicationId, link.IdentityId } equals new { identity.ApplicationId, IdentityId = identity.Id }
                               join evidence in db.Set<ApplicationActivationDocumentEvidenceRecord>().AsNoTracking() on new { link.IdentityId, link.EvidenceVersion } equals new { evidence.IdentityId, evidence.EvidenceVersion }
                               where link.ApplicationId == metadata.RevisionRow.ApplicationId && link.CandidateId == metadata.RevisionRow.CandidateId
                                   && link.Revision == metadata.RevisionRow.Revision && identity.LogicalIdentity == document.LogicalIdentity
                                   && evidence.SourceId == document.SourceId && evidence.Trust == (int)document.Trust
                                   && evidence.Precedence == document.Precedence && evidence.RelativePath == document.RelativePath
                                   && evidence.MediaType == document.MediaType && evidence.IsText == document.IsText
                                   && evidence.ContentFingerprint == document.ContentFingerprint
                                   && evidence.Length == document.Length && evidence.RetainedBytes != null && evidence.RetainedBytes.Length == evidence.Length
                               select evidence.RetainedBytes).SingleOrDefaultAsync(cancellationToken)
                ?? throw Invalid("ACTIVATION_EVIDENCE_MISSING", "Candidate selected document evidence is missing.");
            if (bytes.LongLength != document.Length || HashBytes(bytes) != document.ContentFingerprint)
                throw Invalid("ACTIVATION_EVIDENCE_CORRUPT", "Candidate selected document evidence is corrupt.");
            values.Add(new(document, bytes.ToArray()));
        }
        return Array.AsReadOnly(values.ToArray());
    }
    internal async Task<ApplicationCandidateRetainedReadback?> ReadAsync(
        ApplicationIdentifier applicationId,
        string candidateId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var metadata = await ReadMetadataAsync(applicationId, candidateId, revision, cancellationToken);
        if (metadata is null) return null;
        var complete = await ReadDocumentsAsync(metadata, metadata.Documents.Select(value => value.RelativePath).ToArray(), false, cancellationToken);
        return new(metadata.RevisionRow, metadata.ApplicationRevision, complete);
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
        ContentFingerprint(row, applicationRevision, documents.Select(value => value.Document).ToArray());

    internal static string ContentFingerprint(ApplicationCandidateRevisionRecord row, ApplicationRevision applicationRevision,
        IReadOnlyList<ActivatedApplicationDocument> documents)
    {
        var header = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            row.ApplicationId, row.CandidateId, row.Revision, row.ApplicationRevision,
            applicationFingerprint = applicationRevision.Fingerprint, row.ExpectedActiveFingerprint, row.Origin,
            row.SynchronizationEvidenceReference, row.NewImplementationReason, row.AuthorGrantReference,
            row.SourceOperationId, row.CanonicalCommandFingerprint, documents = Array.Empty<object>()
        }));
        const string marker = "\"documents\":[]";
        var position = header.IndexOf(marker, StringComparison.Ordinal);
        if (position < 0) throw new InvalidOperationException("Candidate canonical header is invalid.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(string value) => hash.AppendData(System.Text.Encoding.UTF8.GetBytes(value));
        Append("dantes-roleplay/application-candidate-revision/v1\0");
        Append(header[..(position + "\"documents\":".Length)]); Append("[");
        for (var index = 0; index < documents.Count; index++)
        {
            if (index != 0) Append(",");
            var value = documents[index];
            Append(InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                value.LogicalIdentity, value.SourceId, value.Trust, value.Precedence, value.RelativePath,
                value.MediaType, value.ContentFingerprint, value.Length, value.IsText
            })));
        }
        Append("]"); Append(header[(position + marker.Length)..]);
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

internal sealed record ApplicationCandidateRetainedMetadata(ApplicationCandidateRevisionRecord RevisionRow,
    ApplicationRevision ApplicationRevision, IReadOnlyList<ActivatedApplicationDocument> Documents);

internal sealed record ApplicationCandidateRetainedReadback(
    ApplicationCandidateRevisionRecord RevisionRow,
    ApplicationRevision ApplicationRevision,
    IReadOnlyList<ApplicationCandidateDocument> Documents);
