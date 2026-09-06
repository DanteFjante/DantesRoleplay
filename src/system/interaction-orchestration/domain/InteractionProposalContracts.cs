using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;

namespace DantesRoleplay.Interactions;

public enum InteractionFeatureScope
{
    System,
    Application
}

public enum InteractionPlanStepKind
{
    Query,
    Action
}

public sealed record InteractionResultBinding
{
    public InteractionResultBinding(
        string fromStepId,
        string fromPointer,
        string? toRole = null,
        string? toInputPointer = null)
    {
        FromStepId = InteractionGuard.Identifier(fromStepId, nameof(fromStepId));
        FromPointer = Pointer(fromPointer, nameof(fromPointer), allowArrayTargets: true);
        if ((toRole is null) == (toInputPointer is null))
            throw new InteractionContractException("RESULT_BINDING_TARGET_INVALID",
                "A result binding must name exactly one role or input target.");
        ToRole = toRole is null ? null : InteractionGuard.Identifier(toRole, nameof(toRole));
        ToInputPointer = toInputPointer is null ? null
            : Pointer(toInputPointer, nameof(toInputPointer), allowArrayTargets: false);
    }

    public string FromStepId { get; }
    public string FromPointer { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToRole { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToInputPointer { get; }
    public string TargetKey => ToRole is not null ? "role:" + ToRole : "input:" + ToInputPointer;

    private static string Pointer(string value, string parameter, bool allowArrayTargets)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 1_000 || (value.Length > 0 && !value.StartsWith("/", StringComparison.Ordinal)))
            throw new InteractionContractException("RESULT_BINDING_POINTER_INVALID",
                "A result binding requires a bounded RFC 6901 JSON pointer.", parameter);
        var tokens = value.Length == 0 ? [] : value.Split('/').Skip(1).ToArray();
        if (tokens.Any(token => token.Contains('~')
                && token.Replace("~0", "", StringComparison.Ordinal)
                    .Replace("~1", "", StringComparison.Ordinal).Contains('~'))
            || (!allowArrayTargets && tokens.Any(token => int.TryParse(token, out _))))
            throw new InteractionContractException("RESULT_BINDING_POINTER_INVALID",
                "A result binding pointer contains a forbidden token.", parameter);
        return value;
    }
}

public sealed record InteractionQueryContractReference
{
    public InteractionQueryContractReference(
        string executor,
        string projectionQualifiedId,
        int projectionVersion,
        string projectionContentHash,
        string outputSchemaHash,
        string outputSchemaJson,
        ApplicationQueryExposure exposure,
        IEnumerable<string> roles,
        string? collectionId = null)
    {
        Executor = InteractionGuard.Identifier(executor, nameof(executor));
        ProjectionQualifiedId = InteractionGuard.Identifier(projectionQualifiedId, nameof(projectionQualifiedId));
        if (projectionVersion < 1)
            throw new InteractionContractException("INVALID_QUERY_PROJECTION", "A query projection version must be positive.");
        ProjectionVersion = projectionVersion;
        ProjectionContentHash = InteractionGuard.UpperSha256(projectionContentHash, nameof(projectionContentHash));
        OutputSchemaHash = InteractionGuard.UpperSha256(outputSchemaHash, nameof(outputSchemaHash));
        OutputSchemaJson = InteractionCanonicalJson.CanonicalizeObject(outputSchemaJson);
        if (!Enum.IsDefined(exposure))
            throw new InteractionContractException("INVALID_QUERY_EXPOSURE", "The query exposure is unsupported.");
        Exposure = exposure;
        Roles = InteractionGuard.CopyDistinctList(roles, InteractionContractLimits.RoleHints,
            "INVALID_QUERY_ROLES", sort: true);
        if (executor == ApplicationQueryContract.ObjectProjectionExecutor)
        {
            if (string.IsNullOrWhiteSpace(collectionId))
                throw new InteractionContractException("INVALID_QUERY_COLLECTION",
                    "An object-projection query requires a collection identity.");
            CollectionId = InteractionGuard.Identifier(collectionId, nameof(collectionId));
        }
        else if (collectionId is not null)
            throw new InteractionContractException("INVALID_QUERY_COLLECTION",
                "Only an object-projection query can carry a collection identity.");
    }

