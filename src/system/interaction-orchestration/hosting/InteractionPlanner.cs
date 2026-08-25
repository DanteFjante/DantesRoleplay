using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;

namespace DantesRoleplay.DataAccess.Composition;

internal sealed class InteractionPlanner(
    IInteractionAuthorizationPolicy authorization,
    IInteractionFeatureRetriever retrieval,
    IActiveCatalogFeatureSnapshotProvider snapshots,
    IInteractionProposalVerifier verifier,
    IVerifiedInteractionRecipeResolver recipes,
    IInteractionReceiptStore receipts,
    IEnumerable<IInteractionPlanningCompletionProvider> providers) : IInteractionPlanner
{
    public async Task<InteractionPlanningOutcome> PlanAsync(
        AuthorizedInteractionEnvelope envelope,
        InteractionAuthorizationRequest authorizationRequest,
        InteractionPlannerKind plannerKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(authorizationRequest);
        if (!Enum.IsDefined(plannerKind)) throw new InteractionContractException("INVALID_PLANNER_KIND", "The planner kind is not supported.");
        var watch = Stopwatch.StartNew();
        InteractionPlannerIdentity? identity = null;
        var searches = new List<SearchTrace>();
        var candidateById = new Dictionary<string, InteractionFeatureHit>(StringComparer.Ordinal);
        var inspectedById = new Dictionary<string, InteractionInspectedFeature>(StringComparer.Ordinal);
        var rounds = 0;
        var searchCount = 0;
        var inspectionCount = 0;

        InteractionResolutionResult? terminal = FreshAuthorization(envelope, authorizationRequest);
        if (terminal is null)
        {
            try
            {
                var recipe = await recipes.ResolveAsync(envelope, cancellationToken);
                if (recipe is not null)
                    terminal = InteractionResolutionResult.Resolved(recipe.Proposal,
                        "Resolved from a current verified route.", ["verified-recipe"], recipe.Reference);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                terminal = NonResolution(InteractionResolutionStatus.Unavailable, "PLANNER_CANCELLED",
                    "Interaction planning was cancelled.");
            }
            catch
            {
                terminal = NonResolution(InteractionResolutionStatus.Unavailable, "RECIPE_RESOLVER_UNAVAILABLE",
                    "The verified recipe resolver is unavailable.");
            }
        }

        var eligibleProviders = providers.Where(value => value.Kind == plannerKind).Take(2).ToArray();
        var provider = eligibleProviders.Length == 1 ? eligibleProviders[0] : null;
        if (terminal is null && provider is null)
            terminal = NonResolution(InteractionResolutionStatus.Unavailable, "PLANNER_PROVIDER_DISABLED",
                eligibleProviders.Length == 0
                    ? "The requested planner provider is disabled."
                    : "The requested planner provider configuration is ambiguous.");
        if (terminal is null && !provider!.Isolation.IsEligible)
            terminal = NonResolution(InteractionResolutionStatus.Unavailable, "PROVIDER_ISOLATION_INSUFFICIENT",
                "No eligible isolated planner provider is available.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(InteractionPlannerLimits.MaximumElapsedMilliseconds);
        try
        {
            while (terminal is null && rounds < InteractionPlannerLimits.MaximumRounds)
            {
                deadline.Token.ThrowIfCancellationRequested();
                rounds++;
                var observation = Observation(envelope, searches, inspectedById.Values,
                    rounds, searchCount, inspectionCount, candidateById.Count, watch.ElapsedMilliseconds);
                if (Encoding.UTF8.GetByteCount(observation) > envelope.Host.Budgets.MaximumObservationBytes)
                {
                    terminal = NonResolution(InteractionResolutionStatus.Unavailable, "PLANNER_OBSERVATION_BUDGET_EXCEEDED",
                        "The bounded planner observation limit was exhausted.");
                    break;
                }

                var completion = await provider!.CompleteAsync(new(
                    envelope.Host.RoleProfile,
                    observation,
                    envelope.Host.Budgets.MaximumModelOutputBytes), deadline.Token);
                if (!completion.Ok || completion.Identity is null)
                {
                    terminal = ProviderFailure(completion.ErrorCode, completion.ErrorMessage);
                    break;
                }
                if (completion.Identity.Kind != plannerKind)
                {
                    terminal = NonResolution(InteractionResolutionStatus.Unavailable, "PLANNER_IDENTITY_MISMATCH",
                        "The planner returned a mismatched provider identity.");
                    break;
                }
                if (identity is not null && identity.StableKey != completion.Identity.StableKey)
                {
                    terminal = NonResolution(InteractionResolutionStatus.Unavailable, "PLANNER_IDENTITY_CHANGED",
                        "The planner identity changed during the bounded interaction.");
                    break;
                }
                identity ??= completion.Identity;

                InteractionPlannerCommand command;
                try { command = InteractionPlannerCommand.Parse(completion.Json); }
                catch (InteractionContractException exception)
                {
                    terminal = NonResolution(InteractionResolutionStatus.Unsafe, exception.Code,
                        "The planner returned a command outside the closed response contract.");
                    break;
                }

                switch (command)
                {
                    case InteractionPlannerSearchCommand search:
                        searchCount++;
                        if (searchCount > InteractionPlannerLimits.MaximumSearches)
                        {
                            terminal = NonResolution(InteractionResolutionStatus.Unavailable, "PLANNER_SEARCH_BUDGET_EXCEEDED",
                                "The bounded planner search limit was exhausted.");
                            break;
                        }
                        var found = await retrieval.SearchAsync(new(
                            envelope.Host.ApplicationRevision.ApplicationId,
                            InteractionRetrievalLane.TrustedFeature), search.Input, deadline.Token);
                        if (found.Mode == InteractionRetrievalMode.Unavailable)
                        {
                            terminal = NonResolution(InteractionResolutionStatus.Unavailable, "TRUSTED_RETRIEVAL_UNAVAILABLE",
                                "Trusted feature retrieval is unavailable.");
                            break;
                        }
                        var hits = found.Hits.Take(InteractionPlannerLimits.MaximumSearchHits).ToArray();
                        foreach (var hit in hits) candidateById.TryAdd(hit.Reference.QualifiedId, hit);
                        if (candidateById.Count > InteractionPlannerLimits.MaximumCandidates)
                        {
                            terminal = NonResolution(InteractionResolutionStatus.Unavailable, "PLANNER_CANDIDATE_BUDGET_EXCEEDED",
                                "The bounded planner candidate limit was exhausted.");
                            break;
                        }
                        searches.Add(new(search.Input.Query, found.Mode,
                            hits.Select(value => new SearchHitTrace(
                                value.Reference, value.Name, value.Description)).ToArray()));
                        if (hits.Length == 0)
                            terminal = NonResolution(InteractionResolutionStatus.Unsupported, "TRUSTED_FEATURE_NOT_FOUND",
                                "No current trusted feature matched the requested interaction.");
                        break;

                    case InteractionPlannerInspectCommand inspect:
                        inspectionCount++;
                        if (inspectionCount > InteractionPlannerLimits.MaximumInspections)
                        {
                            terminal = NonResolution(InteractionResolutionStatus.Unavailable, "PLANNER_INSPECTION_BUDGET_EXCEEDED",
                                "The bounded planner inspection limit was exhausted.");
                            break;
                        }
                        if (inspectedById.ContainsKey(inspect.QualifiedId))
                        {
                            terminal = NonResolution(InteractionResolutionStatus.Unsafe, "DUPLICATE_CONTRACT_INSPECTION",
                                "The planner repeated an exact contract inspection.");
                            break;
                        }
                        if (!candidateById.TryGetValue(inspect.QualifiedId, out var candidate))
                        {
                            terminal = NonResolution(InteractionResolutionStatus.Unsafe, "INSPECTION_CANDIDATE_FORGED",
                                "The planner attempted to inspect a contract that was not returned by trusted search.");
                            break;
                        }
                        if (candidate.Reference.Version != inspect.Version || candidate.Reference.ContentFingerprint != inspect.Fingerprint)
                        {
                            terminal = NonResolution(InteractionResolutionStatus.Stale, "INSPECTION_REFERENCE_STALE",
                                "The requested inspection does not match the current search reference.");
                            break;
                        }
                        if (!snapshots.TryGetSnapshot(envelope.Host.ApplicationRevision.ApplicationId, out var snapshot)
                            || snapshot.Manifest.Fingerprint != candidate.Reference.CatalogFingerprint)
                        {
                            terminal = NonResolution(InteractionResolutionStatus.Stale, "CATALOG_SNAPSHOT_STALE",
                                "The active catalog changed during planning.");
                            break;
                        }
                        var document = snapshot.Documents.SingleOrDefault(value =>
                            value.Trust == SourceTrust.Trusted
                            && value.Record.QualifiedId == inspect.QualifiedId
                            && value.Record.Version == inspect.Version
                            && value.Record.ContentFingerprint == inspect.Fingerprint);
                        if (document is null || document.Record.ContentJson != candidate.ContractJson)
                        {
                            terminal = NonResolution(InteractionResolutionStatus.Stale, "INSPECTED_CONTRACT_STALE",
                                "The exact trusted contract changed during planning.");
                            break;
                        }
                        inspectedById.Add(inspect.QualifiedId, new(candidate, document.Record.ContentJson));
                        break;

                    case InteractionPlannerProposalCommand proposal:
                        terminal = verifier.Verify(new(envelope, inspectedById.Values.ToArray(), proposal));
                        break;

                    case InteractionPlannerNonResolutionCommand nonResolution:
                        terminal = InteractionResolutionResult.NonResolution(
                            nonResolution.Status,
                            ModelStatusCode(nonResolution.Status),
                            nonResolution.SafeSummary,
                            nonResolution.Evidence);
                        break;
                }
            }
            terminal ??= NonResolution(InteractionResolutionStatus.Unavailable, "PLANNER_ROUND_BUDGET_EXCEEDED",
                "The bounded planner round limit was exhausted.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            terminal = NonResolution(InteractionResolutionStatus.Unavailable, "PLANNER_CANCELLED",
                "Interaction planning was cancelled.");
        }
        catch (OperationCanceledException)
        {
            terminal = NonResolution(InteractionResolutionStatus.Unavailable, "PLANNER_TIMED_OUT",
                "Interaction planning exceeded its elapsed-time limit.");
        }
        catch
        {
            terminal = NonResolution(InteractionResolutionStatus.Unavailable, "PLANNER_INTERNAL_FAILURE",
                "Interaction planning failed closed.");
        }

        var usage = new InteractionPlannerUsage(rounds, searchCount, inspectionCount,
            candidateById.Count, Math.Min(watch.ElapsedMilliseconds, InteractionPlannerLimits.MaximumElapsedMilliseconds));
        var trace = TraceFingerprint(envelope, searches, inspectedById.Values, usage);
        terminal = WithPlannerEvidence(terminal!, identity, usage);
        var receipt = await receipts.AppendResolutionAsync(new(envelope, terminal, trace), CancellationToken.None);
        if (receipt.Disposition == InteractionReceiptWriteDisposition.Replay
            && !Equivalent(receipt.Receipt, terminal))
            receipt = InteractionReceiptWriteResult.Conflict();
        if (receipt.Disposition == InteractionReceiptWriteDisposition.Conflict)
            terminal = NonResolution(InteractionResolutionStatus.Unsafe, "INTERACTION_RECEIPT_IDEMPOTENCY_CONFLICT",
                "The interaction idempotency identity was reused with different planning evidence.");
        return new(terminal, identity, usage, trace, receipt);
    }

    private InteractionResolutionResult? FreshAuthorization(
        AuthorizedInteractionEnvelope envelope,
        InteractionAuthorizationRequest request)
    {
        if (request.Capability != InteractionCapability.Plan
            || request.Principal.PrincipalId != envelope.Host.Principal.PrincipalId
            || request.ApplicationId != envelope.Host.ApplicationRevision.ApplicationId
            || request.StateSpaceId != envelope.Host.StateSpaceId)
            return NonResolution(InteractionResolutionStatus.Unsafe, "PLAN_AUTHORIZATION_SCOPE_MISMATCH",
                "The fresh planning authorization request does not match the authorized envelope.");
        try
        {
            var decision = authorization.Evaluate(request);
            if (!decision.Allowed || decision.Capability != InteractionCapability.Plan
                || decision.PrincipalReference != request.Principal.PrincipalId
                || decision.ApplicationId != request.ApplicationId
                || decision.StateSpaceId != request.StateSpaceId)
                return NonResolution(InteractionResolutionStatus.Unsafe, "PLAN_NOT_AUTHORIZED",
                    "Interaction planning is not authorized for this scope.");
        }
        catch
        {
            return NonResolution(InteractionResolutionStatus.Unsafe, "PLAN_AUTHORIZATION_FAILED",
                "Interaction planning authorization failed closed.");
        }
        return null;
    }

    private static string Observation(
        AuthorizedInteractionEnvelope envelope,
        IReadOnlyList<SearchTrace> searches,
        IEnumerable<InteractionInspectedFeature> inspected,
        int rounds,
        int searchCount,
        int inspectionCount,
        int candidateCount,
        long elapsed)
    {
        var value = JsonSerializer.Serialize(new
        {
            envelope = envelope.Fingerprint,
            intent = new
            {
                text = envelope.Intent.IntentText,
                roleHints = envelope.Intent.RoleHints,
                conversationFactReferences = envelope.Intent.ConversationFactReferences,
                maximumPlanSteps = envelope.Intent.MaximumPlanSteps
            },
            scope = new
            {
                applicationId = envelope.Host.ApplicationRevision.ApplicationId.Value,
                envelope.Host.StateSpaceId,
                envelope.Host.SessionContextId,
                envelope.Host.StateRevision,
                envelope.Host.EffectiveSetFingerprint,
                productRole = envelope.Host.RoleProfile.Role.ToString().ToLowerInvariant()
            },
            remaining = new
            {
                rounds = InteractionPlannerLimits.MaximumRounds - rounds,
                searches = InteractionPlannerLimits.MaximumSearches - searchCount,
                inspections = InteractionPlannerLimits.MaximumInspections - inspectionCount,
                candidates = InteractionPlannerLimits.MaximumCandidates - candidateCount,
                elapsedMilliseconds = Math.Max(0, InteractionPlannerLimits.MaximumElapsedMilliseconds - elapsed)
            },
            searches = searches.Select(search => new
            {
                search.Query,
                mode = search.Mode.ToString().ToLowerInvariant(),
                hits = search.Hits.Select(hit => new
                {
                    hit.Reference.QualifiedId,
                    hit.Reference.Kind,
                    hit.Reference.Version,
                    fingerprint = hit.Reference.ContentFingerprint,
                    hit.Name,
                    hit.Description
                })
            }),
            inspections = inspected.OrderBy(value => value.Hit.Reference.QualifiedId, StringComparer.Ordinal).Select(value => new
            {
                value.Hit.Reference.QualifiedId,
                value.Hit.Reference.Kind,
                value.Hit.Reference.Version,
                fingerprint = value.Hit.Reference.ContentFingerprint,
                contract = JsonSerializer.Deserialize<JsonElement>(value.ContractJson)
            })
        });
        return InteractionCanonicalJson.CanonicalizeObject(value);
    }

    private static string TraceFingerprint(
        AuthorizedInteractionEnvelope envelope,
        IReadOnlyList<SearchTrace> searches,
        IEnumerable<InteractionInspectedFeature> inspected,
        InteractionPlannerUsage usage)
    {
        var value = JsonSerializer.Serialize(new
        {
            envelope = envelope.Fingerprint,
            searches = searches.Select(search => new
            {
                search.Query,
                mode = search.Mode.ToString().ToLowerInvariant(),
                hits = search.Hits.Select(hit => new
                {
                    hit.Reference.QualifiedId,
                    hit.Reference.Version,
                    hit.Reference.ContentFingerprint
                })
            }),
            inspections = inspected.OrderBy(value => value.Hit.Reference.QualifiedId, StringComparer.Ordinal)
                .Select(value => new { value.Hit.Reference.QualifiedId, value.Hit.Reference.Version, value.Hit.Reference.ContentFingerprint }),
            usage = new { usage.Rounds, usage.Searches, usage.Inspections, usage.Candidates }
        });
        return InteractionCanonicalJson.Fingerprint(
            InteractionPlannerProtocol.TraceFingerprintDomain,
            InteractionCanonicalJson.CanonicalizeObject(value));
    }

    private static InteractionResolutionResult ProviderFailure(string code, string message)
    {
        var unsafeOutput = code.Contains("SCHEMA", StringComparison.Ordinal)
            || code.Contains("RESPONSE_INVALID", StringComparison.Ordinal)
            || code.Contains("TOOL", StringComparison.Ordinal)
            || code.Contains("IDENTITY", StringComparison.Ordinal);
        return NonResolution(
            unsafeOutput ? InteractionResolutionStatus.Unsafe : InteractionResolutionStatus.Unavailable,
            string.IsNullOrWhiteSpace(code) ? "PLANNER_PROVIDER_UNAVAILABLE" : code,
            string.IsNullOrWhiteSpace(message) ? "The planner provider did not return a usable result." : message);
    }

    private static string ModelStatusCode(InteractionResolutionStatus status) => status switch
    {
        InteractionResolutionStatus.NeedsInput => "PLANNER_NEEDS_INPUT",
        InteractionResolutionStatus.Ambiguous => "PLANNER_AMBIGUOUS",
        InteractionResolutionStatus.Unknown => "PLANNER_UNKNOWN",
        _ => throw new InteractionContractException("PLANNER_STATUS_FORBIDDEN", "The model cannot select this resolution status.")
    };

    private static bool Equivalent(InteractionReceiptProjection? receipt, InteractionResolutionResult result) =>
        receipt is not null
        && receipt.Status == InteractionResolutionStatusNames.Get(result.Status)
        && receipt.Code == result.Code
        && receipt.ProposalFingerprint == result.Proposal?.Fingerprint;

    private static InteractionResolutionResult WithPlannerEvidence(
        InteractionResolutionResult result,
        InteractionPlannerIdentity? identity,
        InteractionPlannerUsage usage)
    {
        var plannerEvidence = new[]
        {
            "planner:" + (identity?.Kind.ToString().ToLowerInvariant() ?? "unavailable"),
            "provider:" + (identity?.Provider ?? "unavailable"),
            "model:" + (identity?.Model ?? "unavailable"),
            "revision:" + (identity?.Revision ?? "unavailable"),
            "profile:" + (identity?.Profile ?? "unavailable"),
            "effort:" + (string.IsNullOrEmpty(identity?.ReasoningEffort) ? "not-applicable" : identity.ReasoningEffort),
            $"usage:rounds={usage.Rounds},searches={usage.Searches},inspections={usage.Inspections},candidates={usage.Candidates}"
        };
        var evidence = result.Evidence
            .Take(InteractionContractLimits.EvidenceItems - plannerEvidence.Length)
            .Concat(plannerEvidence).ToArray();
        return result.Status == InteractionResolutionStatus.Resolved
            ? InteractionResolutionResult.Resolved(result.Proposal!,
                "A current trusted interaction proposal was verified.", evidence,
                result.RecipeReference)
            : InteractionResolutionResult.NonResolution(result.Status, result.Code, result.SafeSummary, evidence);
    }

    private static InteractionResolutionResult NonResolution(InteractionResolutionStatus status, string code, string summary) =>
        InteractionResolutionResult.NonResolution(status, code, Safe(summary), []);

    private static string Safe(string value) => value.Length <= InteractionContractLimits.SafeEvidenceText
        ? value : value[..InteractionContractLimits.SafeEvidenceText];

    private sealed record SearchTrace(
        string Query,
        InteractionRetrievalMode Mode,
        IReadOnlyList<SearchHitTrace> Hits);

    private sealed record SearchHitTrace(
        InteractionFeatureReference Reference,
        string Name,
        string Description);
}
