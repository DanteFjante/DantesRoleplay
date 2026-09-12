using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Content;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>
/// Validates an owner-proved workflow candidate against current state and authority. Samples run
/// through any read-only prefix and stop at their first action or durable-job boundary. The
/// returned proposal evidence is validation-only: it never carries a receipt, task handle, or
/// operation identity and is not publication approval by itself.
/// </summary>
internal sealed class ApplicationCandidateWorkflowRuntimeValidator(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    ApplicationCandidateWorkflowReviewClosureReader reviewClosures,
    IPublicApplicationCatalogProvider catalogs,
    IStateSpaceRegistry stateSpaces,
    IApplicationMechanicProjectionMappingResolver mappings,
    ApplicationMechanicEvaluator evaluator,
    ApplicationActionRunner actionRunner,
    IApplicationEcsEffectApplier effects,
    IStandingGrantReadCandidateReader grantCandidates,
    IStandingGrantTargetResolver targets,
    IStandingGrantPolicy grants,
    IStandingGrantApplicationReadModelInvocationAdapter reads,
    IBoundedJsonSchemaValidator schemas,
    JintMechanicEngine engine)
{
    private readonly IStandingGrantApplicationReadModelInvocationAdapter serviceReads = reads;

    internal const string PolicyVersion = "workflow-service-candidate-runtime-v1";

    internal static readonly string PolicyFingerprint = InteractionCanonicalJson.Fingerprint(
        "dantes-roleplay/workflow-service-candidate-runtime-policy/v1",
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            coverageVersion = PolicyVersion,
            closureGrammar = ApplicationCandidateWorkflowReviewClosureReader.GrammarVersion,
            schemaProfile = SystemJsonSchemaProfile.Id,
            requestBytes = InteractionContractLimits.JsonBytes,
            requestDepth = InteractionContractLimits.JsonDepth,
            samplesPerDefinition = ApplicationAuthoringLimits.SamplesPerDefinition,
            samplesPerValidation = ApplicationAuthoringLimits.SamplesPerValidation,
            exchangedBytes = ApplicationReadOnlyServiceLimits.MaximumExchangedBytesPerRoot,
            execution = new
            {
                ExecutionLimits.ReadModel.MaxStatements,
                timeoutTicks = ExecutionLimits.ReadModel.Timeout.Ticks,
                ExecutionLimits.ReadModel.MemoryBytes,
                ExecutionLimits.ReadModel.MaxRecursionDepth,
                ExecutionLimits.ReadModel.MaxEffects,
                ExecutionLimits.ReadModel.MaxEvents,
                ExecutionLimits.ReadModel.MaxNotifications,
                ExecutionLimits.ReadModel.MaxLogLines
            },
            engine = JintMechanicEngine.ServiceExecutionPolicyFingerprint,
            boundary = "first-action-or-job",
            action = "typed-ecs-dry-run",
            job = "exact-procedure-no-enqueue"
        })));

    internal async Task<ApplicationCandidateRuntimeReport> ValidateAsync(
        ApplicationCandidateValidationRequest request,
        InteractionInvocationHost authoringHost,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authoringHost);
        var results = new List<ApplicationCandidateRuntimeSampleResult>();
        try
        {
            var samples = BoundSamples(request);
            if (db.Database.CurrentTransaction is not null || samples.Count == 0
                || samples.Any(sample => sample.StateSpaceId is null || sample.StateRevision is null
                    || sample.RoleEntityIds is null))
                return Unavailable(request.Candidate, null, results,
                    "WORKFLOW_RUNTIME_SAMPLE_SCOPE_REQUIRED",
                    "Workflow validation samples require exact trusted state and role bindings.");
            if (authoringHost.StateSpaceId is not null || authoringHost.StateRevision is not null
                || authoringHost.ApplicationRevision.ApplicationId != request.Candidate.ApplicationId)
                return Unavailable(request.Candidate, null, results,
                    "WORKFLOW_RUNTIME_AUTHORING_SCOPE_INVALID",
                    "Workflow candidate validation requires the exact application authoring scope.");

            var update = await ReadUpdateAsync(authoringHost, request.Candidate, cancellationToken);
            if (update is null || !catalogs.TryGet(request.Candidate.ApplicationId, out var catalog))
                return Unavailable(request.Candidate, null, results,
                    "WORKFLOW_RUNTIME_CLOSURE_UNAVAILABLE",
                    "The exact candidate and base workflow closure is unavailable.");
            if (samples.Any(sample => sample.Definition != update.Definition))
                return Invalid(request.Candidate, update.Fingerprint, results,
                    "WORKFLOW_RUNTIME_SAMPLE_TARGET_INVALID",
                    "Every workflow sample must select the exact candidate workflow definition.");
            if (!await AuthorizeCandidateAsync(authoringHost, update, cancellationToken))
                return Unavailable(request.Candidate, update.Fingerprint, results,
                    "WORKFLOW_RUNTIME_AUTHORING_AUTHORITY_UNAVAILABLE",
                    "Current candidate Read and Validate authority is unavailable.");

            var closure = ResolveClosure(update);
            if (closure is null)
                return Unavailable(request.Candidate, update.Fingerprint, results,
                    "WORKFLOW_RUNTIME_DEPENDENCY_UNAVAILABLE",
                    "A declared workflow service dependency is unavailable or stale.");

            for (var index = 0; index < samples.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sample = samples[index];
                var input = sample.InputJson;
                var expected = sample.ExpectedDataJson;
                var inputFingerprint = DataFingerprint(input);
                var expectedFingerprint = DataFingerprint(expected);
                var expectedEffectsFingerprint = sample.ExpectedEffectsJson is null
                    ? null : DataFingerprint(sample.ExpectedEffectsJson);
                var state = stateSpaces.Get(sample.StateSpaceId!);
                if (state is null || !SameRevision(state.ApplicationRevision, authoringHost.ApplicationRevision)
                    || state.ManifestFingerprint != update.Basis.ActivationFingerprint
                    || InteractionStateRevision.From(state) != sample.StateRevision)
                    return Unavailable(request.Candidate, update.Fingerprint, results,
                        "WORKFLOW_RUNTIME_STATE_STALE", "A validation state sample is no longer current.");
                if (schemas.Validate(update.Service.InputSchemaJson, input).Status != SchemaValueStatus.Valid)
                    return Invalid(request.Candidate, update.Fingerprint, results,
                        "WORKFLOW_RUNTIME_INPUT_INVALID", "A workflow sample does not satisfy its input schema.");

                var grant = await SelectGrantAsync(authoringHost, state, closure, cancellationToken);
                if (grant is null)
                    return Unavailable(request.Candidate, update.Fingerprint, results,
                        "WORKFLOW_RUNTIME_STATE_AUTHORITY_UNAVAILABLE",
                        "Current authority for every declared workflow dependency is unavailable.");
                var stateHost = StateHost(authoringHost, state, grant.GrantReference);
                if (!stateHost.Budget.TryConsumeOperation())
                    return Unavailable(request.Candidate, update.Fingerprint, results,
                        "WORKFLOW_RUNTIME_BUDGET_EXHAUSTED", "The shared validation budget is exhausted.");

                var capabilities = new ValidationCapabilities(
                    this, stateHost, update, closure, catalog, sample.RoleEntityIds!, grant);
                var seedMaterial = InteractionCanonicalJson.Fingerprint(
                    "dantes-roleplay/workflow-candidate-sample-seed/v1",
                    update.Fingerprint + "\n" + index + "\n" + inputFingerprint);
                var seed = BinaryPrimitives.ReadInt64BigEndian(Convert.FromHexString(seedMaterial[..16]));
                var remaining = authoringHost.Budget.DeadlineUtc - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) throw new OperationCanceledException();
                var limits = ExecutionLimits.ReadModel with
                {
                    Timeout = remaining < ExecutionLimits.ReadModel.Timeout
                        ? remaining : ExecutionLimits.ReadModel.Timeout
                };
                var run = await engine.RunServiceAsync(update.Source,
                    new MechanicProjection
                    {
                        StateSpaceId = state.StateSpaceId,
                        Input = input,
                        Seed = seed
                    }, limits, capabilities, cancellationToken);

                if (capabilities.Failure is not null)
                    return capabilities.Failure.Tag == InteractionInvocationResultTag.Failed
                        ? Invalid(request.Candidate, update.Fingerprint, results,
                            capabilities.Failure.Code, capabilities.Failure.SafeMessage)
                        : Unavailable(request.Candidate, update.Fingerprint, results,
                            capabilities.Failure.Code, capabilities.Failure.SafeMessage);
                if (capabilities.Proposal is { } proposal)
                {
                    var actual = proposal.OutputFingerprint;
                    if (proposal.Kind == ApplicationCandidateRuntimeProposalKind.Action
                        && (sample.ExpectedEffectsJson is null
                            || actual != expectedFingerprint
                            || proposal.ExpectedEffectsJson != InteractionCanonicalJson.Canonicalize(sample.ExpectedEffectsJson)))
                        return Invalid(request.Candidate, update.Fingerprint, results,
                            "WORKFLOW_RUNTIME_ACTION_MISMATCH",
                            "The proposed action output or typed effects do not match the retained sample.");
                    if (proposal.Kind == ApplicationCandidateRuntimeProposalKind.Job
                        && sample.ExpectedEffectsJson != "[]")
                        return Invalid(request.Candidate, update.Fingerprint, results,
                            "WORKFLOW_RUNTIME_JOB_EXPECTATION_INVALID",
                            "A durable-job boundary cannot claim immediate typed effects.");
                    results.Add(new(update.Definition, index, ApplicationCandidateRuntimeStatus.Completed, true,
                        inputFingerprint, expectedFingerprint, actual,
                        proposal.Kind == ApplicationCandidateRuntimeProposalKind.Action
                            ? ApplicationCandidateRuntimeCompletionBoundary.ProposedAction
                            : ApplicationCandidateRuntimeCompletionBoundary.ProposedJob,
                        [proposal.Evidence], capabilities.ReadEvidence, expectedEffectsFingerprint));
                    continue;
                }
                if (!run.Ok || run.Output.Effects.Count != 0 || run.Output.Events.Count != 0
                    || run.Output.Notifications.Count != 0 || !run.Output.HasData)
                    return Invalid(request.Candidate, update.Fingerprint, results,
                        "WORKFLOW_RUNTIME_EXECUTION_INVALID",
                        "The workflow did not return bounded data or reach a declared external boundary.");
                if (sample.ExpectedEffectsJson != "[]")
                    return Invalid(request.Candidate, update.Fingerprint, results,
                        "WORKFLOW_RUNTIME_OUTPUT_EXPECTATION_INVALID",
                        "A returned-data boundary cannot claim typed effects.");
                var output = InteractionCanonicalJson.CanonicalizeObject(run.Output.Data);
                if (schemas.Validate(update.Service.OutputSchemaJson, output).Status != SchemaValueStatus.Valid
                    || output != expected)
                    return Invalid(request.Candidate, update.Fingerprint, results,
                        "WORKFLOW_RUNTIME_OUTPUT_MISMATCH",
                        "The workflow output does not match its retained sample and schema.");
                results.Add(new(update.Definition, index, ApplicationCandidateRuntimeStatus.Completed, true,
                    inputFingerprint, expectedFingerprint, DataFingerprint(output),
                    ApplicationCandidateRuntimeCompletionBoundary.ReturnedOutput, null,
                    capabilities.ReadEvidence, expectedEffectsFingerprint));
            }

            return Report(ApplicationCandidateRuntimeStatus.Completed, request.Candidate,
                update.Fingerprint, results, closure.Dependencies);
        }
        catch (OperationCanceledException)
        {
            return Unavailable(request.Candidate, null, results,
                "WORKFLOW_RUNTIME_CANCELLED", "Workflow validation ended before all samples completed.");
        }
        catch (InteractionContractException error)
        {
            return Invalid(request.Candidate, null, results, error.Code,
                "The workflow validation contract is invalid.");
        }
        catch (ApplicationActivationException error)
        {
            return Invalid(request.Candidate, null, results, error.Code,
                "The workflow validation samples are invalid.");
        }
        catch
        {
            return Unavailable(request.Candidate, null, results,
                "WORKFLOW_RUNTIME_UNAVAILABLE", "Workflow candidate validation is unavailable.");
        }
    }

    internal async Task<bool> CurrentAsync(
        ApplicationCandidateValidationRequest request,
        ApplicationCandidateRuntimeReport report,
        string expectedReportFingerprint,
        InteractionInvocationHost authoringHost,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ApplicationCandidateValidationSample> samples;
        try { samples = BoundSamples(request); }
        catch (Exception error) when (error is InteractionContractException
            or ApplicationActivationException or JsonException) { return false; }
        if (ReportFingerprint(report) != expectedReportFingerprint
            || report.Status != ApplicationCandidateRuntimeStatus.Completed
            || report.RuntimePolicyVersion != PolicyVersion
            || report.RuntimePolicyFingerprint != PolicyFingerprint
            || report.Candidate != request.Candidate || report.Dependencies is null
            || report.Samples.Count != samples.Count) return false;
        var update = await ReadUpdateAsync(authoringHost, request.Candidate, cancellationToken);
        if (update is null || report.SelectionEvidenceFingerprint != update.Fingerprint
            || !catalogs.TryGet(request.Candidate.ApplicationId, out _)
            || !await AuthorizeCandidateAsync(authoringHost, update, cancellationToken)
            || samples.Any(sample => sample.Definition != update.Definition)) return false;
        var closure = ResolveClosure(update);
        if (closure is null || !closure.Dependencies.SequenceEqual(report.Dependencies)) return false;
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            var retained = report.Samples[index];
            if (retained.Outcome != ApplicationCandidateRuntimeStatus.Completed || !retained.Attempted
                || retained.Definition != sample.Definition || retained.SampleIndex != index
                || retained.InputFingerprint != DataFingerprint(sample.InputJson)
                || retained.ExpectedDataFingerprint != DataFingerprint(sample.ExpectedDataJson)
                || retained.ExpectedEffectsFingerprint != (sample.ExpectedEffectsJson is null
                    ? null : DataFingerprint(sample.ExpectedEffectsJson))) return false;
            var state = sample.StateSpaceId is null ? null : stateSpaces.Get(sample.StateSpaceId);
            if (state is null || !SameRevision(state.ApplicationRevision, authoringHost.ApplicationRevision)
                || state.ManifestFingerprint != update.Basis.ActivationFingerprint
                || sample.StateRevision != InteractionStateRevision.From(state)
                || await SelectGrantAsync(authoringHost, state, closure, cancellationToken) is null) return false;
        }
        return true;
    }

    internal static string ReportFingerprint(ApplicationCandidateRuntimeReport report) =>
        InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/workflow-candidate-runtime-report/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(report)));

    private async Task<ApplicationCandidateWorkflowUpdate?> ReadUpdateAsync(
        InteractionInvocationHost host,
        ApplicationCandidateReference candidate,
        CancellationToken cancellationToken)
    {
        await using var scope = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
        var selection = await new ApplicationCandidateSelectionReader(db, applications, activations, targets)
            .ReadAsync(host, candidate, cancellationToken);
        if (selection is null) return null;
        var read = await reviewClosures.ReadAsync(host, candidate, selection, cancellationToken);
        if (read is not { Status: ApplicationCandidateReviewClosureReadStatus.Available,
                Evidence: ApplicationCandidateWorkflowReviewClosureEvidence evidence }) return null;
        var sourceDocument = evidence.ReviewDocuments.SingleOrDefault(value =>
            value.Definition == evidence.Definition
            && value.Document.RelativePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase));
        if (sourceDocument is null || selection.Targets.Length != 1) return null;
        var source = new UTF8Encoding(false, true).GetString(sourceDocument.RetainedBytes.AsSpan());
        return new(candidate, evidence.Basis, evidence.Definition, evidence.Service, source,
            evidence.EvidenceFingerprint, selection.Targets[0], evidence.Dependencies);
    }

    private WorkflowClosure? ResolveClosure(ApplicationCandidateWorkflowUpdate update)
    {
        try
        {
            var dependencies = update.Dependencies.Select(Dependency).ToArray();
            var requirements = new List<AuthorityRequirement>();
            foreach (var read in update.Service.Reads)
            {
                var reference = update.Dependencies.SingleOrDefault(value =>
                    value.DefinitionId == read.QualifiedQueryId && value.Kind == ApplicationQueryContract.CatalogKind);
                if (reference is null) return null;
                requirements.Add(new(reference, StandingGrantCapability.Read));
            }
            foreach (var action in update.Service.Actions)
            {
                var reference = new StandingGrantDefinitionReference(action.QualifiedMechanicId, "mechanic",
                    action.MechanicVersion, action.ContentFingerprint);
                if (!update.Dependencies.Contains(reference)) return null;
                requirements.Add(new(reference, StandingGrantCapability.Execute));
            }
            foreach (var job in update.Service.Jobs)
            {
                var reference = new StandingGrantDefinitionReference(job.QualifiedProcedureId, "procedure",
                    job.ProcedureVersion, job.ContentFingerprint);
                if (!update.Dependencies.Contains(reference)
                    || !catalogs.TryGet(update.Candidate.ApplicationId, out var catalog)) return null;
                var record = catalog.Inspect(new(update.Candidate.ApplicationId,
                    update.Candidate.ApplicationId.Value, job.QualifiedProcedureId));
                if (ProcedureContractFingerprint(record, job) is null
                    || schemas.Compile(job.ResultSchemaJson) is not { IsAccepted: true } compiled
                    || compiled.SchemaHash != job.ResultSchemaFingerprint) return null;
                requirements.Add(new(reference, StandingGrantCapability.Execute));
            }
            return new(dependencies, requirements.Distinct().ToArray());
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or KeyNotFoundException or JsonException or InteractionContractException)
        {
            return null;
        }
    }

    private async Task<bool> AuthorizeCandidateAsync(
        InteractionInvocationHost host,
        ApplicationCandidateWorkflowUpdate update,
        CancellationToken cancellationToken)
    {
        foreach (var capability in new[] { StandingGrantCapability.Read, StandingGrantCapability.Validate })
        {
            var decision = await EvaluateAuthorityAsync(host,
                new(capability, StandingGrantScope.Application, [update.CandidateTarget], []), cancellationToken);
            if (!ExactGrant(host, decision, host.GrantReference)) return false;
        }
        return true;
    }

    private async Task<StandingGrantRevision?> SelectGrantAsync(
        InteractionInvocationHost authoringHost,
        StateSpaceView state,
        WorkflowClosure closure,
        CancellationToken cancellationToken)
    {
        var capabilities = closure.Authority.Select(value => value.Capability).ToHashSet();
        var candidates = await grantCandidates.ReadAsync(authoringHost.Principal,
            authoringHost.ApplicationRevision.ApplicationId, StandingGrantScope.StateSpace,
            state.StateSpaceId, capabilities, cancellationToken);
        if (candidates.Status != StandingGrantReadCandidateStatus.Available) return null;
        foreach (var grant in candidates.Candidates.OrderBy(value => value.GrantReference, StringComparer.Ordinal))
        {
            if (grant.Revoked || grant.ExpiresAtUtc <= DateTime.UtcNow) continue;
            var host = StateHost(authoringHost, state, grant.GrantReference);
            var valid = true;
            foreach (var requirement in closure.Authority)
            {
                var resolution = requirement.Definition.Kind == "query"
                    ? await targets.ResolveCurrentAsync(host, requirement.Definition.DefinitionId,
                        requirement.Definition.Kind, cancellationToken)
                    : await targets.ResolveAsync(host, requirement.Definition, cancellationToken);
                if (resolution is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } target }
                    || target.DefinitionId != requirement.Definition.DefinitionId
                    || target.Kind != requirement.Definition.Kind
                    || target.Revision != requirement.Definition.Revision
                    || target.ContentFingerprint != requirement.Definition.ContentFingerprint)
                {
                    valid = false;
                    break;
                }
                var decision = await EvaluateAuthorityAsync(host,
                    new(requirement.Capability, StandingGrantScope.StateSpace, [target], []), cancellationToken);
                if (!ExactGrant(host, decision, grant.GrantReference))
                {
                    valid = false;
                    break;
                }
            }
            if (valid) return grant;
        }
        return null;
    }

    private async Task<BoundaryProposal?> ProposeActionAsync(
        InteractionInvocationHost host,
        ApplicationCandidateWorkflowUpdate update,
        ICatalogNavigator catalog,
        ApplicationServiceActionDeclaration declaration,
        IReadOnlyDictionary<string, string> hostRoles,
        string input,
        StandingGrantRevision grant,
        CancellationToken cancellationToken)
    {
        if (!host.Budget.TryConsumeOperation()) return null;
        var record = catalog.Inspect(new(update.Candidate.ApplicationId,
            update.Candidate.ApplicationId.Value, declaration.QualifiedMechanicId));
        using var content = JsonDocument.Parse(record.ContentJson);
        var requirementsJson = content.RootElement.GetProperty("requirements").GetString()!;
        var requirements = MechanicRequirements.Parse(requirementsJson);
        if (requirements.Event is not null) return null;
        var roles = MapRoles(declaration.RoleMappings, hostRoles);
        if (roles is null) return null;
        var state = stateSpaces.Get(host.StateSpaceId!)!;
        var mapping = await mappings.ResolveAsync(state.StateSpaceId, update.Candidate.ApplicationId,
            declaration.QualifiedMechanicId, requirements, cancellationToken);
        if (!mapping.Resolved) return null;
        var seedFingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/workflow-candidate-action-seed/v1",
            update.Fingerprint + "\n" + declaration.Alias + "\n" + input);
        var seed = BinaryPrimitives.ReadInt64BigEndian(Convert.FromHexString(seedFingerprint[..16]));
        var evaluated = await evaluator.EvaluateCandidateAsync(new(state.StateSpaceId,
            update.Candidate.ApplicationId, declaration.QualifiedMechanicId,
            declaration.ContentFingerprint, mapping.Mapping!, roles, input, seed), catalog, cancellationToken);
        if (!evaluated.Ok || evaluated.Run is not { Ok: true } run || evaluated.Projection is null
            || evaluated.Proposal.Notifications.Count != 0 || run.Output.Notifications.Count != 0
            || !run.Output.HasData) return null;
        var built = await actionRunner.BuildEffectBatchAsync(state, mapping.Mapping!, evaluated.Projection,
            requirements, evaluated.Proposal.Append(run.Output), declaration.QualifiedMechanicId,
            declaration.MechanicVersion, seed, cancellationToken);
        if (!built.Ok || built.Batch is null) return null;
        var effectKinds = built.Batch.Effects.Select(value => value.Type).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var target = await targets.ResolveAsync(host, new(declaration.QualifiedMechanicId, "mechanic",
            declaration.MechanicVersion, declaration.ContentFingerprint), cancellationToken);
        if (target is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } exact }) return null;
        var authority = await EvaluateAuthorityAsync(host,
            new(StandingGrantCapability.Execute, StandingGrantScope.StateSpace, [exact], effectKinds), cancellationToken);
        if (!ExactGrant(host, authority, grant.GrantReference)) return null;
        var dryRun = await effects.ApplyAsync(built.Batch, dryRun: true, cancellationToken);
        if (!dryRun.Valid || !dryRun.DryRun || dryRun.Applied || dryRun.Replayed) return null;
        var output = InteractionCanonicalJson.CanonicalizeObject(run.Output.Data);
        var batchJson = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(built.Batch));
        var source = content.RootElement.GetProperty("source").GetString()!;
        var schemaMaterial = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            requirements = JsonDocument.Parse(requirementsJson).RootElement,
            built.Batch.ComponentExpectations,
            effectKinds
        }));
        var evidence = new ApplicationCandidateRuntimeProposal(
            ApplicationCandidateRuntimeProposalKind.Action, declaration.Alias,
            new(declaration.QualifiedMechanicId, "mechanic", declaration.MechanicVersion,
                declaration.ContentFingerprint), DataFingerprint(input),
            ApplicationCatalogRecordContent.Fingerprint(source), DataFingerprint(schemaMaterial),
            DataFingerprint(output), DataFingerprint(batchJson), effectKinds);
        var effectsJson = InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(built.Batch.Effects));
        return new(evidence, DataFingerprint(output), effectsJson);
    }

    private async Task<BoundaryProposal?> ProposeJobAsync(
        InteractionInvocationHost host,
        ApplicationServiceJobDeclaration declaration,
        string input,
        StandingGrantRevision grant,
        CancellationToken cancellationToken)
    {
        if (!host.Budget.TryConsumeOperation()) return null;
        _ = InteractionCanonicalJson.CanonicalizeObject(input);
        _ = SystemInnerWorkerAssignmentV1.Parse(input);
        var catalog = catalogs.TryGet(host.ApplicationRevision.ApplicationId, out var current)
            ? current : null;
        if (catalog is null) return null;
        var record = catalog.Inspect(new(host.ApplicationRevision.ApplicationId,
            host.ApplicationRevision.ApplicationId.Value, declaration.QualifiedProcedureId));
        var sourceFingerprint = ProcedureContractFingerprint(record, declaration);
        if (sourceFingerprint is null) return null;
        var reference = new StandingGrantDefinitionReference(declaration.QualifiedProcedureId,
            "procedure", declaration.ProcedureVersion, declaration.ContentFingerprint);
        var target = await targets.ResolveAsync(host, reference, cancellationToken);
        if (target is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } exact }) return null;
        var authority = await EvaluateAuthorityAsync(host,
            new(StandingGrantCapability.Execute, StandingGrantScope.StateSpace, [exact], []), cancellationToken);
        if (!ExactGrant(host, authority, grant.GrantReference)) return null;
        var evidence = new ApplicationCandidateRuntimeProposal(
            ApplicationCandidateRuntimeProposalKind.Job, declaration.Alias, reference,
            DataFingerprint(input), sourceFingerprint,
            declaration.ResultSchemaFingerprint, null, null, []);
        return new(evidence, null, null);
    }

    private sealed class ValidationCapabilities(
        ApplicationCandidateWorkflowRuntimeValidator owner,
        InteractionInvocationHost host,
        ApplicationCandidateWorkflowUpdate update,
        WorkflowClosure closure,
        ICatalogNavigator catalog,
        IReadOnlyDictionary<string, string> hostRoles,
        StandingGrantRevision grant) : IApplicationActionServiceCapabilities,
        IApplicationJobServiceCapabilities, IApplicationTerminalActionServiceCapabilities,
        IApplicationTerminalServiceCapabilities
    {
        private readonly List<ApplicationCandidateRuntimeReadEvidence> readEvidence = [];
        private int exchangedBytes;
        internal BoundaryProposal? Proposal { get; private set; }
        internal InteractionInvocationResult? Failure { get; private set; }
        internal IReadOnlyList<ApplicationCandidateRuntimeReadEvidence> ReadEvidence => readEvidence.AsReadOnly();
        public bool ActionsEnabled => update.Service.Actions.Count != 0;
        public bool JobsEnabled => update.Service.Jobs.Count != 0;
        public bool IsTerminal => Proposal is not null || Failure is not null;

        public async Task<InteractionInvocationResult> ReadAsync(string alias, string inputJson,
            CancellationToken cancellationToken = default)
        {
            if (IsTerminal) return Failure ?? Boundary();
            try
            {
                var declaration = update.Service.Reads.SingleOrDefault(value => value.Alias == alias);
                if (declaration is null) return Fail("SERVICE_READ_ALIAS_INVALID", "The read alias is not declared.");
                var input = InteractionCanonicalJson.CanonicalizeObject(inputJson);
                AddExchange(input);
                var roles = MapRoles(declaration.RoleMappings, hostRoles);
                if (roles is null) return Fail("SERVICE_ROLE_BINDING_MISSING", "A trusted role binding is missing.");
                var readHost = new InteractionInvocationHost(host.Principal, host.ApplicationRevision,
                    host.StateSpaceId!, host.GrantReference, host.CommandId, host.StateRevision!,
                    InteractionExecutionProfile.ReadOnly, host.Budget, host.ParentCommandId);
                var result = await owner.serviceReads.ReadAsync(new(readHost, declaration.QualifiedQueryId,
                    declaration.Contract, roles, input), cancellationToken);
                AddExchange(result.ToJson());
                if (result.Tag != InteractionInvocationResultTag.Completed || result.DataJson is null
                    || result.ReadEvidence is null) return Fail(result.Code, result.SafeMessage, result);
                var dependency = closure.Authority.Single(value =>
                    value.Definition.Kind == "query" && value.Definition.DefinitionId == declaration.QualifiedQueryId);
                readEvidence.Add(new(alias, dependency.Definition, DataFingerprint(input), result.ReadEvidence));
                return result;
            }
            catch (InteractionContractException error)
            {
                return Fail(error.Code, "The workflow read is invalid.");
            }
        }

        public async Task<InteractionInvocationResult> ActionAsync(string alias, string inputJson,
            CancellationToken cancellationToken = default)
        {
            if (IsTerminal) return Failure ?? Boundary();
            try
            {
                var declaration = update.Service.Actions.SingleOrDefault(value => value.Alias == alias);
                if (declaration is null) return Fail("SERVICE_ACTION_ALIAS_INVALID", "The action alias is not declared.");
                var input = InteractionCanonicalJson.CanonicalizeObject(inputJson);
                AddExchange(input);
                Proposal = await owner.ProposeActionAsync(host, update, catalog, declaration,
                    hostRoles, input, grant, cancellationToken);
                return Proposal is null
                    ? Fail("WORKFLOW_ACTION_PROPOSAL_INVALID", "The action could not produce a valid typed dry run.")
                    : Boundary();
            }
            catch (InteractionContractException error)
            {
                return Fail(error.Code, "The workflow action proposal is invalid.");
            }
        }

        public async Task<InteractionInvocationResult> SubmitJobAsync(string alias, string inputJson,
            CancellationToken cancellationToken = default)
        {
            if (IsTerminal) return Failure ?? Boundary();
            try
            {
                var declaration = update.Service.Jobs.SingleOrDefault(value => value.Alias == alias);
                if (declaration is null) return Fail("SERVICE_JOB_ALIAS_INVALID", "The job alias is not declared.");
                var input = InteractionCanonicalJson.CanonicalizeObject(inputJson);
                AddExchange(input);
                Proposal = await owner.ProposeJobAsync(host, declaration, input, grant, cancellationToken);
                return Proposal is null
                    ? Fail("WORKFLOW_JOB_PROPOSAL_INVALID", "The job request is invalid or unauthorized.")
                    : Boundary();
            }
            catch (InteractionContractException error)
            {
                return Fail(error.Code, "The workflow job proposal is invalid.");
            }
        }

        public Task<InteractionInvocationResult> ReadJobStatusAsync(string handleJson,
            CancellationToken cancellationToken = default) => Task.FromResult(
            Fail("WORKFLOW_JOB_STATUS_UNAVAILABLE", "Validation has no durable task handle."));

        public ApplicationServiceProgressDisposition TryWriteProgress(string dataJson) =>
            ApplicationServiceProgressDisposition.Closed;

        private void AddExchange(string json)
        {
            var bytes = Encoding.UTF8.GetByteCount(json);
            if (bytes > ApplicationReadOnlyServiceLimits.MaximumExchangedBytesPerRoot - exchangedBytes)
                throw new InteractionContractException("SERVICE_EXCHANGE_LIMIT",
                    "The workflow exchanged-data allowance is exhausted.");
            exchangedBytes += bytes;
        }

        private InteractionInvocationResult Fail(string code, string message,
            InteractionInvocationResult? source = null)
        {
            Failure ??= source?.Tag is InteractionInvocationResultTag.Unavailable
                ? InteractionInvocationResult.Unavailable(code, message)
                : InteractionInvocationResult.Failed(code, message);
            return Failure;
        }

        private static InteractionInvocationResult Boundary() => InteractionInvocationResult.Unavailable(
            "WORKFLOW_VALIDATION_BOUNDARY", "Validation stopped at the first external workflow boundary.");
    }

    private static IReadOnlyDictionary<string, string>? MapRoles(
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyDictionary<string, string> hostRoles)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (target, source) in mappings)
            if (!hostRoles.TryGetValue(source, out var entityId)) return null;
            else result[target] = entityId;
        return result;
    }

    private static bool ExactGrant(InteractionInvocationHost host,
        StandingGrantDecision decision, string grantReference) =>
        decision is { Allowed: true, Grant: { } grant, Evidence.Allowed: true }
        && grant.GrantReference == grantReference
        && grant.PrincipalReference == host.Principal.PrincipalId
        && grant.ApplicationId == host.ApplicationRevision.ApplicationId
        && grant.ExpiresAtUtc > DateTime.UtcNow && !grant.Revoked;

    private async Task<StandingGrantDecision> EvaluateAuthorityAsync(
        InteractionInvocationHost host,
        StandingGrantRequirement requirement,
        CancellationToken cancellationToken)
    {
        await using var scope = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
        return await grants.EvaluateAsync(host, requirement, cancellationToken);
    }

    private static string? ProcedureContractFingerprint(CatalogRecordView record,
        ApplicationServiceJobDeclaration declaration)
    {
        if (record.Summary.Kind != "procedure" || record.Summary.Status != "active"
            || record.Summary.QualifiedId != declaration.QualifiedProcedureId
            || record.Summary.Version != declaration.ProcedureVersion
            || record.Summary.ContentFingerprint != declaration.ContentFingerprint) return null;
        try
        {
            using var content = JsonDocument.Parse(record.ContentJson);
            string Text(string name) => content.RootElement.GetProperty(name).GetString()
                ?? throw new JsonException();
            var status = Text("status") switch
            {
                "active" => ProcedureStatus.Active,
                "deprecated" => ProcedureStatus.Deprecated,
                "archived" => ProcedureStatus.Archived,
                _ => throw new JsonException()
            };
            return ContentHash.ForProcedure(Text("category"), Text("name"),
                Text("description"), Text("governs"), Text("instructions"), Text("constraints"), status);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException
            or InvalidOperationException) { return null; }
    }

    private static InteractionInvocationHost StateHost(InteractionInvocationHost source,
        StateSpaceView state, string grantReference) => new(source.Principal,
        source.ApplicationRevision, state.StateSpaceId, grantReference, source.CommandId,
        InteractionStateRevision.From(state), InteractionExecutionProfile.Workflow,
        source.Budget, source.ParentCommandId);

    private static bool SameRevision(ApplicationRevision left, ApplicationRevision right) =>
        left.ApplicationId == right.ApplicationId && left.Revision == right.Revision
        && left.Fingerprint == right.Fingerprint
        && left.BaseApplications.SequenceEqual(right.BaseApplications);

    private static ApplicationCandidateDependency Dependency(StandingGrantDefinitionReference value) =>
        new(value.DefinitionId, value.Revision, value.ContentFingerprint);

    private static string DataFingerprint(string json) => InteractionCanonicalJson.Fingerprint(
        "dantes-roleplay/workflow-candidate-runtime-value/v1", InteractionCanonicalJson.Canonicalize(json));

    private static IReadOnlyList<ApplicationCandidateValidationSample> BoundSamples(
        ApplicationCandidateValidationRequest request)
    {
        _ = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(request));
        var samples = SqliteApplicationAuthoringService.NormalizeSamples(request.Samples);
        _ = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(
            new ApplicationCandidateValidationRequest(request.Candidate, samples)));
        return samples;
    }

    private static ApplicationCandidateRuntimeReport Report(
        ApplicationCandidateRuntimeStatus status,
        ApplicationCandidateReference candidate,
        string? selection,
        IReadOnlyList<ApplicationCandidateRuntimeSampleResult> samples,
        IReadOnlyList<ApplicationCandidateDependency>? dependencies,
        string? code = null,
        string? message = null)
    {
        var report = new ApplicationCandidateRuntimeReport(status, candidate, selection,
            selection is null ? null : PolicyVersion, selection is null ? null : PolicyFingerprint,
            samples.ToArray(), code is null ? [] : [new(code, candidate.CandidateId, message!)], dependencies);
        var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(report));
        if (Encoding.UTF8.GetByteCount(canonical) > InteractionContractLimits.JsonBytes)
            throw new InteractionContractException("APPLICATION_CANDIDATE_RUNTIME_REPORT_TOO_LARGE",
                "The workflow runtime report exceeds the interaction contract limit.");
        return report;
    }

    private static ApplicationCandidateRuntimeReport Invalid(ApplicationCandidateReference candidate,
        string? selection, IReadOnlyList<ApplicationCandidateRuntimeSampleResult> samples,
        string code, string message) => Report(ApplicationCandidateRuntimeStatus.Invalid,
        candidate, selection, samples, null, code, message);

    private static ApplicationCandidateRuntimeReport Unavailable(ApplicationCandidateReference candidate,
        string? selection, IReadOnlyList<ApplicationCandidateRuntimeSampleResult> samples,
        string code, string message) => Report(ApplicationCandidateRuntimeStatus.Unavailable,
        candidate, selection, samples, null, code, message);

    private sealed record ApplicationCandidateWorkflowUpdate(
        ApplicationCandidateReference Candidate,
        ActiveApplicationManifest Basis,
        StandingGrantDefinitionReference Definition,
        ApplicationReadOnlyServiceDefinition Service,
        string Source,
        string Fingerprint,
        StandingGrantDefinitionTarget CandidateTarget,
        IReadOnlyList<StandingGrantDefinitionReference> Dependencies);

    private sealed record AuthorityRequirement(
        StandingGrantDefinitionReference Definition,
        StandingGrantCapability Capability);

    private sealed record WorkflowClosure(
        IReadOnlyList<ApplicationCandidateDependency> Dependencies,
        IReadOnlyList<AuthorityRequirement> Authority);

    private sealed record BoundaryProposal(
        ApplicationCandidateRuntimeProposal Evidence,
        string? OutputFingerprint,
        string? ExpectedEffectsJson)
    {
        internal ApplicationCandidateRuntimeProposalKind Kind => Evidence.Kind;
    }
}
