using System.Collections.Immutable;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Exact owner-selected content. This is not Read/Validate authority or complete dependency evidence.</summary>
internal sealed class ApplicationCandidateSelectionReader(DantesRoleplayDbContext db, IApplicationRegistry applications,
    IApplicationActivationReader activations, IStandingGrantTargetResolver targets)
{
    internal async Task<ApplicationCandidateSelectionEvidence?> ReadAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(candidate);
        try { return await ReadCoreAsync(host, candidate, cancellationToken); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException
            or ApplicationActivationException or ApplicationCatalogMaterializationException)
        { return null; }
    }

    private async Task<ApplicationCandidateSelectionEvidence?> ReadCoreAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, CancellationToken cancellationToken)
    {
        if (candidate.ApplicationId != host.ApplicationRevision.ApplicationId || candidate.ApplicationId.IsSystem
            || host.Budget.DeadlineUtc <= DateTime.UtcNow) return null;
        await using var scope = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
        var reader = new ApplicationCandidateRetainedReader(db, applications);
        var retained = await reader.ReadMetadataAsync(candidate.ApplicationId, candidate.CandidateId, candidate.Revision, cancellationToken);
        if (retained is null || retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint) return null;
        var basis = await ApplicationCandidateDocumentSelection.ReadBaseAsync(db, activations, candidate.ApplicationId,
            retained.RevisionRow.ExpectedActiveFingerprint, cancellationToken);
        var changed = ApplicationCandidateDocumentSelection.ChangedPaths(retained.Documents, basis);
        if (changed.Count is < 1 or > ApplicationAuthoringLimits.DocumentsPerWrite) return null;
        var paths = ApplicationCandidateDocumentSelection.WithKnownSidecars(retained.Documents, changed);
        var documents = await reader.ReadSelectedAsync(retained, paths, cancellationToken);
        var records = SqliteApplicationAuthoringService.DefinitionRecords(candidate.ApplicationId, documents, changed);
        var definitions = records.Select(value => new StandingGrantDefinitionReference(value.QualifiedId,
            value.Kind, value.Version, value.ContentFingerprint)).ToArray();
        var bindings = new Dictionary<string, StandingGrantDefinitionReference>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var definition = new StandingGrantDefinitionReference(record.QualifiedId, record.Kind, record.Version, record.ContentFingerprint);
            if (!bindings.TryAdd(record.SourceLogicalPath, definition)) return null;
            if (record.Kind != "mechanic") continue;
            // ParseRetainedRecord has resolved this exact contract and its same-source JavaScript
            // through the mechanic owner. This binding is to that normalized record, not a new JS ID.
            var contract = documents.Single(value => value.Document.RelativePath == record.SourceLogicalPath).Document;
            var sidecarPath = Path.ChangeExtension(record.SourceLogicalPath, ".js").Replace('\\', '/');
            var sidecar = documents.Single(value => value.Document.RelativePath == sidecarPath).Document;
            if (contract.SourceId != sidecar.SourceId || contract.Trust != sidecar.Trust || contract.Precedence != sidecar.Precedence
                || !bindings.TryAdd(sidecarPath, definition)) return null;
        }
        if (documents.Any(value => !bindings.ContainsKey(value.Document.RelativePath))) return null;
        var selectedTargets = ImmutableArray.CreateBuilder<StandingGrantDefinitionTarget>();
        foreach (var definition in definitions)
        {
            var target = await targets.ResolveCandidateReferenceAsync(host, candidate, definition, cancellationToken);
            if (target.Status != StandingGrantTargetResolutionStatus.Available || target.Target is null) return null;
            selectedTargets.Add(target.Target);
        }
        var origin = basis is null ? null : new StandingGrantActivationOrigin(basis.ActivationRevision,
            basis.ActivationFingerprint, basis.ApplicationRevision, basis.ApplicationFingerprint);
        return new(candidate, origin, changed.Order(StringComparer.Ordinal).ToImmutableArray(),
            documents.OrderBy(value => value.Document.RelativePath, StringComparer.Ordinal).Select(value =>
                new ApplicationCandidateSelectedDocument(value.Document, value.RetainedBytes.ToImmutableArray(),
                    changed.Contains(value.Document.RelativePath, StringComparer.Ordinal) ? "changed" : "sidecar",
                    bindings[value.Document.RelativePath])).ToImmutableArray(),
            selectedTargets.OrderBy(value => value.DefinitionId, StringComparer.Ordinal).ToImmutableArray());
    }
}

internal sealed record ApplicationCandidateSelectedDocument(
    ActivatedApplicationDocument Document, ImmutableArray<byte> RetainedBytes, string Role,
    StandingGrantDefinitionReference Definition);

internal sealed class ApplicationCandidateSelectionEvidence
{
    internal ApplicationCandidateSelectionEvidence(ApplicationCandidateReference candidate, StandingGrantActivationOrigin? baseOrigin,
        ImmutableArray<string> changedPaths, ImmutableArray<ApplicationCandidateSelectedDocument> documents,
        ImmutableArray<StandingGrantDefinitionTarget> targets)
    {
        Candidate = candidate; BaseOrigin = baseOrigin; ChangedPaths = changedPaths; Documents = documents; Targets = targets;
        EvidenceFingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-selection-evidence/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                candidate, baseOrigin, changedPaths, coverageVersion = CoverageVersion, dependenciesComplete = DependenciesComplete,
                documents = documents.Select(value => new { value.Document, value.Role, value.Definition }), targets
            })));
    }

    internal ApplicationCandidateReference Candidate { get; }
    internal StandingGrantActivationOrigin? BaseOrigin { get; }
    internal ImmutableArray<string> ChangedPaths { get; }
    internal ImmutableArray<ApplicationCandidateSelectedDocument> Documents { get; }
    internal ImmutableArray<StandingGrantDefinitionTarget> Targets { get; }
    internal string CoverageVersion => "changed-definitions-mechanic-sidecars-v1";
    internal bool DependenciesComplete => false;
    // This host-only hash includes the full candidate/base binding. It is deliberately distinct
    // from plan03's SelectionFingerprint over only material permitted in a model request.
    internal string EvidenceFingerprint { get; }
}
