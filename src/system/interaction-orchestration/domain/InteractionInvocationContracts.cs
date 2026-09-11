using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.Interactions;

[JsonConverter(typeof(InteractionExecutionProfileJsonConverter))]
public enum InteractionExecutionProfile { ReadOnly, Atomic, Workflow }

public static class InteractionExecutionProfileNames
{
    public static string Get(InteractionExecutionProfile value) => value switch
    {
        InteractionExecutionProfile.ReadOnly => "read-only",
        InteractionExecutionProfile.Atomic => "atomic",
        InteractionExecutionProfile.Workflow => "workflow",
        _ => throw new InteractionContractException("INVALID_EXECUTION_PROFILE", "The execution profile is not supported.")
    };
}

public sealed class InteractionInvocationBudget
{
    private readonly InvocationBudgetLedger _ledger;
    private readonly InteractionInvocationBudget? _parent;
    private int _consumed;

    public InteractionInvocationBudget(int maximumOperations, DateTime deadlineUtc)
        : this(maximumOperations, deadlineUtc, new InvocationBudgetLedger(maximumOperations), null) { }

    private InteractionInvocationBudget(int maximumOperations, DateTime deadlineUtc, InvocationBudgetLedger ledger,
        InteractionInvocationBudget? parent)
    {
        if (maximumOperations is < 1 or > InteractionContractLimits.ProposalSteps)
            throw new InteractionContractException("INVALID_INVOCATION_BUDGET", "The operation budget is outside the closed limit.");
        if (deadlineUtc.Kind != DateTimeKind.Utc)
            throw new InteractionContractException("INVALID_INVOCATION_DEADLINE", "The invocation deadline must be UTC.");
        MaximumOperations = maximumOperations;
        DeadlineUtc = deadlineUtc;
        _ledger = ledger;
        _parent = parent;
    }
    public int MaximumOperations { get; }
    public DateTime DeadlineUtc { get; }
    public int RemainingOperations
    {
        get
        {
            lock (_ledger)
                return Math.Min(MaximumOperations - _consumed, _parent?.RemainingOperations ?? _ledger.Remaining);
        }
    }
    public InteractionInvocationBudget Child(int maximumOperations, DateTime deadlineUtc) =>
        maximumOperations > MaximumOperations || deadlineUtc > DeadlineUtc
            ? throw new InteractionContractException("CHILD_BUDGET_EXPANDED", "A child invocation cannot expand its parent budget.")
            : new(maximumOperations, deadlineUtc, _ledger, this);

    /// <summary>Consumes one shared root allowance; retries are new attempts and consume again.</summary>
    public bool TryConsumeOperation()
    {
        lock (_ledger)
        {
            if (!CanConsume() || !_ledger.TryConsume()) return false;
            ConsumeThroughAncestors();
            return true;
        }
    }

    private bool CanConsume() => _consumed < MaximumOperations && (_parent?.CanConsume() ?? true);

    private void ConsumeThroughAncestors()
    {
        _consumed++;
        _parent?.ConsumeThroughAncestors();
    }

    private sealed class InvocationBudgetLedger(int maximum)
    {
        private int _remaining = maximum;
        public int Remaining => _remaining;
        public bool TryConsume()
        {
            if (_remaining == 0) return false;
            _remaining--;
            return true;
        }
    }
}

