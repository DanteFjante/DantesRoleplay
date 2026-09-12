using System.Collections.Immutable;
using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>
/// Proves the narrow publication case where existing pure mechanic bodies change and every
/// contract, identity and other document stays identical. No stored shape or declared dependency
/// changes in this case. This does not approve arbitrary new definitions or schema changes.
/// </summary>
internal sealed class ApplicationCandidateCompatibleUpdateReader(
    DantesRoleplayDbContext db, IApplicationRegistry applications, IApplicationActivationReader activations,
    IActivatedApplicationEvidenceReader evidence, IStandingGrantTargetResolver targets,
    ApplicationCandidatePureMechanicClassifier classifier)
{
    internal async Task<ApplicationCandidateCompatibleUpdateEvidence?> ReadAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, CancellationToken cancellationToken = default)
    {
        await using var scope = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
        var retained = await new ApplicationCandidateRetainedReader(db, applications)
            .ReadMetadataAsync(candidate.ApplicationId, candidate.CandidateId, candidate.Revision, cancellationToken);
        if (retained is null || retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint) return null;
        var basis = await ApplicationCandidateDocumentSelection.ReadBaseAsync(db, activations, candidate.ApplicationId,
            retained.RevisionRow.ExpectedActiveFingerprint, cancellationToken);
        if (basis?.PreparationVersion is null || basis.Winners.Count != retained.Documents.Count
            || basis.ApplicationRevision != retained.ApplicationRevision.Revision
            || basis.ApplicationFingerprint != retained.ApplicationRevision.Fingerprint) return null;
        var closure = await new ApplicationCandidatePureRuntimeClosureReader(db, applications, activations, targets, classifier)
            .ReadAsync(host, candidate, cancellationToken);
        if (closure is null || closure.BaseOrigin?.ActivationFingerprint != basis.ActivationFingerprint) return null;
        var source = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == retained.RevisionRow.SourceOperationId, cancellationToken);
        if (source is null || !ApplicationCandidateOperationProof.WriteMatches(source, retained,
                closure.Definitions.Select(value => value.Plan.Definition).ToArray(), out _)) return null;
        var original = basis.Winners.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in retained.Documents)
        {
            if (!original.TryGetValue(document.RelativePath, out var previous)) return null;
            if (document == previous) continue;
            // Legacy scanner MIME labels and explicit JavaScript labels have the same retained
            // mechanic meaning; ownership, source, trust and text/binary identity cannot change.
            if (!document.RelativePath.EndsWith(".js", StringComparison.Ordinal)
                || !ApplicationCandidateDocumentSelection.SameSourceMediaType(document, previous)
                || document with { ContentFingerprint = previous.ContentFingerprint, Length = previous.Length,
                    MediaType = previous.MediaType } != previous)
                return null;
            changed.Add(document.RelativePath);
        }
        if (changed.Count == 0 || changed.Count != closure.Definitions.Length) return null;
        var predecessors = ImmutableArray.CreateBuilder<StandingGrantDefinitionReference>();
        foreach (var definition in closure.Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!changed.Remove(definition.JavaScript.Document.RelativePath)
                || !original.TryGetValue(definition.Markdown.Document.RelativePath, out var markdown)
                || markdown != definition.Markdown.Document) return null;
            var javascript = original[definition.JavaScript.Document.RelativePath];
            var oldSource = evidence.ReadDocumentEvidence(candidate.ApplicationId, basis.ActivationRevision, javascript.LogicalIdentity);
            if (oldSource?.RetainedBytes is not { } bytes || oldSource.IsLegacyMetadataOnly
                || oldSource.ContentFingerprint != javascript.ContentFingerprint || oldSource.Length != javascript.Length) return null;
            var record = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(candidate.ApplicationId, markdown,
                new Dictionary<string, ActivatedApplicationDocument>(StringComparer.Ordinal)
                { [markdown.RelativePath] = markdown, [javascript.RelativePath] = javascript },
                new Dictionary<string, byte[]>(StringComparer.Ordinal)
                { [markdown.RelativePath] = definition.Markdown.RetainedBytes.ToArray(), [javascript.RelativePath] = bytes });
            if (record is null || record.Kind != "mechanic" || record.QualifiedId != definition.Plan.Definition.DefinitionId)
                return null;
            predecessors.Add(new(record.QualifiedId, record.Kind, record.Version, record.ContentFingerprint));
        }
        return changed.Count == 0 ? ApplicationCandidateCompatibleUpdateEvidence.FromVerifiedUpdate(
            retained, basis, closure, predecessors.ToImmutable()) : null;
    }
}

internal sealed class ApplicationCandidateCompatibleUpdateEvidence
{
    private ApplicationCandidateCompatibleUpdateEvidence(ApplicationCandidateRetainedMetadata retained,
        ActiveApplicationManifest basis, ApplicationCandidatePureRuntimeClosureEvidence closure,
        ImmutableArray<StandingGrantDefinitionReference> predecessors)
    {
        Retained = retained; Basis = basis; Closure = closure; Predecessors = predecessors;
        Fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/compatible-mechanic-body-update/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            { candidate = closure.Candidate, basis = basis.ActivationFingerprint, closure.EvidenceFingerprint, predecessors })));
    }

    internal ApplicationCandidateRetainedMetadata Retained { get; }
    internal ActiveApplicationManifest Basis { get; }
    internal ApplicationCandidatePureRuntimeClosureEvidence Closure { get; }
    internal ImmutableArray<StandingGrantDefinitionReference> Predecessors { get; }
    internal string Fingerprint { get; }

    internal static ApplicationCandidateCompatibleUpdateEvidence FromVerifiedUpdate(ApplicationCandidateRetainedMetadata retained,
        ActiveApplicationManifest basis, ApplicationCandidatePureRuntimeClosureEvidence closure,
        ImmutableArray<StandingGrantDefinitionReference> predecessors) => new(retained, basis, closure, predecessors);
}
