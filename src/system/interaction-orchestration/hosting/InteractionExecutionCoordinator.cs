using System.Buffers.Binary;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Interactions;

internal sealed class InteractionExecutionCoordinator(
    IInteractionAuthorizationPolicy authorization,
    IInteractionExecutionAuthorityStore authorities,
    IInteractionReceiptStore receipts,
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    IStateSpaceRegistry stateSpaces,
    IActiveCatalogFeatureSnapshotProvider snapshots,
    IInteractionProposalVerifier verifier,
    IApplicationActionRunner actions,
    IInteractionRecipeLearner? recipeLearner = null,
    IInteractionQueryExecutorRegistry? queryExecutors = null) : IInteractionExecutionCoordinator
{
    public async Task<InteractionExecutionOutcome> ExecuteAsync(
        InteractionExecutionRequest request,
        InteractionAuthorizationRequest authorizationRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorizationRequest);
        if (authorizationRequest.Capability != InteractionCapability.Execute)
            return Terminal(InteractionExecutionReceiptDisposition.Unauthorized,
                "EXECUTION_AUTHORIZATION_INVALID", "Execution authorization is invalid.");

        InteractionAuthorizationDecision decision;
        try { decision = authorization.Evaluate(authorizationRequest); }
        catch { return Terminal(InteractionExecutionReceiptDisposition.Unauthorized,
            "EXECUTION_AUTHORIZATION_UNAVAILABLE", "Execution authorization is unavailable."); }
        if (!decision.Allowed || decision.Capability != InteractionCapability.Execute
            || decision.PrincipalReference != authorizationRequest.Principal.PrincipalId
            || decision.ApplicationId != authorizationRequest.ApplicationId
            || decision.StateSpaceId != authorizationRequest.StateSpaceId)
            return Terminal(InteractionExecutionReceiptDisposition.Unauthorized,
                "EXECUTION_NOT_AUTHORIZED", "Execution is not authorized for this scope.");

        var authority = await authorities.GetAsync(authorizationRequest, request.ResolutionReceiptId, cancellationToken);
        if (authority is null)
            return Terminal(InteractionExecutionReceiptDisposition.Stale,
                "RESOLUTION_AUTHORITY_UNAVAILABLE", "The resolved interaction is unavailable for execution.");
        if (authority.ProposalFingerprint != request.ProposalFingerprint)
            return await PersistFailureAsync(authority, request, InteractionExecutionReceiptDisposition.Stale,
                "PROPOSAL_FINGERPRINT_MISMATCH", "The execution proposal does not match the resolved interaction.", []);

        AuthorizedInteractionEnvelope? envelope;
        try { envelope = Rehydrate(authority, authorizationRequest.Principal); }
        catch (Exception exception) when (exception is InteractionContractException or ArgumentException or InvalidOperationException)
        { envelope = null; }
        if (envelope is null)
            return await PersistFailureAsync(authority, request, InteractionExecutionReceiptDisposition.Stale,
                "EXECUTION_AUTHORITY_STALE", "The application or state-space authority changed after planning.", []);

        AuthorizedInteractionEnvelope? learningEnvelope = null;
        if (request.Learn)
        {
            try { learningEnvelope = Rehydrate(authority, authorizationRequest.Principal, request.LearningIntent); }
            catch (Exception exception) when (exception is InteractionContractException or ArgumentException or InvalidOperationException)
            { learningEnvelope = null; }
        }

        var verified = Verify(envelope, request.Proposal);
        if (verified.Status != InteractionResolutionStatus.Resolved || verified.Proposal is null)
            return await PersistFailureAsync(authority, request,
                verified.Status == InteractionResolutionStatus.Stale
                    ? InteractionExecutionReceiptDisposition.Stale
                    : InteractionExecutionReceiptDisposition.Failed,
                verified.Code, verified.SafeSummary, []);
        if (verified.Proposal.Fingerprint != request.ProposalFingerprint)
            return await PersistFailureAsync(authority, request, InteractionExecutionReceiptDisposition.Stale,
                "PROPOSAL_BODY_MISMATCH", "The submitted proposal body does not match its resolved fingerprint.", []);

        var executionFingerprint = ExecutionFingerprint(authority, request);
        var consent = Consent(authority, request);
        var existingExecution = await receipts.FindExecutionAsync(consent, executionFingerprint, cancellationToken);
        if (existingExecution is not null)
        {
            if (existingExecution.Disposition == InteractionReceiptWriteDisposition.Conflict
                || existingExecution.Receipt is null)
                return new(InteractionExecutionReceiptDisposition.Failed,
                    "INTERACTION_EXECUTION_IDEMPOTENCY_CONFLICT",
                    "The execution idempotency key is already bound to another request.",
                    [], existingExecution, executionFingerprint);
            var replayDisposition = ReceiptDisposition(existingExecution.Receipt.Status);
            return new(replayDisposition, existingExecution.Receipt.Code,
                existingExecution.Receipt.SafeSummary, [], existingExecution, executionFingerprint,
                QueryResults: existingExecution.Receipt.QueryResults ?? []);
        }
        var stepReceipts = new List<InteractionExecutionStepReceiptDraft>(verified.Proposal.Steps.Count);
        var actionResults = new List<ApplicationActionExecutionResult>(verified.Proposal.Steps.Count);
        var queryResults = new Dictionary<string, InteractionQueryExecutionResult>(StringComparer.Ordinal);
        var queryProjections = new List<InteractionQueryResultProjection>();
        var queryReceiptDrafts = new List<InteractionExecutionQueryResultDraft>();
        var stopped = false;
        var cancelled = false;
        for (var index = 0; index < verified.Proposal.Steps.Count; index++)
        {
            var step = verified.Proposal.Steps[index];
            if (stopped)
            {
                stepReceipts.Add(new(index + 1, step.StepId, InteractionExecutionStepDisposition.Skipped));
                continue;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                stopped = true;
                stepReceipts.Add(new(index + 1, step.StepId, InteractionExecutionStepDisposition.Skipped));
                continue;
            }

            InteractionBoundStepInput bound;
            try { bound = InteractionResultBinder.Bind(step, queryResults); }
            catch (InteractionContractException)
            {
                stopped = true;
                stepReceipts.Add(new(index + 1, step.StepId, InteractionExecutionStepDisposition.Failed));
                continue;
            }

            var baseStepFingerprint = StepFingerprint(executionFingerprint, request.ProposalFingerprint, step, index + 1);
            var stepFingerprint = step.ResultBindings.Count == 0 ? baseStepFingerprint
                : BoundStepFingerprint(baseStepFingerprint, bound);
            if (step.Kind == InteractionPlanStepKind.Query)
            {
                if (step.QueryContract is null || queryExecutors is null
                    || !queryExecutors.TryGet(step.QueryContract.Executor, out var executor))
                {
                    stopped = true;
                    stepReceipts.Add(new(index + 1, step.StepId, InteractionExecutionStepDisposition.Failed));
                    continue;
                }
                try
                {
                    var query = await executor.ExecuteAsync(new(authority.StateSpaceId,
                        authority.ApplicationId, step.QueryContract, bound.RoleBindings), cancellationToken);
                    queryResults.Add(step.StepId, query);
                    queryProjections.Add(new(step.StepId, step.Contract.QualifiedKey,
                        query.OutputSchemaHash, query.ResultFingerprint, query.SourceRevisionFingerprint,
                        step.QueryContract.Exposure == ApplicationQueryExposure.ModelVisible
                            ? JsonSerializer.Deserialize<JsonElement>(query.OutputJson) : null));
                    queryReceiptDrafts.Add(new(index + 1, step.StepId, step.Contract.QualifiedKey,
                        query.OutputSchemaHash, query.ResultFingerprint, query.SourceRevisionFingerprint,
                        step.QueryContract.Exposure,
                        step.QueryContract.Exposure == ApplicationQueryExposure.ModelVisible
                            ? query.OutputJson : null));
                    stepReceipts.Add(new(index + 1, step.StepId, InteractionExecutionStepDisposition.Succeeded));
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    stopped = true;
                    stepReceipts.Add(new(index + 1, step.StepId, InteractionExecutionStepDisposition.Failed));
                }
                catch
                {
                    stopped = true;
                    stepReceipts.Add(new(index + 1, step.StepId, InteractionExecutionStepDisposition.Failed));
                }
                continue;
            }

            var identity = new ApplicationEcsExecutionIdentity(
                baseStepFingerprint[..32].ToLowerInvariant(), stepFingerprint);
            var seedBytes = Convert.FromHexString(stepFingerprint[..16]);
            var seed = BinaryPrimitives.ReadInt64BigEndian(seedBytes);
            ApplicationActionExecutionResult action;
            try
            {
                action = await actions.RunAsync(new(
                    authority.StateSpaceId,
                    authority.ApplicationId,
                    step.Contract.QualifiedKey,
                    step.Contract.Fingerprint,
                    bound.RoleBindings,
                    bound.InputJson,
                    seed,
                    identity), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                stopped = true;
                stepReceipts.Add(new(index + 1, step.StepId, InteractionExecutionStepDisposition.Failed));
                continue;
            }
            catch
            {
                stopped = true;
                stepReceipts.Add(new(index + 1, step.StepId, InteractionExecutionStepDisposition.Failed));
                continue;
            }
            actionResults.Add(action);
            var stepDisposition = action.Disposition switch
            {
                ApplicationActionExecutionDisposition.Succeeded => InteractionExecutionStepDisposition.Succeeded,
                ApplicationActionExecutionDisposition.Replayed => InteractionExecutionStepDisposition.Replayed,
                _ => InteractionExecutionStepDisposition.Failed
            };
            stepReceipts.Add(new(index + 1, step.StepId, stepDisposition,
                string.IsNullOrWhiteSpace(action.OperationId) ? null : action.OperationId));
            if (!action.Successful) stopped = true;
        }

        var successful = stepReceipts.Count(value => value.Disposition is
            InteractionExecutionStepDisposition.Succeeded or InteractionExecutionStepDisposition.Replayed);
        var failed = stepReceipts.Count(value => value.Disposition == InteractionExecutionStepDisposition.Failed);
        var disposition = cancelled
            ? successful > 0 ? InteractionExecutionReceiptDisposition.Partial : InteractionExecutionReceiptDisposition.Cancelled
            : failed == 0 && successful == verified.Proposal.Steps.Count
                ? InteractionExecutionReceiptDisposition.Succeeded
                : successful > 0 ? InteractionExecutionReceiptDisposition.Partial : InteractionExecutionReceiptDisposition.Failed;
        var summary = disposition switch
        {
            InteractionExecutionReceiptDisposition.Succeeded => "The verified interaction completed.",
            InteractionExecutionReceiptDisposition.Partial => "The interaction stopped after partial committed progress.",
            InteractionExecutionReceiptDisposition.Cancelled => "The interaction was cancelled before a step committed.",
            _ => "The interaction failed before any step committed."
        };
        var draft = new InteractionExecutionReceiptDraft(consent, executionFingerprint, disposition,
            summary,
            ["steps:" + verified.Proposal.Steps.Count, "committed-or-replayed:" + successful, "failed:" + failed],
            stepReceipts,
            queryReceiptDrafts);
        var write = await receipts.AppendExecutionAsync(draft, CancellationToken.None);
        if (write.Disposition == InteractionReceiptWriteDisposition.Conflict)
            return new(InteractionExecutionReceiptDisposition.Failed,
                "INTERACTION_EXECUTION_IDEMPOTENCY_CONFLICT",
                "The execution idempotency key is already bound to another request.",
                actionResults.AsReadOnly(), write, executionFingerprint);
        if (authority.RecipeReference is not null && write.Receipt is not null)
        {
            try
            {
                await (recipeLearner ?? new UnavailableInteractionRecipeLearner()).RecordUseAsync(new(
                    authority.RecipeReference, authority.ResolutionReceiptId, write.Receipt.Id,
                    disposition == InteractionExecutionReceiptDisposition.Succeeded,
                    authority.EnvelopeFingerprint, authority.RoleProfile), CancellationToken.None);
            }
            catch
            {
                // Use evidence is diagnostic and may never rewrite completed action truth.
            }
        }
        var learning = InteractionRecipeLearningResult.NotRequested();
        if (request.Learn)
        {
            if (disposition != InteractionExecutionReceiptDisposition.Succeeded || write.Receipt is null)
                learning = new(InteractionRecipeLearningDisposition.NotCreated, "LEARNING_EXECUTION_INELIGIBLE",
                    "Only a completely successful interaction can be learned.");
            else if (learningEnvelope is null)
                learning = new(InteractionRecipeLearningDisposition.Conflict, "LEARNING_INTENT_MISMATCH",
                    "The supplied learning intent does not match the authorized resolution.");
            else
            {
                try
                {
                    learning = await (recipeLearner ?? new UnavailableInteractionRecipeLearner()).LearnAsync(
                        new(learningEnvelope, request.Proposal, write.Receipt), CancellationToken.None);
                }
                catch
                {
                    learning = new(InteractionRecipeLearningDisposition.NotCreated, "RECIPE_LEARNING_FAILED",
                        "The completed interaction remains successful, but its route was not learned.");
                }
            }
        }
        return new(disposition,
            disposition == InteractionExecutionReceiptDisposition.Succeeded
                ? "INTERACTION_EXECUTION_SUCCEEDED"
                : disposition == InteractionExecutionReceiptDisposition.Partial
                    ? "INTERACTION_EXECUTION_PARTIAL"
                    : disposition == InteractionExecutionReceiptDisposition.Cancelled
                        ? "INTERACTION_EXECUTION_CANCELLED"
                        : "INTERACTION_EXECUTION_FAILED",
            summary, actionResults.AsReadOnly(), write, executionFingerprint, learning,
            write.Receipt?.QueryResults ?? queryProjections.AsReadOnly());
    }

    private AuthorizedInteractionEnvelope? Rehydrate(
        InteractionResolutionExecutionAuthority authority,
        DantesRoleplay.Authorization.TrustedPrincipalContext principal,
        InteractionIntent? originalIntent = null)
    {
        var application = applications.Get(authority.ApplicationId);
        var activation = activations.Current(authority.ApplicationId);
        var stateSpace = stateSpaces.Get(authority.StateSpaceId);
        if (application is null || application.Revision != authority.ApplicationRevision
            || application.Fingerprint != authority.ApplicationFingerprint
            || activation is null || activation.ActivationFingerprint != authority.EffectiveSetFingerprint
            || stateSpace is null || !SameRevision(stateSpace.ApplicationRevision, application)
            || stateSpace.ManifestFingerprint != authority.EffectiveSetFingerprint
            || InteractionStateRevision.From(stateSpace) != authority.StateRevision)
            return null;
        InteractionRoleProfile role;
        if (authority.RoleProfile == InteractionRoleProfile.Inner.StableKey) role = InteractionRoleProfile.Inner;
        else if (authority.RoleProfile == InteractionRoleProfile.Outer.StableKey) role = InteractionRoleProfile.Outer;
        else if (authority.RoleProfile == InteractionRoleProfile.Direct.StableKey) role = InteractionRoleProfile.Direct;
        else return null;
        var planRequest = new InteractionAuthorizationRequest(principal, authority.ApplicationId,
            authority.StateSpaceId, InteractionCapability.Plan, "execution.rehydrate");
        var planEvidence = InteractionAuthorizationDecision.Allow(planRequest, authority.AuthorizationEvidenceReference);
        var host = new InteractionHostContext(principal, application, authority.StateSpaceId,
            authority.SessionContextId, authority.StateRevision, authority.EffectiveSetFingerprint,
            role, new(InteractionContractLimits.ProposalSteps, InteractionContractLimits.JsonBytes,
                InteractionContractLimits.JsonBytes), planEvidence, authority.ConversationId,
            authority.ParentDelegationId);
        if (originalIntent is not null)
        {
            if (originalIntent.IdempotencyKey != authority.ResolutionIdempotencyKey) return null;
            var original = AuthorizedInteractionEnvelope.Create(originalIntent, host);
            return original.Fingerprint == authority.EnvelopeFingerprint ? original : null;
        }
        var intent = InteractionIntent.Parse(JsonSerializer.Serialize(new
        {
            idempotencyKey = authority.ResolutionIdempotencyKey,
            intentText = "Persisted interaction intent is redacted.",
            maximumPlanSteps = InteractionContractLimits.ProposalSteps,
            plannerPreference = "automatic"
        }));
        return AuthorizedInteractionEnvelope.FromReceipt(intent, host, authority.EnvelopeFingerprint);
    }

    private InteractionResolutionResult Verify(
        AuthorizedInteractionEnvelope envelope,
        InteractionPlannerProposalCommand draft)
    {
        if (!snapshots.TryGetSnapshot(envelope.Host.ApplicationRevision.ApplicationId, out var snapshot))
            return InteractionResolutionResult.NonResolution(InteractionResolutionStatus.Stale,
                "CATALOG_SNAPSHOT_STALE", "The active application catalog is unavailable.", []);
        var inspected = new List<InteractionInspectedFeature>();
        foreach (var reference in draft.Steps.Select(value => value.QualifiedId).Distinct(StringComparer.Ordinal))
        {
            var document = snapshot.Documents.SingleOrDefault(value =>
                value.Trust == SourceTrust.Trusted && value.Record.QualifiedId == reference);
            if (document is null)
                return InteractionResolutionResult.NonResolution(InteractionResolutionStatus.Stale,
                    "EXECUTION_CONTRACT_STALE", "A proposed contract is no longer a current trusted record.", []);
            var featureReference = InteractionFeatureReference.Create(envelope.Host.ApplicationRevision.ApplicationId,
                InteractionRetrievalLane.TrustedFeature, snapshot.Manifest.Fingerprint, document.Record);
            var hit = InteractionFeatureHit.Create(featureReference, document.Record, null, null, exact: true);
            inspected.Add(new(hit, document.Record.ContentJson));
        }
        return verifier.Verify(new(envelope, inspected.AsReadOnly(), draft));
    }

    private async Task<InteractionExecutionOutcome> PersistFailureAsync(
        InteractionResolutionExecutionAuthority authority,
        InteractionExecutionRequest request,
        InteractionExecutionReceiptDisposition disposition,
        string code,
        string summary,
        IReadOnlyList<InteractionExecutionStepReceiptDraft> steps)
    {
        var fingerprint = ExecutionFingerprint(authority, request);
        var write = await receipts.AppendExecutionAsync(new(
            Consent(authority, request), fingerprint, disposition,
            string.IsNullOrWhiteSpace(summary) ? "The interaction execution was rejected." : summary,
            [code], steps), CancellationToken.None);
        if (authority.RecipeReference is not null && write.Receipt is not null)
        {
            try
            {
                await (recipeLearner ?? new UnavailableInteractionRecipeLearner()).RecordUseAsync(new(
                    authority.RecipeReference, authority.ResolutionReceiptId, write.Receipt.Id, false,
                    authority.EnvelopeFingerprint, authority.RoleProfile), CancellationToken.None);
            }
            catch
            {
                // Diagnostic evidence cannot alter the persisted execution outcome.
            }
        }
        return new(disposition, code, summary, [], write, fingerprint,
            request.Learn
                ? new(InteractionRecipeLearningDisposition.NotCreated, "LEARNING_EXECUTION_INELIGIBLE",
                    "Only a completely successful interaction can be learned.")
                : InteractionRecipeLearningResult.NotRequested());
    }

    private static InteractionExecutionConsentReference Consent(
        InteractionResolutionExecutionAuthority authority,
        InteractionExecutionRequest request) => new(authority.ResolutionReceiptId,
            authority.ProposalFingerprint, authority.PrincipalReference, authority.ApplicationId,
            authority.StateSpaceId, request.IdempotencyKey);

    private static string ExecutionFingerprint(
        InteractionResolutionExecutionAuthority authority,
        InteractionExecutionRequest request)
    {
        var value = request.Learn
            ? JsonSerializer.Serialize(new
            {
                authority.ResolutionReceiptId,
                authority.EnvelopeFingerprint,
                proposalFingerprint = request.ProposalFingerprint,
                executionIdempotencyKey = request.IdempotencyKey,
                stopOnFailure = true,
                learn = true,
                learningIntentFingerprint = request.LearningIntent is null ? null : InteractionCanonicalJson.Fingerprint(
                    "dantes-roleplay/interaction-learning-intent/v1",
                    InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(request.LearningIntent)))
            })
            : JsonSerializer.Serialize(new
            {
                authority.ResolutionReceiptId,
                authority.EnvelopeFingerprint,
                proposalFingerprint = request.ProposalFingerprint,
                executionIdempotencyKey = request.IdempotencyKey,
                stopOnFailure = true
            });
        return InteractionCanonicalJson.Fingerprint(
            InteractionExecutionProtocol.RequestFingerprintDomain,
            InteractionCanonicalJson.CanonicalizeObject(value));
    }

    private static string StepFingerprint(
        string executionFingerprint,
        string proposalFingerprint,
        InteractionPlanStep step,
        int ordinal) => InteractionCanonicalJson.Fingerprint(
            InteractionExecutionProtocol.StepFingerprintDomain,
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                executionFingerprint,
                proposalFingerprint,
                ordinal,
                step.StepId,
                contract = step.Contract.Fingerprint
            })));

    private static string BoundStepFingerprint(string baseStepFingerprint, InteractionBoundStepInput bound) =>
        InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/interaction-bound-step/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                baseStepFingerprint,
                roles = bound.RoleBindings,
                input = JsonSerializer.Deserialize<JsonElement>(bound.InputJson),
                sourceResults = bound.SourceResultFingerprints
            })));

    private static bool SameRevision(ApplicationRevision left, ApplicationRevision right) =>
        left.ApplicationId == right.ApplicationId && left.Revision == right.Revision
        && left.Fingerprint == right.Fingerprint
        && left.BaseApplications.SequenceEqual(right.BaseApplications);

    private static InteractionExecutionReceiptDisposition ReceiptDisposition(string value) => value switch
    {
        "succeeded" => InteractionExecutionReceiptDisposition.Succeeded,
        "failed" => InteractionExecutionReceiptDisposition.Failed,
        "partial" => InteractionExecutionReceiptDisposition.Partial,
        "skipped" => InteractionExecutionReceiptDisposition.Skipped,
        "stale" => InteractionExecutionReceiptDisposition.Stale,
        "unauthorized" => InteractionExecutionReceiptDisposition.Unauthorized,
        "cancelled" => InteractionExecutionReceiptDisposition.Cancelled,
        "timed-out" => InteractionExecutionReceiptDisposition.TimedOut,
        _ => InteractionExecutionReceiptDisposition.Failed
    };

    private static InteractionExecutionOutcome Terminal(
        InteractionExecutionReceiptDisposition disposition,
        string code,
        string summary) => new(disposition, code, summary, [], null, "");
}
