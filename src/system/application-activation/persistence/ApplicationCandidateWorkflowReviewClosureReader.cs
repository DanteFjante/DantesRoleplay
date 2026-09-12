using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>
/// Owns the closed review grammar for exactly one workflow service mechanic. The evidence is
/// reconstructed from retained candidate and active-generation bytes; authored dependency lists
/// are never accepted as closure evidence.
/// </summary>
internal sealed class ApplicationCandidateWorkflowReviewClosureReader(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    IActivatedApplicationEvidenceReader evidence,
    IStandingGrantTargetResolver targets,
    ActivatedApplicationCatalogMaterializer catalogs,
    IApplicationReadOnlyServiceDefinitionReader services) : IApplicationCandidateReviewClosureReader
{
    internal const string GrammarVersion = "workflow-service-mechanic-v1";
    public string Grammar => GrammarVersion;

    public async Task<ApplicationCandidateReviewClosureReadResult> ReadAsync(
        InteractionInvocationHost host,
        ApplicationCandidateReference candidate,
        ApplicationCandidateSelectionEvidence selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(selection);
        var claimed = false;
        try
        {
            if (selection.Candidate != candidate || selection.Targets.Length != 1
                || selection.Targets[0].Kind != "mechanic" || selection.Documents.Length != 2)
                return ApplicationCandidateReviewClosureReadResult.NotApplicable();
            var markdown = selection.Documents.SingleOrDefault(value =>
                value.Document.RelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
            var javascript = selection.Documents.SingleOrDefault(value =>
                value.Document.RelativePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase));
            if (markdown is null || javascript is null) return ApplicationCandidateReviewClosureReadResult.NotApplicable();

            var selectedDocuments = selection.Documents.ToDictionary(value => value.Document.RelativePath,
                value => value.Document, StringComparer.Ordinal);
            var selectedBytes = selection.Documents.ToDictionary(value => value.Document.RelativePath,
                value => value.RetainedBytes.ToArray(), StringComparer.Ordinal);
            var parsed = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(candidate.ApplicationId,
                markdown.Document, selectedDocuments, selectedBytes);
            if (parsed is null || parsed.Kind != "mechanic")
                return ApplicationCandidateReviewClosureReadResult.NotApplicable();
            claimed = HasServiceRequirement(parsed.ContentJson);
            if (!claimed) return ApplicationCandidateReviewClosureReadResult.NotApplicable();
            if (!ClosedServiceRequirement(parsed.ContentJson)
                || !ExactPair(markdown, javascript) || !ExactSelected(selection, parsed)) return Rejected();

            var retained = await new ApplicationCandidateRetainedReader(db, applications).ReadMetadataAsync(
                candidate.ApplicationId, candidate.CandidateId, candidate.Revision, cancellationToken);
            var basis = retained is null ? null : await ApplicationCandidateDocumentSelection.ReadBaseAsync(
                db, activations, candidate.ApplicationId, retained.RevisionRow.ExpectedActiveFingerprint, cancellationToken);
            var active = activations.Current(candidate.ApplicationId);
            var basisIsCurrent = active?.ActivationFingerprint == basis?.ActivationFingerprint;
            var candidateIsCurrent = active?.CandidateManifestFingerprint == candidate.ContentFingerprint
                && retained is not null && active.Winners.SequenceEqual(retained.Documents);
            if (retained is null || basis is null || basis.PreparationVersion is null
                || selection.BaseOrigin?.ActivationFingerprint != basis.ActivationFingerprint
                || !basisIsCurrent && !candidateIsCurrent
                || retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint
                || !OnlySelectedPairChanged(retained.Documents, basis.Winners, markdown.Document.RelativePath,
                    javascript.Document.RelativePath)) return Rejected();

            var rootReference = new StandingGrantDefinitionReference(parsed.QualifiedId, parsed.Kind,
                parsed.Version, parsed.ContentFingerprint);
            var predecessor = ReadPredecessor(candidate.ApplicationId, basis, markdown.Document.RelativePath,
                javascript.Document.RelativePath, parsed.QualifiedId);
            if (predecessor.Status == PredecessorStatus.Invalid) return Rejected();
            if (predecessor.Status == PredecessorStatus.New && basisIsCurrent)
            {
                var current = await targets.ResolveCurrentAsync(host, rootReference.DefinitionId,
                    rootReference.Kind, cancellationToken);
                if (current.Code != "STANDING_GRANT_DEFINITION_UNAVAILABLE") return Rejected();
            }
            var service = services.ReadRetained(new SystemTaskSelectedDefinition(rootReference.DefinitionId,
                rootReference.Revision, rootReference.ContentFingerprint), View(parsed));
            if (service.Actions.Count == 0 && service.Jobs.Count == 0) return Rejected();
            using (var root = JsonDocument.Parse(parsed.ContentJson))
                _ = JintMechanicEngine.PrepareMechanicProgram(root.RootElement.GetProperty("source").GetString()!);

            var snapshot = catalogs.BuildPermissionSnapshot(candidate.ApplicationId, basis);
            if (snapshot.EffectiveSetFingerprint != basis.ActivationFingerprint) return Rejected();
            var dependencies = ImmutableArray.CreateBuilder<StandingGrantDefinitionReference>();
            var reviewDocuments = ImmutableArray.CreateBuilder<ApplicationCandidateReviewClosureDocument>();
            var seen = new HashSet<StandingGrantDefinitionReference>();

            foreach (var read in service.Reads.OrderBy(value => value.Alias, StringComparer.Ordinal))
            {
                if (!await AddQueryClosureAsync(host, candidate.ApplicationId, basis, snapshot,
                        read.QualifiedQueryId, read.Contract, dependencies, reviewDocuments, seen,
                        new HashSet<string>(StringComparer.Ordinal), cancellationToken)) return Rejected();
            }
            foreach (var action in service.Actions.OrderBy(value => value.Alias, StringComparer.Ordinal))
            {
                var reference = new StandingGrantDefinitionReference(action.QualifiedMechanicId, "mechanic",
                    action.MechanicVersion, action.ContentFingerprint);
                if (!await AddDependencyAsync(host, candidate.ApplicationId, basis, snapshot, reference,
                        dependencies, reviewDocuments, seen, cancellationToken, prepareMechanic: true)) return Rejected();
            }
            foreach (var job in service.Jobs.OrderBy(value => value.Alias, StringComparer.Ordinal))
            {
                var reference = new StandingGrantDefinitionReference(job.QualifiedProcedureId, "procedure",
                    job.ProcedureVersion, job.ContentFingerprint);
                if (!await AddDependencyAsync(host, candidate.ApplicationId, basis, snapshot, reference,
                        dependencies, reviewDocuments, seen, cancellationToken, prepareMechanic: false)) return Rejected();
            }
            if (dependencies.Count is 0 or > ApplicationAuthoringLimits.Dependencies
                || reviewDocuments.Count + selection.Documents.Length > ApplicationCandidateReuseJudgmentLimits.Documents)
                return Rejected();

            var selectedReview = selection.Documents.OrderBy(value => value.Document.RelativePath, StringComparer.Ordinal)
                .Select(value => new ApplicationCandidateReviewClosureDocument(value.Definition,
                    value.Role == "changed" ? ApplicationCandidateReviewDocumentRole.Changed
                        : ApplicationCandidateReviewDocumentRole.Sidecar,
                    value.Document, value.RetainedBytes));
            var evidenceValue = ApplicationCandidateWorkflowReviewClosureEvidence.Create(candidate, retained, basis,
                rootReference, predecessor.Reference, service, selection.EvidenceFingerprint,
                selectedReview.Concat(reviewDocuments).ToImmutableArray(), dependencies.ToImmutable());
            return ApplicationCandidateReviewClosureReadResult.Available(evidenceValue);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException
            or InteractionContractException or ApplicationActivationException
            or ApplicationCatalogMaterializationException or CryptographicException or KeyNotFoundException)
        {
            return claimed ? Rejected() : ApplicationCandidateReviewClosureReadResult.NotApplicable();
        }
    }

    private async Task<bool> AddQueryClosureAsync(InteractionInvocationHost host, ApplicationIdentifier applicationId,
        ActiveApplicationManifest basis, ActiveCatalogFeatureSnapshot snapshot, string queryId,
        InteractionQueryContractReference? expected, ImmutableArray<StandingGrantDefinitionReference>.Builder dependencies,
        ImmutableArray<ApplicationCandidateReviewClosureDocument>.Builder documents,
        HashSet<StandingGrantDefinitionReference> seen, HashSet<string> path, CancellationToken cancellationToken)
    {
        if (!path.Add(queryId) || path.Count > ApplicationQueryContract.MaximumDeclaredSelectionLinks) return false;
        var resolution = await targets.ResolveCurrentAsync(host, queryId, ApplicationQueryContract.CatalogKind, cancellationToken);
        if (resolution is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } target }
            || target.OwnerApplicationId != applicationId || target.Candidate is not null
            || target.RetainedActivation is not null) return false;
        var reference = new StandingGrantDefinitionReference(target.DefinitionId, target.Kind,
            target.Revision, target.ContentFingerprint);
        var record = ExactRecord(snapshot, reference);
        if (record is null) return false;
        var contract = ApplicationQueryContract.Parse(record.ContentJson, applicationId);
        if (contract.Status != "active" || contract.Id != queryId
            || expected is not null && !SameContract(contract, expected)) return false;
        if (!await AddDependencyAsync(host, applicationId, basis, snapshot, reference, dependencies,
                documents, seen, cancellationToken, prepareMechanic: false)) return false;
        var nested = contract.Selection?.QueryId ?? contract.CampaignSelection?.QueryId;
        return nested is null || await AddQueryClosureAsync(host, applicationId, basis, snapshot, nested, null,
            dependencies, documents, seen, path, cancellationToken);
    }

    private async Task<bool> AddDependencyAsync(InteractionInvocationHost host, ApplicationIdentifier applicationId,
        ActiveApplicationManifest basis, ActiveCatalogFeatureSnapshot snapshot,
        StandingGrantDefinitionReference reference,
        ImmutableArray<StandingGrantDefinitionReference>.Builder dependencies,
        ImmutableArray<ApplicationCandidateReviewClosureDocument>.Builder documents,
        HashSet<StandingGrantDefinitionReference> seen, CancellationToken cancellationToken, bool prepareMechanic)
    {
        if (!seen.Add(reference)) return true;
        if (dependencies.Count >= ApplicationAuthoringLimits.Dependencies) return false;
        var resolution = await targets.ResolveAsync(host, reference, cancellationToken);
        if (resolution is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } target }
            || target.OwnerApplicationId != applicationId || target.Candidate is not null
            || target.RetainedActivation is not null) return false;
        var record = ExactRecord(snapshot, reference);
        if (record is null || record.Status != "active") return false;
        var paths = reference.Kind == "mechanic"
            ? new[] { record.SourceLogicalPath, Path.ChangeExtension(record.SourceLogicalPath, ".js").Replace('\\', '/') }
            : new[] { record.SourceLogicalPath };
        foreach (var relativePath in paths)
        {
            var winner = basis.Winners.SingleOrDefault(value => value.RelativePath == relativePath);
            if (winner is null || !winner.IsText || winner.Trust != Sources.SourceTrust.Trusted) return false;
            var retained = evidence.ReadDocumentEvidence(applicationId, basis.ActivationRevision, winner.LogicalIdentity);
            if (retained?.RetainedBytes is not { } bytes || retained.IsLegacyMetadataOnly
                || retained.ContentFingerprint != winner.ContentFingerprint || retained.Length != winner.Length
                || bytes.LongLength != winner.Length || Convert.ToHexString(SHA256.HashData(bytes)) != winner.ContentFingerprint)
                return false;
            documents.Add(new(reference, ApplicationCandidateReviewDocumentRole.Dependency,
                winner, bytes.ToImmutableArray()));
            if (prepareMechanic && relativePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                _ = JintMechanicEngine.PrepareMechanicProgram(new UTF8Encoding(false, true).GetString(bytes));
        }
        dependencies.Add(reference);
        return true;
    }

    private static CatalogRecordDefinition? ExactRecord(ActiveCatalogFeatureSnapshot snapshot,
        StandingGrantDefinitionReference reference)
    {
        var records = snapshot.Manifest.Records.Where(value => value.QualifiedId == reference.DefinitionId).ToArray();
        return records.Length == 1 && records[0].Kind == reference.Kind && records[0].Version == reference.Revision
            && records[0].ContentFingerprint == reference.ContentFingerprint ? records[0] : null;
    }

    private static bool SameContract(ApplicationQueryContract current, InteractionQueryContractReference expected) =>
        current.Executor == expected.Executor && current.ProjectionQualifiedId == expected.ProjectionQualifiedId
        && current.ProjectionVersion == expected.ProjectionVersion
        && current.ProjectionContentHash == expected.ProjectionContentHash
        && current.OutputSchemaHash == expected.OutputSchemaHash
        && current.ObjectCollectionId == expected.CollectionId && current.Exposure == expected.Exposure
        && current.Roles.Keys.Order(StringComparer.Ordinal).SequenceEqual(expected.Roles.Order(StringComparer.Ordinal), StringComparer.Ordinal)
        && InteractionCanonicalJson.CanonicalizeObject(current.OutputSchemaJson) == expected.OutputSchemaJson;

    private static bool HasServiceRequirement(string contentJson)
    {
        using var content = JsonDocument.Parse(contentJson);
        if (!content.RootElement.TryGetProperty("requirements", out var requirements)
            || requirements.ValueKind != JsonValueKind.String) return false;
        using var value = JsonDocument.Parse(requirements.GetString()!);
        return value.RootElement.ValueKind == JsonValueKind.Object
            && value.RootElement.EnumerateObject().Any(property => property.NameEquals("service"));
    }

    private static bool ClosedServiceRequirement(string contentJson)
    {
        using var content = JsonDocument.Parse(contentJson);
        using var value = JsonDocument.Parse(content.RootElement.GetProperty("requirements").GetString()!);
        var fields = value.RootElement.EnumerateObject().ToArray();
        return fields.Length == 1 && fields[0].NameEquals("service");
    }

    private static bool ExactPair(ApplicationCandidateSelectedDocument markdown,
        ApplicationCandidateSelectedDocument javascript) =>
        markdown.Definition == javascript.Definition
        && Path.ChangeExtension(markdown.Document.RelativePath, ".js").Replace('\\', '/') == javascript.Document.RelativePath
        && markdown.Document.SourceId == javascript.Document.SourceId
        && markdown.Document.Trust == javascript.Document.Trust
        && markdown.Document.Precedence == javascript.Document.Precedence;

    private static bool ExactSelected(ApplicationCandidateSelectionEvidence selection, CatalogRecordDefinition record)
    {
        var target = selection.Targets[0];
        return record.QualifiedId == target.DefinitionId && record.Kind == target.Kind
            && record.Version == target.Revision && record.ContentFingerprint == target.ContentFingerprint
            && record.Status == "active";
    }

    private static bool OnlySelectedPairChanged(IReadOnlyList<ActivatedApplicationDocument> current,
        IReadOnlyList<ActivatedApplicationDocument> previous, string markdown, string javascript)
    {
        var allowed = new HashSet<string>([markdown, javascript], StringComparer.Ordinal);
        var currentByPath = current.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        var previousByPath = previous.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        if (previous.Any(value => !currentByPath.ContainsKey(value.RelativePath))) return false;
        var changed = current.Where(value => !previousByPath.TryGetValue(value.RelativePath, out var old) || old != value)
            .Select(value => value.RelativePath).ToArray();
        return changed.Length is > 0 and <= 2 && changed.All(allowed.Contains)
            && current.All(value => previousByPath.TryGetValue(value.RelativePath, out var old)
                ? old == value || allowed.Contains(value.RelativePath)
                : allowed.Contains(value.RelativePath));
    }

    private PredecessorResult ReadPredecessor(ApplicationIdentifier applicationId,
        ActiveApplicationManifest basis, string markdownPath, string javascriptPath, string definitionId)
    {
        var oldMarkdown = basis.Winners.SingleOrDefault(value => value.RelativePath == markdownPath);
        var oldJavaScript = basis.Winners.SingleOrDefault(value => value.RelativePath == javascriptPath);
        if (oldMarkdown is null && oldJavaScript is null) return new(PredecessorStatus.New, null);
        if (oldMarkdown is null || oldJavaScript is null) return new(PredecessorStatus.Invalid, null);
        var markdownEvidence = evidence.ReadDocumentEvidence(applicationId,
            basis.ActivationRevision, oldMarkdown.LogicalIdentity);
        var javascriptEvidence = evidence.ReadDocumentEvidence(applicationId,
            basis.ActivationRevision, oldJavaScript.LogicalIdentity);
        if (markdownEvidence?.RetainedBytes is not { } markdownBytes || markdownEvidence.IsLegacyMetadataOnly
            || javascriptEvidence?.RetainedBytes is not { } javascriptBytes || javascriptEvidence.IsLegacyMetadataOnly)
            return new(PredecessorStatus.Invalid, null);
        var record = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(applicationId, oldMarkdown,
            new Dictionary<string, ActivatedApplicationDocument>(StringComparer.Ordinal)
                { [markdownPath] = oldMarkdown, [javascriptPath] = oldJavaScript },
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
                { [markdownPath] = markdownBytes, [javascriptPath] = javascriptBytes });
        return record is not null && record.Kind == "mechanic" && record.QualifiedId == definitionId
            ? new(PredecessorStatus.Existing, new(record.QualifiedId, record.Kind,
                record.Version, record.ContentFingerprint))
            : new(PredecessorStatus.Invalid, null);
    }

    private static CatalogRecordView View(CatalogRecordDefinition record) => new(new(record.Collection,
        record.Kind, record.QualifiedId, record.Name, record.Description, record.Path, record.Status,
        record.Version, record.ContentFingerprint, record.SourceId, record.SourceLogicalPath), record.ContentJson);

    private static ApplicationCandidateReviewClosureReadResult Rejected() =>
        ApplicationCandidateReviewClosureReadResult.Rejected();

    private enum PredecessorStatus { New, Existing, Invalid }
    private sealed record PredecessorResult(PredecessorStatus Status,
        StandingGrantDefinitionReference? Reference);
}

