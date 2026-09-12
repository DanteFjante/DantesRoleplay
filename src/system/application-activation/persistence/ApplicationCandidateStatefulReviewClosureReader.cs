using System.Collections.Immutable;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Claims only new or contract-changed, service-free stateful atomic mechanics.</summary>
internal sealed class ApplicationCandidateStatefulReviewClosureReader(
    DantesRoleplayDbContext db, IApplicationRegistry applications, IApplicationActivationReader activations,
    IActivatedApplicationEvidenceReader evidence, IStandingGrantTargetResolver targets,
    IPublicApplicationCatalogProvider catalogs) : IApplicationCandidateReviewClosureReader
{
    public string Grammar => "stateful-atomic-mechanic-v1";

    public async Task<ApplicationCandidateReviewClosureReadResult> ReadAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        ApplicationCandidateSelectionEvidence selection, CancellationToken cancellationToken = default)
    {
        if (selection.Targets.Length != 1 || selection.Targets[0].Kind != "mechanic")
            return ApplicationCandidateReviewClosureReadResult.NotApplicable();
        try
        {
            var result = await ReadCoreAsync(host, candidate, selection, cancellationToken);
            return result.Applicable
                ? result.Evidence is null ? ApplicationCandidateReviewClosureReadResult.Rejected()
                    : ApplicationCandidateReviewClosureReadResult.Available(result.Evidence)
                : ApplicationCandidateReviewClosureReadResult.NotApplicable();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException
            or ApplicationActivationException or ApplicationCatalogMaterializationException or KeyNotFoundException)
        { return ApplicationCandidateReviewClosureReadResult.Rejected(); }
    }

    private async Task<(bool Applicable, ApplicationCandidateStatefulReviewClosureEvidence? Evidence)> ReadCoreAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        ApplicationCandidateSelectionEvidence selection, CancellationToken cancellationToken)
    {
        await using var scope = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
        if (selection.Candidate != candidate || selection.Documents.Length != 2
            || selection.BaseOrigin is null || !catalogs.TryGet(candidate.ApplicationId, out var activeCatalog))
            return (true, null);
        var retained = await new ApplicationCandidateRetainedReader(db, applications).ReadMetadataAsync(
            candidate.ApplicationId, candidate.CandidateId, candidate.Revision, cancellationToken);
        var basis = retained is null ? null : await ApplicationCandidateDocumentSelection.ReadBaseAsync(db, activations,
            candidate.ApplicationId, retained.RevisionRow.ExpectedActiveFingerprint, cancellationToken);
        var active = activations.Current(candidate.ApplicationId);
        var basisIsCurrent = active?.ActivationFingerprint == basis?.ActivationFingerprint;
        var candidateIsCurrent = active?.CandidateManifestFingerprint == candidate.ContentFingerprint
            && retained is not null && active.Winners.SequenceEqual(retained.Documents);
        if (retained is null || basis is null || retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint
            || selection.BaseOrigin.ActivationFingerprint != basis.ActivationFingerprint
            || !basisIsCurrent && !candidateIsCurrent)
            return (true, null);
        var markdown = selection.Documents.SingleOrDefault(value =>
            value.Document.RelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        var javascript = selection.Documents.SingleOrDefault(value =>
            value.Document.RelativePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase));
        if (markdown is null || javascript is null
            || Path.ChangeExtension(markdown.Document.RelativePath, ".js").Replace('\\', '/') != javascript.Document.RelativePath
            || markdown.Document.SourceId != javascript.Document.SourceId
            || markdown.Document.Trust != javascript.Document.Trust
            || markdown.Document.Precedence != javascript.Document.Precedence) return (true, null);
        var successorDefinition = Parse(candidate.ApplicationId, markdown, javascript);
        if (successorDefinition is null || successorDefinition.Kind != "mechanic"
            || successorDefinition.QualifiedId != selection.Targets[0].DefinitionId
            || successorDefinition.Version != selection.Targets[0].Revision
            || successorDefinition.ContentFingerprint != selection.Targets[0].ContentFingerprint
            || successorDefinition.Status != "active") return (true, null);
        var successor = View(successorDefinition);
        var requirements = Requirements(successor);
        if (requirements is null) return (true, null);
        if (ServiceRequirement(successor) || requirements.Event is not null) return (false, null);
        if (!Stateful(requirements)) return (false, null);
        if (requirements.CompositionProblems().Count != 0 || requirements.ProjectionProblems().Count != 0)
            return (true, null);

        var previous = basis.Winners.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        var current = retained.Documents.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        var selectedPaths = new HashSet<string>([markdown.Document.RelativePath, javascript.Document.RelativePath], StringComparer.Ordinal);
        if (basis.Winners.Any(value => !current.ContainsKey(value.RelativePath))
            || retained.Documents.Any(value => !previous.TryGetValue(value.RelativePath, out var old)
                ? !selectedPaths.Contains(value.RelativePath)
                : value != old && !selectedPaths.Contains(value.RelativePath))) return (true, null);

        CatalogRecordView? predecessor = null;
        StandingGrantDefinitionReference? predecessorReference = null;
        var hasMarkdown = previous.TryGetValue(markdown.Document.RelativePath, out var oldMarkdown);
        var hasJavascript = previous.TryGetValue(javascript.Document.RelativePath, out var oldJavascript);
        if (hasMarkdown != hasJavascript) return (true, null);
        if (hasMarkdown)
        {
            if (oldMarkdown == markdown.Document && oldJavascript != javascript.Document)
                return (false, null); // the existing no-review body-update path owns this exact shape
            if (oldMarkdown == markdown.Document || oldJavascript is null) return (true, null);
            var oldMarkdownBytes = ReadBytes(candidate.ApplicationId, basis.ActivationRevision, oldMarkdown!);
            var oldJavascriptBytes = ReadBytes(candidate.ApplicationId, basis.ActivationRevision, oldJavascript);
            if (oldMarkdownBytes is null || oldJavascriptBytes is null) return (true, null);
            var parsed = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(candidate.ApplicationId, oldMarkdown!,
                new Dictionary<string, ActivatedApplicationDocument>(StringComparer.Ordinal)
                { [oldMarkdown!.RelativePath] = oldMarkdown, [oldJavascript.RelativePath] = oldJavascript },
                new Dictionary<string, byte[]>(StringComparer.Ordinal)
                { [oldMarkdown.RelativePath] = oldMarkdownBytes, [oldJavascript.RelativePath] = oldJavascriptBytes });
            if (parsed is null || parsed.Kind != "mechanic" || parsed.QualifiedId != successor.Summary.QualifiedId)
                return (true, null);
            predecessor = View(parsed);
            predecessorReference = Reference(parsed);
            if (basisIsCurrent)
            {
                var resolved = await targets.ResolveAsync(host, predecessorReference, cancellationToken);
                if (resolved is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } predecessorTarget }
                    || predecessorTarget.OwnerApplicationId != candidate.ApplicationId) return (true, null);
            }
        }
        else
        {
            if (basisIsCurrent)
            {
                var resolved = await targets.ResolveCurrentAsync(host, successor.Summary.QualifiedId, "mechanic", cancellationToken);
                if (resolved.Code != "STANDING_GRANT_DEFINITION_UNAVAILABLE") return (true, null);
            }
        }

        var candidateDefinition = Reference(successorDefinition);
        var source = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == retained.RevisionRow.SourceOperationId, cancellationToken);
        if (source is null || !ApplicationCandidateOperationProof.WriteMatches(
                source, retained, [candidateDefinition], out _)) return (true, null);
        var dependencyResult = Dependencies(candidate.ApplicationId, requirements, activeCatalog, basis);
        if (dependencyResult is null) return (true, null);
        var reviewDocuments = ImmutableArray.CreateBuilder<ApplicationCandidateReviewClosureDocument>();
        reviewDocuments.Add(new(candidateDefinition, ApplicationCandidateReviewDocumentRole.Changed,
            markdown.Document, markdown.RetainedBytes));
        reviewDocuments.Add(new(candidateDefinition, ApplicationCandidateReviewDocumentRole.Sidecar,
            javascript.Document, javascript.RetainedBytes));
        reviewDocuments.AddRange(dependencyResult.Value.Documents);
        return (true, ApplicationCandidateStatefulReviewClosureEvidence.Create(retained, basis, successor,
            predecessor, requirements, candidateDefinition, predecessorReference, selection.Targets[0],
            selection.EvidenceFingerprint, dependencyResult.Value.Dependencies, reviewDocuments.ToImmutable()));
    }

    private (ImmutableArray<StandingGrantDefinitionReference> Dependencies,
        ImmutableArray<ApplicationCandidateReviewClosureDocument> Documents)? Dependencies(
        ApplicationIdentifier applicationId, MechanicRequirements root, ICatalogNavigator catalog,
        ActiveApplicationManifest basis)
    {
        var dependencies = new SortedDictionary<string, StandingGrantDefinitionReference>(StringComparer.Ordinal);
        var records = new Dictionary<string, CatalogRecordView>(StringComparer.Ordinal);
        var visits = 0;
        Walk(root, string.Empty, 0, new HashSet<string>(StringComparer.Ordinal));
        var documents = ImmutableArray.CreateBuilder<ApplicationCandidateReviewClosureDocument>();
        foreach (var dependency in dependencies.Values)
        {
            var record = records[dependency.DefinitionId];
            var markdown = basis.Winners.SingleOrDefault(value => value.RelativePath == record.Summary.SourceLogicalPath);
            var javascriptPath = Path.ChangeExtension(record.Summary.SourceLogicalPath, ".js").Replace('\\', '/');
            var javascript = basis.Winners.SingleOrDefault(value => value.RelativePath == javascriptPath);
            var markdownBytes = markdown is null ? null : ReadBytes(applicationId, basis.ActivationRevision, markdown);
            var javascriptBytes = javascript is null ? null : ReadBytes(applicationId, basis.ActivationRevision, javascript);
            if (markdown is null || javascript is null || markdownBytes is null || javascriptBytes is null) return null;
            documents.Add(new(dependency, ApplicationCandidateReviewDocumentRole.Dependency,
                markdown, markdownBytes.ToImmutableArray()));
            documents.Add(new(dependency, ApplicationCandidateReviewDocumentRole.Dependency,
                javascript, javascriptBytes.ToImmutableArray()));
        }
        return (dependencies.Values.ToImmutableArray(), documents.ToImmutable());

        void Walk(MechanicRequirements requirements, string current, int depth, HashSet<string> lineage)
        {
            if (depth >= 8 || current.Length > 0 && !lineage.Add(current)) throw new InvalidOperationException();
            foreach (var child in requirements.Children.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                if (++visits > 256) throw new InvalidOperationException();
                var id = child.Value.MechanicId.StartsWith(applicationId.Value + ".", StringComparison.Ordinal)
                    ? child.Value.MechanicId : applicationId.Value + "." + child.Value.MechanicId;
                var record = catalog.Inspect(new(applicationId, applicationId.Value, id));
                if (record.Summary.Kind != "mechanic" || record.Summary.Status != "active"
                    || child.Value.MechanicVersion > 0 && (child.Value.MechanicVersion != record.Summary.Version
                        || child.Value.ContentFingerprint != record.Summary.ContentFingerprint))
                    throw new InvalidOperationException();
                dependencies[id] = new(id, "mechanic", record.Summary.Version, record.Summary.ContentFingerprint);
                records[id] = record;
                var nested = Requirements(record) ?? throw new InvalidOperationException();
                if (ServiceRequirement(record) || nested.Event is not null) throw new InvalidOperationException();
                Walk(nested, id, depth + 1, new HashSet<string>(lineage, StringComparer.Ordinal));
            }
        }
    }

    private byte[]? ReadBytes(ApplicationIdentifier applicationId, int activationRevision,
        ActivatedApplicationDocument document)
    {
        var found = evidence.ReadDocumentEvidence(applicationId, activationRevision, document.LogicalIdentity);
        return found?.RetainedBytes is { } bytes && !found.IsLegacyMetadataOnly
            && found.ContentFingerprint == document.ContentFingerprint && found.Length == document.Length ? bytes : null;
    }

    private static CatalogRecordDefinition? Parse(ApplicationIdentifier app,
        ApplicationCandidateSelectedDocument markdown, ApplicationCandidateSelectedDocument javascript) =>
        ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(app, markdown.Document,
            new Dictionary<string, ActivatedApplicationDocument>(StringComparer.Ordinal)
            { [markdown.Document.RelativePath] = markdown.Document, [javascript.Document.RelativePath] = javascript.Document },
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            { [markdown.Document.RelativePath] = markdown.RetainedBytes.ToArray(), [javascript.Document.RelativePath] = javascript.RetainedBytes.ToArray() });

    internal static MechanicRequirements? Requirements(CatalogRecordView record)
    {
        using var document = JsonDocument.Parse(record.ContentJson);
        return document.RootElement.TryGetProperty("requirements", out var value)
            && value.ValueKind == JsonValueKind.String ? MechanicRequirements.Parse(value.GetString()!) : null;
    }

    internal static bool ServiceRequirement(CatalogRecordView record)
    {
        using var document = JsonDocument.Parse(record.ContentJson);
        using var requirements = JsonDocument.Parse(document.RootElement.GetProperty("requirements").GetString()!);
        return requirements.RootElement.EnumerateObject().Any(value =>
            value.Name.Equals("service", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool Stateful(MechanicRequirements requirements) => requirements.Roles.Count > 0
        || requirements.ObjectRoles.Count > 0 || requirements.SnapshotObjects.Count > 0
        || requirements.GraphSnapshots.Count > 0 || requirements.AuthorizedContext is not null
        || requirements.Children.Count > 0 || requirements.EffectComponentIds.Count > 0
        || requirements.ElapsedTime is not null;
    private static CatalogRecordView View(CatalogRecordDefinition record) => new(new(record.Collection, record.Kind,
        record.QualifiedId, record.Name, record.Description, record.Path, record.Status, record.Version,
        record.ContentFingerprint, record.SourceId, record.SourceLogicalPath), record.ContentJson);
    private static StandingGrantDefinitionReference Reference(CatalogRecordDefinition record) =>
        new(record.QualifiedId, record.Kind, record.Version, record.ContentFingerprint);
}

internal interface IApplicationCandidateStatefulRuntimeEvidence
{
    ApplicationCandidateReference Candidate { get; }
    ActiveApplicationManifest Basis { get; }
    CatalogRecordView Successor { get; }
    MechanicRequirements Requirements { get; }
    StandingGrantDefinitionReference Definition { get; }
    StandingGrantDefinitionReference? PredecessorDefinition { get; }
    StandingGrantDefinitionTarget CandidateTarget { get; }
    ImmutableArray<StandingGrantDefinitionReference> Dependencies { get; }
    string Fingerprint { get; }
}

internal sealed class ApplicationCandidateStatefulReviewClosureEvidence :
    IApplicationCandidateReviewClosureEvidence, IApplicationCandidateStatefulRuntimeEvidence
{
    private ApplicationCandidateStatefulReviewClosureEvidence(ApplicationCandidateRetainedMetadata retained,
        ActiveApplicationManifest basis, CatalogRecordView successor, CatalogRecordView? predecessor,
        MechanicRequirements requirements, StandingGrantDefinitionReference definition,
        StandingGrantDefinitionReference? predecessorDefinition, StandingGrantDefinitionTarget candidateTarget,
        string selectionEvidenceFingerprint, ImmutableArray<StandingGrantDefinitionReference> dependencies,
        ImmutableArray<ApplicationCandidateReviewClosureDocument> documents)
    {
        Retained = retained; Basis = basis; Successor = successor; Predecessor = predecessor;
        Requirements = requirements; Definition = definition; PredecessorDefinition = predecessorDefinition;
        CandidateTarget = candidateTarget; SelectionEvidenceFingerprint = selectionEvidenceFingerprint;
        Dependencies = dependencies; ReviewDocuments = documents;
        Candidate = new(retained.ApplicationRevision.ApplicationId, retained.RevisionRow.CandidateId,
            retained.RevisionRow.Revision, retained.RevisionRow.ContentFingerprint);
        EvidenceFingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/application-candidate-stateful-review-closure/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                Candidate, basis = basis.ActivationFingerprint, SelectionEvidenceFingerprint, Definition,
                PredecessorDefinition, successor = successor.Summary, predecessor = predecessor?.Summary,
                dependencies, documents = documents.Select(value => new { value.Definition, value.Role, value.Document })
            })));
    }

    public string Grammar => "stateful-atomic-mechanic-v1";
    public ApplicationCandidateReference Candidate { get; }
    public string SelectionEvidenceFingerprint { get; }
    public string EvidenceFingerprint { get; }
    public ImmutableArray<ApplicationCandidateReviewClosureDocument> ReviewDocuments { get; }
    public ImmutableArray<StandingGrantDefinitionReference> Dependencies { get; }
    internal ApplicationCandidateRetainedMetadata Retained { get; }
    public ActiveApplicationManifest Basis { get; }
    public CatalogRecordView Successor { get; }
    internal CatalogRecordView? Predecessor { get; }
    public MechanicRequirements Requirements { get; }
    public StandingGrantDefinitionReference Definition { get; }
    public StandingGrantDefinitionReference? PredecessorDefinition { get; }
    public StandingGrantDefinitionTarget CandidateTarget { get; }
    public string Fingerprint => EvidenceFingerprint;
    internal bool HasNew => PredecessorDefinition is null;

    internal static ApplicationCandidateStatefulReviewClosureEvidence Create(
        ApplicationCandidateRetainedMetadata retained, ActiveApplicationManifest basis,
        CatalogRecordView successor, CatalogRecordView? predecessor, MechanicRequirements requirements,
        StandingGrantDefinitionReference definition, StandingGrantDefinitionReference? predecessorDefinition,
        StandingGrantDefinitionTarget candidateTarget, string selectionEvidenceFingerprint,
        ImmutableArray<StandingGrantDefinitionReference> dependencies,
        ImmutableArray<ApplicationCandidateReviewClosureDocument> documents) => new(retained, basis, successor,
            predecessor, requirements, definition, predecessorDefinition, candidateTarget,
            selectionEvidenceFingerprint, dependencies, documents);
}