    public string Executor { get; }
    public string ProjectionQualifiedId { get; }
    public int ProjectionVersion { get; }
    public string ProjectionContentHash { get; }
    public string OutputSchemaHash { get; }
    public string OutputSchemaJson { get; }
    public ApplicationQueryExposure Exposure { get; }
    public IReadOnlyList<string> Roles { get; }
    public string? CollectionId { get; }
}

public sealed record InteractionContractReference
{
    public InteractionContractReference(
        InteractionFeatureScope scope,
        ApplicationIdentifier applicationId,
        string qualifiedKey,
        string authoritativeId,
        int version,
        string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (!Enum.IsDefined(scope)) throw new InteractionContractException("INVALID_FEATURE_SCOPE", "The feature scope is not supported.");
        qualifiedKey = InteractionGuard.Identifier(qualifiedKey, nameof(qualifiedKey));
        var expectedPrefix = scope == InteractionFeatureScope.System ? "system." : applicationId.Value + ".";
        if (!qualifiedKey.StartsWith(expectedPrefix, StringComparison.Ordinal) || qualifiedKey.Length == expectedPrefix.Length)
            throw new InteractionContractException("CONTRACT_NAMESPACE_MISMATCH", "The contract key does not match its declared owner.");
        if (version < 1) throw new InteractionContractException("INVALID_CONTRACT_VERSION", "The contract version must be positive.");
        Scope = scope;
        ApplicationId = applicationId;
        QualifiedKey = qualifiedKey;
        AuthoritativeId = InteractionGuard.Identifier(authoritativeId, nameof(authoritativeId));
        Version = version;
        Fingerprint = InteractionGuard.UpperSha256(fingerprint, nameof(fingerprint));
    }

    public InteractionFeatureScope Scope { get; }
    public ApplicationIdentifier ApplicationId { get; }
    public string QualifiedKey { get; }
    public string AuthoritativeId { get; }
    public int Version { get; }
    public string Fingerprint { get; }
}

public sealed record InteractionPlanStep
{
    public InteractionPlanStep(
        string stepId,
        InteractionPlanStepKind kind,
        InteractionContractReference contract,
        IEnumerable<string> dependsOn,
        IReadOnlyDictionary<string, string> roleBindings,
        string inputJson,
        string expectedStateRevision,
        IEnumerable<InteractionResultBinding>? resultBindings = null,
        InteractionQueryContractReference? queryContract = null)
    {
        if (!Enum.IsDefined(kind)) throw new InteractionContractException("INVALID_STEP_KIND", "The step kind is not supported.");
        ArgumentNullException.ThrowIfNull(contract);
        StepId = InteractionGuard.Identifier(stepId, nameof(stepId));
        Kind = kind;
        Contract = contract;
        DependsOn = InteractionGuard.CopyDistinctList(dependsOn, InteractionContractLimits.DependenciesPerStep,
            "INVALID_STEP_DEPENDENCIES", sort: true);
        RoleBindings = InteractionGuard.CopyMap(roleBindings, InteractionContractLimits.RoleHints, "INVALID_ROLE_BINDINGS");
        InputJson = InteractionCanonicalJson.CanonicalizeObject(inputJson);
        ExpectedStateRevision = InteractionGuard.Identifier(expectedStateRevision, nameof(expectedStateRevision));
        var copiedBindings = resultBindings?.ToArray() ?? [];
        if (copiedBindings.Length > InteractionContractLimits.ResultBindingsPerStep
            || copiedBindings.Any(binding => binding is null)
            || copiedBindings.Select(binding => binding.TargetKey).Distinct(StringComparer.Ordinal).Count() != copiedBindings.Length)
            throw new InteractionContractException("INVALID_RESULT_BINDINGS",
                "Result bindings must be bounded with distinct targets.");
        ResultBindings = Array.AsReadOnly(copiedBindings);
        if ((kind == InteractionPlanStepKind.Query) != (queryContract is not null))
            throw new InteractionContractException("QUERY_CONTRACT_REFERENCE_INVALID",
                "Only query steps require an exact query execution contract.");
        if (queryContract is not null
            && !queryContract.ProjectionQualifiedId.StartsWith(contract.ApplicationId.Value + ".", StringComparison.Ordinal))
            throw new InteractionContractException("QUERY_PROJECTION_NAMESPACE_MISMATCH",
                "A query projection must belong to the same application as its query contract.");
        QueryContract = queryContract;
    }

