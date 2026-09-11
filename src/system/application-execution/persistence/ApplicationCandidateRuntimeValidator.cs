using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>
/// Runs the bounded examples for the initial pure-mechanic candidate subset. A completed report
/// does not approve a candidate; the authoring owner must bind it into its durable operation proof.
/// </summary>
internal sealed class ApplicationCandidateRuntimeValidator(
    ApplicationCandidatePureRuntimeClosureReader closureReader,
    JintMechanicEngine engine,
    IBoundedJsonSchemaValidator schemas,
    IStandingGrantTargetResolver targets,
    IStandingGrantPolicy grants) : IApplicationCandidatePreparation
{
    internal const string RuntimePolicyVersion = "selected-pure-mechanic-runtime-v1";
    private const string ClassifierGrammarVersion =
        "dantes-roleplay/application-candidate-pure-mechanic-classification/v1";

    internal static string RuntimePolicyFingerprint { get; } = InteractionCanonicalJson.Fingerprint(
        "dantes-roleplay/application-candidate-runtime-policy/v1",
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            coverageVersion = RuntimePolicyVersion,
            classifierGrammarVersion = ClassifierGrammarVersion,
            schemaProfile = SystemJsonSchemaProfile.Id,
            requestBytes = InteractionContractLimits.JsonBytes,
            requestDepth = InteractionContractLimits.JsonDepth,
            outputBytes = InteractionContractLimits.JsonBytes,
            outputDepth = InteractionContractLimits.JsonDepth,
            samplesPerDefinition = ApplicationAuthoringLimits.SamplesPerDefinition,
            samplesPerValidation = ApplicationAuthoringLimits.SamplesPerValidation,
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
            engine = JintMechanicEngine.PureExecutionPolicyFingerprint
        })));

    internal static string DataFingerprint(string json)
    {
        var canonical = InteractionCanonicalJson.CanonicalizeObject(json);
        if (Encoding.UTF8.GetByteCount(canonical) > InteractionContractLimits.JsonBytes)
            throw new InteractionContractException(
                "JSON_TOO_LARGE", "JSON exceeds the interaction contract limit.", nameof(json));
        return InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/application-candidate-runtime-data/v1", canonical);
    }

    internal async Task<ApplicationCandidateRuntimeReport> CheckAsync(
        ApplicationCandidateValidationRequest request,
        InteractionInvocationHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(host);
        ValidateCandidate(request.Candidate, host);

        var samples = FreezeAndBoundRequest(request);
        var checkedSamples = new List<ApplicationCandidateRuntimeSampleResult>(samples.Count);
        string? selectionFingerprint = null;
        ApplicationCandidateValidationSample? currentSample = null;
        var currentSampleIndex = -1;
        string? currentInputFingerprint = null;
        string? currentExpectedFingerprint = null;
        var currentAttempted = false;
        try
        {
            CheckTime(host, cancellationToken);
            var closure = await AwaitAsync(
                token => closureReader.ReadAsync(host, request.Candidate, token), host, cancellationToken);
            if (closure is null)
                return Report(ApplicationCandidateRuntimeStatus.Unavailable, request.Candidate, null,
                    checkedSamples, "PURE_RUNTIME_CLOSURE_UNAVAILABLE",
                    "The exact pure-mechanic selection is unavailable.");
            if (closure.Candidate != request.Candidate
                || closure.CoverageVersion != RuntimePolicyVersion
                || closure.Definitions.IsDefaultOrEmpty)
                return Report(ApplicationCandidateRuntimeStatus.Unavailable, request.Candidate, null,
                    checkedSamples, "PURE_RUNTIME_CLOSURE_INVALID",
                    "The exact pure-mechanic selection could not be verified.");

            RequireHash(closure.EvidenceFingerprint, nameof(closure.EvidenceFingerprint));
            var entries = closure.Definitions.ToArray();
            if (entries.Length is < 1 or > ApplicationAuthoringLimits.SamplesPerValidation
                || entries.Any(entry => entry is null || entry.Plan is null)
                || entries.Select(entry => entry.Plan.Definition.DefinitionId)
                    .Distinct(StringComparer.Ordinal).Count() != entries.Length)
                return Report(ApplicationCandidateRuntimeStatus.Unavailable, request.Candidate, null,
                    checkedSamples, "PURE_RUNTIME_CLOSURE_INVALID",
                    "The exact pure-mechanic selection could not be verified.");

            var byId = entries.ToDictionary(
                entry => entry.Plan.Definition.DefinitionId, StringComparer.Ordinal);
            var initialAuthority = new Dictionary<string, AuthorityStamp>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                var stamp = await AuthorizeAsync(
                    host, request.Candidate, entry.Plan.Definition, cancellationToken);
                if (stamp is null)
                    return Report(ApplicationCandidateRuntimeStatus.Unavailable, request.Candidate, null,
                        checkedSamples, "PURE_RUNTIME_AUTHORITY_UNAVAILABLE",
                        "Current candidate read and validation authority is unavailable.");
                initialAuthority.Add(entry.Plan.Definition.DefinitionId, stamp);
            }
            selectionFingerprint = closure.EvidenceFingerprint;

            if (samples.Any(sample => !byId.TryGetValue(sample.Definition.DefinitionId, out var entry)
                    || sample.Definition != entry.Plan.Definition))
                return Report(ApplicationCandidateRuntimeStatus.Invalid, request.Candidate,
                    selectionFingerprint, checkedSamples, "PURE_RUNTIME_SAMPLE_TARGET_INVALID",
                    "A retained sample does not select an exact pure mechanic in the closure.");
            if (entries.Any(entry => !samples.Any(sample => sample.Definition == entry.Plan.Definition)))
                return Report(ApplicationCandidateRuntimeStatus.Unavailable, request.Candidate,
                    selectionFingerprint, checkedSamples, "PURE_RUNTIME_SAMPLES_INCOMPLETE",
                    "Every selected pure mechanic needs an exact retained sample.");

            for (var index = 0; index < samples.Count; index++)
            {
                var sample = samples[index];
                var entry = byId[sample.Definition.DefinitionId];
                var sampleIndex = index;
                var inputFingerprint = DataFingerprint(sample.InputJson);
                var expectedFingerprint = DataFingerprint(sample.ExpectedDataJson);
                currentSample = sample;
                currentSampleIndex = sampleIndex;
                currentInputFingerprint = inputFingerprint;
                currentExpectedFingerprint = expectedFingerprint;
                currentAttempted = false;

                if (entry.Plan.NormalizedInputSchema is { } inputSchema)
                {
                    var inputStatus = schemas.Validate(
                        SystemJsonSchemaProfile.Id, inputSchema, sample.InputJson).Status;
                    CheckTime(host, cancellationToken);
                    if (inputStatus != SchemaValueStatus.Valid)
                    {
                        checkedSamples.Add(Sample(sample.Definition, sampleIndex,
                            ApplicationCandidateRuntimeStatus.Invalid, false,
                            inputFingerprint, expectedFingerprint, null));
                        return Report(ApplicationCandidateRuntimeStatus.Invalid, request.Candidate,
                            selectionFingerprint, checkedSamples, "PURE_RUNTIME_INPUT_SCHEMA_INVALID",
                            "A retained sample does not satisfy the selected mechanic input schema.");
                    }
                }

                var before = await AuthorizeAsync(
                    host, request.Candidate, sample.Definition, cancellationToken);
                if (before is null || before != initialAuthority[sample.Definition.DefinitionId])
                    return Report(ApplicationCandidateRuntimeStatus.Unavailable, request.Candidate,
                        selectionFingerprint, checkedSamples,
                        "PURE_RUNTIME_AUTHORITY_CHANGED",
                        "Current candidate authority changed before execution.");

                var seed = Seed(request.Candidate, selectionFingerprint, inputFingerprint, index);
                var projection = new MechanicProjection { Input = sample.InputJson, Seed = seed };
                CheckTime(host, cancellationToken);
                var remaining = host.Budget.DeadlineUtc - DateTime.UtcNow;
                var limits = ExecutionLimits.ReadModel with
                {
                    Timeout = remaining < ExecutionLimits.ReadModel.Timeout
                        ? remaining : ExecutionLimits.ReadModel.Timeout
                };
                using var executionDeadline = DeadlineToken(host, cancellationToken);
                if (!host.Budget.TryConsumeOperation())
                    return Report(ApplicationCandidateRuntimeStatus.Unavailable, request.Candidate,
                        selectionFingerprint, checkedSamples,
                        "PURE_RUNTIME_BUDGET_EXHAUSTED",
                        "The invocation budget ended before all samples could run.");
                currentAttempted = true;
                var run = await engine.RunAsync(
                    entry.Plan.Source, projection, limits, executionDeadline.Token);
                CheckTime(host, cancellationToken);

                var after = await AuthorizeAsync(
                    host, request.Candidate, sample.Definition, cancellationToken);
                if (after is null || after != before)
                    return StopUnavailable(request.Candidate, selectionFingerprint, checkedSamples,
                        sample, sampleIndex, inputFingerprint, expectedFingerprint,
                        "PURE_RUNTIME_AUTHORITY_CHANGED",
                        "Current candidate authority changed during execution.", attempted: true);

                var pureData = TryReadPureData(run, out var actualJson, out var actualFingerprint);
                CheckTime(host, cancellationToken);
                if (!pureData)
                {
                    checkedSamples.Add(Sample(sample.Definition, sampleIndex,
                        ApplicationCandidateRuntimeStatus.Invalid, true,
                        inputFingerprint, expectedFingerprint, actualFingerprint));
                    return Report(ApplicationCandidateRuntimeStatus.Invalid, request.Candidate,
                        selectionFingerprint, checkedSamples, "PURE_RUNTIME_EXECUTION_INVALID",
                        "A selected mechanic did not produce bounded pure data.");
                }
                if (actualJson != sample.ExpectedDataJson)
                {
                    checkedSamples.Add(Sample(sample.Definition, sampleIndex,
                        ApplicationCandidateRuntimeStatus.Invalid, true,
                        inputFingerprint, expectedFingerprint, actualFingerprint));
                    return Report(ApplicationCandidateRuntimeStatus.Invalid, request.Candidate,
                        selectionFingerprint, checkedSamples, "PURE_RUNTIME_DATA_MISMATCH",
                        "A selected mechanic produced data that did not match its retained sample.");
                }
                checkedSamples.Add(Sample(sample.Definition, sampleIndex,
                    ApplicationCandidateRuntimeStatus.Completed, true,
                    inputFingerprint, expectedFingerprint, actualFingerprint));
                currentSample = null;
                currentAttempted = false;
            }

            CheckTime(host, cancellationToken);
            return Report(ApplicationCandidateRuntimeStatus.Completed, request.Candidate,
                selectionFingerprint, checkedSamples);
        }
        catch (OperationCanceledException)
        {
            if (currentAttempted && currentSample is not null)
                checkedSamples.Add(Sample(currentSample.Definition, currentSampleIndex,
                    ApplicationCandidateRuntimeStatus.Unavailable, true,
                    currentInputFingerprint!, currentExpectedFingerprint!, null));
            return Report(ApplicationCandidateRuntimeStatus.Unavailable, request.Candidate,
                selectionFingerprint, checkedSamples, "PURE_RUNTIME_CANCELLED",
                "Runtime validation ended before all samples could run.");
        }
    }

    public Task<ApplicationCandidateRuntimeReport> ValidateAsync(
        ApplicationCandidateValidationRequest request,
        InteractionInvocationHost host,
        CancellationToken cancellationToken = default) =>
        CheckAsync(request, host, cancellationToken);

    public Task<ApplicationCandidateCheckResult> ValidateAsync(
        InteractionInvocationHost host,
        ApplicationCandidateSnapshot candidate,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ApplicationCandidateCheckResult(
            ApplicationCandidateCheckStatus.Unavailable,
            candidate.Candidate.ContentFingerprint,
            candidate.Candidate.ContentFingerprint,
            null,
            null,
            [new("CANDIDATE_PREPARATION_REQUEST_REQUIRED", candidate.Candidate.CandidateId,
                "Runtime preparation requires the retained sample request.")]));

    private async Task<AuthorityStamp?> AuthorizeAsync(
        InteractionInvocationHost host,
        ApplicationCandidateReference candidate,
        StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken)
    {
        var resolution = await AwaitAsync(
            token => targets.ResolveCandidateReferenceAsync(host, candidate, selection, token),
            host, cancellationToken);
        if (resolution is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } target }
            || target.DefinitionId != selection.DefinitionId
            || target.Kind != selection.Kind
            || target.Revision != selection.Revision
            || target.ContentFingerprint != selection.ContentFingerprint
            || target.OwnerApplicationId != candidate.ApplicationId
            || target.Candidate != candidate)
            return null;

        var read = await DecideAsync(host, target, StandingGrantCapability.Read, cancellationToken);
        if (read is null) return null;
        var validate = await DecideAsync(host, target, StandingGrantCapability.Validate, cancellationToken);
        return validate is not null && validate == read ? new(target, read) : null;
    }

    private async Task<GrantIdentity?> DecideAsync(
        InteractionInvocationHost host,
        StandingGrantDefinitionTarget target,
        StandingGrantCapability capability,
        CancellationToken cancellationToken)
    {
        var requirement = new StandingGrantRequirement(
            capability, StandingGrantScope.Application, [target], []);
        StandingGrantContractRules.ValidateRequirement(host, requirement);
        var decision = await AwaitAsync(
            token => grants.EvaluateAsync(host, requirement, token), host, cancellationToken);
        if (!ExactAllowedDecision(host, target, capability, decision)) return null;
        return GrantIdentity.From(decision.Grant!);
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

    private static IReadOnlyList<ApplicationCandidateValidationSample> FreezeAndBoundRequest(
        ApplicationCandidateValidationRequest request)
    {
        _ = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(request));
        var normalized = SqliteApplicationAuthoringService.NormalizeSamples(request.Samples);
        var frozen = Array.AsReadOnly(normalized.Select(sample => sample with
        {
            Definition = sample.Definition with { },
            InputJson = InteractionCanonicalJson.CanonicalizeObject(sample.InputJson),
            ExpectedDataJson = InteractionCanonicalJson.CanonicalizeObject(sample.ExpectedDataJson)
        }).ToArray());
        _ = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(
            new ApplicationCandidateValidationRequest(request.Candidate, frozen)));
        return frozen;
    }

    private static void ValidateCandidate(
        ApplicationCandidateReference candidate,
        InteractionInvocationHost host)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (host.StateSpaceId is not null || host.StateRevision is not null
            || candidate.ApplicationId is null || candidate.ApplicationId.IsSystem
            || candidate.ApplicationId != host.ApplicationRevision.ApplicationId
            || candidate.Revision < 1
            || candidate.CandidateId is not { Length: 32 }
            || candidate.CandidateId.Any(value =>
                !(char.IsAsciiDigit(value) || value is >= 'a' and <= 'f')))
            throw new InteractionContractException(
                "INVALID_APPLICATION_CANDIDATE_RUNTIME_REQUEST",
                "Runtime validation requires an exact candidate and an application-scoped host.");
        RequireHash(candidate.ContentFingerprint, nameof(candidate.ContentFingerprint));
    }

    private static void RequireHash(string value, string parameter)
    {
        if (value is not { Length: 64 }
            || value.Any(character => !(char.IsAsciiDigit(character)
                || character is >= 'A' and <= 'F')))
            throw new InteractionContractException(
                "INVALID_SHA256", $"{parameter} must be an uppercase SHA-256 value.", parameter);
    }

    private static bool TryReadPureData(
        MechanicRunResult run,
        out string? actualJson,
        out string? actualFingerprint)
    {
        actualJson = null;
        actualFingerprint = null;
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
            actualJson = InteractionCanonicalJson.CanonicalizeObject(output.Data);
            if (Encoding.UTF8.GetByteCount(actualJson) > InteractionContractLimits.JsonBytes)
                return false;
            actualFingerprint = DataFingerprint(actualJson);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ApplicationCandidateRuntimeSampleResult Sample(
        StandingGrantDefinitionReference definition,
        int sampleIndex,
        ApplicationCandidateRuntimeStatus outcome,
        bool attempted,
        string inputFingerprint,
        string expectedFingerprint,
        string? actualFingerprint) => new(
        definition, sampleIndex, outcome, attempted,
        inputFingerprint, expectedFingerprint, actualFingerprint);

    private static ApplicationCandidateRuntimeReport StopUnavailable(
        ApplicationCandidateReference candidate,
        string selectionFingerprint,
        List<ApplicationCandidateRuntimeSampleResult> checkedSamples,
        ApplicationCandidateValidationSample sample,
        int sampleIndex,
        string inputFingerprint,
        string expectedFingerprint,
        string code,
        string message,
        bool attempted = false)
    {
        checkedSamples.Add(Sample(sample.Definition, sampleIndex,
            ApplicationCandidateRuntimeStatus.Unavailable, attempted,
            inputFingerprint, expectedFingerprint, null));
        return Report(ApplicationCandidateRuntimeStatus.Unavailable, candidate,
            selectionFingerprint, checkedSamples, code, message);
    }

    private static ApplicationCandidateRuntimeReport Report(
        ApplicationCandidateRuntimeStatus status,
        ApplicationCandidateReference candidate,
        string? selectionFingerprint,
        IReadOnlyList<ApplicationCandidateRuntimeSampleResult> samples,
        string? diagnosticCode = null,
        string? diagnosticMessage = null)
    {
        var report = new ApplicationCandidateRuntimeReport(
            status, candidate, selectionFingerprint,
            selectionFingerprint is null ? null : RuntimePolicyVersion,
            selectionFingerprint is null ? null : RuntimePolicyFingerprint,
            Array.AsReadOnly(samples.ToArray()),
            diagnosticCode is null ? [] : Array.AsReadOnly(new[]
            {
                new ApplicationCandidateDiagnostic(
                    diagnosticCode, candidate.CandidateId, diagnosticMessage!)
            }));
        var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(report));
        if (Encoding.UTF8.GetByteCount(canonical) > InteractionContractLimits.JsonBytes)
            throw new InteractionContractException(
                "APPLICATION_CANDIDATE_RUNTIME_REPORT_TOO_LARGE",
                "The runtime report exceeds the interaction contract limit.");
        return report;
    }

    private static void CheckTime(
        InteractionInvocationHost host,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow)
            throw new OperationCanceledException();
    }

    private static async Task<T> AwaitAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        InteractionInvocationHost host,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ArmDeadline(deadline, host);
        var result = await operation(deadline.Token).ConfigureAwait(false);
        CheckTime(host, cancellationToken);
        return result;
    }

    private static CancellationTokenSource DeadlineToken(
        InteractionInvocationHost host,
        CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ArmDeadline(deadline, host);
        return deadline;
    }

    private static void ArmDeadline(
        CancellationTokenSource deadline,
        InteractionInvocationHost host)
    {
        var remaining = host.Budget.DeadlineUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) deadline.Cancel();
        else if (remaining <= TimeSpan.FromMilliseconds(int.MaxValue)) deadline.CancelAfter(remaining);
    }

    private static long Seed(
        ApplicationCandidateReference candidate,
        string selectionFingerprint,
        string inputFingerprint,
        int index)
    {
        var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            applicationId = candidate.ApplicationId.Value,
            candidate.CandidateId,
            candidate.Revision,
            candidate.ContentFingerprint,
            selectionFingerprint,
            inputFingerprint,
            index
        }));
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/application-candidate-runtime-seed/v1", canonical);
        return BinaryPrimitives.ReadInt64LittleEndian(Convert.FromHexString(fingerprint)) & long.MaxValue;
    }

    private sealed record AuthorityStamp(
        StandingGrantDefinitionTarget Target,
        GrantIdentity Grant);

    private sealed record GrantIdentity(
        string GrantReference,
        string GrantId,
        int Revision,
        string ContentFingerprint,
        string PrincipalReference,
        string ApplicationId,
        StandingGrantScope Scope,
        string? StateSpaceId)
    {
        internal static GrantIdentity From(StandingGrantRevision grant) => new(
            grant.GrantReference, grant.GrantId, grant.Revision, grant.ContentFingerprint,
            grant.PrincipalReference, grant.ApplicationId.Value, grant.Scope, grant.StateSpaceId);
    }
}
