using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Sources;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>
/// Issues retained review evidence for one existing query whose callable contract is unchanged,
/// except that a query without public media authority may add one strict route-owner proof.
/// The query may select another current read-only mechanic, but cannot broaden its inputs or roles.
/// </summary>
internal sealed class ApplicationCandidateQueryClosureReader(
    IApplicationActivationReader activations,
    IActivatedApplicationEvidenceReader evidence,
    IStandingGrantTargetResolver targets,
    IActiveCatalogFeatureSnapshotProvider snapshots,
    IBoundedJsonSchemaValidator schemas) : IApplicationCandidateReviewClosureReader
{
    public const string GrammarVersion = "existing-query-mechanic-projection-v2";
    public string Grammar => GrammarVersion;

    public async Task<ApplicationCandidateReviewClosureReadResult> ReadAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        ApplicationCandidateSelectionEvidence selection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Candidate != candidate) return ApplicationCandidateReviewClosureReadResult.Rejected();

        var queryTargets = selection.Targets.Where(value => value.Kind == ApplicationQueryContract.CatalogKind).ToArray();
        if (queryTargets.Length == 0) return ApplicationCandidateReviewClosureReadResult.NotApplicable();
        try
        {
            if (queryTargets.Length != 1 || selection.Targets.Length != 1
                || selection.ChangedPaths.Length != 1 || selection.Documents.Length != 1)
                return ApplicationCandidateReviewClosureReadResult.Rejected();
            var selected = selection.Documents[0];
            var target = queryTargets[0];
            if (selected.Role != "changed" || selected.Definition.DefinitionId != target.DefinitionId
                || selected.Definition.Kind != ApplicationQueryContract.CatalogKind
                || selected.Document.RelativePath != selection.ChangedPaths[0]
                || !selected.Document.RelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || !selected.Document.IsText || selected.Document.Trust != SourceTrust.Trusted)
                return ApplicationCandidateReviewClosureReadResult.Rejected();

            var successor = ApplicationQueryContract.Parse(Text(selected.RetainedBytes), candidate.ApplicationId);
            if (!Eligible(successor) || successor.Id != target.DefinitionId
                || ApplicationCatalogRecordContent.Fingerprint(
                    ApplicationCatalogRecordContent.QueryJson(successor)) != selected.Definition.ContentFingerprint
                || !SameDefinition(selected.Definition, target))
                return ApplicationCandidateReviewClosureReadResult.Rejected();

            var active = RequireActiveBaseOrCandidate(candidate, selection, selected);
            if (active is null || !snapshots.TryGetSnapshot(candidate.ApplicationId, out var snapshot)
                || (snapshot.Resolution?.Fingerprint ?? snapshot.Manifest.Fingerprint)
                    != active.Value.Current.ResolutionFingerprint)
                return ApplicationCandidateReviewClosureReadResult.Rejected();

            var predecessor = ReadPredecessor(candidate, active.Value.Basis, selected);
            if (predecessor is null || !CompatibleContract(predecessor.Value.Contract, successor,
                    out var inputSchemaHash, out var addsMediaOwnerReference))
                return ApplicationCandidateReviewClosureReadResult.Rejected();

            var projection = await ReadProjectionAsync(host, candidate.ApplicationId, active.Value.Current,
                snapshot, successor, cancellationToken);
            if (projection is null || !ProjectionContractMatches(successor, projection.Value.Requirements))
                return ApplicationCandidateReviewClosureReadResult.Rejected();

            return ApplicationCandidateReviewClosureReadResult.Available(
                ApplicationCandidateQueryReviewClosureEvidence.FromVerified(selection, selected,
                    predecessor.Value.Definition, projection.Value.Definition, projection.Value.Documents,
                    inputSchemaHash, addsMediaOwnerReference));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or JsonException or DecoderFallbackException or InteractionContractException
            or ApplicationActivationException or ApplicationCatalogMaterializationException)
        {
            return ApplicationCandidateReviewClosureReadResult.Rejected();
        }
    }

    private (ActiveApplicationManifest Basis, ActiveApplicationManifest Current)? RequireActiveBaseOrCandidate(
        ApplicationCandidateReference candidate, ApplicationCandidateSelectionEvidence selection,
        ApplicationCandidateSelectedDocument selected)
    {
        var origin = selection.BaseOrigin;
        var basis = origin is null ? null : activations.ReadRevision(candidate.ApplicationId, origin.ActivationRevision);
        var current = activations.Current(candidate.ApplicationId);
        if (basis is null || current is null || basis.ActivationFingerprint != origin!.ActivationFingerprint
            || basis.ApplicationRevision != origin.ApplicationRevision
            || basis.ApplicationFingerprint != origin.ApplicationFingerprint || basis.PreparationVersion is null)
            return null;
        if (current.ActivationFingerprint != basis.ActivationFingerprint)
        {
            var currentSelected = current.Winners.SingleOrDefault(value =>
                value.RelativePath == selected.Document.RelativePath);
            var unchanged = basis.Winners.Where(value => value.RelativePath != selected.Document.RelativePath).ToArray();
            if (current.CandidateManifestFingerprint != candidate.ContentFingerprint
                || currentSelected != selected.Document || unchanged.Any(value => !current.Winners.Contains(value)))
                return null;
        }
        return (basis, current);
    }

    private (ApplicationQueryContract Contract, StandingGrantDefinitionReference Definition)? ReadPredecessor(
        ApplicationCandidateReference candidate, ActiveApplicationManifest basis,
        ApplicationCandidateSelectedDocument selected)
    {
        var old = basis.Winners.SingleOrDefault(value => value.RelativePath == selected.Document.RelativePath);
        if (old is null || old with
            { ContentFingerprint = selected.Document.ContentFingerprint, Length = selected.Document.Length } != selected.Document)
            return null;
        var retained = evidence.ReadDocumentEvidence(candidate.ApplicationId,
            basis.ActivationRevision, old.LogicalIdentity);
        if (retained?.RetainedBytes is not { } bytes || retained.IsLegacyMetadataOnly
            || retained.ContentFingerprint != old.ContentFingerprint || retained.Length != old.Length
            || Convert.ToHexString(SHA256.HashData(bytes)) != old.ContentFingerprint)
            return null;
        var predecessor = ApplicationQueryContract.Parse(new UTF8Encoding(false, true).GetString(bytes),
            candidate.ApplicationId);
        if (!Eligible(predecessor) || predecessor.Id != selected.Definition.DefinitionId)
            return null;
        var content = ApplicationCatalogRecordContent.QueryJson(predecessor);
        return (predecessor, new(predecessor.Id, ApplicationQueryContract.CatalogKind, 1,
            ApplicationCatalogRecordContent.Fingerprint(content)));
    }

    private bool CompatibleContract(ApplicationQueryContract predecessor, ApplicationQueryContract successor,
        out string? inputSchemaHash, out bool addsMediaOwnerReference)
    {
        inputSchemaHash = null;
        addsMediaOwnerReference = false;
        if (predecessor.Id != successor.Id || predecessor.Executor != successor.Executor
            || predecessor.Exposure != successor.Exposure || !SameMap(predecessor.Roles, successor.Roles)
            || !SameBindings(predecessor.RoleBindings, successor.RoleBindings)
            || !CompatibleMediaOwnerReference(predecessor.MediaOwnerReference,
                successor.MediaOwnerReference, out addsMediaOwnerReference)
            || predecessor.OutputSchemaHash != successor.OutputSchemaHash)
            return false;
        var priorOutput = schemas.Compile(predecessor.OutputSchemaJson);
        var nextOutput = schemas.Compile(successor.OutputSchemaJson);
        if (!priorOutput.IsAccepted || !nextOutput.IsAccepted
            || priorOutput.SchemaHash != predecessor.OutputSchemaHash
            || nextOutput.SchemaHash != successor.OutputSchemaHash
            || priorOutput.NormalizedSchema != nextOutput.NormalizedSchema)
            return false;
        if ((predecessor.InputSchemaJson is null) != (successor.InputSchemaJson is null)) return false;
        if (predecessor.InputSchemaJson is null) return true;
        var priorInput = schemas.Compile(predecessor.InputSchemaJson);
        var nextInput = schemas.Compile(successor.InputSchemaJson!);
        if (!priorInput.IsAccepted || !nextInput.IsAccepted || priorInput.SchemaHash != nextInput.SchemaHash
            || priorInput.NormalizedSchema != nextInput.NormalizedSchema) return false;
        inputSchemaHash = nextInput.SchemaHash;
        return true;
    }

    private static bool CompatibleMediaOwnerReference(
        ApplicationQueryMediaOwnerReference? predecessor,
        ApplicationQueryMediaOwnerReference? successor,
        out bool addsMediaOwnerReference)
    {
        addsMediaOwnerReference = false;
        if (predecessor == successor) return true;
        // This one-way opt-in is surfaced in retained v2 review evidence. Once declared,
        // removal or replacement requires a different publication grammar.
        if (predecessor is not null || successor is null) return false;
        addsMediaOwnerReference = true;
        return true;
    }

    private async Task<(StandingGrantDefinitionReference Definition, MechanicRequirements Requirements,
        ImmutableArray<ApplicationCandidateReviewClosureDocument> Documents)?> ReadProjectionAsync(
        InteractionInvocationHost host, ApplicationIdentifier application, ActiveApplicationManifest current,
        ActiveCatalogFeatureSnapshot snapshot, ApplicationQueryContract query, CancellationToken cancellationToken)
    {
        var records = snapshot.Documents.Where(value => value.Trust == SourceTrust.Trusted
            && value.Record.Kind == "mechanic" && value.Record.Status == "active"
            && value.Record.QualifiedId == query.ProjectionQualifiedId
            && value.Record.Version == query.ProjectionVersion
            && value.Record.ContentFingerprint == query.ProjectionContentHash).ToArray();
        if (records.Length != 1) return null;
        var record = records[0].Record;
        var definition = new StandingGrantDefinitionReference(record.QualifiedId, record.Kind,
            record.Version, record.ContentFingerprint);
        var resolved = await targets.ResolveCurrentAsync(host, definition.DefinitionId,
            definition.Kind, cancellationToken);
        if (resolved.Status != StandingGrantTargetResolutionStatus.Available || resolved.Target is null
            || resolved.Target.OwnerApplicationId != application
            || resolved.Target.DefinitionId != definition.DefinitionId || resolved.Target.Kind != definition.Kind
            || resolved.Target.Revision != definition.Revision
            || resolved.Target.ContentFingerprint != definition.ContentFingerprint)
            return null;

        var paths = new[] { record.SourceLogicalPath,
            Path.ChangeExtension(record.SourceLogicalPath, ".js").Replace('\\', '/') };
        var retainedDocuments = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var winnerDocuments = new Dictionary<string, ActivatedApplicationDocument>(StringComparer.Ordinal);
        var reviewDocuments = ImmutableArray.CreateBuilder<ApplicationCandidateReviewClosureDocument>();
        foreach (var path in paths)
        {
            var document = current.Winners.SingleOrDefault(value => value.RelativePath == path);
            if (document is null || document.SourceId != record.SourceId || !document.IsText
                || document.Trust != SourceTrust.Trusted)
                return null;
            var retained = evidence.ReadDocumentEvidence(application, current.ActivationRevision,
                document.LogicalIdentity);
            if (retained?.RetainedBytes is not { } bytes || retained.IsLegacyMetadataOnly
                || retained.ContentFingerprint != document.ContentFingerprint || retained.Length != document.Length
                || Convert.ToHexString(SHA256.HashData(bytes)) != document.ContentFingerprint)
                return null;
            winnerDocuments.Add(path, document);
            retainedDocuments.Add(path, bytes);
            reviewDocuments.Add(new(definition, ApplicationCandidateReviewDocumentRole.Dependency,
                document, bytes.ToImmutableArray()));
        }
        var parsed = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(application,
            winnerDocuments[record.SourceLogicalPath], winnerDocuments, retainedDocuments);
        if (parsed is null || parsed.Kind != record.Kind || parsed.QualifiedId != record.QualifiedId
            || parsed.Version != record.Version || parsed.ContentFingerprint != record.ContentFingerprint
            || parsed.ContentJson != record.ContentJson)
            return null;
        using var content = JsonDocument.Parse(parsed.ContentJson);
        if (!content.RootElement.TryGetProperty("requirements", out var requirementsElement)
            || requirementsElement.ValueKind != JsonValueKind.String)
            return null;
        var requirements = MechanicRequirements.Parse(requirementsElement.GetString()!);
        if (requirements.Event is not null || requirements.ProjectionProblems().Count != 0
            || requirements.CompositionProblems().Count != 0)
            return null;
        return (definition, requirements, reviewDocuments.ToImmutable());
    }

    private bool ProjectionContractMatches(ApplicationQueryContract query, MechanicRequirements requirements)
    {
        var roles = requirements.Roles.Keys.Concat(requirements.GraphSnapshots.Values
            .Select(value => value.RootRole)).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (!roles.SetEquals(query.Roles.Keys)) return false;
        if ((query.InputSchemaJson is null) != (requirements.InputSchema is null)) return false;
        if (query.InputSchemaJson is null) return true;
        var queryInput = schemas.Compile(query.InputSchemaJson);
        var mechanicInput = schemas.Compile(requirements.InputSchema!.Value.GetRawText());
        return queryInput.IsAccepted && mechanicInput.IsAccepted
            && queryInput.SchemaHash == mechanicInput.SchemaHash
            && queryInput.NormalizedSchema == mechanicInput.NormalizedSchema;
    }

    private static bool Eligible(ApplicationQueryContract query) =>
        query.Status == "active" && query.Executor == ApplicationQueryContract.MechanicProjectionExecutor
        && query.CampaignSelection is null && query.Selection is null && query.RoleBindings is not null
        && query.RoleBindings.Count == query.Roles.Count
        && query.RoleBindings.All(value => query.Roles.ContainsKey(value.Key)
            && value.Value.Source == "route-entity" && value.Value.Pointer is null && value.Value.Key is null);

    private static bool SameDefinition(StandingGrantDefinitionReference definition,
        StandingGrantDefinitionTarget target) => definition.DefinitionId == target.DefinitionId
        && definition.Kind == target.Kind && definition.Revision == target.Revision
        && definition.ContentFingerprint == target.ContentFingerprint;

    private static bool SameMap(IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) => left.Count == right.Count
        && left.All(value => right.TryGetValue(value.Key, out var found) && found == value.Value);

    private static bool SameBindings(IReadOnlyDictionary<string, ApplicationQueryRoleBinding>? left,
        IReadOnlyDictionary<string, ApplicationQueryRoleBinding>? right) => left is not null && right is not null
        && left.Count == right.Count && left.All(value => right.TryGetValue(value.Key, out var found)
            && found == value.Value);

    private static string Text(ImmutableArray<byte> bytes) =>
        new UTF8Encoding(false, true).GetString(bytes.AsSpan());
}

