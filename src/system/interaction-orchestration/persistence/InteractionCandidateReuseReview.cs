using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Interactions;

/// <summary>
/// Bounded discovery and deterministic copy rejection. A semantic review attestation is a separate,
/// required host dependency: discovery or a supplied reason alone never produces Valid.
/// </summary>
public sealed class InteractionCandidateReuseReview(
    IInteractionManualContextService manuals,
    InteractionFeatureRetriever features,
    IStandingGrantPolicy grants,
    IStandingGrantTargetResolver targets,
    IApplicationDefinitionChangeReader changes) : IApplicationCandidateReuseReview
{
    private const int MaximumRetainedBytes = 32_000;

    public async Task<ApplicationCandidateReuseResult> ReviewAsync(InteractionInvocationHost host,
        ApplicationCandidateSnapshot candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(candidate);
        string? manualHash = null;
        var alternatives = new List<ApplicationDefinitionAlternative>();
        try
        {
            if (candidate.Candidate.ApplicationId != host.ApplicationRevision.ApplicationId
                || candidate.ApplicationRevision != host.ApplicationRevision.Revision
                || candidate.ApplicationFingerprint != host.ApplicationRevision.Fingerprint
                || !UpperHash(candidate.Candidate.ContentFingerprint))
                return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_CANDIDATE_SCOPE", "The candidate does not match the current invocation.");
            if (string.IsNullOrWhiteSpace(candidate.NewImplementationReason)
                || candidate.NewImplementationReason.Length > ApplicationAuthoringLimits.ReasonCharacters)
                return Result(ApplicationCandidateCheckStatus.Invalid, "REUSE_REASON_REQUIRED", "A bounded candidate-specific reason is required.");
            if (candidate.EffectiveDocuments.Count is 0 or > ApplicationAuthoringLimits.DocumentsPerWrite
                || candidate.EffectiveDocuments.Sum(value => (long)value.RetainedBytes.Length) > MaximumRetainedBytes)
                return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_CONTEXT_LIMIT", "Exact retained candidate context exceeds the review bound.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var remaining = host.Budget.DeadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) deadline.Cancel();
            else if (remaining.TotalMilliseconds <= int.MaxValue) deadline.CancelAfter(remaining);
            var token = deadline.Token;
            token.ThrowIfCancellationRequested();
            // A read-only child preserves identity, grant and the same shared operation ledger.
            InteractionInvocationHost readHost;
            if (host.StateSpaceId is { } stateSpaceId && host.StateRevision is { } stateRevision)
                readHost = new(host.Principal, host.ApplicationRevision, stateSpaceId,
                    host.GrantReference, host.CommandId, stateRevision, InteractionExecutionProfile.ReadOnly,
                    host.Budget, host.ParentCommandId);
            else if (host.StateSpaceId is null && host.StateRevision is null)
                readHost = InteractionInvocationHost.ForApplication(host.Principal, host.ApplicationRevision,
                    host.GrantReference, host.CommandId, InteractionExecutionProfile.ReadOnly,
                    host.Budget, host.ParentCommandId);
            else
                return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_CANDIDATE_SCOPE",
                    "Review requires an application invocation or a complete state scope.");
            var generation = changes.CurrentChange(host.ApplicationRevision.ApplicationId);
            if (generation?.Fingerprint != candidate.ExpectedActiveFingerprint)
                return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_ACTIVE_CHANGED", "The expected active generation changed.");
            var retained = candidate.EffectiveDocuments.Select(value =>
                new ApplicationCandidateDocument(value.Document, value.RetainedBytes.ToArray())).ToArray();
            foreach (var document in retained)
            {
                token.ThrowIfCancellationRequested();
                if (!document.Document.IsText || document.Document.Trust != SourceTrust.Trusted
                    || document.RetainedBytes.LongLength != document.Document.Length
                    || Hash(document.RetainedBytes) != document.Document.ContentFingerprint)
                    return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_RETAINED_EVIDENCE", "Exact trusted retained document evidence is required.");
            }
            var winners = retained.ToDictionary(value => value.Document.RelativePath, value => value.Document, StringComparer.Ordinal);
            var bytes = retained.ToDictionary(value => value.Document.RelativePath, value => value.RetainedBytes, StringComparer.Ordinal);
            var records = new List<CatalogRecordDefinition>();
            foreach (var document in retained.OrderBy(value => value.Document.RelativePath, StringComparer.Ordinal))
            {
                var record = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(
                    host.ApplicationRevision.ApplicationId, document.Document, winners, bytes);
                if (record is null)
                {
                    // Only mechanic source sidecars are meaningful without an independent record.
                    var path = document.Document.RelativePath;
                    if (!path.EndsWith(".js", StringComparison.Ordinal)
                        || !winners.ContainsKey(Path.ChangeExtension(path, ".md").Replace('\\', '/')))
                        return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_KIND_UNSUPPORTED", "A candidate document has no supported review meaning.");
                    continue;
                }
                if (record.Kind is not ("procedure" or "mechanic" or "query")
                    || !await CanReadAsync(readHost, Reference(record), candidate, token))
                    return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_CANDIDATE_AUTHORITY", "Candidate review authority or source meaning is unavailable.");
                records.Add(record);
            }
            if (records.Count == 0)
                return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_KIND_UNSUPPORTED", "The candidate contains no supported review target.");
            // The bounded query only selects discovery candidates; the full reason remains retained.
            var query = string.Join(' ', candidate.NewImplementationReason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (query.Length > InteractionRetrievalLimits.MaximumQueryLength)
                query = query[..InteractionRetrievalLimits.MaximumQueryLength];
            var discovery = await manuals.DiscoverAsync(new(readHost, query), token);
            if (discovery.Tag != InteractionInvocationResultTag.Completed || discovery.DataJson is null)
                return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_MANUAL_UNAVAILABLE", "A fresh authorized manual packet is required.");
            var packet = JsonNode.Parse(discovery.DataJson)!.AsObject();
            manualHash = packet["resultFingerprint"]!.GetValue<string>();
            packet["resultFingerprint"] = new string('0', 64);
            if (!UpperHash(manualHash) || Hash(Encoding.UTF8.GetBytes(InteractionCanonicalJson.CanonicalizeObject(packet.ToJsonString()))) != manualHash
                || discovery.CompletionEvidenceReference != "manual.context." + manualHash.ToLowerInvariant())
                return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_MANUAL_EVIDENCE", "The manual packet lacks matching completion evidence.");
            if (packet["bounded"]!.GetValue<bool>())
                return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_CONTEXT_LIMIT", "Complete bounded manual context is required for review.");
            if (packet["reusableTasks"]!.AsArray().Count != 0)
                return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_RECIPE_CONTEXT_UNAVAILABLE", "Exact reusable-task review context is not yet supported.");
            var selected = new List<InteractionFeatureHit>();
            foreach (var item in packet["candidates"]!.AsArray())
            {
                if (selected.Count >= ApplicationAuthoringLimits.Alternatives)
                    return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_CONTEXT_LIMIT", "The authorized alternative set exceeds the review bound.");
                var reference = item!["reference"]!;
                if (reference["applicationId"]!.GetValue<string>() != host.ApplicationRevision.ApplicationId.Value
                    || reference["lane"]!.GetValue<string>() != "trustedFeature")
                    return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_MANUAL_EVIDENCE", "The manual alternative has an unsupported scope.");
                var selection = new StandingGrantDefinitionReference(reference["qualifiedId"]!.GetValue<string>(),
                    reference["kind"]!.GetValue<string>(), reference["version"]!.GetValue<int>(),
                    reference["contentFingerprint"]!.GetValue<string>());
                var found = await features.SearchAuthorizedAsync(new(host.ApplicationRevision.ApplicationId,
                    InteractionRetrievalLane.TrustedFeature), new(selection.DefinitionId, 1),
                    (value, cancellation) => CanReadAsync(readHost,
                        new(value.QualifiedId, value.Kind, value.Version, value.ContentFingerprint), null, cancellation), token);
                var hit = found.Hits.SingleOrDefault(value => value.Reference.QualifiedId == selection.DefinitionId
                    && value.Reference.Kind == selection.Kind && value.Reference.Version == selection.Revision
                    && value.Reference.ContentFingerprint == selection.ContentFingerprint);
                if (hit is null)
                    return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_ALTERNATIVE_CHANGED", "An authorized alternative changed during review preparation.");
                selected.Add(hit);
                alternatives.Add(new(hit.Reference.QualifiedId, hit.Reference.Version, hit.Reference.ContentFingerprint,
                    "Authorized discovery candidate; semantic reuse judgment is still required."));
            }
            foreach (var record in records)
                if (!await CanReadAsync(readHost, Reference(record), candidate, token))
                    return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_AUTHORITY_CHANGED", "Candidate authority changed during review preparation.");
            foreach (var hit in selected)
                if (!await CanReadAsync(readHost, new(hit.Reference.QualifiedId, hit.Reference.Kind,
                    hit.Reference.Version, hit.Reference.ContentFingerprint), null, token))
                    return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_AUTHORITY_CHANGED", "Alternative authority changed during review preparation.");
            var current = changes.CurrentChange(host.ApplicationRevision.ApplicationId);
            if (generation?.Fingerprint != current?.Fingerprint || generation?.Revision != current?.Revision)
                return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_ACTIVE_CHANGED", "The active generation changed during review preparation.");
            _ = ApplicationCandidateReuseInput.Create(candidate, discovery.DataJson, selected.Select(hit =>
                new ApplicationCandidateReuseAlternative(new(hit.Reference.QualifiedId, hit.Reference.Kind,
                    hit.Reference.Version, hit.Reference.ContentFingerprint), hit.ContractJson)).ToArray());
            foreach (var record in records)
            foreach (var hit in selected)
            {
                var alternative = new CatalogRecordDefinition(host.ApplicationRevision.ApplicationId.Value,
                    hit.Reference.Kind, hit.Reference.QualifiedId, hit.Name, hit.Description, [], [], "", "active",
                    hit.Reference.Version, hit.ContractJson, hit.Reference.ContentFingerprint, "", "");
                if (CandidateReuseContentComparison.TryFindExactDuplicate(host.ApplicationRevision.ApplicationId, record, alternative))
                    return Result(ApplicationCandidateCheckStatus.Invalid, "REUSE_EXACT_CONTENT_DUPLICATE",
                        "The candidate copies an authorized active definition's exact normalized content under a different ID.");
            }
            // No positive attestation is fabricated here. The required 05 worker and 04 evidence
            // verifier will consume these exact pins after coordinator contract integration.
            return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_REVIEW_WORKER_UNAVAILABLE",
                "An accounted candidate-specific semantic review is required before publication.");
        }
        catch (OperationCanceledException)
        { return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_CANCELLED", "Candidate reuse review was cancelled."); }
        catch
        { return Result(ApplicationCandidateCheckStatus.Unavailable, "REUSE_PREPARATION_UNAVAILABLE", "Exact authorized review context is unavailable."); }

        ApplicationCandidateReuseResult Result(ApplicationCandidateCheckStatus status, string code, string message) =>
            new(status, candidate.Candidate.ContentFingerprint,
                status == ApplicationCandidateCheckStatus.Unavailable && code != "REUSE_REVIEW_WORKER_UNAVAILABLE" ? null : manualHash, null,
                status == ApplicationCandidateCheckStatus.Unavailable && code != "REUSE_REVIEW_WORKER_UNAVAILABLE" ? [] : alternatives.ToArray(),
                [new(code, candidate.Candidate.CandidateId, message)]);
    }

    private async Task<bool> CanReadAsync(InteractionInvocationHost host, StandingGrantDefinitionReference reference,
        ApplicationCandidateSnapshot? candidate, CancellationToken cancellationToken)
    {
        var resolved = candidate is null
            ? await targets.ResolveAsync(host, reference, cancellationToken)
            : await targets.ResolveCandidateAsync(host, candidate, reference, cancellationToken);
        var target = resolved.Target;
        if (resolved.Status != StandingGrantTargetResolutionStatus.Available || target is null
            || target.DefinitionId != reference.DefinitionId || target.Kind != reference.Kind
            || target.Revision != reference.Revision || target.ContentFingerprint != reference.ContentFingerprint
            || target.OwnerApplicationId != host.ApplicationRevision.ApplicationId) return false;
        var decision = await grants.EvaluateAsync(host, new(StandingGrantCapability.Read, StandingGrantScope.Application, [target], []), cancellationToken);
        var grant = decision.Grant;
        return decision.Allowed && grant is not null && grant.GrantReference == host.GrantReference
            && grant.PrincipalReference == host.Principal.PrincipalId && grant.ApplicationId == host.ApplicationRevision.ApplicationId
            && grant.Scope == StandingGrantScope.Application && grant.StateSpaceId is null
            && grant.Capabilities.Contains(StandingGrantCapability.Read) && !grant.Revoked && grant.ExpiresAtUtc > DateTime.UtcNow;
    }

    private static StandingGrantDefinitionReference Reference(CatalogRecordDefinition record) =>
        new(record.QualifiedId, record.Kind, record.Version, record.ContentFingerprint);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool UpperHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitUpper);
}
