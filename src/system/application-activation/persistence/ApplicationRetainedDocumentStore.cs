using System.Security.Cryptography;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Owns immutable document identity and evidence retention within the caller's transaction.</summary>
internal sealed class ApplicationRetainedDocumentStore(DantesRoleplayDbContext db)
{
    internal async Task<IReadOnlyList<ApplicationRetainedDocumentLink>> RetainAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<ActivatedApplicationDocument> winners,
        IReadOnlyDictionary<string, byte[]>? retainedBytes,
        CancellationToken cancellationToken)
    {
        var identities = await db.Set<ApplicationActivationDocumentIdentityRecord>()
            .Where(value => value.ApplicationId == applicationId.Value)
            .ToDictionaryAsync(value => value.LogicalIdentity, StringComparer.Ordinal, cancellationToken);
        foreach (var logicalIdentity in winners.Select(value => value.LogicalIdentity).Distinct(StringComparer.Ordinal))
        {
            if (identities.ContainsKey(logicalIdentity)) continue;
            var identity = new ApplicationActivationDocumentIdentityRecord
            {
                ApplicationId = applicationId.Value,
                LogicalIdentity = logicalIdentity
            };
            identities.Add(logicalIdentity, identity);
            db.Add(identity);
        }
        await db.SaveChangesAsync(cancellationToken);

        var identityIds = identities.Values.Select(value => value.Id).ToArray();
        var evidence = await db.Set<ApplicationActivationDocumentEvidenceRecord>()
            .Where(value => identityIds.Contains(value.IdentityId))
            .ToArrayAsync(cancellationToken);
        var evidenceByIdentity = evidence.GroupBy(value => value.IdentityId)
            .ToDictionary(value => value.Key, value => value.ToList());
        var links = new List<ApplicationRetainedDocumentLink>(winners.Count);
        foreach (var (document, ordinal) in winners.Select((value, index) => (value, index)))
        {
            var identity = identities[document.LogicalIdentity];
            if (!evidenceByIdentity.TryGetValue(identity.Id, out var candidates))
            {
                candidates = [];
                evidenceByIdentity.Add(identity.Id, candidates);
            }
            var retained = candidates.SingleOrDefault(value => SameEvidence(value, document));
            var bytes = retainedBytes?.GetValueOrDefault(document.LogicalIdentity);
            if (bytes is not null && (bytes.LongLength != document.Length || HashBytes(bytes) != document.ContentFingerprint))
                throw Invalid("ACTIVATION_EVIDENCE_CORRUPT",
                    "Retained activation document bytes do not match their immutable evidence.");
            if (retained is null)
            {
                retained = new ApplicationActivationDocumentEvidenceRecord
                {
                    IdentityId = identity.Id,
                    EvidenceVersion = candidates.Select(value => value.EvidenceVersion).DefaultIfEmpty().Max() + 1,
                    SourceId = document.SourceId,
                    Trust = (int)document.Trust,
                    Precedence = document.Precedence,
                    RelativePath = document.RelativePath,
                    MediaType = document.MediaType,
                    ContentFingerprint = document.ContentFingerprint,
                    Length = document.Length,
                    IsText = document.IsText,
                    RetainedBytes = bytes?.ToArray()
                };
                candidates.Add(retained);
                db.Add(retained);
            }
            else
            {
                if (retained.RetainedBytes is { } existingBytes &&
                    (existingBytes.LongLength != retained.Length || HashBytes(existingBytes) != retained.ContentFingerprint))
                    throw Invalid("ACTIVATION_EVIDENCE_CORRUPT",
                        "Retained activation document bytes do not match their immutable evidence.");
                if (bytes is not null && retained.RetainedBytes is null)
                {
                    var wasPrepared = await (from link in db.Set<ApplicationActivationDocumentRecord>()
                        join prior in db.Set<ApplicationActivationRevisionRecord>()
                            on new { link.ApplicationId, link.ActivationRevision }
                            equals new { prior.ApplicationId, prior.ActivationRevision }
                        where link.IdentityId == retained.IdentityId
                            && link.EvidenceVersion == retained.EvidenceVersion
                            && prior.PreparationVersion != null
                        select link).AnyAsync(cancellationToken);
                    var isCandidateEvidence = await db.Set<ApplicationCandidateDocumentRecord>()
                        .AnyAsync(link => link.IdentityId == retained.IdentityId
                            && link.EvidenceVersion == retained.EvidenceVersion, cancellationToken);
                    if (wasPrepared)
                        throw Invalid("ACTIVATION_EVIDENCE_MISSING",
                            "A prepared activation revision is missing its retained document bytes.");
                    if (isCandidateEvidence)
                        throw Invalid("ACTIVATION_EVIDENCE_MISSING",
                            "Candidate-linked document evidence is missing its retained bytes.");
                    // Only legacy metadata-only evidence may acquire bytes at a new activation.
                    retained.RetainedBytes = bytes.ToArray();
                }
            }
            links.Add(new(ordinal, identity.Id, retained.EvidenceVersion));
        }
        return Array.AsReadOnly(links.ToArray());
    }

    private static bool SameEvidence(
        ApplicationActivationDocumentEvidenceRecord retained,
        ActivatedApplicationDocument candidate) =>
        retained.SourceId == candidate.SourceId
        && retained.Trust == (int)candidate.Trust
        && retained.Precedence == candidate.Precedence
        && retained.RelativePath == candidate.RelativePath
        && retained.MediaType == candidate.MediaType
        && retained.ContentFingerprint == candidate.ContentFingerprint
        && retained.Length == candidate.Length
        && retained.IsText == candidate.IsText;

    private static string HashBytes(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static ApplicationActivationException Invalid(string code, string message) => new(code, message);
}

internal sealed record ApplicationRetainedDocumentLink(int Ordinal, long IdentityId, int EvidenceVersion);