    public string StepId { get; }
    public InteractionPlanStepKind Kind { get; }
    public InteractionContractReference Contract { get; }
    public IReadOnlyList<string> DependsOn { get; }
    public IReadOnlyDictionary<string, string> RoleBindings { get; }
    public string InputJson { get; }
    public string ExpectedStateRevision { get; }
    public IReadOnlyList<InteractionResultBinding> ResultBindings { get; }
    public InteractionQueryContractReference? QueryContract { get; }
}

public sealed record InteractionProposal
{
    private InteractionProposal(AuthorizedInteractionEnvelope envelope, IReadOnlyList<InteractionPlanStep> steps, string fingerprint)
    {
        Envelope = envelope;
        Steps = steps;
        Fingerprint = fingerprint;
    }

    public AuthorizedInteractionEnvelope Envelope { get; }
    public IReadOnlyList<InteractionPlanStep> Steps { get; }
    public string Fingerprint { get; }

    public static InteractionProposal Create(AuthorizedInteractionEnvelope envelope, IEnumerable<InteractionPlanStep> steps)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(steps);
        var values = steps.ToArray();
        if (values.Length is < 1 || values.Length > envelope.Intent.MaximumPlanSteps
            || values.Length > envelope.Host.Budgets.MaximumPlanSteps)
            throw new InteractionContractException("INVALID_PROPOSAL_SIZE", "The proposal step count is outside the authorized budget.");
        if (values.Select(x => x.StepId).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InteractionContractException("DUPLICATE_PLAN_STEP", "Proposal step IDs must be unique.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in values)
        {
            if (step.Contract.ApplicationId != envelope.Host.ApplicationRevision.ApplicationId)
                throw new InteractionContractException("CROSS_APPLICATION_REFERENCE", "A proposal cannot reference another application's contract.");
            if (step.ExpectedStateRevision != envelope.Host.StateRevision)
                throw new InteractionContractException("STALE_PROPOSAL_REVISION", "A proposal step must use the host-bound state revision.");
            if (step.DependsOn.Contains(step.StepId, StringComparer.Ordinal))
                throw new InteractionContractException("SELF_DEPENDENCY", "A proposal step cannot depend on itself.");
            if (step.DependsOn.Any(x => !seen.Contains(x)))
                throw new InteractionContractException("MISSING_OR_FORWARD_DEPENDENCY", "Dependencies must name distinct earlier steps.");
            if (step.ResultBindings.Any(binding => !seen.Contains(binding.FromStepId)
                    || !step.DependsOn.Contains(binding.FromStepId, StringComparer.Ordinal)))
                throw new InteractionContractException("RESULT_BINDING_SOURCE_INVALID",
                    "Result bindings must name earlier explicit dependencies.");
            seen.Add(step.StepId);
        }

        var canonicalProposal = Canonicalize(envelope.Fingerprint, values);
        if (Encoding.UTF8.GetByteCount(canonicalProposal) > envelope.Host.Budgets.MaximumModelOutputBytes)
            throw new InteractionContractException("MODEL_OUTPUT_BUDGET_EXCEEDED", "The proposal exceeds the authorized model-output byte budget.");
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/interaction-proposal/v1", canonicalProposal);
        return new(envelope, Array.AsReadOnly(values), fingerprint);
    }

    public static string ComputeFingerprint(string envelopeFingerprint, IEnumerable<InteractionPlanStep> steps)
    {
        envelopeFingerprint = InteractionGuard.UpperSha256(envelopeFingerprint, nameof(envelopeFingerprint));
        ArgumentNullException.ThrowIfNull(steps);
        var canonicalProposal = Canonicalize(envelopeFingerprint, steps.ToArray());
        if (Encoding.UTF8.GetByteCount(canonicalProposal) > InteractionContractLimits.JsonBytes)
            throw new InteractionContractException("MODEL_OUTPUT_BUDGET_EXCEEDED", "The proposal exceeds the closed byte budget.");
        return InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/interaction-proposal/v1", canonicalProposal);
    }

    private static string Canonicalize(string envelopeFingerprint, IReadOnlyList<InteractionPlanStep> values)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            envelope = envelopeFingerprint,
            steps = values.Select(step => step.ResultBindings.Count == 0 && step.QueryContract is null
                ? (object)new
                {
                    step.StepId,
                    kind = step.Kind.ToString().ToLowerInvariant(),
                    contract = new
                    {
                        scope = step.Contract.Scope.ToString().ToLowerInvariant(),
                        applicationId = step.Contract.ApplicationId.Value,
                        step.Contract.QualifiedKey,
                        step.Contract.AuthoritativeId,
                        step.Contract.Version,
                        step.Contract.Fingerprint
                    },
                    dependsOn = step.DependsOn,
                    roleBindings = step.RoleBindings,
                    input = JsonSerializer.Deserialize<JsonElement>(step.InputJson),
                    step.ExpectedStateRevision
                }
                : new
                {
                    step.StepId,
                    kind = step.Kind.ToString().ToLowerInvariant(),
                    contract = new
                    {
                        scope = step.Contract.Scope.ToString().ToLowerInvariant(),
                        applicationId = step.Contract.ApplicationId.Value,
                        step.Contract.QualifiedKey,
                        step.Contract.AuthoritativeId,
                        step.Contract.Version,
                        step.Contract.Fingerprint
                    },
                    dependsOn = step.DependsOn,
                    roleBindings = step.RoleBindings,
                    input = JsonSerializer.Deserialize<JsonElement>(step.InputJson),
                    resultBindings = step.ResultBindings.Select(binding => new
                    {
                        binding.FromStepId,
                        binding.FromPointer,
                        binding.ToRole,
                        binding.ToInputPointer
                    }),
                    queryContract = step.QueryContract is null ? null : new
                    {
                        step.QueryContract.Executor,
                        step.QueryContract.ProjectionQualifiedId,
                        step.QueryContract.ProjectionVersion,
                        step.QueryContract.ProjectionContentHash,
                        step.QueryContract.OutputSchemaHash,
                        step.QueryContract.Exposure,
                        step.QueryContract.Roles
                    },
                    step.ExpectedStateRevision
                })
        });
        return InteractionCanonicalJson.CanonicalizeObject(canonical);
    }
}