/// <summary>Trusted host-only authority. Its converter rejects authored JSON outright.</summary>
[JsonConverter(typeof(RejectInteractionInvocationHostJsonConverter))]
public sealed record InteractionInvocationHost
{
    public InteractionInvocationHost(TrustedPrincipalContext principal, ApplicationRevision applicationRevision,
        string stateSpaceId, string grantReference, string commandId, string stateRevision,
        InteractionExecutionProfile profile, InteractionInvocationBudget budget, string? parentCommandId = null)
    {
        Principal = principal ?? throw new ArgumentNullException(nameof(principal));
        ArgumentNullException.ThrowIfNull(applicationRevision);
        if (!principal.Verified) throw new InteractionContractException("UNVERIFIED_PRINCIPAL", "An invocation host requires a verified principal.");
        if (applicationRevision.Revision < 1 || applicationRevision.BaseApplications is null)
            throw new InteractionContractException("INVALID_APPLICATION_REVISION", "The application revision is invalid.");
        InteractionGuard.UpperSha256(applicationRevision.Fingerprint, nameof(applicationRevision.Fingerprint));
        ApplicationRevision = applicationRevision with { BaseApplications = Array.AsReadOnly(applicationRevision.BaseApplications.ToArray()) };
        StateSpaceId = InteractionGuard.Identifier(stateSpaceId, nameof(stateSpaceId));
        GrantReference = InteractionGuard.Identifier(grantReference, nameof(grantReference));
        CommandId = InteractionGuard.IdempotencyKey(commandId);
        StateRevision = InteractionGuard.Identifier(stateRevision, nameof(stateRevision));
        if (!Enum.IsDefined(profile)) throw new InteractionContractException("INVALID_EXECUTION_PROFILE", "The execution profile is not supported.");
        Profile = profile;
        Budget = budget ?? throw new ArgumentNullException(nameof(budget));
        ParentCommandId = parentCommandId is null ? null : InteractionGuard.IdempotencyKey(parentCommandId);
    }
    public TrustedPrincipalContext Principal { get; }
    public ApplicationRevision ApplicationRevision { get; }
    public string StateSpaceId { get; }
    public string GrantReference { get; }
    public string CommandId { get; }
    public string StateRevision { get; }
    public InteractionExecutionProfile Profile { get; }
    public InteractionInvocationBudget Budget { get; }
    public string? ParentCommandId { get; }
}

public enum InteractionInvocationResultTag { Completed, Proposed, Committed, Pending, Failed, Cancelled, Unavailable }
public static class InteractionInvocationResultTagNames
{
    public static string Get(InteractionInvocationResultTag value) => value switch
    {
        InteractionInvocationResultTag.Completed => "completed", InteractionInvocationResultTag.Proposed => "proposed",
        InteractionInvocationResultTag.Committed => "committed", InteractionInvocationResultTag.Pending => "pending",
        InteractionInvocationResultTag.Failed => "failed", InteractionInvocationResultTag.Cancelled => "cancelled",
        InteractionInvocationResultTag.Unavailable => "unavailable",
        _ => throw new InteractionContractException("INVALID_INVOCATION_RESULT", "The invocation result tag is not supported.")
    };
}

public sealed record InteractionInvocationReadEvidence(string StateSpaceFingerprint, string ResolutionFingerprint,
    string OutputSchemaHash, string ResultFingerprint, string SourceRevisionFingerprint)
{
    public InteractionInvocationReadEvidence Validate()
    {
        InteractionGuard.UpperSha256(StateSpaceFingerprint, nameof(StateSpaceFingerprint));
        InteractionGuard.UpperSha256(ResolutionFingerprint, nameof(ResolutionFingerprint));
        InteractionGuard.UpperSha256(OutputSchemaHash, nameof(OutputSchemaHash));
        InteractionGuard.UpperSha256(ResultFingerprint, nameof(ResultFingerprint));
        InteractionGuard.UpperSha256(SourceRevisionFingerprint, nameof(SourceRevisionFingerprint));
        return this;
    }
}

