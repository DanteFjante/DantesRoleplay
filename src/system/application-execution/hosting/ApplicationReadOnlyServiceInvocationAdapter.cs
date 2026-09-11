using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.ApplicationExecution;

internal sealed record ApplicationWorkflowServiceInvocationRequest
{
    internal ApplicationWorkflowServiceInvocationRequest(
        InteractionInvocationHost host,
        SystemTaskSelectedDefinition selectedDefinition,
        ApplicationReadOnlyServiceDefinition definition,
        IReadOnlyDictionary<string, string> hostRoleBindings,
        string inputJson,
        ExecutionLimits computationLimits,
        ApplicationServiceProgressChannel? progress = null)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        if (host.Profile != InteractionExecutionProfile.Workflow)
            throw new InteractionContractException(
                "SERVICE_PROFILE_UNAVAILABLE", "The workflow service requires the workflow profile.");
        SelectedDefinition = selectedDefinition ?? throw new ArgumentNullException(nameof(selectedDefinition));
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        var roles = InteractionInvocationRoles.Normalize(hostRoleBindings);
        HostRoleBindings = new ReadOnlyDictionary<string, string>(new SortedDictionary<string, string>(
            roles.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal));
        InputJson = InteractionCanonicalJson.CanonicalizeObject(inputJson);
        ComputationLimits = computationLimits ?? throw new ArgumentNullException(nameof(computationLimits));
        Progress = progress;
    }

    internal InteractionInvocationHost Host { get; }
    internal SystemTaskSelectedDefinition SelectedDefinition { get; }
    internal ApplicationReadOnlyServiceDefinition Definition { get; }
    internal IReadOnlyDictionary<string, string> HostRoleBindings { get; }
    internal string InputJson { get; }
    internal ExecutionLimits ComputationLimits { get; }
    internal ApplicationServiceProgressChannel? Progress { get; }
}