internal sealed class ApplicationCandidateWorkflowReviewClosureEvidence : IApplicationCandidateReviewClosureEvidence
{
    private ApplicationCandidateWorkflowReviewClosureEvidence(ApplicationCandidateReference candidate,
        ApplicationCandidateRetainedMetadata retained,
        ActiveApplicationManifest basis, StandingGrantDefinitionReference definition,
        StandingGrantDefinitionReference? predecessor, ApplicationReadOnlyServiceDefinition service,
        string selectionEvidenceFingerprint,
        ImmutableArray<ApplicationCandidateReviewClosureDocument> reviewDocuments,
        ImmutableArray<StandingGrantDefinitionReference> dependencies)
    {
        Retained = retained;
        Basis = basis;
        Definition = definition;
        Predecessor = predecessor;
        Service = service;
        Candidate = candidate;
        SelectionEvidenceFingerprint = selectionEvidenceFingerprint;
        ReviewDocuments = reviewDocuments;
        Dependencies = dependencies;
        EvidenceFingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/application-candidate-workflow-review-closure/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                Candidate, basis = Basis.ActivationFingerprint, SelectionEvidenceFingerprint,
                grammar = ApplicationCandidateWorkflowReviewClosureReader.GrammarVersion, Definition, Predecessor,
                service = Service.ToJson(),
                documents = ReviewDocuments.Select(value => new { value.Definition, value.Role, value.Document }),
                dependencies = Dependencies
            })));
    }

    internal ApplicationCandidateRetainedMetadata Retained { get; }
    internal ActiveApplicationManifest Basis { get; }
    internal StandingGrantDefinitionReference Definition { get; }
    internal StandingGrantDefinitionReference? Predecessor { get; }
    internal ApplicationReadOnlyServiceDefinition Service { get; }
    public string Grammar => ApplicationCandidateWorkflowReviewClosureReader.GrammarVersion;
    public ApplicationCandidateReference Candidate { get; }
    public string SelectionEvidenceFingerprint { get; }
    public string EvidenceFingerprint { get; }
    public ImmutableArray<ApplicationCandidateReviewClosureDocument> ReviewDocuments { get; }
    public ImmutableArray<StandingGrantDefinitionReference> Dependencies { get; }
    public ImmutableArray<ApplicationCandidateReviewAlternativeEvidence> ReviewAlternatives => [];

    internal static ApplicationCandidateWorkflowReviewClosureEvidence Create(
        ApplicationCandidateReference candidate, ApplicationCandidateRetainedMetadata retained, ActiveApplicationManifest basis,
        StandingGrantDefinitionReference definition, StandingGrantDefinitionReference? predecessor,
        ApplicationReadOnlyServiceDefinition service,
        string selectionEvidenceFingerprint, ImmutableArray<ApplicationCandidateReviewClosureDocument> documents,
        ImmutableArray<StandingGrantDefinitionReference> dependencies) =>
        new(candidate, retained, basis, definition, predecessor, service,
            selectionEvidenceFingerprint, documents, dependencies);
}