public sealed record InteractionInvocationCommitReceipt(string OperationId, string RequestFingerprint,
    IReadOnlyList<ApplicationEcsEffectReceipt> Effects, bool EffectDetailsAvailable = true)
{
    public InteractionInvocationCommitReceipt Validate()
    {
        if (OperationId is not { Length: 32 } || OperationId.Any(value => !(char.IsAsciiDigit(value) || value is >= 'a' and <= 'f')))
            throw new InteractionContractException("INVALID_OPERATION_ID", "The commit receipt needs an operation ID.");
        InteractionGuard.UpperSha256(RequestFingerprint, nameof(RequestFingerprint));
        ArgumentNullException.ThrowIfNull(Effects);
        if (!EffectDetailsAvailable && Effects.Count != 0)
            throw new InteractionContractException("INVALID_COMMIT_RECEIPT", "Unavailable effect details cannot include a partial effect list.");
        return this;
    }
}

[JsonConverter(typeof(InteractionInvocationResultJsonConverter))]
public sealed record InteractionInvocationResult
{
    private InteractionInvocationResult(InteractionInvocationResultTag tag, string code, string safeMessage,
        string? dataJson, InteractionInvocationReadEvidence? readEvidence, InteractionInvocationCommitReceipt? receipt,
        InteractionProposalProjection? proposal, SystemTaskDurableHandle? taskHandle, string? completionEvidenceReference,
        IReadOnlyList<InteractionInvocationCommitReceipt>? previousCommits,
        ApplicationEcsExecutionIdentity? recoveryIdentity = null)
    {
        Tag = tag; Code = InteractionGuard.Identifier(code, nameof(code));
        SafeMessage = InteractionGuard.Bounded(safeMessage, InteractionContractLimits.SafeEvidenceText, "INVALID_SAFE_MESSAGE", nameof(safeMessage));
        DataJson = dataJson is null ? null : InteractionCanonicalJson.Canonicalize(dataJson);
        ReadEvidence = readEvidence;
        Receipt = CopyReceipt(receipt);
        Proposal = proposal;
        TaskHandle = taskHandle;
        CompletionEvidenceReference = completionEvidenceReference is null ? null : InteractionGuard.Identifier(completionEvidenceReference, nameof(completionEvidenceReference));
        PreviousCommits = Array.AsReadOnly(previousCommits?.Select(value => CopyReceipt(value)!).ToArray() ?? []);
        RecoveryIdentity = recoveryIdentity;
        Validate();
    }
    public InteractionInvocationResultTag Tag { get; }
    public string WireTag => InteractionInvocationResultTagNames.Get(Tag);
    public string Code { get; }
    public string SafeMessage { get; }
    public string? DataJson { get; }
    public InteractionInvocationReadEvidence? ReadEvidence { get; }
    public InteractionInvocationCommitReceipt? Receipt { get; }
    public InteractionProposalProjection? Proposal { get; }
    public SystemTaskDurableHandle? TaskHandle { get; }
    public string? CompletionEvidenceReference { get; }
    public IReadOnlyList<InteractionInvocationCommitReceipt> PreviousCommits { get; }
    public ApplicationEcsExecutionIdentity? RecoveryIdentity { get; }
    public string ToJson() => JsonSerializer.Serialize(new
    {
        tag = WireTag, code = Code, message = SafeMessage, dataJson = DataJson, readEvidence = ReadEvidence,
        receipt = Receipt, proposal = Proposal, pending = TaskHandle, completionEvidenceReference = CompletionEvidenceReference,
        previousCommits = PreviousCommits, recoveryIdentity = RecoveryIdentity
    }, WireJson);
    public static InteractionInvocationResult Completed(string dataJson, InteractionInvocationReadEvidence evidence) =>
        new(InteractionInvocationResultTag.Completed, "INVOCATION_COMPLETED", "The invocation completed.",
            dataJson, evidence, null, null, null, null, null);
    public static InteractionInvocationResult CompletedComputation(string dataJson, string evidenceReference,
        IReadOnlyList<InteractionInvocationCommitReceipt>? previousCommits = null) =>
        new(InteractionInvocationResultTag.Completed, "INVOCATION_COMPLETED", "The invocation completed.",
            dataJson, null, null, null, null, evidenceReference, previousCommits);
    public static InteractionInvocationResult Proposed(InteractionProposalProjection proposal) =>
        new(InteractionInvocationResultTag.Proposed, "INVOCATION_PROPOSED", "The invocation produced a proposal.",
            null, null, null, proposal, null, null, null);
    public static InteractionInvocationResult Committed(InteractionInvocationCommitReceipt receipt) =>
        new(InteractionInvocationResultTag.Committed, "INVOCATION_COMMITTED", "The invocation committed.",
            null, null, receipt, null, null, null, null);
    public static InteractionInvocationResult Pending(SystemTaskDurableHandle handle,
        IReadOnlyList<InteractionInvocationCommitReceipt>? previousCommits = null) =>
        new(InteractionInvocationResultTag.Pending, "INVOCATION_PENDING", "The invocation is pending.",
            null, null, null, null, handle, null, previousCommits);
    public static InteractionInvocationResult Failed(string code, string message,
        IReadOnlyList<InteractionInvocationCommitReceipt>? previousCommits = null,
        ApplicationEcsExecutionIdentity? recoveryIdentity = null) =>
        new(InteractionInvocationResultTag.Failed, code, message, null, null, null, null, null, null, previousCommits, recoveryIdentity);

