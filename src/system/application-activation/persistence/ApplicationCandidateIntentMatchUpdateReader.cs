using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>
/// Proves the narrow metadata-only publication case where one existing trusted procedure or
/// mechanic changes only its explicit Matches section. It neither creates a definition nor
/// treats similarity, model output, or runtime execution as equivalence evidence.
/// </summary>
internal sealed class ApplicationCandidateIntentMatchUpdateReader(
    DantesRoleplayDbContext db, IApplicationRegistry applications,
    IApplicationActivationReader activations, IActivatedApplicationEvidenceReader evidence)
{
    internal async Task<ApplicationCandidateIntentMatchUpdateEvidence?> ReadAsync(
        ApplicationCandidateReference candidate, CancellationToken cancellationToken = default)
    {
        var retained = await new ApplicationCandidateRetainedReader(db, applications)
            .ReadMetadataAsync(candidate.ApplicationId, candidate.CandidateId, candidate.Revision, cancellationToken);
        if (retained is null || retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint)
            return null;
        var basis = await ApplicationCandidateDocumentSelection.ReadBaseAsync(db, activations,
            candidate.ApplicationId, retained.RevisionRow.ExpectedActiveFingerprint, cancellationToken);
        if (basis?.PreparationVersion is null || basis.Winners.Count != retained.Documents.Count
            || basis.ApplicationRevision != retained.ApplicationRevision.Revision
            || basis.ApplicationFingerprint != retained.ApplicationRevision.Fingerprint)
            return null;

        var original = basis.Winners.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        var changed = retained.Documents.Where(value => !original.TryGetValue(value.RelativePath, out var prior)
                || prior != value).ToArray();
        if (changed.Length != 1 || !original.TryGetValue(changed[0].RelativePath, out var predecessorDocument))
            return null;
        var successorDocument = changed[0];
        if (!successorDocument.IsText || successorDocument.Trust != SourceTrust.Trusted
            || !successorDocument.RelativePath.EndsWith(".md", StringComparison.Ordinal)
            || successorDocument with
            {
                ContentFingerprint = predecessorDocument.ContentFingerprint,
                Length = predecessorDocument.Length
            } != predecessorDocument)
            return null;

        if (!ActivatedApplicationCatalogMaterializer.TryRecordKind(successorDocument.RelativePath, out var kind)
            || kind is not ("procedure" or "mechanic"))
            return null;
        var paths = new List<string> { successorDocument.RelativePath };
        if (kind == "mechanic")
        {
            var sidecarPath = Path.ChangeExtension(successorDocument.RelativePath, ".js").Replace('\\', '/');
            var newSidecar = retained.Documents.SingleOrDefault(value => value.RelativePath == sidecarPath);
            if (!original.TryGetValue(sidecarPath, out var oldSidecar)
                || newSidecar is null
                || oldSidecar != newSidecar)
                return null;
            paths.Add(sidecarPath);
        }

        var selected = await new ApplicationCandidateRetainedReader(db, applications)
            .ReadSelectedAsync(retained, paths, cancellationToken);
        var successorWinners = selected.ToDictionary(value => value.Document.RelativePath,
            value => value.Document, StringComparer.Ordinal);
        var successorBytes = selected.ToDictionary(value => value.Document.RelativePath,
            value => value.RetainedBytes, StringComparer.Ordinal);
        var predecessorWinners = paths.ToDictionary(path => path, path => original[path], StringComparer.Ordinal);
        var predecessorBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var document = predecessorWinners[path];
            var retainedEvidence = evidence.ReadDocumentEvidence(candidate.ApplicationId,
                basis.ActivationRevision, document.LogicalIdentity);
            if (retainedEvidence is null || retainedEvidence.IsLegacyMetadataOnly
                || retainedEvidence.RetainedBytes is not { } bytes
                || retainedEvidence.ApplicationId != candidate.ApplicationId
                || retainedEvidence.ActivationRevision != basis.ActivationRevision
                || retainedEvidence.LogicalIdentity != document.LogicalIdentity
                || retainedEvidence.ContentFingerprint != document.ContentFingerprint
                || retainedEvidence.Length != document.Length || bytes.LongLength != document.Length
                || Convert.ToHexString(SHA256.HashData(bytes)) != document.ContentFingerprint)
                return null;
            predecessorBytes.Add(path, bytes);
        }

        var predecessor = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(candidate.ApplicationId,
            predecessorDocument, predecessorWinners, predecessorBytes);
        var successor = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(candidate.ApplicationId,
            successorDocument, successorWinners, successorBytes);
        if (predecessor is null || successor is null || predecessor.Kind != kind || successor.Kind != kind
            || predecessor.QualifiedId != successor.QualifiedId || predecessor.Version != successor.Version
            || predecessor.SourceId != successor.SourceId || predecessor.SourceLogicalPath != successor.SourceLogicalPath
            || predecessor.Status != "active" || successor.Status != "active"
            || !ExactMatchesEdit(kind, predecessorBytes, successorBytes, successorDocument.RelativePath))
            return null;

        var predecessorReference = Reference(predecessor);
        var successorReference = Reference(successor);
        var sourceOperation = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == retained.RevisionRow.SourceOperationId, cancellationToken);
        if (sourceOperation is null || !ApplicationCandidateOperationProof.WriteMatches(
                sourceOperation, retained, [successorReference], out _))
            return null;
        var publication = await db.Set<ApplicationActivationReceiptRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OperationId == basis.ActivatedByOperationId
                && value.ApplicationId == candidate.ApplicationId.Value
                && value.ActivationRevision == basis.ActivationRevision
                && value.Outcome == "activated", cancellationToken);
        var publicationOperation = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == basis.ActivatedByOperationId && value.Success, cancellationToken);
        if (publication is null || publicationOperation is null) return null;

        return ApplicationCandidateIntentMatchUpdateEvidence.Create(retained, basis, candidate,
            predecessorReference, successorReference, predecessor, successor);
    }

    private static bool ExactMatchesEdit(string kind,
        IReadOnlyDictionary<string, byte[]> predecessorBytes,
        IReadOnlyDictionary<string, byte[]> successorBytes, string markdownPath)
    {
        try
        {
            var before = Strict(predecessorBytes[markdownPath]);
            var after = Strict(successorBytes[markdownPath]);
            IReadOnlyList<string> phrases;
            string expected;
            if (kind == "procedure")
            {
                var parsed = ProcedureFile.Parse(after, markdownPath);
                phrases = Phrases(parsed.Matches);
                expected = ProcedureIntentPhraseEditor.Edit(before, phrases);
            }
            else
            {
                var sidecarPath = Path.ChangeExtension(markdownPath, ".js").Replace('\\', '/');
                var sidecar = Strict(successorBytes[sidecarPath]);
                var parsed = MechanicFile.Parse(after, markdownPath, sidecar);
                phrases = Phrases(parsed.Matches);
                expected = ProcedureIntentPhraseEditor.EditMechanic(before, sidecar, phrases);
            }
            return expected == after;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or DecoderFallbackException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> Phrases(string matches) => matches
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string Strict(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes);
    private static StandingGrantDefinitionReference Reference(CatalogRecordDefinition value) =>
        new(value.QualifiedId, value.Kind, value.Version, value.ContentFingerprint);
}

internal sealed class ApplicationCandidateIntentMatchUpdateEvidence
{
    private ApplicationCandidateIntentMatchUpdateEvidence(
        ApplicationCandidateRetainedMetadata retained, ActiveApplicationManifest basis,
        ApplicationCandidateReference candidate, StandingGrantDefinitionReference predecessor,
        StandingGrantDefinitionReference successor, CatalogRecordDefinition predecessorRecord,
        CatalogRecordDefinition successorRecord)
    {
        Retained = retained;
        Basis = basis;
        Candidate = candidate;
        Predecessor = predecessor;
        Successor = successor;
        PredecessorRecord = predecessorRecord;
        SuccessorRecord = successorRecord;
        Fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/intent-match-update/v1",
            InteractionCanonicalJson.CanonicalizeObject(System.Text.Json.JsonSerializer.Serialize(new
            {
                candidate, basis = basis.ActivationFingerprint, predecessor, successor,
                sourcePath = successorRecord.SourceLogicalPath
            })));
    }

    internal ApplicationCandidateRetainedMetadata Retained { get; }
    internal ActiveApplicationManifest Basis { get; }
    internal ApplicationCandidateReference Candidate { get; }
    internal StandingGrantDefinitionReference Predecessor { get; }
    internal StandingGrantDefinitionReference Successor { get; }
    internal CatalogRecordDefinition PredecessorRecord { get; }
    internal CatalogRecordDefinition SuccessorRecord { get; }
    internal string Fingerprint { get; }

    internal static ApplicationCandidateIntentMatchUpdateEvidence Create(
        ApplicationCandidateRetainedMetadata retained, ActiveApplicationManifest basis,
        ApplicationCandidateReference candidate, StandingGrantDefinitionReference predecessor,
        StandingGrantDefinitionReference successor, CatalogRecordDefinition predecessorRecord,
        CatalogRecordDefinition successorRecord) => new(retained, basis, candidate, predecessor,
            successor, predecessorRecord, successorRecord);
}
