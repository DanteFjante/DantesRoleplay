using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using DantesRoleplay.Sources;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// Records one bounded three-way comparison. It never calls export/import and never writes authored content.
/// </summary>
public sealed class CatalogSynchronizationService(
    CatalogImporter importer,
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    ISourceRegistry sources,
    IAllowedSourceRootResolver roots,
    IRegisteredSourceScanner scanner,
    ISourceOverlayResolver overlays,
    IOperationLog operations) : ICatalogSynchronizationService, IApplicationCatalogSynchronizationEvidenceReader
{
    private const string Tool = "catalog-synchronization-compare";
    private const int MaximumRecords = 16;
    private const int MaximumDocuments = 32;
    private const int MaximumEvidenceBytes = 64 * 1024;

    public async Task<InteractionInvocationResult> CompareAsync(InteractionInvocationHost host,
        CatalogSynchronizationCompareRequest request, CancellationToken cancellationToken = default)
    {
        if (!host.Budget.TryConsumeOperation()) return Failed("INVOCATION_BUDGET_EXHAUSTED");
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow) return Failed("INVOCATION_DEADLINE_EXCEEDED");
        if (!Valid(request)) return Failed("CATALOG_SYNCHRONIZATION_INVALID");
        try
        {
            var app = host.ApplicationRevision.ApplicationId;
            if (app.IsSystem) return Failed("CATALOG_SYNCHRONIZATION_APPLICATION_INVALID");
            var registered = applications.Get(app);
            if (registered is null || registered.Revision != host.ApplicationRevision.Revision
                || registered.Fingerprint != host.ApplicationRevision.Fingerprint)
                return Failed("CATALOG_SYNCHRONIZATION_APPLICATION_STALE");
            if (!roots.TryResolve(request.AllowedRootId, out var root) || string.IsNullOrWhiteSpace(root))
                return InteractionInvocationResult.Unavailable("CATALOG_SYNCHRONIZATION_ROOT_UNAVAILABLE",
                    "The configured catalog root is unavailable.");
            var canonicalRoot = Path.GetFullPath(root);
            var projection = Projection(host, request);
            var operationId = ApplicationCandidateOperationProof.OperationId(
                host.Principal.PrincipalId, app.Value, host.CommandId, "catalog-sync");
            var replay = await operations.GetAsync(operationId, cancellationToken);
            if (replay is not null)
            {
                if (replay.Tool != Tool || !replay.Success || replay.Subject != app.Value
                    || replay.ProjectionJson != projection || !TryEvidence(replay.GuardEvidenceJson, out var retained))
                    return Failed("CATALOG_SYNCHRONIZATION_COMMAND_CONFLICT");
                return Result(operationId, retained!);
            }

            var evidence = await BuildEvidenceAsync(host, request, canonicalRoot, cancellationToken);
            var evidenceJson = EvidenceJson(evidence);
            if (Encoding.UTF8.GetByteCount(projection) > MaximumEvidenceBytes
                || Encoding.UTF8.GetByteCount(evidenceJson) > MaximumEvidenceBytes)
                return InteractionInvocationResult.Unavailable("CATALOG_SYNCHRONIZATION_EVIDENCE_LIMIT",
                    "The selected catalog comparison exceeds the evidence limit.");
            await operations.RecordAsync(Tool, "Compared a bounded catalog synchronization selection.", true,
                subject: app.Value, projectionJson: projection, guardEvidenceJson: evidenceJson,
                id: operationId, cancellationToken: cancellationToken);
            return Result(operationId, evidence);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return InteractionInvocationResult.Unavailable("CATALOG_SYNCHRONIZATION_UNAVAILABLE",
                "The catalog comparison is unavailable.");
        }
    }

    public async Task<string?> ValidateCandidateAsync(InteractionInvocationHost host,
        ApplicationCandidateWriteRequest request, CancellationToken cancellationToken = default)
    {
        var reference = request.SynchronizationEvidenceReference;
        if (reference is null || reference.Length != 32
            || reference.Any(value => !char.IsAsciiDigit(value) && value is not (>= 'a' and <= 'f')))
            return "APPLICATION_CANDIDATE_SYNC_EVIDENCE_INVALID";
        var operation = await operations.GetAsync(reference, cancellationToken);
        if (operation is null || operation.Tool != Tool || !operation.Success
            || operation.Subject != host.ApplicationRevision.ApplicationId.Value
            || !TryProjection(operation.ProjectionJson, out var projection)
            || !TryEvidence(operation.GuardEvidenceJson, out var retained))
            return "APPLICATION_CANDIDATE_SYNC_EVIDENCE_UNAVAILABLE";
        if (operation.Id != ApplicationCandidateOperationProof.OperationId(
                projection!.Principal, projection.ApplicationId, projection.CommandId, "catalog-sync"))
            return "APPLICATION_CANDIDATE_SYNC_EVIDENCE_UNAVAILABLE";
        if (projection.ApplicationId != host.ApplicationRevision.ApplicationId.Value
            || projection.ApplicationRevision != host.ApplicationRevision.Revision
            || projection.ApplicationFingerprint != host.ApplicationRevision.Fingerprint
            || projection.Request.ExpectedActiveFingerprint != request.ExpectedActiveFingerprint)
            return "APPLICATION_CANDIDATE_SYNC_EVIDENCE_STALE";
        if (retained!.Status != "ready") return retained.Status switch
        {
            "conflict" => "APPLICATION_CANDIDATE_SYNC_CONFLICT",
            "needs-export" => "APPLICATION_CANDIDATE_SYNC_NEEDS_EXPORT",
            _ => "APPLICATION_CANDIDATE_SYNC_INCOMPLETE"
        };
        if (!roots.TryResolve(projection.Request.AllowedRootId, out var root) || string.IsNullOrWhiteSpace(root))
            return "APPLICATION_CANDIDATE_SYNC_EVIDENCE_UNAVAILABLE";
        CatalogSynchronizationEvidence current;
        try
        {
            current = await BuildEvidenceAsync(host, projection.Request, Path.GetFullPath(root), cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return "APPLICATION_CANDIDATE_SYNC_EVIDENCE_UNAVAILABLE"; }
        if (current.Status != "ready" || current.EvidenceFingerprint != retained.EvidenceFingerprint)
            return "APPLICATION_CANDIDATE_SYNC_EVIDENCE_STALE";
        return DocumentsMatch(request.Documents, retained.Documents)
            ? null
            : "APPLICATION_CANDIDATE_SYNC_DOCUMENT_MISMATCH";
    }

    private async Task<CatalogSynchronizationEvidence> BuildEvidenceAsync(InteractionInvocationHost host,
        CatalogSynchronizationCompareRequest request, string root, CancellationToken cancellationToken)
    {
        var active = activations.Current(host.ApplicationRevision.ApplicationId);
        var comparison = await importer.CompareSelectedAsync(root, request.AllowedRootId, request.Records, cancellationToken);
        var sourceRegistrations = sources.For(host.ApplicationRevision.ApplicationId)
            .Where(value => value.AllowedRootId == request.AllowedRootId && value.Trust == SourceTrust.Trusted)
            .ToDictionary(value => value.SourceId, StringComparer.Ordinal);
        var scan = await scanner.ScanAsync(host.ApplicationRevision.ApplicationId, cancellationToken);
        var selectedScan = scan.Documents.Where(value => sourceRegistrations.ContainsKey(value.SourceId)).ToArray();
        var selectedProblems = scan.Problems.Where(value => string.IsNullOrEmpty(value.SourceId)
            || sourceRegistrations.ContainsKey(value.SourceId)).ToArray();
        var overlay = overlays.Resolve(host.ApplicationRevision.ApplicationId, selectedScan, selectedProblems);
        var paths = comparison.Records.SelectMany(value => value.RelativePaths).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var documents = new List<CatalogSynchronizationDocumentEvidence>();
        foreach (var path in paths)
        {
            var winner = overlay.Winners.SingleOrDefault(value => value.RelativePath == path);
            if (winner is null || !sourceRegistrations.TryGetValue(winner.SourceId, out var source)) continue;
            documents.Add(new(winner.LogicalIdentity, winner.SourceId, SourceRegistrationFingerprint.Compute(source),
                winner.RelativePath, winner.MediaType, winner.ContentFingerprint, winner.Length, winner.IsText));
        }
        var status = Status(request, active?.ActivationFingerprint, comparison, paths, documents, overlay.Problems);
        var core = new CatalogSynchronizationEvidenceCore(host.ApplicationRevision.ApplicationId.Value,
            host.ApplicationRevision.Revision, host.ApplicationRevision.Fingerprint,
            active?.ActivationRevision, request.ExpectedActiveFingerprint, comparison.AllowedRootId, comparison.ManifestFingerprint,
            status, comparison.Records, documents.OrderBy(value => value.RelativePath, StringComparer.Ordinal).ToArray());
        var coreJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(core));
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/catalog-synchronization-evidence/v1", coreJson);
        return new(core.ApplicationId, core.ApplicationRevision, core.ApplicationFingerprint,
            core.BaseActivationRevision, core.ExpectedActiveFingerprint, core.AllowedRootId, core.ManifestFingerprint, core.Status,
            core.Records, core.Documents, fingerprint);
    }

    private static string Status(CatalogSynchronizationCompareRequest request, string? activeFingerprint,
        CatalogSynchronizationPlanEvidence comparison, IReadOnlyList<string> paths,
        IReadOnlyList<CatalogSynchronizationDocumentEvidence> documents,
        IReadOnlyList<SourceOverlayProblem> problems)
    {
        if (!comparison.Complete || activeFingerprint != request.ExpectedActiveFingerprint
            || comparison.Records.Count != request.Records.Count
            || comparison.Records.Any(value => value.FileFingerprint is null || value.RelativePaths.Count == 0)
            || paths.Count is < 1 or > MaximumDocuments || documents.Count != paths.Count
            || documents.Any(value => !value.IsText || value.Length > 64 * 1024) || problems.Count != 0)
            return "incomplete";
        if (comparison.Records.Any(value => value.Change == CatalogChange.Conflict)) return "conflict";
        if (comparison.Records.Any(value => value.Change is CatalogChange.DatabaseEdited or CatalogChange.NewInDatabase))
            return "needs-export";
        if (comparison.Records.Any(value => value.Change is CatalogChange.MissingFromFiles or CatalogChange.GoneFromBoth))
            return "incomplete";
        return "ready";
    }

    private static bool DocumentsMatch(IReadOnlyList<ApplicationCandidateDocumentInput> proposed,
        IReadOnlyList<CatalogSynchronizationDocumentEvidence> retained)
    {
        if (proposed.Count != retained.Count) return false;
        var expected = retained.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        foreach (var value in proposed)
        {
            if (!expected.TryGetValue(value.RelativePath, out var document)
                || value.LogicalIdentity != document.LogicalIdentity || value.SourceId != document.SourceId
                || value.MediaType != document.MediaType) return false;
            var bytes = Encoding.UTF8.GetBytes(value.Text);
            if (bytes.LongLength != document.Length
                || Convert.ToHexString(SHA256.HashData(bytes)) != document.ContentFingerprint) return false;
        }
        return true;
    }

    private static bool Valid(CatalogSynchronizationCompareRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.AllowedRootId) || request.AllowedRootId.Length > 200
            || request.AllowedRootId != request.AllowedRootId.Trim() || request.Records is null
            || request.Records.Count is < 1 or > MaximumRecords
            || request.ExpectedActiveFingerprint is { } fingerprint && !Hash(fingerprint)
            || request.Records.Any(value => value is null || !Enum.IsDefined(value.Kind)
                || value.Kind is CatalogRecordKind.Entity or CatalogRecordKind.Relationships
                || string.IsNullOrWhiteSpace(value.Id))) return false;
        try { foreach (var value in request.Records) CatalogNamespaces.CatalogNamespaceIdentity.ValidateRecordId(value.Id); }
        catch (ArgumentException) { return false; }
        return request.Records.Select(value => (value.Kind, value.Id)).Distinct().Count() == request.Records.Count;
    }

    private static string Projection(InteractionInvocationHost host, CatalogSynchronizationCompareRequest request) =>
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new CatalogSynchronizationProjection(
            host.Principal.PrincipalId, host.Principal.AuthenticationMethod,
            host.ApplicationRevision.ApplicationId.Value, host.ApplicationRevision.Revision,
            host.ApplicationRevision.Fingerprint, host.CommandId,
            request with { Records = request.Records.OrderBy(value => value.Kind).ThenBy(value => value.Id, StringComparer.Ordinal).ToArray() })));

    private static string EvidenceJson(CatalogSynchronizationEvidence evidence) =>
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(evidence));

    private static bool TryProjection(string json, out CatalogSynchronizationProjection? value)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(json) > MaximumEvidenceBytes
                || InteractionCanonicalJson.CanonicalizeObject(json) != json) { value = null; return false; }
            value = JsonSerializer.Deserialize<CatalogSynchronizationProjection>(json);
            return value is not null && Valid(value.Request);
        }
        catch (JsonException) { value = null; return false; }
    }

    private static bool TryEvidence(string json, out CatalogSynchronizationEvidence? value)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(json) > MaximumEvidenceBytes
                || InteractionCanonicalJson.CanonicalizeObject(json) != json) { value = null; return false; }
            value = JsonSerializer.Deserialize<CatalogSynchronizationEvidence>(json);
            if (value is null || !Hash(value.EvidenceFingerprint)) return false;
            var core = new CatalogSynchronizationEvidenceCore(value.ApplicationId, value.ApplicationRevision,
                value.ApplicationFingerprint, value.BaseActivationRevision, value.ExpectedActiveFingerprint, value.AllowedRootId,
                value.ManifestFingerprint, value.Status, value.Records, value.Documents);
            var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(core));
            return InteractionCanonicalJson.Fingerprint(
                "dantes-roleplay/catalog-synchronization-evidence/v1", canonical) == value.EvidenceFingerprint;
        }
        catch (JsonException) { value = null; return false; }
    }

    private static InteractionInvocationResult Result(string operationId, CatalogSynchronizationEvidence evidence)
    {
        var json = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            status = evidence.Status, evidenceReference = operationId,
            evidence.EvidenceFingerprint,
            records = evidence.Records.Select(value => new
            {
                kind = value.Kind.ToString(), value.Id, change = value.Change.ToString(),
                value.FileFingerprint, value.DatabaseFingerprint, value.AncestorFingerprint,
                value.RelativePaths
            })
        }));
        return InteractionInvocationResult.CompletedComputation(json, operationId);
    }

    private static InteractionInvocationResult Failed(string code) =>
        InteractionInvocationResult.Failed(code, "The catalog synchronization comparison was rejected.");
    private static bool Hash(string value) => value.Length == 64
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');

    private sealed record CatalogSynchronizationProjection(string Principal, string AuthenticationMethod,
        string ApplicationId, int ApplicationRevision, string ApplicationFingerprint, string CommandId,
        CatalogSynchronizationCompareRequest Request);
    private sealed record CatalogSynchronizationDocumentEvidence(string LogicalIdentity, string SourceId,
        string SourceRegistrationFingerprint, string RelativePath, string MediaType,
        string ContentFingerprint, long Length, bool IsText);
    private sealed record CatalogSynchronizationEvidenceCore(string ApplicationId, int ApplicationRevision,
        string ApplicationFingerprint, int? BaseActivationRevision, string? ExpectedActiveFingerprint, string AllowedRootId,
        string? ManifestFingerprint, string Status, IReadOnlyList<CatalogSynchronizationRecordEvidence> Records,
        IReadOnlyList<CatalogSynchronizationDocumentEvidence> Documents);
    private sealed record CatalogSynchronizationEvidence(string ApplicationId, int ApplicationRevision,
        string ApplicationFingerprint, int? BaseActivationRevision, string? ExpectedActiveFingerprint, string AllowedRootId,
        string? ManifestFingerprint, string Status, IReadOnlyList<CatalogSynchronizationRecordEvidence> Records,
        IReadOnlyList<CatalogSynchronizationDocumentEvidence> Documents, string EvidenceFingerprint);
}
