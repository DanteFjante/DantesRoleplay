using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Operations;
using DantesRoleplay.Sources;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Durably retains inert runtime candidates. Publication remains the activation owner's work.</summary>
public sealed partial class SqliteApplicationAuthoringService(
    DantesRoleplayDbContext db, IApplicationRegistry applications, IApplicationActivationReader activations,
    IActivatedApplicationEvidenceReader evidence, ISourceRegistry sources, IStandingGrantPolicy grants,
    IStandingGrantTargetResolver targets, IOperationLog operations,
    IApplicationCandidatePreparation? preparation = null) : IApplicationAuthoringService
{
    private const string Tool = "application-candidate";

    public async Task<InteractionInvocationResult> WriteCandidateAsync(InteractionInvocationHost host,
        ApplicationCandidateWriteRequest request, CancellationToken cancellationToken = default)
    {
        var opened = false;
        if (db.Database.CurrentTransaction is not null) return Failed("APPLICATION_CANDIDATE_OUTER_TRANSACTION");
        if (db.ChangeTracker.HasChanges()) return Failed("APPLICATION_CANDIDATE_CONTEXT_HAS_PENDING_WRITES");
        if (!host.Budget.TryConsumeOperation()) return Failed("INVOCATION_BUDGET_EXHAUSTED");
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow) return Failed("INVOCATION_DEADLINE_EXCEEDED");
        try
        {
            Validate(request);
            var app = host.ApplicationRevision.ApplicationId;
            if (app.IsSystem) return Failed("APPLICATION_CANDIDATE_SYSTEM_UNAVAILABLE");
            if (request.Origin != "runtime") return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_SYNC_UNAVAILABLE", "Catalog synchronization needs its export receipt owner.");

            var derived = Id(host, app);
            var id = request.ExpectedCandidateRevision == 0 ? derived : request.CandidateId!;
            if (request.ExpectedCandidateRevision == 0 && request.CandidateId is not null && request.CandidateId != derived)
                return Failed("APPLICATION_CANDIDATE_ID_MISMATCH");
            request = request with { CandidateId = id };
            var canonical = ApplicationCandidateOperationProof.CanonicalWrite(host, request, id);
            if (Encoding.UTF8.GetByteCount(canonical) > 64 * 1024) return Failed("APPLICATION_CANDIDATE_REQUEST_LIMIT");
            var commandFingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/application-candidate-command/v1", canonical);
            var operationId = Id(host, app, "operation");

            await db.Database.OpenConnectionAsync(cancellationToken);
            opened = true;
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: false);
            await using var enlistment = await db.Database.UseTransactionAsync(transaction, cancellationToken);
            var registered = applications.Get(app);
            if (registered is null || registered.Revision != host.ApplicationRevision.Revision
                || registered.Fingerprint != host.ApplicationRevision.Fingerprint
                || !registered.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications))
                return Failed("APPLICATION_CANDIDATE_APPLICATION_STALE");
            var replay = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == operationId, cancellationToken);
            if (replay is not null)
            {
                if (replay.Tool != Tool || !replay.Success || replay.ProjectionJson != canonical) return Failed("APPLICATION_CANDIDATE_COMMAND_CONFLICT");
                var prior = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.SourceOperationId == operationId, cancellationToken);
                if (prior is null || prior.CanonicalCommandFingerprint != commandFingerprint) return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_RECEIPT_INCONSISTENT", "The stored candidate receipt cannot be reconciled.");
                var retained = await new ApplicationCandidateRetainedReader(db, applications).ReadMetadataAsync(app, prior.CandidateId, prior.Revision, cancellationToken);
                if (retained is null) return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_RECEIPT_INCONSISTENT", "The stored candidate cannot be rehydrated.");
                var replayTargets = new List<StandingGrantDefinitionTarget>();
                var replayBase = await BaseAsync(app, prior.ExpectedActiveFingerprint, cancellationToken);
                var replayChanged = ApplicationCandidateDocumentSelection.ChangedPaths(retained.Documents, replayBase);
                var replayDocuments = await ReadChangedAsync(retained, replayChanged, cancellationToken);
                var replayDefinitions = Definitions(app, replayDocuments, replayChanged);
                foreach (var definition in replayDefinitions)
                {
                    var replayResolved = await targets.ResolveCandidateReferenceAsync(host, new(app, prior.CandidateId, prior.Revision, prior.ContentFingerprint), definition, cancellationToken);
                    if (replayResolved.Status == StandingGrantTargetResolutionStatus.Unavailable) return InteractionInvocationResult.Unavailable(replayResolved.Code, "Candidate definition ownership is unavailable.");
                    if (replayResolved.Status != StandingGrantTargetResolutionStatus.Available || replayResolved.Target is null) return Failed(replayResolved.Code);
                    replayTargets.Add(replayResolved.Target);
                }
                var replayAuthority = await grants.EvaluateAsync(host, new(StandingGrantCapability.Author, StandingGrantScope.Application, replayTargets, []), cancellationToken);
                if (!replayAuthority.Allowed) return replayAuthority.Code.EndsWith("UNAVAILABLE", StringComparison.Ordinal) ? InteractionInvocationResult.Unavailable(replayAuthority.Code, "Author authority is unavailable.") : Failed(replayAuthority.Code);
                if (!ApplicationCandidateOperationProof.WriteMatches(replay, retained, replayDefinitions, out _))
                    return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_RECEIPT_INCONSISTENT", "The stored candidate receipt cannot be reconciled.");
                await transaction.CommitAsync(cancellationToken);
                return Receipt(operationId, commandFingerprint);
            }

            var active = activations.Current(app);
            if (!string.Equals(active?.ActivationFingerprint, request.ExpectedActiveFingerprint, StringComparison.Ordinal)) return Failed("APPLICATION_CANDIDATE_ACTIVE_STALE");
            var latest = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().Where(x => x.ApplicationId == app.Value && x.CandidateId == id).MaxAsync(x => (int?)x.Revision, cancellationToken) ?? 0;
            if (latest != request.ExpectedCandidateRevision) return Failed("APPLICATION_CANDIDATE_CAS_MISMATCH");
            var effective = Effective(app, active, request.Documents);
            var next = latest + 1;

            // The audit principal must exist before the candidate's FK can be inserted. Its final
            // guard decision is set after the staged retained candidate has been resolved.
            var op = await operations.RecordAsync(Tool, "Staged inert application candidate.", true,
                subject: app.Value, projectionJson: canonical, guardEvidenceJson: "{}", id: operationId,
                cancellationToken: cancellationToken);
            var row = new ApplicationCandidateRevisionRecord { ApplicationId = app.Value, CandidateId = id, Revision = next,
                ApplicationRevision = registered.Revision, ExpectedActiveFingerprint = request.ExpectedActiveFingerprint,
                Origin = request.Origin, SynchronizationEvidenceReference = null, NewImplementationReason = request.NewImplementationReason.Trim(),
                AuthorGrantReference = host.GrantReference, SourceOperationId = operationId, CanonicalCommandFingerprint = commandFingerprint,
                ContentFingerprint = new string('0', 64) };
            row.ContentFingerprint = ApplicationCandidateRetainedReader.MetadataFingerprint(row, registered,
                effective.Select(x => x.Document).ToArray());
            db.Add(row);
            var links = await RetainEffectiveAsync(app, active, effective, cancellationToken);
            foreach (var link in links) db.Add(new ApplicationCandidateDocumentRecord { ApplicationId = app.Value, CandidateId = id, Revision = next, Ordinal = link.Ordinal, IdentityId = link.IdentityId, EvidenceVersion = link.EvidenceVersion });
            await db.SaveChangesAsync(cancellationToken);
            var readback = await new ApplicationCandidateRetainedReader(db, applications).ReadMetadataAsync(app, id, next, cancellationToken)
                ?? throw new ApplicationActivationException("APPLICATION_CANDIDATE_WRITE_INCONSISTENT", "Candidate was not retained.");
            var changed = ApplicationCandidateDocumentSelection.ChangedPaths(readback.Documents, active);
            var definitions = Definitions(app, await ReadChangedAsync(readback, changed, cancellationToken), changed);
            var resolved = new List<StandingGrantDefinitionTarget>();
            foreach (var definition in definitions)
            {
                var target = await targets.ResolveCandidateReferenceAsync(host, new(app, id, next, row.ContentFingerprint), definition, cancellationToken);
                if (target.Status == StandingGrantTargetResolutionStatus.Unavailable) return InteractionInvocationResult.Unavailable(target.Code, "Candidate definition ownership is unavailable.");
                if (target.Status != StandingGrantTargetResolutionStatus.Available || target.Target is null) return Failed(target.Code);
                resolved.Add(target.Target);
            }
            var decision = await grants.EvaluateAsync(host, new(StandingGrantCapability.Author, StandingGrantScope.Application, resolved, []), cancellationToken);
            if (!decision.Allowed) return decision.Code.EndsWith("UNAVAILABLE", StringComparison.Ordinal) ? InteractionInvocationResult.Unavailable(decision.Code, "Author authority is unavailable.") : Failed(decision.Code);
            var candidateReference = new ApplicationCandidateReference(app, id, next, row.ContentFingerprint);
            op.GuardEvidenceJson = ApplicationCandidateOperationProof.WriteGuard(host, candidateReference,
                host.GrantReference, definitions, commandFingerprint);
            await db.SaveChangesAsync(cancellationToken);
            var saved = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == app.Value && x.CandidateId == id && x.Revision == next, cancellationToken);
            if (saved is null || saved.ContentFingerprint != row.ContentFingerprint || await operations.GetAsync(operationId, cancellationToken) is null)
                return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_WRITE_INCONSISTENT", "Candidate and audit receipt could not be retained together.");
            await transaction.CommitAsync(cancellationToken);
            return Receipt(operationId, commandFingerprint);
        }
        catch (ApplicationActivationException e) when (e.Code.EndsWith("UNAVAILABLE", StringComparison.Ordinal)) { return InteractionInvocationResult.Unavailable(e.Code, "Candidate authoring is unavailable."); }
        catch (ApplicationActivationException e) { return Failed(e.Code); }
        catch (OperationCanceledException) { return InteractionInvocationResult.Cancelled("APPLICATION_CANDIDATE_CANCELLED", "Candidate authoring was cancelled; reconcile the command before retrying."); }
        catch (Exception) { return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_UNAVAILABLE", "Candidate authoring is unavailable."); }
        finally
        {
            // This owner starts only from a clean context, so every tracked mutation is ours.
            foreach (var entry in db.ChangeTracker.Entries().ToArray()) entry.State = EntityState.Detached;
            if (opened) await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<InteractionInvocationResult> InspectAsync(InteractionInvocationHost host, ApplicationCandidateLookup candidate, CancellationToken cancellationToken = default)
    {
        if (!host.Budget.TryConsumeOperation()) return Failed("INVOCATION_BUDGET_EXHAUSTED");
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow) return Failed("INVOCATION_DEADLINE_EXCEEDED");
        if (candidate is null) return Failed("INVALID_PAYLOAD");
        var bySourceOperation = candidate.SourceOperationId is not null;
        // Operation receipts use the same exact 32 lowercase hexadecimal format as candidate IDs
        // (InteractionInvocationCommitReceipt.Validate); correlation never grants read authority.
        if (bySourceOperation
            ? candidate.CandidateId is not null || candidate.Revision != 0 || !CandidateId(candidate.SourceOperationId!)
            : candidate.CandidateId is null || !CandidateId(candidate.CandidateId) || candidate.Revision < 1)
            return Failed("INVALID_PAYLOAD");
        try
        {
            await using var readScope = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
            var app = host.ApplicationRevision.ApplicationId;
            if (bySourceOperation)
            {
                var matches = await (from row in db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking()
                    join operation in db.Operations.AsNoTracking() on row.SourceOperationId equals operation.Id
                    where row.ApplicationId == app.Value && row.SourceOperationId == candidate.SourceOperationId
                        && operation.Tool == Tool && operation.Success && operation.Subject == app.Value
                    select new { row.CandidateId, row.Revision }).Take(2).ToArrayAsync(cancellationToken);
                if (matches.Length != 1) return Failed("APPLICATION_CANDIDATE_NOT_FOUND");
                candidate = new(matches[0].CandidateId, matches[0].Revision);
            }
            var readback = await new ApplicationCandidateRetainedReader(db, applications).ReadMetadataAsync(app, candidate.CandidateId!, candidate.Revision, cancellationToken);
            if (readback is null) return Failed("APPLICATION_CANDIDATE_NOT_FOUND");
            var baseManifest = await BaseAsync(app, readback.RevisionRow.ExpectedActiveFingerprint, cancellationToken);
            var changed = ApplicationCandidateDocumentSelection.ChangedPaths(readback.Documents, baseManifest);
            var selectedDocuments = await ReadChangedAsync(readback, changed, cancellationToken);
            var candidateReference = new ApplicationCandidateReference(app, candidate.CandidateId!, candidate.Revision, readback.RevisionRow.ContentFingerprint);
            var resolved = new List<StandingGrantDefinitionTarget>();
            var definitions = Definitions(app, selectedDocuments, changed);
            foreach (var definition in definitions)
            {
                var result = await targets.ResolveCandidateReferenceAsync(host, candidateReference, definition, cancellationToken);
                if (result.Status == StandingGrantTargetResolutionStatus.Unavailable) return InteractionInvocationResult.Unavailable(result.Code, "Candidate definition ownership is unavailable.");
                if (result.Status != StandingGrantTargetResolutionStatus.Available || result.Target is null) return Failed(result.Code);
                resolved.Add(result.Target);
            }
            var decision = await grants.EvaluateAsync(host, new(StandingGrantCapability.Read, StandingGrantScope.Application, resolved, []), cancellationToken);
            if (!decision.Allowed) return decision.Code.EndsWith("UNAVAILABLE", StringComparison.Ordinal) ? InteractionInvocationResult.Unavailable(decision.Code, "Read authority is unavailable.") : Failed(decision.Code);
            var sourceOperation = await db.Operations.AsNoTracking().SingleOrDefaultAsync(value => value.Id == readback.RevisionRow.SourceOperationId, cancellationToken);
            if (sourceOperation is null || !ApplicationCandidateOperationProof.WriteMatches(sourceOperation, readback, definitions, out _))
                return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_RECEIPT_INCONSISTENT", "The stored candidate receipt cannot be reconciled.");
            var items = selectedDocuments.Where(x => changed.Contains(x.Document.RelativePath)).Select(x => new { x.Document.LogicalIdentity, x.Document.SourceId, x.Document.RelativePath, x.Document.MediaType, x.Document.ContentFingerprint, text = x.Document.IsText ? Encoding.UTF8.GetString(x.RetainedBytes) : null }).ToArray();
            var latestValidation = await (from value in db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
                                    join operation in db.Operations.AsNoTracking() on value.OperationId equals operation.Id
                                    where value.ApplicationId == app.Value && value.CandidateId == candidateReference.CandidateId
                                        && value.Revision == candidateReference.Revision && value.CandidateFingerprint == candidateReference.ContentFingerprint
                                        && operation.Tool == "application-candidate-validation" && operation.Success && operation.Subject == app.Value
                                    orderby operation.Timestamp descending, operation.Id descending
                                    select new { Record = value, Operation = operation }).FirstOrDefaultAsync(cancellationToken);
            object? validation = null;
            if (latestValidation is not null)
            {
                var matches = ApplicationCandidateOperationProof.ValidationMatches(latestValidation.Operation,
                    latestValidation.Record, candidateReference, definitions);
                ApplicationCandidateRuntimeReport? runtimeReport = null;
                var hasRuntime = matches && ApplicationCandidateOperationProof.TryReadRuntimeReport(
                    latestValidation.Operation, latestValidation.Record, candidateReference, definitions, out runtimeReport);
                validation = matches
                    ? new { latestValidation.Record.OperationId, latestValidation.Record.Outcome,
                        latestValidation.Record.DiagnosticsJson, RuntimeReport = hasRuntime ? runtimeReport : null }
                    : new { OperationId = latestValidation.Record.OperationId, Outcome = "unavailable",
                        DiagnosticsJson = "[{\"Code\":\"APPLICATION_CANDIDATE_VALIDATION_EVIDENCE_INCONSISTENT\",\"Message\":\"Stored validation evidence cannot be reconciled.\"}]",
                        RuntimeReport = (ApplicationCandidateRuntimeReport?)null };
            }
            var json = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new { candidate = candidateReference, sourceOperationId = readback.RevisionRow.SourceOperationId, origin = readback.RevisionRow.Origin, documents = items, validation }));
            if (Encoding.UTF8.GetByteCount(json) > 64 * 1024) return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_INSPECT_LIMIT", "Candidate inspection exceeds its bounded result limit.");
            return InteractionInvocationResult.CompletedComputation(json, readback.RevisionRow.SourceOperationId);
        }
        catch (ApplicationActivationException e) { return Failed(e.Code); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_INSPECT_UNAVAILABLE", "Candidate inspection is unavailable."); }
    }
    public Task<InteractionInvocationResult> ActivateAsync(InteractionInvocationHost host, ApplicationCandidateActivationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_ACTIVATE_UNAVAILABLE", "Candidate activation is not available."));
    private List<(ActivatedApplicationDocument Document, byte[]? Bytes)> Effective(ApplicationIdentifier app, ActiveApplicationManifest? active, IReadOnlyList<ApplicationCandidateDocumentInput> replacements)
    {
        var values = new Dictionary<string, (ActivatedApplicationDocument Document, byte[]? Bytes)>(StringComparer.Ordinal);
        if (active is not null && (active.Winners.Count > CatalogNavigation.CatalogNavigationLimits.MaximumRecords * 4
                || active.Winners.Any(x => x.Length < 0 || x.Length > 10L * 1024 * 1024)
                || active.Winners.Sum(x => x.Length) > 256L * 1024 * 1024))
            throw new ApplicationActivationException("APPLICATION_CANDIDATE_BASE_LIMIT", "The retained base generation exceeds candidate bounds.");
        if (active is not null && active.PreparationVersion is null)
            throw new ApplicationActivationException("APPLICATION_CANDIDATE_LEGACY_BASE_UNAVAILABLE", "Legacy base evidence cannot be repaired by candidate authoring.");
        if (active is not null) foreach (var winner in active.Winners)
        {
            values.Add(winner.RelativePath, (winner, null));
        }
        foreach (var input in replacements)
        {
            var source = sources.Get(app, input.SourceId) ?? throw new ApplicationActivationException("APPLICATION_CANDIDATE_SOURCE_UNAVAILABLE", "The candidate source is not registered.");
            if (!values.TryGetValue(input.RelativePath, out var prior))
            {
                if (source.ApplicationId != app || !LocalDocumentScanner.MatchesNormalizedRelativeGlob(source.RelativePathOrGlob, input.RelativePath))
                    throw new ApplicationActivationException("APPLICATION_CANDIDATE_NEW_PATH_UNAVAILABLE", "The registered source does not own the candidate path.");
            }
            else if (prior.Document.SourceId != source.SourceId || source.Trust != prior.Document.Trust || source.Precedence != prior.Document.Precedence)
                throw new ApplicationActivationException("APPLICATION_CANDIDATE_SOURCE_DRIFT", "Replacement source does not own its retained path.");
            var bytes = Encoding.UTF8.GetBytes(input.Text);
            values[input.RelativePath] = (new(input.LogicalIdentity, source.SourceId, source.Trust, source.Precedence, input.RelativePath, input.MediaType, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, true), bytes);
        }
        if (values.Count > CatalogNavigation.CatalogNavigationLimits.MaximumRecords * 4
            || values.Values.Sum(x => x.Document.Length) > 256L * 1024 * 1024)
            throw new ApplicationActivationException("APPLICATION_CANDIDATE_LIMIT", "Effective candidate content exceeds its retained generation bound.");
        return values.Values.OrderBy(x => x.Document.RelativePath, StringComparer.Ordinal).ToList();
    }

    private async Task<ActiveApplicationManifest?> BaseAsync(ApplicationIdentifier app, string? fingerprint, CancellationToken ct)
    {
        return await ApplicationCandidateDocumentSelection.ReadBaseAsync(db, activations, app, fingerprint, ct);
    }

    private async Task<IReadOnlyList<ApplicationRetainedDocumentLink>> RetainEffectiveAsync(ApplicationIdentifier app,
        ActiveApplicationManifest? basis, IReadOnlyList<(ActivatedApplicationDocument Document, byte[]? Bytes)> effective, CancellationToken ct)
    {
        var replacements = effective.Where(value => value.Bytes is not null).ToArray();
        var retained = await new ApplicationRetainedDocumentStore(db).RetainAsync(app,
            replacements.Select(value => value.Document).ToArray(), replacements.ToDictionary(
                value => value.Document.LogicalIdentity, value => value.Bytes!, StringComparer.Ordinal), ct);
        var byIdentity = retained.ToDictionary(value => replacements[value.Ordinal].Document.LogicalIdentity, StringComparer.Ordinal);
        if (basis is not null)
        {
            var baselineLinks = await (from link in db.Set<ApplicationActivationDocumentRecord>().AsNoTracking()
                join identity in db.Set<ApplicationActivationDocumentIdentityRecord>().AsNoTracking()
                    on new { link.ApplicationId, link.IdentityId } equals new { identity.ApplicationId, IdentityId = identity.Id }
                where link.ApplicationId == app.Value && link.ActivationRevision == basis.ActivationRevision
                select new { identity.LogicalIdentity, link.IdentityId, link.EvidenceVersion }).ToArrayAsync(ct);
            foreach (var link in baselineLinks) byIdentity.TryAdd(link.LogicalIdentity, new(0, link.IdentityId, link.EvidenceVersion));
        }
        return effective.Select((value, ordinal) => byIdentity.TryGetValue(value.Document.LogicalIdentity, out var link)
            ? link with { Ordinal = ordinal }
            : throw new ApplicationActivationException("APPLICATION_CANDIDATE_BASE_UNAVAILABLE", "The unchanged base document link is missing.")).ToArray();
    }

    private Task<IReadOnlyList<ApplicationCandidateDocument>> ReadChangedAsync(ApplicationCandidateRetainedMetadata metadata,
        IReadOnlyList<string> changed, CancellationToken ct) => new ApplicationCandidateRetainedReader(db, applications)
        .ReadSelectedAsync(metadata, ApplicationCandidateDocumentSelection.WithKnownSidecars(metadata.Documents, changed), ct);

    internal static IReadOnlyList<StandingGrantDefinitionReference> Definitions(ApplicationIdentifier app, IReadOnlyList<ApplicationCandidateDocument> value, IReadOnlyList<string> changed)
        => DefinitionRecords(app, value, changed).Select(x => new StandingGrantDefinitionReference(x.QualifiedId, x.Kind, x.Version, x.ContentFingerprint)).ToArray();

    internal static IReadOnlyList<CatalogNavigation.CatalogRecordDefinition> DefinitionRecords(ApplicationIdentifier app,
        IReadOnlyList<ApplicationCandidateDocument> value, IReadOnlyList<string> changed)
    {
        var winners = value.Where(x => x.Document.IsText).ToDictionary(x => x.Document.RelativePath, x => x.Document, StringComparer.Ordinal);
        var bytes = value.Where(x => x.Document.IsText).ToDictionary(x => x.Document.RelativePath, x => x.RetainedBytes, StringComparer.Ordinal);
        var changedPaths = changed.ToHashSet(StringComparer.Ordinal);
        var records = winners.Values.Select(x => CatalogNavigation.ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(app, x, winners, bytes)).Where(x => x is not null).ToArray();
        var selected = records.Where(x => changedPaths.Contains(x!.SourceLogicalPath) || (x!.Kind == "mechanic" && changedPaths.Contains(Path.ChangeExtension(x.SourceLogicalPath, ".js").Replace('\\', '/')))).ToArray();
        var covered = selected.SelectMany(x => x!.Kind == "mechanic"
                ? new[] { x.SourceLogicalPath, Path.ChangeExtension(x.SourceLogicalPath, ".js").Replace('\\', '/') }
                : new[] { x.SourceLogicalPath })
            .ToHashSet(StringComparer.Ordinal);
        if (selected.Length == 0 || selected.Any(x => x is null) || !changedPaths.IsSubsetOf(covered))
            throw new ApplicationActivationException("APPLICATION_CANDIDATE_COVERAGE_UNAVAILABLE", "Every changed document needs one supported definition.");
        return selected.Select(x => x!).ToArray();
    }

    internal static void Validate(ApplicationCandidateWriteRequest request)
    {
        if (request is null || request.ExpectedCandidateRevision is < 0 or int.MaxValue || request.Documents is null
            || request.Documents.Count is < 1 or > ApplicationAuthoringLimits.DocumentsPerWrite
            || request.Documents.Any(x => x is null) || string.IsNullOrWhiteSpace(request.NewImplementationReason)
            || request.NewImplementationReason.Length > ApplicationAuthoringLimits.ReasonCharacters
            || request.Origin is not ("runtime" or "catalog-sync")
            || request.Origin == "runtime" && request.SynchronizationEvidenceReference is not null
            || request.ExpectedActiveFingerprint is not null && !Hash(request.ExpectedActiveFingerprint)
            || request.CandidateId is { } supplied && !CandidateId(supplied))
            throw new ApplicationActivationException("INVALID_PAYLOAD", "Candidate request is invalid.");
        if (request.Documents.Select(x => x.RelativePath).Distinct(StringComparer.Ordinal).Count() != request.Documents.Count || request.Documents.Select(x => x.LogicalIdentity).Distinct(StringComparer.Ordinal).Count() != request.Documents.Count) throw new ApplicationActivationException("INVALID_PAYLOAD", "Candidate documents must have unique paths and identities.");
        foreach (var x in request.Documents) if (x.LogicalIdentity != "file:" + x.RelativePath || !GenericSourceDocument.IsNormalizedRelativePath(x.RelativePath) || string.IsNullOrWhiteSpace(x.SourceId) || string.IsNullOrWhiteSpace(x.MediaType) || x.Text is null || Encoding.UTF8.GetByteCount(x.Text) > 64 * 1024) throw new ApplicationActivationException("INVALID_PAYLOAD", "Candidate document metadata is invalid.");
        if (request.ExpectedCandidateRevision > 0 && request.CandidateId is null) throw new ApplicationActivationException("INVALID_PAYLOAD", "A revision needs its candidate identity.");
    }
    private static string Id(InteractionInvocationHost host, ApplicationIdentifier app, string suffix = "candidate") =>
        ApplicationCandidateOperationProof.OperationId(host.Principal.PrincipalId, app.Value, host.CommandId, suffix);
    private static InteractionInvocationResult Receipt(string operationId, string fingerprint) => InteractionInvocationResult.Committed(new(operationId, fingerprint, []));
    private static InteractionInvocationResult Failed(string code) => InteractionInvocationResult.Failed(code, "The candidate request was rejected.");
    private static bool CandidateId(string value) => value.Length == 32 && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
    private static bool Hash(string value) => value.Length == 64 && value.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');
}
