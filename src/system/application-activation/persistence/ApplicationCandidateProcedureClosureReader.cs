using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>
/// Issues retained, closed review evidence for exactly one changed procedure.  Its executable
/// references are deliberately limited to the INNER worker's governed-reference grammar.
/// </summary>
internal sealed class ApplicationCandidateProcedureClosureReader(
    IApplicationActivationReader activations,
    IActivatedApplicationEvidenceReader evidence,
    IStandingGrantTargetResolver targets,
    IActiveCatalogFeatureSnapshotProvider snapshots) : IApplicationCandidateReviewClosureReader
{
    public string Grammar => "procedure-governed-references-v1";

    public async Task<ApplicationCandidateReviewClosureReadResult> ReadAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        ApplicationCandidateSelectionEvidence selection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Candidate != candidate) return ApplicationCandidateReviewClosureReadResult.Rejected();

        var procedureTargets = selection.Targets.Where(value => value.Kind == "procedure").ToArray();
        if (procedureTargets.Length == 0) return ApplicationCandidateReviewClosureReadResult.NotApplicable();
        try
        {
            if (procedureTargets.Length != 1 || selection.Targets.Length != 1
                || selection.ChangedPaths.Length != 1 || selection.Documents.Length != 1)
                return ApplicationCandidateReviewClosureReadResult.Rejected();
            var selected = selection.Documents[0];
            var target = procedureTargets[0];
            if (selected.Role != "changed" || selected.Definition.DefinitionId != target.DefinitionId
                || selected.Definition.Kind != "procedure" || selected.Document.RelativePath != selection.ChangedPaths[0]
                || !selected.Document.RelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || !selected.Document.IsText || selected.Document.Trust != SourceTrust.Trusted)
                return ApplicationCandidateReviewClosureReadResult.Rejected();

            var successor = ProcedureFile.Parse(Text(selected.RetainedBytes), selected.Document.RelativePath);
            var qualifiedProcedure = successor.Id.StartsWith(candidate.ApplicationId.Value + ".", StringComparison.Ordinal)
                ? successor.Id : candidate.ApplicationId.Value + "." + successor.Id;
            if (successor.Status != DantesRoleplay.Procedures.ProcedureStatus.Active || qualifiedProcedure != target.DefinitionId)
                return ApplicationCandidateReviewClosureReadResult.Rejected();
            var active = RequireActiveBaseOrCandidate(candidate, selection, selected);
            if (active is null || !snapshots.TryGetSnapshot(candidate.ApplicationId, out var snapshot)
                || (snapshot.Resolution?.Fingerprint ?? snapshot.Manifest.Fingerprint)
                    != active.Value.Current.ResolutionFingerprint)
                return ApplicationCandidateReviewClosureReadResult.Rejected();

            var basis = active.Value.Basis;
            var predecessor = await PredecessorAsync(host, candidate, basis, active.Value.BasisIsCurrent,
                selected, target, successor, cancellationToken);
            if (predecessor.Rejected) return ApplicationCandidateReviewClosureReadResult.Rejected();
            var references = SystemInnerWorkerGovernedReferences.Parse(successor.Governs);
            if (!ExplicitReferencesWellFormed(successor.Governs, references))
                return ApplicationCandidateReviewClosureReadResult.Rejected();
            var dependencies = await DependenciesAsync(host, candidate, basis, snapshot, references, cancellationToken);
            if (dependencies is null) return ApplicationCandidateReviewClosureReadResult.Rejected();
            return ApplicationCandidateReviewClosureReadResult.Available(
                ApplicationCandidateProcedureReviewClosureEvidence.FromVerified(
                    selection, selected, predecessor.Definition,
                    dependencies.Value.Definitions, dependencies.Value.Documents));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or JsonException or DecoderFallbackException or InteractionContractException
            or ApplicationActivationException)
        {
            // A procedure-shaped candidate with malformed explicit grammar never falls through to
            // another review owner as ordinary prose.
            return ApplicationCandidateReviewClosureReadResult.Rejected();
        }
    }

    private (ActiveApplicationManifest Basis, ActiveApplicationManifest Current, bool BasisIsCurrent)?
        RequireActiveBaseOrCandidate(ApplicationCandidateReference candidate,
            ApplicationCandidateSelectionEvidence selection, ApplicationCandidateSelectedDocument selected)
    {
        var origin = selection.BaseOrigin;
        var basis = origin is null ? null : activations.ReadRevision(candidate.ApplicationId, origin.ActivationRevision);
        var current = activations.Current(candidate.ApplicationId);
        if (basis is null || current is null || basis.ActivationFingerprint != origin!.ActivationFingerprint
            || basis.ApplicationRevision != origin.ApplicationRevision || basis.ApplicationFingerprint != origin.ApplicationFingerprint
            || basis.PreparationVersion is null) return null;
        var basisIsCurrent = current.ActivationFingerprint == basis.ActivationFingerprint;
        if (!basisIsCurrent)
        {
            var currentSelected = current.Winners.SingleOrDefault(value =>
                value.RelativePath == selected.Document.RelativePath);
            var unchanged = basis.Winners.Where(value => value.RelativePath != selected.Document.RelativePath).ToArray();
            if (current.CandidateManifestFingerprint != candidate.ContentFingerprint
                || currentSelected != selected.Document
                || unchanged.Any(value => !current.Winners.Contains(value))) return null;
        }
        return (basis, current, basisIsCurrent);
    }

    private async Task<(bool Rejected, StandingGrantDefinitionReference? Definition)> PredecessorAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate, ActiveApplicationManifest basis,
        bool basisIsCurrent,
        ApplicationCandidateSelectedDocument selected, StandingGrantDefinitionTarget target, ProcedureFile successor,
        CancellationToken cancellationToken)
    {
        var old = basis.Winners.SingleOrDefault(value => value.RelativePath == selected.Document.RelativePath);
        if (old is null)
        {
            if (!basisIsCurrent) return (false, null);
            var absent = await targets.ResolveCurrentAsync(host, target.DefinitionId,
                "procedure", cancellationToken);
            return absent.Code == "STANDING_GRANT_DEFINITION_UNAVAILABLE"
                ? (false, null) : (true, null);
        }
        if (old with { ContentFingerprint = selected.Document.ContentFingerprint, Length = selected.Document.Length } != selected.Document)
            return (true, null);
        var retained = evidence.ReadDocumentEvidence(candidate.ApplicationId, basis.ActivationRevision, old.LogicalIdentity);
        if (retained?.RetainedBytes is not { } bytes || retained.IsLegacyMetadataOnly
            || retained.ContentFingerprint != old.ContentFingerprint || retained.Length != old.Length
            || Convert.ToHexString(SHA256.HashData(bytes)) != old.ContentFingerprint)
            return (true, null);
        var prior = ProcedureFile.Parse(Text(bytes.ToImmutableArray()), old.RelativePath);
        if (prior.Id != successor.Id || prior.Status != DantesRoleplay.Procedures.ProcedureStatus.Active
            || prior with { Instructions = successor.Instructions, Governs = successor.Governs } != successor)
            return (true, null);
        var record = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(candidate.ApplicationId,
            old, new Dictionary<string, ActivatedApplicationDocument>(StringComparer.Ordinal)
            { [old.RelativePath] = old }, new Dictionary<string, byte[]>(StringComparer.Ordinal)
            { [old.RelativePath] = bytes });
        if (record is null || record.Kind != "procedure" || record.QualifiedId != target.DefinitionId)
            return (true, null);
        var predecessor = new StandingGrantDefinitionReference(record.QualifiedId, record.Kind,
            record.Version, record.ContentFingerprint);
        if (basisIsCurrent)
        {
            var resolved = await targets.ResolveAsync(host, predecessor, cancellationToken);
            if (resolved.Status != StandingGrantTargetResolutionStatus.Available || resolved.Target is null
                || resolved.Target.OwnerApplicationId != candidate.ApplicationId) return (true, null);
        }
        return (false, predecessor);
    }

    private async Task<(ImmutableArray<StandingGrantDefinitionReference> Definitions,
        ImmutableArray<ApplicationCandidateReviewClosureDocument> Documents)?> DependenciesAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate, ActiveApplicationManifest basis,
        ActiveCatalogFeatureSnapshot snapshot, IReadOnlyList<SystemInnerWorkerGovernedReference> references,
        CancellationToken cancellationToken)
    {
        var definitions = ImmutableArray.CreateBuilder<StandingGrantDefinitionReference>();
        var documents = ImmutableArray.CreateBuilder<ApplicationCandidateReviewClosureDocument>();
        foreach (var reference in references)
        {
            var kind = reference.Kind == SystemInnerWorkerGovernedReferenceKind.Action ? "mechanic" : "query";
            var record = snapshot.Documents.Where(value => value.Trust == SourceTrust.Trusted
                && value.Record.Kind == kind && value.Record.Status == "active"
                && value.Record.QualifiedId == reference.QualifiedId).Select(value => value.Record).ToArray();
            if (record.Length != 1 || reference.Kind == SystemInnerWorkerGovernedReferenceKind.Action
                && !ActionScoped(record[0])) return null;
            var resolved = await targets.ResolveCurrentAsync(host, reference.QualifiedId, kind, cancellationToken);
            if (resolved.Status != StandingGrantTargetResolutionStatus.Available || resolved.Target is null
                || resolved.Target.OwnerApplicationId != candidate.ApplicationId
                || resolved.Target.DefinitionId != record[0].QualifiedId || resolved.Target.Kind != record[0].Kind
                || resolved.Target.Revision != record[0].Version || resolved.Target.ContentFingerprint != record[0].ContentFingerprint)
                return null;
            var definition = new StandingGrantDefinitionReference(record[0].QualifiedId, record[0].Kind,
                record[0].Version, record[0].ContentFingerprint);
            var source = await SourceDocumentsAsync(candidate.ApplicationId, basis, definition, record[0], cancellationToken);
            if (source is null) return null;
            definitions.Add(definition); documents.AddRange(source.Value);
        }
        return (definitions.ToImmutable(), documents.ToImmutable());
    }

    private Task<ImmutableArray<ApplicationCandidateReviewClosureDocument>?> SourceDocumentsAsync(ApplicationIdentifier application,
        ActiveApplicationManifest basis, StandingGrantDefinitionReference definition, CatalogRecordDefinition record,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var paths = record.Kind == "mechanic"
            ? new[] { record.SourceLogicalPath, Path.ChangeExtension(record.SourceLogicalPath, ".js").Replace('\\', '/') }
            : new[] { record.SourceLogicalPath };
        var values = ImmutableArray.CreateBuilder<ApplicationCandidateReviewClosureDocument>();
        foreach (var path in paths)
        {
            var document = basis.Winners.SingleOrDefault(value => value.RelativePath == path);
            if (document is null || document.SourceId != record.SourceId || !document.IsText || document.Trust != SourceTrust.Trusted)
                return Task.FromResult<ImmutableArray<ApplicationCandidateReviewClosureDocument>?>(null);
            var retained = evidence.ReadDocumentEvidence(application, basis.ActivationRevision, document.LogicalIdentity);
            if (retained?.RetainedBytes is not { } bytes || retained.IsLegacyMetadataOnly || retained.Length != document.Length
                || retained.ContentFingerprint != document.ContentFingerprint
                || Convert.ToHexString(SHA256.HashData(bytes)) != document.ContentFingerprint)
                return Task.FromResult<ImmutableArray<ApplicationCandidateReviewClosureDocument>?>(null);
            values.Add(new(definition, ApplicationCandidateReviewDocumentRole.Dependency, document, bytes.ToImmutableArray()));
        }
        return Task.FromResult<ImmutableArray<ApplicationCandidateReviewClosureDocument>?>(values.ToImmutable());
    }

    private static bool ActionScoped(CatalogRecordDefinition record)
    {
        using var content = JsonDocument.Parse(record.ContentJson);
        return content.RootElement.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.String
            && scope.GetString() == "action";
    }

    private static bool ExplicitReferencesWellFormed(string governs,
        IReadOnlyList<SystemInnerWorkerGovernedReference> references)
    {
        // Ordinary explanation has no executable meaning.  A lower-case clause which begins with
        // execute, and every query(kind: marker, is an explicit attempt at this closed grammar.
        var actions = references.Where(value => value.Kind == SystemInnerWorkerGovernedReferenceKind.Action)
            .Select(value => value.QualifiedId).ToHashSet(StringComparer.Ordinal);
        foreach (var clause in governs.Split([';', ',']))
        {
            var value = clause.Trim();
            if (!value.StartsWith("execute", StringComparison.Ordinal)) continue;
            if (!ActionClause.IsMatch(value) || !actions.Contains(value[8..].Trim())) return false;
        }
        return QueryMarker.Matches(governs).Count == QueryClause.Matches(governs).Count;
    }

    private static readonly Regex ActionClause = new("^execute\\s+([a-z0-9][a-z0-9._-]{2,159})$",
        RegexOptions.CultureInvariant);
    private static readonly Regex QueryMarker = new("query\\(kind:", RegexOptions.CultureInvariant);
    private static readonly Regex QueryClause = new("query\\(kind:\\s*\"[a-z0-9][a-z0-9._-]{2,159}\"\\)",
        RegexOptions.CultureInvariant);

    private static string Text(ImmutableArray<byte> bytes) => new UTF8Encoding(false, true).GetString(bytes.AsSpan());

}