internal interface IApplicationWorkflowServiceInvocationAdapter
{
    Task<InteractionInvocationResult> InvokeAsync(
        ApplicationWorkflowServiceInvocationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes one exact retained service mechanic. Read-only calls expose declared reads; the
/// registered workflow boundary additionally exposes independently committed declared actions.
/// </summary>
internal sealed class ApplicationReadOnlyServiceInvocationAdapter(
    IPublicApplicationCatalogProvider catalogs,
    IApplicationReadOnlyServiceDefinitionReader definitionReader,
    IStandingGrantApplicationReadModelInvocationAdapter reads,
    IBoundedJsonSchemaValidator schemas,
    JintMechanicEngine mechanics,
    IStateSpaceRegistry stateSpaces,
    IStandingGrantTargetResolver grantTargets,
    IStandingGrantPolicy standingGrants,
    ApplicationActionInvocationAdapter? actions = null,
    IEcsWriteTransactionFactory? transactions = null) : IApplicationReadOnlyServiceInvocationAdapter,
    IApplicationWorkflowServiceInvocationAdapter
{
    public async Task<InteractionInvocationResult> InvokeAsync(
        ApplicationReadOnlyServiceInvocationRequest request,
        CancellationToken cancellationToken = default) =>
        await InvokeAsync(ServiceInvocation.From(request), workflow: false, cancellationToken);

    public async Task<InteractionInvocationResult> InvokeAsync(
        ApplicationWorkflowServiceInvocationRequest request,
        CancellationToken cancellationToken = default) =>
        await InvokeAsync(ServiceInvocation.From(request), workflow: true, cancellationToken);

    private async Task<InteractionInvocationResult> InvokeAsync(
        ServiceInvocation request,
        bool workflow,
        CancellationToken cancellationToken)
    {
        ApplicationServiceProgressChannel? progress = null;
        var ownsProgress = false;
        IInvocationCapabilityState? invocationState = null;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Host.StateSpaceId is not { } stateSpaceId || request.Host.StateRevision is null)
                return InteractionInvocationResult.Failed(
                    "INVOCATION_STATE_SCOPE_REQUIRED", "The service requires a state scope.");
            progress = request.Progress ?? new ApplicationServiceProgressChannel();
            if (!progress.TryBind())
                return InteractionInvocationResult.Failed(
                    "SERVICE_PROGRESS_ALREADY_BOUND", "The progress outlet is already bound.");
            ownsProgress = true;
            if (request.Progress is null) progress.Complete();

            if ((!workflow && request.Host.Profile != InteractionExecutionProfile.ReadOnly)
                || (workflow && request.Host.Profile != InteractionExecutionProfile.Workflow))
                return InteractionInvocationResult.Unavailable(
                    "SERVICE_PROFILE_UNAVAILABLE", "The requested service profile is unavailable.");
            if (workflow && (actions is null || transactions is null))
                return InteractionInvocationResult.Unavailable(
                    "WORKFLOW_SERVICE_UNAVAILABLE", "The state-changing service runtime is unavailable.");
            var deadline = DeadlineFailure(request.Host, cancellationToken);
            if (deadline is not null) return deadline;
            if (!request.Host.Budget.TryConsumeOperation())
                return InteractionInvocationResult.Failed(
                    "INVOCATION_BUDGET_EXHAUSTED", "The invocation operation budget is exhausted.");
            var scope = CurrentScopeFailure(request.Host);
            if (scope is not null) return scope;

            var rootSelection = new StandingGrantDefinitionReference(
                request.SelectedDefinition.ExactDefinitionId,
                "mechanic",
                request.SelectedDefinition.Version,
                request.SelectedDefinition.Fingerprint);
            var rootAuthorization = await AuthorizeAsync(
                request.Host, rootSelection,
                workflow ? StandingGrantCapability.Execute : StandingGrantCapability.Read,
                cancellationToken).ConfigureAwait(false);
            if (rootAuthorization.Failure is not null) return rootAuthorization.Failure;

            var retained = ResolveRetained(request);
            var retainedDefinition = definitionReader.ReadRetained(request.SelectedDefinition, retained.Record);
            if (!string.Equals(retainedDefinition.ToJson(), request.Definition.ToJson(), StringComparison.Ordinal))
                return InteractionInvocationResult.Failed(
                    "SERVICE_DECLARATION_STALE", "The service declaration is no longer current.");
            if (workflow && retainedDefinition.Actions.Count == 0)
                return InteractionInvocationResult.Unavailable(
                    "WORKFLOW_ACTIONS_UNAVAILABLE", "The selected service declares no state-changing actions.");
            if (HasUnsupportedRequirements(retained.RequirementsJson))
                return InteractionInvocationResult.Unavailable(
                    "SERVICE_REQUIREMENTS_UNAVAILABLE",
                    "The service uses projection or composition requirements unavailable to this runtime.");

            var input = CanonicalObject(request.InputJson);
            if (schemas.Validate(retainedDefinition.InputSchemaJson, input).Status != SchemaValueStatus.Valid)
                return InteractionInvocationResult.Failed(
                    "SERVICE_INPUT_INVALID", "The service input does not match its declared schema.");
            var limits = EffectiveLimits(request.ComputationLimits, request.Host);
            var invocationFingerprint = InteractionInvocationIdentity.Fingerprint(
                request.Host,
                request.SelectedDefinition.ExactDefinitionId,
                request.SelectedDefinition.Version,
                request.SelectedDefinition.Fingerprint,
                request.HostRoleBindings,
                input);
            var seed = BinaryPrimitives.ReadInt64BigEndian(Convert.FromHexString(invocationFingerprint[..16]));
            var exchange = new ExchangedDataBudget();
            exchange.Add(input);
            var readCapabilities = new InvocationCapabilities(
                request.Host,
                retainedDefinition,
                request.HostRoleBindings,
                reads,
                schemas,
                stateSpaces,
                progress,
                exchange);
            IApplicationReadOnlyServiceCapabilities capabilities = workflow
                ? new WorkflowInvocationCapabilities(
                    readCapabilities,
                    request.Host,
                    request.SelectedDefinition,
                    retainedDefinition,
                    request.HostRoleBindings,
                    actions!,
                    exchange)
                : readCapabilities;
            var state = capabilities as IInvocationCapabilityState ?? readCapabilities;
            invocationState = state;

            MechanicRunResult run;
            var priorContext = SynchronizationContext.Current;
            try
            {
                // The existing async read adapter may capture a caller context. Jint owns this
                // thread and waits synchronously, so suppress that context only while its native
                // callback can call the adapter, then restore it before returning to the caller.
                SynchronizationContext.SetSynchronizationContext(null);
                run = mechanics.RunServiceAsync(
                    retained.Source,
                    new MechanicProjection { StateSpaceId = stateSpaceId, Input = input, Seed = seed },
                    limits,
                    capabilities,
                    cancellationToken).GetAwaiter().GetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(priorContext);
            }

            if (state.TerminalResult is { } terminal) return terminal;
            deadline = DeadlineFailure(request.Host, cancellationToken);
            if (deadline is not null) return AttachPrevious(deadline, state.PreviousCommits);
            scope = CurrentScopeFailure(request.Host);
            if (scope is not null) return AttachPrevious(scope, state.PreviousCommits);
            rootAuthorization = await AuthorizeAsync(
                request.Host, rootSelection,
                workflow ? StandingGrantCapability.Execute : StandingGrantCapability.Read,
                cancellationToken, rootAuthorization.Authorization).ConfigureAwait(false);
            if (rootAuthorization.Failure is not null)
                return AttachPrevious(rootAuthorization.Failure, state.PreviousCommits);
            if (!run.Ok)
                return AttachPrevious(run.LimitHit is "cancelled" && cancellationToken.IsCancellationRequested
                    ? InteractionInvocationResult.Cancelled(
                        "INVOCATION_CANCELLED", "The service computation was cancelled.")
                    : InteractionInvocationResult.Failed(
                        "SERVICE_COMPUTATION_FAILED", "The service computation could not complete."), state.PreviousCommits);
            if (run.Output.Effects.Count != 0 || run.Output.Events.Count != 0
                || run.Output.Notifications.Count != 0)
                return InteractionInvocationResult.Failed(
                    "SERVICE_EFFECTS_FORBIDDEN", "A service cannot produce direct effects or announcements.", state.PreviousCommits);
            if (!run.Output.HasData)
                return InteractionInvocationResult.Failed(
                    "SERVICE_OUTPUT_REQUIRED", "The service did not produce data.", state.PreviousCommits);
            var output = CanonicalObject(run.Output.Data);
            exchange.Add(output);
            if (schemas.Validate(retainedDefinition.OutputSchemaJson, output).Status != SchemaValueStatus.Valid)
                return InteractionInvocationResult.Failed(
                    "SERVICE_OUTPUT_INVALID", "The service output does not match its declared schema.", state.PreviousCommits);

            var evidence = InteractionCanonicalJson.Fingerprint(
                "dantes-roleplay/application-read-only-service/process-local/v1",
                InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
                {
                    selected = new
                    {
                        id = request.SelectedDefinition.ExactDefinitionId,
                        request.SelectedDefinition.Version,
                        request.SelectedDefinition.Fingerprint
                    },
                    host = new
                    {
                        principal = request.Host.Principal.PrincipalId,
                        applicationId = request.Host.ApplicationRevision.ApplicationId.Value,
                        applicationRevision = request.Host.ApplicationRevision.Revision,
                        request.Host.ApplicationRevision.Fingerprint,
                        request.Host.StateSpaceId,
                        request.Host.StateRevision,
                        request.Host.CommandId
                    },
                    inputFingerprint = InteractionCanonicalJson.Fingerprint(
                        "dantes-roleplay/application-read-only-service/input/v1", input),
                    outputFingerprint = InteractionCanonicalJson.Fingerprint(
                        "dantes-roleplay/application-read-only-service/output/v1", output),
                    reads = state.ReadEvidence,
                    actions = state.ActionEvidence
                })));
            return InteractionInvocationResult.CompletedComputation(
                output, "service-process-local." + evidence.ToLowerInvariant(), state.PreviousCommits);
        }
        catch (OperationCanceledException)
        {
            return InteractionInvocationResult.Cancelled(
                "INVOCATION_CANCELLED", "The service invocation was cancelled.", invocationState?.PreviousCommits);
        }
        catch (InteractionContractException exception)
        {
            return InteractionInvocationResult.Failed(
                exception.Code, "The service invocation is invalid.", invocationState?.PreviousCommits);
        }
        catch (ApplicationReadModelException exception)
        {
            return InteractionInvocationResult.Failed(
                exception.Code, "The service read could not complete.", invocationState?.PreviousCommits);
        }
        catch
        {
            return invocationState is { PreviousCommits.Count: > 0 }
                ? InteractionInvocationResult.Failed(
                    "SERVICE_SEQUENCE_INCOMPLETE",
                    "The service runtime became unavailable after earlier commits.", invocationState.PreviousCommits)
                : InteractionInvocationResult.Unavailable(
                    "SERVICE_RUNTIME_UNAVAILABLE", "The service runtime is unavailable.");
        }
        finally
        {
            if (ownsProgress) progress!.Complete();
        }
    }

    private RetainedMechanic ResolveRetained(ServiceInvocation request)
    {
        var application = request.Host.ApplicationRevision.ApplicationId;
        if (!catalogs.TryGet(application, out var catalog))
            throw new ServiceUnavailableException();
        var effective = catalog.EffectiveContent(new(
            application,
            PageSize: CatalogNavigationLimits.MaximumPageSize,
            Kinds: ["mechanic"],
            QualifiedIds: [request.SelectedDefinition.ExactDefinitionId]));
        var winner = effective.ResolvedWinners.SingleOrDefault(value =>
            value.Record.Kind == "mechanic"
            && value.Record.QualifiedId == request.SelectedDefinition.ExactDefinitionId
            && value.Record.Version == request.SelectedDefinition.Version
            && value.Record.ContentFingerprint == request.SelectedDefinition.Fingerprint);
        if (winner is null) throw new ServiceUnavailableException();
        var record = catalog.Inspect(new(application, winner.Record.Collection, winner.Record.QualifiedId));
        if (record.Summary != winner.Record) throw new ServiceUnavailableException();

        using var content = JsonDocument.Parse(record.ContentJson,
            new JsonDocumentOptions { MaxDepth = InteractionContractLimits.JsonDepth });
        if (!content.RootElement.TryGetProperty("requirements", out var requirements)
            || requirements.ValueKind != JsonValueKind.String
            || !content.RootElement.TryGetProperty("source", out var source)
            || source.ValueKind != JsonValueKind.String)
            throw new InteractionContractException(
                "INVALID_SERVICE_MECHANIC", "The retained service mechanic is malformed.");
        return new(record, requirements.GetString()!, source.GetString()!);
    }

    private static bool HasUnsupportedRequirements(string requirementsJson)
    {
        var canonical = InteractionCanonicalJson.CanonicalizeObject(requirementsJson);
        using var requirements = JsonDocument.Parse(canonical);
        var fields = requirements.RootElement.EnumerateObject().Select(value => value.Name).ToArray();
        return fields.Length != 1 || fields[0] != "service";
    }

    private async Task<ServiceAuthorizationResult> AuthorizeAsync(
        InteractionInvocationHost host,
        StandingGrantDefinitionReference selection,
        StandingGrantCapability capability,
        CancellationToken cancellationToken,
        ServiceAuthorization? initial = null)
    {
        using var deadline = DeadlineToken(host, cancellationToken);
        await using var transaction = capability == StandingGrantCapability.Execute && transactions is not null
            ? await transactions.BeginAsync(deadline.Token).ConfigureAwait(false)
            : null;
        if (capability == StandingGrantCapability.Execute
            && (transaction is null || transactions is null || !transactions.OwnsCurrent(transaction)))
            return new(null, InteractionInvocationResult.Unavailable(
                "SERVICE_TRANSACTION_UNAVAILABLE", "The workflow service authorization transaction is unavailable."));
        var resolution = await grantTargets.ResolveAsync(host, selection, deadline.Token)
            .WaitAsync(deadline.Token).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow) throw new OperationCanceledException();
        if (resolution is null || !Enum.IsDefined(resolution.Status)
            || resolution.Status == StandingGrantTargetResolutionStatus.Unavailable)
            return new(null, InteractionInvocationResult.Unavailable(
                "SERVICE_AUTHORITY_UNAVAILABLE", "Current service authority is unavailable."));
        if (resolution.Status != StandingGrantTargetResolutionStatus.Available || resolution.Target is null)
            return new(null, InteractionInvocationResult.Failed(
                "INVOCATION_NOT_AUTHORIZED", "The service is not authorized for this scope."));
        if (resolution.Target.DefinitionId != selection.DefinitionId
            || resolution.Target.Kind != selection.Kind
            || resolution.Target.Revision != selection.Revision
            || resolution.Target.ContentFingerprint != selection.ContentFingerprint
            || resolution.Target.OwnerApplicationId != host.ApplicationRevision.ApplicationId
            || resolution.Target.Candidate is not null
            || resolution.Target.RetainedActivation is not null)
            return new(null, InteractionInvocationResult.Unavailable(
                "SERVICE_AUTHORITY_UNAVAILABLE", "The selected service authority is unavailable."));
        if (initial is not null && initial.Target != resolution.Target)
            return AuthorityChanged();
        var requirement = Requirement(host, resolution.Target, capability);
        var decision = await standingGrants.EvaluateAsync(
            host,
            requirement,
            deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow) throw new OperationCanceledException();
        if (!ExactAllowedDecision(host, resolution.Target, capability, decision))
            return new(null, InteractionInvocationResult.Failed(
                "INVOCATION_NOT_AUTHORIZED", "The service is not authorized for this scope."));
        var authorized = new ServiceAuthorization(resolution.Target, GrantIdentity.From(decision.Grant!));
        if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        return initial is not null && initial.Grant != authorized.Grant
            ? AuthorityChanged()
            : new(authorized, null);
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
            || decision.Evidence.Scope != host.StateSpaceId
            || decision.Evidence.CorrelationId != host.CommandId)
            return false;
        var grant = decision.Grant;
        try { StandingGrantContractRules.ValidateConfiguration(grant); }
        catch (InteractionContractException) { return false; }
        return grant.GrantReference == host.GrantReference
            && grant.PrincipalReference == host.Principal.PrincipalId
            && grant.ApplicationId == host.ApplicationRevision.ApplicationId
            && grant.Scope == StandingGrantScope.StateSpace
            && grant.StateSpaceId == host.StateSpaceId
            && !grant.Revoked
            && grant.ExpiresAtUtc > DateTime.UtcNow
            && host.Budget.DeadlineUtc <= grant.ExpiresAtUtc
            && host.Budget.MaximumOperations <= grant.MaximumOperations
            && grant.Capabilities.Contains(capability)
            && StandingGrantContractRules.MatchesDefinitionAllowance(
                host.ApplicationRevision.ApplicationId, grant.Definitions, target);
    }

    private static ServiceAuthorizationResult AuthorityChanged() => new(null,
        InteractionInvocationResult.Failed("INVOCATION_AUTHORITY_CHANGED",
            "The service authority changed before the result could be returned."));

    private sealed record ServiceAuthorizationResult(
        ServiceAuthorization? Authorization, InteractionInvocationResult? Failure);

    private sealed record ServiceAuthorization(StandingGrantDefinitionTarget Target, GrantIdentity Grant);

    private sealed record GrantIdentity(
        string GrantReference, string GrantId, int Revision, string ContentFingerprint,
        string PrincipalReference, string ApplicationId, StandingGrantScope Scope, string? StateSpaceId)
    {
        internal static GrantIdentity From(StandingGrantRevision grant) => new(
            grant.GrantReference, grant.GrantId, grant.Revision, grant.ContentFingerprint,
            grant.PrincipalReference, grant.ApplicationId.Value, grant.Scope, grant.StateSpaceId);
    }

    private InteractionInvocationResult? CurrentScopeFailure(InteractionInvocationHost host)
    {
        if (host.StateSpaceId is not { } stateSpaceId || host.StateRevision is null)
            return InteractionInvocationResult.Failed(
                "INVOCATION_STATE_SCOPE_REQUIRED", "The service requires a state scope.");
        var state = stateSpaces.Get(stateSpaceId);
        return state is not null
               && state.ApplicationRevision.ApplicationId == host.ApplicationRevision.ApplicationId
               && state.ApplicationRevision.Revision == host.ApplicationRevision.Revision
               && state.ApplicationRevision.Fingerprint == host.ApplicationRevision.Fingerprint
               && state.ApplicationRevision.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications)
               && InteractionStateRevision.From(state) == host.StateRevision
            ? null
            : InteractionInvocationResult.Failed(
                "INVOCATION_SCOPE_STALE", "The requested state scope is no longer current.");
    }

    private static InteractionInvocationResult? DeadlineFailure(
        InteractionInvocationHost host,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return InteractionInvocationResult.Cancelled(
                "INVOCATION_CANCELLED", "The service invocation was cancelled.");
        return host.Budget.DeadlineUtc <= DateTime.UtcNow
            ? InteractionInvocationResult.Cancelled(
                "INVOCATION_DEADLINE_EXCEEDED", "The invocation deadline elapsed.")
            : null;
    }

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

    private static ExecutionLimits EffectiveLimits(ExecutionLimits requested, InteractionInvocationHost host)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (requested.MaxStatements <= 0 || requested.Timeout <= TimeSpan.Zero
            || requested.MemoryBytes <= 0 || requested.MaxRecursionDepth <= 0
            || requested.MaxLogLines < 0)
            throw new InteractionContractException(
                "INVALID_SERVICE_LIMITS", "The service computation limits are invalid.");
        var remaining = host.Budget.DeadlineUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) throw new OperationCanceledException();
        var ceiling = ExecutionLimits.ReadModel;
        var timeout = requested.Timeout < ceiling.Timeout ? requested.Timeout : ceiling.Timeout;
        if (remaining < timeout) timeout = remaining;
        return requested with
        {
            MaxStatements = Math.Min(requested.MaxStatements, ceiling.MaxStatements),
            Timeout = timeout,
            MemoryBytes = Math.Min(requested.MemoryBytes, ceiling.MemoryBytes),
            MaxRecursionDepth = Math.Min(requested.MaxRecursionDepth, ceiling.MaxRecursionDepth),
            MaxEffects = 0,
            MaxEvents = 0,
            MaxNotifications = 0,
            MaxLogLines = Math.Min(requested.MaxLogLines, ceiling.MaxLogLines)
        };
    }

    private static string CanonicalObject(string json) => InteractionCanonicalJson.CanonicalizeObject(json);

    private static InteractionInvocationResult AttachPrevious(
        InteractionInvocationResult result,
        IReadOnlyList<InteractionInvocationCommitReceipt> previousCommits)
    {
        if (previousCommits.Count == 0) return result;
        return result.Tag switch
        {
            InteractionInvocationResultTag.Failed => InteractionInvocationResult.Failed(
                result.Code, result.SafeMessage, previousCommits.Concat(result.PreviousCommits).ToArray(), result.RecoveryIdentity),
            InteractionInvocationResultTag.Cancelled => InteractionInvocationResult.Cancelled(
                result.Code, result.SafeMessage, previousCommits.Concat(result.PreviousCommits).ToArray(), result.RecoveryIdentity),
            InteractionInvocationResultTag.Unavailable => InteractionInvocationResult.Failed(
                "SERVICE_SEQUENCE_INCOMPLETE", "The service became unavailable after earlier commits.",
                previousCommits, result.RecoveryIdentity),
            _ => InteractionInvocationResult.Failed(
                "SERVICE_RESULT_INVALID", "The service returned an invalid result after earlier commits.", previousCommits)
        };
    }

    private sealed record RetainedMechanic(
        CatalogRecordView Record,
        string RequirementsJson,
        string Source);

    private sealed class ServiceUnavailableException : Exception { }

    private sealed class ExchangedDataBudget
    {
        private int _bytes;

        public void Add(string canonicalJson)
        {
            var bytes = Encoding.UTF8.GetByteCount(canonicalJson);
            if (bytes > ApplicationReadOnlyServiceLimits.MaximumExchangedBytesPerRoot - _bytes)
                throw new InteractionContractException(
                    "SERVICE_EXCHANGE_LIMIT", "The service exchanged-data allowance is exhausted.");
            _bytes += bytes;
        }
    }

    private interface IInvocationCapabilityState
    {
        InteractionInvocationResult? TerminalResult { get; }
        IReadOnlyList<object> ReadEvidence { get; }
        IReadOnlyList<object> ActionEvidence { get; }
        IReadOnlyList<InteractionInvocationCommitReceipt> PreviousCommits { get; }
    }

    private sealed class InvocationCapabilities(
        InteractionInvocationHost host,
        ApplicationReadOnlyServiceDefinition definition,
        IReadOnlyDictionary<string, string> hostRoles,
        IStandingGrantApplicationReadModelInvocationAdapter reads,
        IBoundedJsonSchemaValidator schemas,
        IStateSpaceRegistry stateSpaces,
        ApplicationServiceProgressChannel progress,
        ExchangedDataBudget exchange) : IApplicationReadOnlyServiceCapabilities,
        IApplicationReadOnlyServiceProgressAttemptSink, IInvocationCapabilityState
    {
        private readonly List<object> _readEvidence = [];
        private InteractionInvocationResult? _terminal;
        private int _progressAttempts;

        public InteractionInvocationResult? TerminalResult => _terminal;
        public IReadOnlyList<object> ReadEvidence => _readEvidence.AsReadOnly();
        public IReadOnlyList<object> ActionEvidence => [];
        public IReadOnlyList<InteractionInvocationCommitReceipt> PreviousCommits => [];

        public async Task<InteractionInvocationResult> ReadAsync(
            string alias,
            string inputJson,
            CancellationToken cancellationToken = default)
        {
            // Terminal callbacks return the same failure without dispatch or another operation
            // debit. They remain bounded by the root interpreter statement/deadline limits.
            if (_terminal is not null) return _terminal;
            try
            {
                var declaration = definition.Reads.SingleOrDefault(
                    value => string.Equals(value.Alias, alias, StringComparison.Ordinal));
                if (declaration is null)
                    return SetTerminal(InteractionInvocationResult.Failed(
                        "SERVICE_READ_ALIAS_INVALID", "The service read alias is not declared."));
                var scope = ScopeFailure();
                if (scope is not null) return SetTerminal(scope);
                var input = CanonicalObject(inputJson);
                exchange.Add(input);

                var roles = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var (queryRole, hostRole) in declaration.RoleMappings)
                {
                    if (!hostRoles.TryGetValue(hostRole, out var entityId))
                        return SetTerminal(InteractionInvocationResult.Failed(
                            "SERVICE_ROLE_BINDING_MISSING", "A trusted service role binding is missing."));
                    roles.Add(queryRole, entityId);
                }

                using var deadline = DeadlineToken(host, cancellationToken);
                var task = reads.ReadAsync(new(
                    host,
                    declaration.QualifiedQueryId,
                    declaration.Contract,
                    roles,
                    input), deadline.Token);
                var result = await task.WaitAsync(deadline.Token).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (host.Budget.DeadlineUtc <= DateTime.UtcNow) throw new OperationCanceledException();
                var afterScope = ScopeFailure();
                if (afterScope is not null) return SetTerminal(afterScope);
                if (result.Tag != InteractionInvocationResultTag.Completed)
                    return SetTerminal(result.Tag is InteractionInvocationResultTag.Failed
                            or InteractionInvocationResultTag.Cancelled
                            or InteractionInvocationResultTag.Unavailable
                        ? result
                        : InteractionInvocationResult.Failed(
                            "SERVICE_READ_RESULT_INVALID", "The service read returned an invalid result kind."));
                if (result.ReadEvidence is null || result.DataJson is null
                    || result.ReadEvidence.OutputSchemaHash != declaration.Contract.OutputSchemaHash)
                    return SetTerminal(InteractionInvocationResult.Failed(
                        "SERVICE_READ_RESULT_INVALID", "The service read returned invalid evidence."));
                var canonicalResult = InteractionCanonicalJson.CanonicalizeObject(result.ToJson());
                exchange.Add(canonicalResult);
                if (schemas.Validate(declaration.Contract.OutputSchemaJson, result.DataJson).Status
                    != SchemaValueStatus.Valid)
                    return SetTerminal(InteractionInvocationResult.Failed(
                        "SERVICE_READ_OUTPUT_INVALID", "The service read output is invalid."));
                _readEvidence.Add(new
                {
                    declaration.Alias,
                    inputFingerprint = InteractionCanonicalJson.Fingerprint(
                        "dantes-roleplay/application-read-only-service/read-input/v1", input),
                    result.ReadEvidence.StateSpaceFingerprint,
                    result.ReadEvidence.ResolutionFingerprint,
                    result.ReadEvidence.OutputSchemaHash,
                    result.ReadEvidence.ResultFingerprint,
                    result.ReadEvidence.SourceRevisionFingerprint
                });
                return result;
            }
            catch (OperationCanceledException)
            {
                return SetTerminal(InteractionInvocationResult.Cancelled(
                    "SERVICE_READ_CANCELLED", "The service read was cancelled."));
            }
            catch (InteractionContractException exception)
            {
                return SetTerminal(InteractionInvocationResult.Failed(
                    exception.Code, "The service read request is invalid."));
            }
            catch
            {
                return SetTerminal(InteractionInvocationResult.Unavailable(
                    "SERVICE_READ_UNAVAILABLE", "The service read is unavailable."));
            }
        }

        public ApplicationServiceProgressDisposition TryWriteProgress(string dataJson)
        {
            BeginProgressAttempt();
            return WriteProgress(dataJson);
        }

        public void BeginProgressAttempt()
        {
            if (_progressAttempts >= ApplicationReadOnlyServiceLimits.MaximumProgressFrames)
            {
                SetTerminal(InteractionInvocationResult.Failed(
                    "SERVICE_PROGRESS_LIMIT", "The service progress attempt allowance is exhausted."));
                throw new InteractionContractException(
                    "SERVICE_PROGRESS_LIMIT", "The service progress attempt allowance is exhausted.");
            }
            _progressAttempts++;
        }

        public ApplicationServiceProgressDisposition WriteProgress(string dataJson)
        {
            try
            {
                var data = CanonicalObject(dataJson);
                exchange.Add(data);
                return progress.TryWrite(data);
            }
            catch (InteractionContractException exception)
            {
                SetTerminal(InteractionInvocationResult.Failed(
                    exception.Code, "The service progress request is invalid."));
                throw;
            }
            catch
            {
                SetTerminal(InteractionInvocationResult.Unavailable(
                    "SERVICE_PROGRESS_UNAVAILABLE", "Service progress is unavailable."));
                throw;
            }
        }

        internal InteractionInvocationResult? ScopeFailure()
        {
            if (host.StateSpaceId is not { } stateSpaceId || host.StateRevision is null)
                return InteractionInvocationResult.Failed(
                    "INVOCATION_STATE_SCOPE_REQUIRED", "The service read requires a state scope.");
            var state = stateSpaces.Get(stateSpaceId);
            return state is not null
                   && state.ApplicationRevision.ApplicationId == host.ApplicationRevision.ApplicationId
                   && state.ApplicationRevision.Revision == host.ApplicationRevision.Revision
                   && state.ApplicationRevision.Fingerprint == host.ApplicationRevision.Fingerprint
                   && state.ApplicationRevision.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications)
                   && InteractionStateRevision.From(state) == host.StateRevision
                ? null
                : InteractionInvocationResult.Failed(
                    "INVOCATION_SCOPE_STALE", "The requested state scope is no longer current.");
        }

        internal InteractionInvocationResult SetTerminal(InteractionInvocationResult result)
        {
            _terminal ??= result;
            progress.Complete();
            return _terminal;
        }
    }

    private sealed class WorkflowInvocationCapabilities(
        InvocationCapabilities readCapabilities,
        InteractionInvocationHost host,
        SystemTaskSelectedDefinition rootDefinition,
        ApplicationReadOnlyServiceDefinition definition,
        IReadOnlyDictionary<string, string> hostRoles,
        ApplicationActionInvocationAdapter actions,
        ExchangedDataBudget exchange) : IApplicationActionServiceCapabilities,
        IApplicationReadOnlyServiceProgressAttemptSink, IInvocationCapabilityState
    {
        private readonly List<object> _actionEvidence = [];
        private readonly List<InteractionInvocationCommitReceipt> _previousCommits = [];
        private InteractionInvocationResult? _terminal;
        private int _actionAttempts;

        public InteractionInvocationResult? TerminalResult => _terminal ?? readCapabilities.TerminalResult;
        public IReadOnlyList<object> ReadEvidence => readCapabilities.ReadEvidence;
        public IReadOnlyList<object> ActionEvidence => _actionEvidence.AsReadOnly();
        public IReadOnlyList<InteractionInvocationCommitReceipt> PreviousCommits => _previousCommits.AsReadOnly();

        public Task<InteractionInvocationResult> ReadAsync(
            string alias,
            string inputJson,
            CancellationToken cancellationToken = default) =>
            TerminalResult is { } terminal
                ? Task.FromResult(terminal)
                : readCapabilities.ReadAsync(alias, inputJson, cancellationToken);

        public async Task<InteractionInvocationResult> ActionAsync(
            string alias,
            string inputJson,
            CancellationToken cancellationToken = default)
        {
            if (TerminalResult is { } terminal) return terminal;
            try
            {
                if (_actionAttempts >= ApplicationReadOnlyServiceLimits.MaximumActions)
                    return SetTerminal(InteractionInvocationResult.Failed(
                        "SERVICE_ACTION_LIMIT", "The service action attempt allowance is exhausted.", _previousCommits));
                _actionAttempts++;
                var declaration = definition.Actions.SingleOrDefault(
                    value => string.Equals(value.Alias, alias, StringComparison.Ordinal));
                if (declaration is null)
                    return SetTerminal(InteractionInvocationResult.Failed(
                        "SERVICE_ACTION_ALIAS_INVALID", "The service action alias is not declared.", _previousCommits));
                var scope = readCapabilities.ScopeFailure();
                if (scope is not null) return SetTerminal(WithPreviousCommits(scope));
                var input = CanonicalObject(inputJson);
                exchange.Add(input);
                var roles = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var (actionRole, hostRole) in declaration.RoleMappings)
                {
                    if (!hostRoles.TryGetValue(hostRole, out var entityId))
                        return SetTerminal(InteractionInvocationResult.Failed(
                            "SERVICE_ROLE_BINDING_MISSING", "A trusted service role binding is missing.", _previousCommits));
                    roles.Add(actionRole, entityId);
                }

                var commandMaterial = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
                {
                    parentCommandId = host.CommandId,
                    ordinal = _actionAttempts,
                    rootDefinition.ExactDefinitionId,
                    rootDefinition.Version,
                    rootDefinition.Fingerprint,
                    declaration.QualifiedMechanicId,
                    declaration.MechanicVersion,
                    declaration.ContentFingerprint,
                    roles,
                    input
                }));
                var childCommand = InteractionCanonicalJson.Fingerprint(
                    "dantes-roleplay/application-workflow-service/child-command/v1", commandMaterial)[..32]
                    .ToLowerInvariant();
                var childHost = new InteractionInvocationHost(
                    host.Principal,
                    host.ApplicationRevision,
                    host.StateSpaceId!,
                    host.GrantReference,
                    childCommand,
                    host.StateRevision!,
                    InteractionExecutionProfile.Atomic,
                    host.Budget,
                    host.CommandId);
                var result = await actions.ExecuteWorkflowChildAsync(new(
                    childHost,
                    declaration.QualifiedMechanicId,
                    declaration.MechanicVersion,
                    declaration.ContentFingerprint,
                    roles,
                    input), cancellationToken).ConfigureAwait(false);
                if (result.Tag == InteractionInvocationResultTag.Committed && result.Receipt is not null)
                {
                    // Record commit evidence before any subsequent bound/check operation can fail.
                    _previousCommits.Add(result.Receipt);
                    _actionEvidence.Add(new
                    {
                        declaration.Alias,
                        inputFingerprint = InteractionCanonicalJson.Fingerprint(
                            "dantes-roleplay/application-workflow-service/action-input/v1", input),
                        result.Receipt.OperationId,
                        result.Receipt.RequestFingerprint,
                        effectCount = result.Receipt.Effects.Count,
                        result.Receipt.EffectDetailsAvailable
                    });
                    exchange.Add(InteractionCanonicalJson.CanonicalizeObject(result.ToJson()));
                    var committedScope = readCapabilities.ScopeFailure();
                    return committedScope is null ? result : SetTerminal(WithPreviousCommits(committedScope));
                }
                var afterScope = readCapabilities.ScopeFailure();
                if (afterScope is not null) return SetTerminal(WithPreviousCommits(afterScope));
                exchange.Add(InteractionCanonicalJson.CanonicalizeObject(result.ToJson()));
                return SetTerminal(WithPreviousCommits(result));
            }
            catch (OperationCanceledException)
            {
                return SetTerminal(InteractionInvocationResult.Cancelled(
                    "SERVICE_ACTION_CANCELLED", "The service action was cancelled.", _previousCommits));
            }
            catch (InteractionContractException exception)
            {
                return SetTerminal(InteractionInvocationResult.Failed(
                    exception.Code, "The service action request is invalid.", _previousCommits));
            }
            catch
            {
                return SetTerminal(InteractionInvocationResult.Failed(
                    "SERVICE_ACTION_UNAVAILABLE", "The service action is unavailable.", _previousCommits));
            }
        }

        public ApplicationServiceProgressDisposition TryWriteProgress(string dataJson) =>
            TerminalResult is null
                ? readCapabilities.TryWriteProgress(dataJson)
                : ApplicationServiceProgressDisposition.Closed;

        public void BeginProgressAttempt() => readCapabilities.BeginProgressAttempt();

        public ApplicationServiceProgressDisposition WriteProgress(string dataJson) =>
            TerminalResult is null
                ? readCapabilities.WriteProgress(dataJson)
                : ApplicationServiceProgressDisposition.Closed;

        private InteractionInvocationResult WithPreviousCommits(InteractionInvocationResult result)
        {
            var prior = _previousCommits.Concat(result.PreviousCommits).ToArray();
            return result.Tag switch
            {
                InteractionInvocationResultTag.Failed => InteractionInvocationResult.Failed(
                    result.Code, result.SafeMessage, prior, result.RecoveryIdentity),
                InteractionInvocationResultTag.Cancelled => InteractionInvocationResult.Cancelled(
                    result.Code, result.SafeMessage, prior, result.RecoveryIdentity),
                InteractionInvocationResultTag.Unavailable when prior.Length == 0 => result,
                InteractionInvocationResultTag.Unavailable => InteractionInvocationResult.Failed(
                    "SERVICE_ACTION_SEQUENCE_INCOMPLETE",
                    "A workflow action outcome is unavailable after earlier commits.", prior, result.RecoveryIdentity),
                _ => InteractionInvocationResult.Failed(
                    "SERVICE_ACTION_RESULT_INVALID", "The service action returned an invalid result kind.", prior)
            };
        }

        private InteractionInvocationResult SetTerminal(InteractionInvocationResult result)
        {
            _terminal ??= result;
            readCapabilities.SetTerminal(_terminal);
            return _terminal;
        }
    }

    private sealed record ServiceInvocation(
        InteractionInvocationHost Host,
        SystemTaskSelectedDefinition SelectedDefinition,
        ApplicationReadOnlyServiceDefinition Definition,
        IReadOnlyDictionary<string, string> HostRoleBindings,
        string InputJson,
        ExecutionLimits ComputationLimits,
        ApplicationServiceProgressChannel? Progress)
    {
        internal static ServiceInvocation From(ApplicationReadOnlyServiceInvocationRequest request) =>
            new(request.Host, request.SelectedDefinition, request.Definition, request.HostRoleBindings,
                request.InputJson, request.ComputationLimits, request.Progress);

        internal static ServiceInvocation From(ApplicationWorkflowServiceInvocationRequest request) =>
            new(request.Host, request.SelectedDefinition, request.Definition, request.HostRoleBindings,
                request.InputJson, request.ComputationLimits, request.Progress);
    }

    private static StandingGrantRequirement Requirement(
        InteractionInvocationHost host,
        StandingGrantDefinitionTarget target,
        StandingGrantCapability capability)
    {
        var requirement = new StandingGrantRequirement(
            capability,
            StandingGrantScope.StateSpace,
            [target],
            []);
        StandingGrantContractRules.ValidateRequirement(host, requirement);
        return requirement;
    }
}