internal sealed class ApplicationCandidateQueryReviewClosureEvidence : IApplicationCandidateReviewClosureEvidence
{
    private ApplicationCandidateQueryReviewClosureEvidence(
        ApplicationCandidateSelectionEvidence selection, ApplicationCandidateSelectedDocument selected,
        StandingGrantDefinitionReference predecessor, StandingGrantDefinitionReference projection,
        ImmutableArray<ApplicationCandidateReviewClosureDocument> projectionDocuments,
        string? inputSchemaHash, bool addsMediaOwnerReference)
    {
        Candidate = selection.Candidate;
        BaseOrigin = selection.BaseOrigin!;
        SelectionEvidenceFingerprint = selection.EvidenceFingerprint;
        Successor = selected.Definition;
        Predecessor = predecessor;
        Projection = projection;
        InputSchemaHash = inputSchemaHash;
        AddsMediaOwnerReference = addsMediaOwnerReference;
        Dependencies = [projection];
        ReviewDocuments = [new(selected.Definition, ApplicationCandidateReviewDocumentRole.Changed,
            selected.Document, selected.RetainedBytes), .. projectionDocuments];
        EvidenceFingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/application-candidate-query-closure/v2",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                Candidate, BaseOrigin, SelectionEvidenceFingerprint,
                grammar = ApplicationCandidateQueryClosureReader.GrammarVersion, Successor, Predecessor,
                Projection, InputSchemaHash, AddsMediaOwnerReference,
                documents = ReviewDocuments.Select(value => new { value.Definition, value.Role, value.Document })
            })));
    }

    public string Grammar => ApplicationCandidateQueryClosureReader.GrammarVersion;
    public ApplicationCandidateReference Candidate { get; }
    public string SelectionEvidenceFingerprint { get; }
    public string EvidenceFingerprint { get; }
    public ImmutableArray<ApplicationCandidateReviewClosureDocument> ReviewDocuments { get; }
    public ImmutableArray<StandingGrantDefinitionReference> Dependencies { get; }
    internal StandingGrantActivationOrigin BaseOrigin { get; }
    internal StandingGrantDefinitionReference Successor { get; }
    internal StandingGrantDefinitionReference Predecessor { get; }
    internal StandingGrantDefinitionReference Projection { get; }
    internal string? InputSchemaHash { get; }
    internal bool AddsMediaOwnerReference { get; }

    internal static ApplicationCandidateQueryReviewClosureEvidence FromVerified(
        ApplicationCandidateSelectionEvidence selection, ApplicationCandidateSelectedDocument selected,
        StandingGrantDefinitionReference predecessor, StandingGrantDefinitionReference projection,
        ImmutableArray<ApplicationCandidateReviewClosureDocument> projectionDocuments,
        string? inputSchemaHash, bool addsMediaOwnerReference) => new(selection, selected, predecessor,
            projection, projectionDocuments, inputSchemaHash, addsMediaOwnerReference);
}
