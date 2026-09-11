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

/// <summary>
/// Executes one exact retained read-only service mechanic. This owner is intentionally unregistered
/// until the standing-grant read adapter and host integration are accepted together.
/// </summary>
internal sealed class ApplicationReadOnlyServiceInvocationAdapter(
    IPublicApplicationCatalogProvider catalogs,
    IApplicationReadOnlyServiceDefinitionReader definitionReader,
    IStandingGrantApplicationReadModelInvocationAdapter reads,
    IBoundedJsonSchemaValidator schemas,
    JintMechanicEngine mechanics,
    IStateSpaceRegistry stateSpaces,
    IStandingGrantTargetResolver grantTargets,
    IStandingGrantPolicy standingGrants) : IApplicationReadOnlyServiceInvocationAdapter
{
    public async Task<InteractionInvocationResult> InvokeAsync(
        ApplicationReadOnlyServiceInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ApplicationServiceProgressChannel? progress = null;
        var ownsProgress = false;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            progress = request.Progress ?? new ApplicationServiceProgressChannel();
            if (!progress.TryBind())
                return InteractionInvocationResult.Failed(
                    "SERVICE_PROGRESS_ALREADY_BOUND", "The progress outlet is already bound.");
            ownsProgress = true;
            if (request.Progress is null) progress.Complete();

            if (request.Host.Profile != InteractionExecutionProfile.ReadOnly)
                return InteractionInvocationResult.Unavailable(
                    "SERVICE_PROFILE_UNAVAILABLE", "The requested service profile is unavailable.");
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
                request.Host, rootSelection, cancellationToken).ConfigureAwait(false);
            if (rootAuthorization.Failure is not null) return rootAuthorization.Failure;

            var retained = ResolveRetained(request);
            var retainedDefinition = definitionReader.ReadRetained(request.SelectedDefinition, retained.Record);
            if (!string.Equals(retainedDefinition.ToJson(), request.Definition.ToJson(), StringComparison.Ordinal))
                return InteractionInvocationResult.Failed(
                    "SERVICE_DECLARATION_STALE", "The service declaration is no longer current.");
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
            var capabilities = new InvocationCapabilities(
                request.Host,
                retainedDefinition,
                request.HostRoleBindings,
                reads,
                schemas,
                stateSpaces,
                progress,
                exchange);

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
                    new MechanicProjection { StateSpaceId = request.Host.StateSpaceId, Input = input, Seed = seed },
                    limits,
                    capabilities,
                    cancellationToken).GetAwaiter().GetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(priorContext);
            }

            if (capabilities.TerminalResult is { } terminal) return terminal;
            deadline = DeadlineFailure(request.Host, cancellationToken);
            if (deadline is not null) return deadline;
            scope = CurrentScopeFailure(request.Host);
            if (scope is not null) return scope;
            rootAuthorization = await AuthorizeAsync(
                request.Host, rootSelection, cancellationToken, rootAuthorization.Authorization).ConfigureAwait(false);
            if (rootAuthorization.Failure is not null) return rootAuthorization.Failure;
            if (!run.Ok)
                return run.LimitHit is "cancelled" && cancellationToken.IsCancellationRequested
                    ? InteractionInvocationResult.Cancelled(
                        "INVOCATION_CANCELLED", "The service computation was cancelled.")
                    : InteractionInvocationResult.Failed(
                        "SERVICE_COMPUTATION_FAILED", "The service computation could not complete.");
            if (run.Output.Effects.Count != 0 || run.Output.Events.Count != 0
                || run.Output.Notifications.Count != 0)
                return InteractionInvocationResult.Failed(
                    "SERVICE_EFFECTS_FORBIDDEN", "A read-only service cannot produce effects or announcements.");
            if (!run.Output.HasData)
                return InteractionInvocationResult.Failed(
                    "SERVICE_OUTPUT_REQUIRED", "The service did not produce data.");
            var output = CanonicalObject(run.Output.Data);
            exchange.Add(output);
            if (schemas.Validate(retainedDefinition.OutputSchemaJson, output).Status != SchemaValueStatus.Valid)
                return InteractionInvocationResult.Failed(
                    "SERVICE_OUTPUT_INVALID", "The service output does not match its declared schema.");

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
                    reads = capabilities.ReadEvidence
                })));
            return InteractionInvocationResult.CompletedComputation(
                output, "service-process-local." + evidence.ToLowerInvariant());
        }
        catch (OperationCanceledException)
        {
            return InteractionInvocationResult.Cancelled(
                "INVOCATION_CANCELLED", "The service invocation was cancelled.");
        }
        catch (InteractionContractException exception)
        {
            return InteractionInvocationResult.Failed(exception.Code, "The service invocation is invalid.");
        }
        catch (ApplicationReadModelException exception)
        {
            return InteractionInvocationResult.Failed(exception.Code, "The service read could not complete.");
        }
        catch
        {
            return InteractionInvocationResult.Unavailable(
                "SERVICE_RUNTIME_UNAVAILABLE", "The read-only service runtime is unavailable.");
        }
        finally
        {
            if (ownsProgress) progress!.Complete();
        }
    }

    private RetainedMechanic ResolveRetained(ApplicationReadOnlyServiceInvocationRequest request)
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
        CancellationToken cancellationToken,
        ServiceAuthorization? initial = null)
    {
        using var deadline = DeadlineToken(host, cancellationToken);
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
            || resolution.Target.OwnerApplicationId != host.ApplicationRevision.ApplicationId)
            return new(null, InteractionInvocationResult.Unavailable(
                "SERVICE_AUTHORITY_UNAVAILABLE", "The selected service authority is unavailable."));
        if (initial is not null && initial.Target != resolution.Target)
            return AuthorityChanged();
        var requirement = ReadRequirement(host, resolution.Target);
        var decision = await standingGrants.EvaluateAsync(
            host,
            requirement,
            deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow) throw new OperationCanceledException();
        if (!ExactAllowedDecision(host, resolution.Target, decision))
            return new(null, InteractionInvocationResult.Failed(
                "INVOCATION_NOT_AUTHORIZED", "The service is not authorized for this scope."));
        var authorized = new ServiceAuthorization(resolution.Target, GrantIdentity.From(decision.Grant!));
        return initial is not null && initial.Grant != authorized.Grant
            ? AuthorityChanged()
            : new(authorized, null);
    }

    private static bool ExactAllowedDecision(
        InteractionInvocationHost host,
        StandingGrantDefinitionTarget target,
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
            && grant.Capabilities.Contains(StandingGrantCapability.Read)
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
        var state = stateSpaces.Get(host.StateSpaceId);
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

    private sealed class InvocationCapabilities(
        InteractionInvocationHost host,
        ApplicationReadOnlyServiceDefinition definition,
        IReadOnlyDictionary<string, string> hostRoles,
        IStandingGrantApplicationReadModelInvocationAdapter reads,
        IBoundedJsonSchemaValidator schemas,
        IStateSpaceRegistry stateSpaces,
        ApplicationServiceProgressChannel progress,
        ExchangedDataBudget exchange) : IApplicationReadOnlyServiceCapabilities,
        IApplicationReadOnlyServiceProgressAttemptSink
    {
        private readonly List<object> _readEvidence = [];
        private InteractionInvocationResult? _terminal;
        private int _progressAttempts;

        public InteractionInvocationResult? TerminalResult => _terminal;
        public IReadOnlyList<object> ReadEvidence => _readEvidence.AsReadOnly();

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

        private InteractionInvocationResult? ScopeFailure()
        {
            var state = stateSpaces.Get(host.StateSpaceId);
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

        private InteractionInvocationResult SetTerminal(InteractionInvocationResult result)
        {
            _terminal ??= result;
            progress.Complete();
            return _terminal;
        }
    }

    private static StandingGrantRequirement ReadRequirement(
        InteractionInvocationHost host,
        StandingGrantDefinitionTarget target)
    {
        var requirement = new StandingGrantRequirement(
            StandingGrantCapability.Read,
            StandingGrantScope.StateSpace,
            [target],
            []);
        StandingGrantContractRules.ValidateRequirement(host, requirement);
        return requirement;
    }
}
