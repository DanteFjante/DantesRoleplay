using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.ApplicationExecution;

internal interface IApplicationPureActionExecutor
{
    Task<InteractionInvocationResult> ExecuteAsync(
        ApplicationActionInvocationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes the initial application-scoped, state-free mechanic subset. The active navigator and
/// exact record are retained for one invocation; current owner and grant authority are rechecked
/// before returning process-local computation evidence.
/// </summary>
internal sealed class ApplicationPureActionExecutor(
    IPublicApplicationCatalogProvider catalogs,
    ApplicationCandidatePureMechanicClassifier classifier,
    JintMechanicEngine engine,
    IBoundedJsonSchemaValidator schemas,
    IStandingGrantTargetResolver targets,
    IStandingGrantPolicy grants,
    IEcsWriteTransactionFactory transactions) : IApplicationPureActionExecutor
{
    public async Task<InteractionInvocationResult> ExecuteAsync(
        ApplicationActionInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.Host.StateSpaceId is not null || request.Host.StateRevision is not null)
                return InteractionInvocationResult.Failed(
                    "INVOCATION_APPLICATION_SCOPE_REQUIRED",
                    "Pure action execution requires an application scope.");
            var deadlineFailure = DeadlineFailure(request.Host, cancellationToken);
            if (deadlineFailure is not null) return deadlineFailure;
            if (request.RoleEntityIds.Count != 0)
                return InteractionInvocationResult.Failed(
                    "PURE_ACTION_ROLES_FORBIDDEN",
                    "A pure application action cannot bind state roles.");

            var retained = ResolveRetained(request);
            if (retained.Failure is not null) return retained.Failure;
            var classification = classifier.Classify(retained.Record!);
            if (classification.Outcome != ApplicationCandidatePureMechanicOutcome.Supported
                || classification.Plan is null)
                return classification.Outcome == ApplicationCandidatePureMechanicOutcome.Invalid
                    ? InteractionInvocationResult.Failed(
                        classification.Diagnostic?.Code ?? "PURE_ACTION_INVALID",
                        "The selected mechanic is not a valid pure action.")
                    : InteractionInvocationResult.Unavailable(
                        classification.Diagnostic?.Code ?? "PURE_ACTION_UNAVAILABLE",
                        "The selected mechanic is outside the pure action subset.");
            var plan = classification.Plan;
            if (plan.Definition.DefinitionId != request.QualifiedMechanicId
                || plan.Definition.Revision != request.MechanicVersion
                || plan.Definition.ContentFingerprint != request.ContentFingerprint)
                return InteractionInvocationResult.Failed(
                    "PURE_ACTION_SELECTION_STALE",
                    "The selected pure action is no longer current.");

            var input = InteractionCanonicalJson.CanonicalizeObject(request.InputJson);
            if (Encoding.UTF8.GetByteCount(input) > InteractionContractLimits.JsonBytes)
                return InteractionInvocationResult.Failed(
                    "JSON_TOO_LARGE", "The pure action input exceeds the interaction limit.");
            if (plan.NormalizedInputSchema is { } inputSchema
                && schemas.Validate(SystemJsonSchemaProfile.Id, inputSchema, input).Status != SchemaValueStatus.Valid)
                return InteractionInvocationResult.Failed(
                    "PURE_ACTION_INPUT_INVALID",
                    "The pure action input does not match its declared schema.");

            var initialAuthority = await AuthorizeAsync(request.Host, plan.Definition, cancellationToken);
            if (initialAuthority.Failure is not null) return initialAuthority.Failure;
            if (!request.Host.Budget.TryConsumeOperation())
                return InteractionInvocationResult.Failed(
                    "INVOCATION_BUDGET_EXHAUSTED", "The invocation operation budget is exhausted.");

            var invocationFingerprint = InteractionInvocationIdentity.Fingerprint(
                request.Host, request.QualifiedMechanicId, request.MechanicVersion,
                request.ContentFingerprint, new Dictionary<string, string>(), input);
            var seed = BinaryPrimitives.ReadInt64BigEndian(
                Convert.FromHexString(invocationFingerprint[..16]));
            var remaining = request.Host.Budget.DeadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new OperationCanceledException();
            var limits = ExecutionLimits.ReadModel with
            {
                Timeout = remaining < ExecutionLimits.ReadModel.Timeout
                    ? remaining : ExecutionLimits.ReadModel.Timeout,
                MaxEffects = 0,
                MaxEvents = 0,
                MaxNotifications = 0
            };
            using var deadline = DeadlineToken(request.Host, cancellationToken);
            var run = await engine.RunAsync(plan.Source,
                new MechanicProjection { Input = input, Seed = seed }, limits, deadline.Token);
            var finalAuthority = await AuthorizeAsync(request.Host, plan.Definition, cancellationToken);
            if (finalAuthority.Failure is not null || finalAuthority.Stamp != initialAuthority.Stamp)
                return InteractionInvocationResult.Failed(
                    "INVOCATION_AUTHORITY_CHANGED",
                    "The pure action authority changed before its result could be returned.");
            if (!ApplicationPureMechanicOutput.TryReadData(run, out var output, out var outputFingerprint))
                return InteractionInvocationResult.Failed(
                    "PURE_ACTION_OUTPUT_INVALID",
                    "The selected mechanic did not produce bounded pure data.");

            var evidence = InteractionCanonicalJson.Fingerprint(
                "dantes-roleplay/application-pure-action/process-local/v1",
                InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
                {
                    definition = plan.Definition,
                    classification = plan.ClassificationFingerprint,
                    principal = request.Host.Principal.PrincipalId,
                    applicationRevision = request.Host.ApplicationRevision,
                    request.Host.CommandId,
                    invocationFingerprint,
                    outputFingerprint
                })));
            return InteractionInvocationResult.CompletedComputation(
                output!, "pure-action-process-local." + evidence.ToLowerInvariant());
        }
        catch (OperationCanceledException)
        {
            return InteractionInvocationResult.Cancelled(
                "INVOCATION_CANCELLED", "The pure action invocation was cancelled.");
        }
        catch (InteractionContractException exception)
        {
            return InteractionInvocationResult.Failed(
                exception.Code, "The pure action invocation is invalid.");
        }
        catch
        {
            return InteractionInvocationResult.Unavailable(
                "PURE_ACTION_RUNTIME_UNAVAILABLE", "The pure action runtime is unavailable.");
        }
    }

    private RetainedResolution ResolveRetained(ApplicationActionInvocationRequest request)
    {
        var application = request.Host.ApplicationRevision.ApplicationId;
        if (!catalogs.TryGet(application, out var catalog))
            return Unavailable();
        try
        {
            var effective = catalog.EffectiveContent(new(application,
                PageSize: CatalogNavigationLimits.MaximumPageSize,
                Kinds: ["mechanic"], QualifiedIds: [request.QualifiedMechanicId]));
            var winner = effective.ResolvedWinners.SingleOrDefault(value =>
                value.Record.Kind == "mechanic"
                && value.Record.Status == "active"
                && value.Record.QualifiedId == request.QualifiedMechanicId
                && value.Record.Version == request.MechanicVersion
                && value.Record.ContentFingerprint == request.ContentFingerprint);
            if (winner is null) return Stale();
            var record = catalog.Inspect(new(
                application, winner.Record.Collection, winner.Record.QualifiedId));
            return record.Summary == winner.Record
                ? new(record, null)
                : Stale();
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException
            or InvalidOperationException)
        {
            return Unavailable();
        }

        static RetainedResolution Stale() => new(null, InteractionInvocationResult.Failed(
            "PURE_ACTION_SELECTION_STALE", "The selected pure action is no longer current."));
        static RetainedResolution Unavailable() => new(null, InteractionInvocationResult.Unavailable(
            "PURE_ACTION_CATALOG_UNAVAILABLE", "The exact active application catalog is unavailable."));
    }

    private async Task<AuthorizationResult> AuthorizeAsync(
        InteractionInvocationHost host,
        StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken)
    {
        using var deadline = DeadlineToken(host, cancellationToken);
        await using var transaction = await transactions.BeginAsync(deadline.Token);
        if (!transactions.OwnsCurrent(transaction))
            return Denied("PURE_ACTION_TRANSACTION_UNAVAILABLE",
                "The pure action authorization transaction is unavailable.");
        var resolution = await targets.ResolveAsync(host, selection, deadline.Token);
        if (resolution is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } target })
            return resolution?.Status == StandingGrantTargetResolutionStatus.Denied
                ? Denied("INVOCATION_NOT_AUTHORIZED", "The pure action is not authorized.")
                : Denied("PURE_ACTION_AUTHORITY_UNAVAILABLE", "Current pure action authority is unavailable.");
        if (target.DefinitionId != selection.DefinitionId || target.Kind != selection.Kind
            || target.Revision != selection.Revision
            || target.ContentFingerprint != selection.ContentFingerprint
            || target.OwnerApplicationId != host.ApplicationRevision.ApplicationId
            || target.Candidate is not null || target.RetainedActivation is not null)
            return Denied("PURE_ACTION_AUTHORITY_UNAVAILABLE", "Current pure action authority is unavailable.");
        var readDecision = await grants.EvaluateAsync(host,
            new(StandingGrantCapability.Read, StandingGrantScope.Application, [target], []),
            deadline.Token);
        if (!ExactAllowedDecision(host, target, StandingGrantCapability.Read, readDecision))
            return Denied("INVOCATION_NOT_AUTHORIZED", "The pure action is not authorized.");
        var executeDecision = await grants.EvaluateAsync(host,
            new(StandingGrantCapability.Execute, StandingGrantScope.Application, [target], []),
            deadline.Token);
        if (!ExactAllowedDecision(host, target, StandingGrantCapability.Execute, executeDecision)
            || GrantIdentity.From(readDecision!.Grant!) != GrantIdentity.From(executeDecision!.Grant!))
            return Denied("INVOCATION_NOT_AUTHORIZED", "The pure action is not authorized.");
        await transaction.RollbackAsync(CancellationToken.None);
        return new(new(target, GrantIdentity.From(executeDecision.Grant!)), null);
    }

    private static bool ExactAllowedDecision(
        InteractionInvocationHost host,
        StandingGrantDefinitionTarget target,
        StandingGrantCapability capability,
        StandingGrantDecision? decision)
    {
        if (decision is not { Allowed: true, Grant: not null, Evidence.Allowed: true }
            || decision.Evidence.PrincipalReference != host.Principal.PrincipalId
            || decision.Evidence.AuthenticationMethod != host.Principal.AuthenticationMethod
            || decision.Evidence.Scope != host.ApplicationRevision.ApplicationId.Value
            || decision.Evidence.CorrelationId != host.CommandId)
            return false;
        var grant = decision.Grant;
        try { StandingGrantContractRules.ValidateConfiguration(grant); }
        catch (InteractionContractException) { return false; }
        return grant.GrantReference == host.GrantReference
            && grant.PrincipalReference == host.Principal.PrincipalId
            && grant.ApplicationId == host.ApplicationRevision.ApplicationId
            && grant.Scope == StandingGrantScope.Application
            && grant.StateSpaceId is null
            && !grant.Revoked
            && grant.ExpiresAtUtc > DateTime.UtcNow
            && host.Budget.DeadlineUtc <= grant.ExpiresAtUtc
            && host.Budget.MaximumOperations <= grant.MaximumOperations
            && grant.Capabilities.Contains(capability)
            && StandingGrantContractRules.MatchesDefinitionAllowance(
                host.ApplicationRevision.ApplicationId, grant.Definitions, target);
    }

    private static InteractionInvocationResult? DeadlineFailure(
        InteractionInvocationHost host,
        CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? InteractionInvocationResult.Cancelled(
                "INVOCATION_CANCELLED", "The pure action invocation was cancelled.")
            : host.Budget.DeadlineUtc <= DateTime.UtcNow
                ? InteractionInvocationResult.Cancelled(
                    "INVOCATION_DEADLINE_EXCEEDED", "The invocation deadline elapsed.")
                : null;

    private static CancellationTokenSource DeadlineToken(
        InteractionInvocationHost host,
        CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = host.Budget.DeadlineUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) source.Cancel();
        else if (remaining <= TimeSpan.FromMilliseconds(int.MaxValue)) source.CancelAfter(remaining);
        return source;
    }

    private static AuthorizationResult Denied(string code, string message) =>
        new(null, code == "PURE_ACTION_AUTHORITY_UNAVAILABLE"
            || code == "PURE_ACTION_TRANSACTION_UNAVAILABLE"
            ? InteractionInvocationResult.Unavailable(code, message)
            : InteractionInvocationResult.Failed(code, message));

    private sealed record RetainedResolution(
        CatalogRecordView? Record,
        InteractionInvocationResult? Failure);

    private sealed record AuthorizationResult(
        AuthorityStamp? Stamp,
        InteractionInvocationResult? Failure);

    private sealed record AuthorityStamp(
        StandingGrantDefinitionTarget Target,
        GrantIdentity Grant);

    private sealed record GrantIdentity(
        string GrantReference,
        string GrantId,
        int Revision,
        string ContentFingerprint)
    {
        internal static GrantIdentity From(StandingGrantRevision grant) => new(
            grant.GrantReference, grant.GrantId, grant.Revision, grant.ContentFingerprint);
    }
}

internal static class ApplicationPureMechanicOutput
{
    internal static bool TryReadData(
        MechanicRunResult run,
        out string? dataJson,
        out string? dataFingerprint)
    {
        dataJson = null;
        dataFingerprint = null;
        if (!run.Ok || run.Output is not { HasData: true } output
            || output.Effects is null || output.Effects.Count != 0
            || output.Events is null || output.Events.Count != 0
            || output.Notifications is null || output.Notifications.Count != 0
            || !string.IsNullOrEmpty(output.Narration)
            || !string.IsNullOrEmpty(output.Decision)
            || !string.IsNullOrEmpty(output.Code)
            || !string.IsNullOrEmpty(output.Reason))
            return false;
        try
        {
            dataJson = InteractionCanonicalJson.CanonicalizeObject(output.Data);
            if (Encoding.UTF8.GetByteCount(dataJson) > InteractionContractLimits.JsonBytes)
                return false;
            dataFingerprint = ApplicationCandidateRuntimeValidator.DataFingerprint(dataJson);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