public enum InteractionResolutionStatus
{
    Resolved,
    NeedsInput,
    Ambiguous,
    Unknown,
    Unsupported,
    Unavailable,
    Unsafe,
    Stale
}

public static class InteractionResolutionStatusNames
{
    public static string Get(InteractionResolutionStatus status) => status switch
    {
        InteractionResolutionStatus.Resolved => "resolved",
        InteractionResolutionStatus.NeedsInput => "needs-input",
        InteractionResolutionStatus.Ambiguous => "ambiguous",
        InteractionResolutionStatus.Unknown => "unknown",
        InteractionResolutionStatus.Unsupported => "unsupported",
        InteractionResolutionStatus.Unavailable => "unavailable",
        InteractionResolutionStatus.Unsafe => "unsafe",
        InteractionResolutionStatus.Stale => "stale",
        _ => throw new InteractionContractException("INVALID_RESOLUTION_STATUS", "The resolution status is not supported.")
    };
}

public sealed record InteractionResolutionResult
{
    private InteractionResolutionResult(
        InteractionResolutionStatus status,
        InteractionProposal? proposal,
        string code,
        string safeSummary,
        IReadOnlyList<string> evidence,
        InteractionRecipeReference? recipeReference)
    {
        Status = status;
        Proposal = proposal;
        Code = code;
        SafeSummary = safeSummary;
        Evidence = evidence;
        RecipeReference = recipeReference;
    }