internal sealed class ApplicationCandidateProcedureReviewClosureEvidence
    : IApplicationCandidateReviewClosureEvidence
{
    private ApplicationCandidateProcedureReviewClosureEvidence(
        ApplicationCandidateSelectionEvidence selection, ApplicationCandidateSelectedDocument selected,
        StandingGrantDefinitionReference? predecessor,
        ImmutableArray<StandingGrantDefinitionReference> dependencies,
        ImmutableArray<ApplicationCandidateReviewClosureDocument> dependencyDocuments)
    {
        Candidate = selection.Candidate;
        BaseOrigin = selection.BaseOrigin!;
        SelectionEvidenceFingerprint = selection.EvidenceFingerprint;
        Successor = selected.Definition;
        Predecessor = predecessor;
        Dependencies = dependencies;
        ReviewDocuments = [new(selected.Definition, ApplicationCandidateReviewDocumentRole.Changed,
            selected.Document, selected.RetainedBytes), .. dependencyDocuments];
        EvidenceFingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/application-candidate-procedure-closure/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                Candidate, BaseOrigin, SelectionEvidenceFingerprint,
                grammar = "procedure-governed-references-v1", Successor, predecessor, dependencies,
                documents = ReviewDocuments.Select(value => new { value.Definition, value.Role, value.Document })
            })));
    }

    public string Grammar => "procedure-governed-references-v1";
    public ApplicationCandidateReference Candidate { get; }
    public string SelectionEvidenceFingerprint { get; }
    public string EvidenceFingerprint { get; }
    public ImmutableArray<ApplicationCandidateReviewClosureDocument> ReviewDocuments { get; }
    public ImmutableArray<StandingGrantDefinitionReference> Dependencies { get; }
    internal StandingGrantActivationOrigin BaseOrigin { get; }
    internal StandingGrantDefinitionReference Successor { get; }
    internal StandingGrantDefinitionReference? Predecessor { get; }

    internal static ApplicationCandidateProcedureReviewClosureEvidence FromVerified(
        ApplicationCandidateSelectionEvidence selection, ApplicationCandidateSelectedDocument selected,
        StandingGrantDefinitionReference? predecessor,
        ImmutableArray<StandingGrantDefinitionReference> dependencies,
        ImmutableArray<ApplicationCandidateReviewClosureDocument> dependencyDocuments) =>
        new(selection, selected, predecessor, dependencies, dependencyDocuments);
}