    public static InteractionInvocationResult Cancelled(string code, string message,
        IReadOnlyList<InteractionInvocationCommitReceipt>? previousCommits = null,
        ApplicationEcsExecutionIdentity? recoveryIdentity = null) =>
        new(InteractionInvocationResultTag.Cancelled, code, message, null, null, null, null, null, null, previousCommits, recoveryIdentity);

    public static InteractionInvocationResult Unavailable(string code, string message,
        ApplicationEcsExecutionIdentity? recoveryIdentity = null) =>
        new(InteractionInvocationResultTag.Unavailable, code, message, null, null, null, null, null, null, null, recoveryIdentity);

    private static InteractionInvocationCommitReceipt? CopyReceipt(InteractionInvocationCommitReceipt? receipt)
    {
        if (receipt is null) return null;
        receipt.Validate();
        return receipt with { Effects = Array.AsReadOnly(receipt.Effects.ToArray()) };
    }
    private void Validate()
    {
        var shape = Tag switch
        {
            InteractionInvocationResultTag.Completed => DataJson is not null && (ReadEvidence is not null ^ CompletionEvidenceReference is not null) && Receipt is null && Proposal is null && TaskHandle is null,
            InteractionInvocationResultTag.Proposed => DataJson is null && ReadEvidence is null && Receipt is null && Proposal is not null && TaskHandle is null && CompletionEvidenceReference is null,
            InteractionInvocationResultTag.Committed => DataJson is null && ReadEvidence is null && Receipt is not null && Proposal is null && TaskHandle is null && CompletionEvidenceReference is null,
            InteractionInvocationResultTag.Pending => DataJson is null && ReadEvidence is null && Receipt is null && Proposal is null && TaskHandle is not null && CompletionEvidenceReference is null,
            _ => DataJson is null && ReadEvidence is null && Receipt is null && Proposal is null && TaskHandle is null && CompletionEvidenceReference is null
        };
        if (!shape) throw new InteractionContractException("INVALID_INVOCATION_RESULT", "The invocation result carries fields outside its tag.");
        var permitsPriorCommits = Tag is InteractionInvocationResultTag.Pending or InteractionInvocationResultTag.Failed or InteractionInvocationResultTag.Cancelled
            || (Tag == InteractionInvocationResultTag.Completed && CompletionEvidenceReference is not null);
        if (!permitsPriorCommits && PreviousCommits.Count != 0)
            throw new InteractionContractException("INVALID_INVOCATION_RESULT", "Only completed computations, pending, failed, or cancelled results may retain prior commits.");
        if (PreviousCommits.Count > InteractionContractLimits.EvidenceItems)
            throw new InteractionContractException("INVALID_INVOCATION_RESULT", "The prior commit collection exceeds its bound.");
        if (RecoveryIdentity is not null)
        {
            if (Tag is not (InteractionInvocationResultTag.Failed or InteractionInvocationResultTag.Cancelled or InteractionInvocationResultTag.Unavailable))
                throw new InteractionContractException("INVALID_INVOCATION_RESULT", "Only unresolved outcomes may carry a recovery identity.");
            new InteractionInvocationCommitReceipt(RecoveryIdentity.OperationId, RecoveryIdentity.RequestFingerprint, []).Validate();
        }
        ReadEvidence?.Validate(); Receipt?.Validate(); foreach (var prior in PreviousCommits) prior.Validate();
    }