    public InteractionResolutionStatus Status { get; }
    public InteractionProposal? Proposal { get; }
    public string Code { get; }
    public string SafeSummary { get; }
    public IReadOnlyList<string> Evidence { get; }
    public InteractionRecipeReference? RecipeReference { get; }

    public static InteractionResolutionResult Resolved(
        InteractionProposal proposal,
        string safeSummary = "",
        IEnumerable<string>? evidence = null,
        InteractionRecipeReference? recipeReference = null)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var summary = safeSummary.Length == 0 ? string.Empty : InteractionReceiptSafety.SafeSummary(safeSummary);
        var safeEvidence = InteractionReceiptSafety.Evidence(evidence ?? []);
        if (recipeReference is not null && recipeReference.TemplateFingerprint.Length != 64)
            throw new InteractionContractException("INVALID_RECIPE_REFERENCE", "The chosen recipe reference is invalid.");
        return new(InteractionResolutionStatus.Resolved, proposal, "INTERACTION_RESOLVED", summary, safeEvidence, recipeReference);
    }

    public static InteractionResolutionResult NonResolution(
        InteractionResolutionStatus status,
        string code,
        string safeSummary,
        IEnumerable<string> evidence)
    {
        if (status == InteractionResolutionStatus.Resolved || !Enum.IsDefined(status))
            throw new InteractionContractException("INVALID_RESOLUTION_STATUS", "A non-resolution requires a supported non-resolved status.");
        code = InteractionGuard.Identifier(code, nameof(code));
        safeSummary = InteractionGuard.Bounded(safeSummary, InteractionContractLimits.SafeEvidenceText,
            "INVALID_SAFE_SUMMARY", nameof(safeSummary));
        var values = evidence.Select(x => InteractionGuard.Bounded(x, InteractionContractLimits.SafeEvidenceText,
            "INVALID_SAFE_EVIDENCE", nameof(evidence))).ToArray();
        if (values.Length > InteractionContractLimits.EvidenceItems)
            throw new InteractionContractException("INVALID_SAFE_EVIDENCE", "The evidence collection exceeds the closed limit.");
        return new(status, null, code, safeSummary, Array.AsReadOnly(values), null);
    }
}

public sealed record InteractionProviderIsolation(
    bool FilesystemDenied,
    bool ShellDenied,
    bool NetworkDenied,
    bool ArbitraryMcpDenied,
    bool ApprovalsDenied,
    bool DirectExecutionDenied)
{
    public bool IsEligible => FilesystemDenied && ShellDenied && NetworkDenied && ArbitraryMcpDenied
        && ApprovalsDenied && DirectExecutionDenied;
}

public sealed record InteractionProviderAttestation
{
    public InteractionProviderAttestation(string providerId, InteractionRoleProfile profile, InteractionProviderIsolation isolation)
    {
        ProviderId = InteractionGuard.Identifier(providerId, nameof(providerId));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Isolation = isolation ?? throw new ArgumentNullException(nameof(isolation));
    }

    public string ProviderId { get; }
    public InteractionRoleProfile Profile { get; }
    public InteractionProviderIsolation Isolation { get; }

    public InteractionProviderEligibility EvaluateEligibility()
    {
        return Isolation.IsEligible
            ? InteractionProviderEligibility.Eligible()
            : InteractionProviderEligibility.Ineligible(InteractionResolutionResult.NonResolution(
                InteractionResolutionStatus.Unavailable, "PROVIDER_ISOLATION_INSUFFICIENT",
                "No eligible isolated planner provider is available.", Array.Empty<string>()));
    }
}

public sealed record InteractionProviderEligibility
{
    private InteractionProviderEligibility(bool isEligible, InteractionResolutionResult? failure)
    {
        IsEligible = isEligible;
        Failure = failure;
    }

    public bool IsEligible { get; }
    public InteractionResolutionResult? Failure { get; }

    internal static InteractionProviderEligibility Eligible() => new(true, null);

    internal static InteractionProviderEligibility Ineligible(InteractionResolutionResult failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (failure.Status != InteractionResolutionStatus.Unavailable || failure.Proposal is not null)
            throw new InteractionContractException("INVALID_PROVIDER_FAILURE", "Provider isolation failure must be unavailable without a proposal.");
        return new(false, failure);
    }
}

public sealed record InteractionExecutionConsentReference
{
    public InteractionExecutionConsentReference(
        string resolutionReceiptId,
        string proposalFingerprint,
        string principalReference,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string idempotencyKey)
    {
        ResolutionReceiptId = InteractionGuard.Identifier(resolutionReceiptId, nameof(resolutionReceiptId));
        ProposalFingerprint = InteractionGuard.UpperSha256(proposalFingerprint, nameof(proposalFingerprint));
        PrincipalReference = InteractionGuard.Identifier(principalReference, nameof(principalReference));
        if (PrincipalReference.Length != 74
            || !PrincipalReference.StartsWith("principal.", StringComparison.Ordinal)
            || PrincipalReference[10..].Any(c => !(char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')))
            throw new InteractionContractException("INVALID_PRINCIPAL_REFERENCE", "Execution consent requires an opaque principal reference.");
        ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
        StateSpaceId = InteractionGuard.Identifier(stateSpaceId, nameof(stateSpaceId));
        IdempotencyKey = InteractionGuard.IdempotencyKey(idempotencyKey);
    }

    public string ResolutionReceiptId { get; }
    public string ProposalFingerprint { get; }
    public string PrincipalReference { get; }
    public ApplicationIdentifier ApplicationId { get; }
    public string StateSpaceId { get; }
    public string IdempotencyKey { get; }
}

public enum InteractionReplayDisposition
{
    New,
    Replay,
    Conflict
}

public static class InteractionReplay
{
    public static InteractionReplayDisposition Decide(
        string? existingIdempotencyKey,
        string? existingFingerprint,
        string candidateIdempotencyKey,
        string candidateFingerprint)
    {
        candidateIdempotencyKey = InteractionGuard.IdempotencyKey(candidateIdempotencyKey);
        candidateFingerprint = InteractionGuard.UpperSha256(candidateFingerprint, nameof(candidateFingerprint));
        if (existingIdempotencyKey is null && existingFingerprint is null) return InteractionReplayDisposition.New;
        if (existingIdempotencyKey is null || existingFingerprint is null)
            throw new InteractionContractException("INCOMPLETE_REPLAY_EVIDENCE", "Replay evidence must contain both a key and fingerprint.");
        existingIdempotencyKey = InteractionGuard.IdempotencyKey(existingIdempotencyKey);
        existingFingerprint = InteractionGuard.UpperSha256(existingFingerprint, nameof(existingFingerprint));
        if (!string.Equals(existingIdempotencyKey, candidateIdempotencyKey, StringComparison.Ordinal))
            return InteractionReplayDisposition.New;
        return string.Equals(existingFingerprint, candidateFingerprint, StringComparison.Ordinal)
            ? InteractionReplayDisposition.Replay
            : InteractionReplayDisposition.Conflict;
    }
}