    private static readonly JsonSerializerOptions WireJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}

public static class InteractionInvocationIdentity
{
    public static string Fingerprint(InteractionInvocationHost host, string exactId, int version, string contentFingerprint,
        IReadOnlyDictionary<string, string> roles, string canonicalInput) => InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/interaction-invocation-command/v1", InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                principal = host.Principal.PrincipalId, applicationId = host.ApplicationRevision.ApplicationId.Value,
                applicationRevision = host.ApplicationRevision.Revision, applicationFingerprint = host.ApplicationRevision.Fingerprint,
                host.StateSpaceId, host.StateRevision, host.GrantReference, profile = InteractionExecutionProfileNames.Get(host.Profile),
                exactId, version, contentFingerprint, roles = roles.OrderBy(value => value.Key, StringComparer.Ordinal), input = canonicalInput
            })));
    public static string OperationId(InteractionInvocationHost host) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes("application.action.execute\n" + host.Principal.PrincipalId + "\n" + host.CommandId)))[..32].ToLowerInvariant();
}

public static class InteractionInvocationRoles
{
    public static IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > 32)
            throw new InteractionContractException("INVALID_ROLE_BINDINGS", "The role bindings exceed the closed limit.");
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (role, entity) in values)
        {
            var safeRole = InteractionGuard.Identifier(role, "role");
            var safeEntity = InteractionGuard.Identifier(entity, "entity");
            if (!result.TryAdd(safeRole, safeEntity))
                throw new InteractionContractException("INVALID_ROLE_BINDINGS", "The role bindings contain duplicate roles.");
        }
        return result;
    }
}

public sealed class RejectInteractionInvocationHostJsonConverter : JsonConverter<InteractionInvocationHost>
{
    public override InteractionInvocationHost Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("Invocation host authority is supplied by the host and cannot be deserialized.");
    public override void Write(Utf8JsonWriter writer, InteractionInvocationHost value, JsonSerializerOptions options) =>
        throw new JsonException("Invocation host authority is not serializable.");
}

public sealed class InteractionExecutionProfileJsonConverter : JsonConverter<InteractionExecutionProfile>
{
    public override InteractionExecutionProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? reader.GetString() switch
            {
                "read-only" => InteractionExecutionProfile.ReadOnly,
                "atomic" => InteractionExecutionProfile.Atomic,
                "workflow" => InteractionExecutionProfile.Workflow,
                _ => throw new JsonException("The execution profile is unsupported.")
            }
            : throw new JsonException("The execution profile must be a string.");
    public override void Write(Utf8JsonWriter writer, InteractionExecutionProfile value, JsonSerializerOptions options) =>
        writer.WriteStringValue(InteractionExecutionProfileNames.Get(value));
}

public sealed class InteractionInvocationResultJsonConverter : JsonConverter<InteractionInvocationResult>
{
    public override InteractionInvocationResult Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("Invocation results are emitted by the host and cannot be accepted as input.");

    public override void Write(Utf8JsonWriter writer, InteractionInvocationResult value, JsonSerializerOptions options)
    {
        using var document = JsonDocument.Parse(value.ToJson());
        document.RootElement.WriteTo(writer);
    }
}
