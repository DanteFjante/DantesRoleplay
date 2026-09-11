using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>Host-selected token ceiling behavior. This is never model-provided input.</summary>
[JsonConverter(typeof(SystemInnerWorkerTokenBudgetModeJsonConverter))]
public enum SystemInnerWorkerTokenBudgetMode { HardCap, MeasuredStop }

public static class SystemInnerWorkerTokenBudgetModeNames
{
    public static string Get(SystemInnerWorkerTokenBudgetMode value) => value switch
    {
        SystemInnerWorkerTokenBudgetMode.HardCap => "hard-cap",
        SystemInnerWorkerTokenBudgetMode.MeasuredStop => "measured-stop",
        _ => throw new InteractionContractException("INNER_AI_BUDGET_MODE_INVALID", "The AI token budget mode is unsupported.")
    };

    public static SystemInnerWorkerTokenBudgetMode Parse(string value) => value switch
    {
        "hard-cap" => SystemInnerWorkerTokenBudgetMode.HardCap,
        "measured-stop" => SystemInnerWorkerTokenBudgetMode.MeasuredStop,
        _ => throw new InteractionContractException("INNER_AI_BUDGET_MODE_INVALID", "The AI token budget mode is unsupported.")
    };
}

public sealed class SystemInnerWorkerTokenBudgetModeJsonConverter : JsonConverter<SystemInnerWorkerTokenBudgetMode>
{
    public override SystemInnerWorkerTokenBudgetMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? SystemInnerWorkerTokenBudgetModeNames.Parse(reader.GetString() ?? "")
            : throw new JsonException("The AI token budget mode must be a supported string.");
    public override void Write(Utf8JsonWriter writer, SystemInnerWorkerTokenBudgetMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(SystemInnerWorkerTokenBudgetModeNames.Get(value));
}

/// <summary>
/// PROPOSAL: aggregate input+output provider tokens and host-observed tool dispatches over a
/// task and all descendants/retries. These limits grant no tool authority. Existing shared
/// operation allowances and deadlines still apply independently. No persisted ledger is supplied.
/// </summary>
public sealed record SystemInnerWorkerAiBudget
{
    public const int DefaultProviderTokens = 32_768;
    public const int DefaultToolCalls = 8;
    public const int DefaultMaxConcurrentProviderRequests = 2;
    public const int MaximumProviderTokens = 131_072;
    public const int MaximumToolCalls = 16;
    public const int MaximumConcurrentProviderRequests = 4;

    [JsonConstructor]
    public SystemInnerWorkerAiBudget(int providerTokens = DefaultProviderTokens, int toolCalls = DefaultToolCalls,
        SystemInnerWorkerTokenBudgetMode mode = SystemInnerWorkerTokenBudgetMode.MeasuredStop,
        int maxConcurrentProviderRequests = DefaultMaxConcurrentProviderRequests)
    {
        if (providerTokens is < 1 or > MaximumProviderTokens || toolCalls is < 0 or > MaximumToolCalls
            || maxConcurrentProviderRequests is < 1 or > MaximumConcurrentProviderRequests)
            throw new InteractionContractException("INNER_AI_BUDGET_INVALID", "The aggregate AI budget exceeds the closed limits.");
        if (!Enum.IsDefined(mode))
            throw new InteractionContractException("INNER_AI_BUDGET_MODE_INVALID", "The AI token budget mode is unsupported.");
        ProviderTokens = providerTokens;
        ToolCalls = toolCalls;
        Mode = mode;
        MaxConcurrentProviderRequests = maxConcurrentProviderRequests;
    }

    public int ProviderTokens { get; }
    public int ToolCalls { get; }
    public SystemInnerWorkerTokenBudgetMode Mode { get; }
    public int MaxConcurrentProviderRequests { get; }

    /// <summary>Shape check only. Plan 04 must enforce remaining balances at every persisted ancestor.</summary>
    public SystemInnerWorkerAiBudget NarrowTo(SystemInnerWorkerAiBudget child) =>
        child.ProviderTokens > ProviderTokens || child.ToolCalls > ToolCalls
            ? throw new InteractionContractException("INNER_AI_BUDGET_EXPANDED", "A child AI ceiling cannot exceed its ancestor ceiling.")
            : child.Mode != Mode
                ? throw new InteractionContractException("INNER_AI_BUDGET_MODE_CHANGED", "A child AI ceiling cannot switch token budget mode.")
                : child.MaxConcurrentProviderRequests > MaxConcurrentProviderRequests
                    ? throw new InteractionContractException("INNER_AI_BUDGET_EXPANDED", "A child AI ceiling cannot raise provider concurrency.")
            : child;

    /// <summary>
    /// Pure admission calculation. Plan 04 must run the equivalent check atomically against its
    /// root and ancestors before dispatch; this method reserves or invokes nothing.
    /// </summary>
    public int ProviderReservationAmount(int requestedProviderTokens, long knownChargedProviderTokens,
        long outstandingReservedProviderTokens, int activeProviderReservations, bool hasUnknownUsage)
    {
        if (requestedProviderTokens < 1 || requestedProviderTokens > ProviderTokens || knownChargedProviderTokens < 0
            || outstandingReservedProviderTokens < 0 || activeProviderReservations < 0)
            throw new InteractionContractException("INNER_AI_RESERVATION_INVALID", "The provider admission snapshot is invalid.");
        if (hasUnknownUsage)
            throw new InteractionContractException("INNER_AI_USAGE_UNKNOWN", "Unknown provider usage blocks automatic dispatch.");
        if (knownChargedProviderTokens >= ProviderTokens)
            throw new InteractionContractException("INNER_AI_TOKEN_THRESHOLD_REACHED", "The AI token threshold has been reached.");
        var remaining = ProviderTokens - knownChargedProviderTokens;
        if (outstandingReservedProviderTokens >= remaining)
            throw new InteractionContractException("INNER_AI_TOKEN_THRESHOLD_REACHED", "Outstanding provider reservations exhaust the remaining token ceiling.");
        var remainingAfterHolds = remaining - outstandingReservedProviderTokens;
        if (activeProviderReservations >= MaxConcurrentProviderRequests)
            throw new InteractionContractException("INNER_AI_PROVIDER_IN_FLIGHT", "The host-selected provider concurrency ceiling has been reached.");
        if (Mode == SystemInnerWorkerTokenBudgetMode.MeasuredStop)
        {
            // This is a hold that stops new dispatch at the threshold; it is not a guaranteed usage bound.
            // Already admitted provider calls may overrun before their host-observed usage returns.
            return Math.Min(requestedProviderTokens, (int)remainingAfterHolds);
        }
        if (requestedProviderTokens > remainingAfterHolds)
            throw new InteractionContractException("INNER_AI_TOKEN_THRESHOLD_REACHED", "The requested provider reservation exceeds the remaining token ceiling.");
        return requestedProviderTokens;
    }
}

/// <summary>
/// Host-verified provider capability evidence for a total input+output token upper bound, including
/// provider overhead. Both fields null means unavailable. A model name, output-token setting or
/// token estimate is not proof of this bound. Only hard-cap dispatch requires this evidence;
/// measured-stop remains a host ceiling and does not claim a guaranteed total bound.
/// </summary>
public sealed record SystemInnerWorkerAiProviderTokenBound
{
    [JsonConstructor]
    public SystemInnerWorkerAiProviderTokenBound(int? maximumTotalTokens, string? evidenceReference)
    {
        if ((maximumTotalTokens is null) != (evidenceReference is null)
            || maximumTotalTokens is < 1 or > SystemInnerWorkerAiBudget.MaximumProviderTokens)
            throw new InteractionContractException("INNER_AI_PROVIDER_BOUND_INVALID", "A provider bound needs both a valid ceiling and evidence, or neither.");
        MaximumTotalTokens = maximumTotalTokens;
        EvidenceReference = evidenceReference is null ? null : InteractionGuard.Identifier(evidenceReference, nameof(evidenceReference));
    }

    public int? MaximumTotalTokens { get; }
    public string? EvidenceReference { get; }
}

/// <summary>
/// PROPOSAL: reserve before dispatch. Hard-cap requires a trusted upper bound for total provider
/// input+output tokens, including overhead; measured-stop uses a bounded admission charge and
/// permits overruns by already admitted requests. Tool calls are host-counted dispatch attempts
/// (including failed calls), not model
/// assertions. A provider request and a tool dispatch may reserve separately; neither may bypass
/// the shared operation/deadline budget. ReservationId identifies one dispatch across redelivery.
/// Every provider dispatch requires a nonzero provider-token reservation; a tool-only reservation
/// cannot authorize it. The future host executor must enforce this at the actual dispatch boundary.
/// Plan 04 derives root/ancestors from its records; the caller supplies no ancestry or balance.
/// </summary>
public sealed record SystemInnerWorkerAiReservationRequest
{
    public SystemInnerWorkerAiReservationRequest(InteractionInvocationHost host, SystemTaskDurableHandle task,
        SystemTaskAttemptIdentity attempt, string reservationId, SystemInnerWorkerAiBudget ceiling,
        int providerTokens, int toolCalls, SystemInnerWorkerAiProviderTokenBound? providerBound = null)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Task = task ?? throw new ArgumentNullException(nameof(task));
        Attempt = attempt ?? throw new ArgumentNullException(nameof(attempt));
        Ceiling = ceiling ?? throw new ArgumentNullException(nameof(ceiling));
        if (host.CommandId != task.CommandId || task.CommandId != attempt.StableCommandId)
            throw new InteractionContractException("INNER_AI_RESERVATION_IDENTITY_MISMATCH", "Reservation command and attempt identities differ.");
        if (host.Profile == InteractionExecutionProfile.Atomic)
            throw new InteractionContractException("INNER_AI_ATOMIC_UNSUPPORTED", "An atomic invocation cannot dispatch a background worker.");
        if (providerTokens < 0 || toolCalls < 0 || providerTokens > ceiling.ProviderTokens || toolCalls > ceiling.ToolCalls
            || (providerTokens == 0 && toolCalls == 0))
            throw new InteractionContractException("INNER_AI_RESERVATION_INVALID", "A nonempty reservation must fit the selected AI ceiling.");
        ReservationId = InteractionGuard.IdempotencyKey(reservationId);
        ProviderTokens = providerTokens;
        ToolCalls = toolCalls;
        ProviderBound = providerBound ?? new(null, null);
        if (ProviderBound.MaximumTotalTokens > providerTokens)
            throw new InteractionContractException("INNER_AI_RESERVATION_INVALID", "The reservation is smaller than the verified provider upper bound.");
    }

    public InteractionInvocationHost Host { get; }
    public SystemTaskDurableHandle Task { get; }
    public SystemTaskAttemptIdentity Attempt { get; }
    public string ReservationId { get; }
    public SystemInnerWorkerAiBudget Ceiling { get; }
    public int ProviderTokens { get; }
    public int ToolCalls { get; }
    public SystemInnerWorkerAiProviderTokenBound ProviderBound { get; }
    /// <summary>
    /// Deterministic identity for immutable reservation inputs. It deliberately projects host
    /// scope fields instead of serializing <see cref="InteractionInvocationHost"/> or its mutable ledger.
    /// Future plan-04 storage must retain and compare this identity before debiting a root.
    /// </summary>
    public string ReservationFingerprint => InteractionCanonicalJson.Fingerprint(
        "dantes-roleplay/system-inner-worker-ai-reservation/v1",
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            principal = Host.Principal.PrincipalId,
            applicationId = Host.ApplicationRevision.ApplicationId.Value,
            applicationRevision = Host.ApplicationRevision.Revision,
            applicationFingerprint = Host.ApplicationRevision.Fingerprint,
            Host.StateSpaceId,
            Host.GrantReference,
            Host.CommandId,
            Host.StateRevision,
            profile = InteractionExecutionProfileNames.Get(Host.Profile),
            maximumOperations = Host.Budget.MaximumOperations,
            deadlineUtc = Host.Budget.DeadlineUtc,
            taskId = Task.TaskId,
            taskCommandId = Task.CommandId,
            attemptId = Attempt.AttemptId,
            Attempt.LeaseToken,
            Attempt.FencingCounter,
            Attempt.LeaseExpiresAtUtc,
            ReservationId,
            ceiling = new
            {
                Ceiling.ProviderTokens,
                Ceiling.ToolCalls,
                mode = SystemInnerWorkerTokenBudgetModeNames.Get(Ceiling.Mode),
                Ceiling.MaxConcurrentProviderRequests
            },
            ProviderTokens,
            ToolCalls,
            providerBound = new { ProviderBound.MaximumTotalTokens, ProviderBound.EvidenceReference }
        })));

    /// <summary>Null means only that this prerequisite is present; it is not a reservation or permission.</summary>
    public InteractionInvocationResult? ProviderBoundFailure() => Ceiling.Mode == SystemInnerWorkerTokenBudgetMode.HardCap
        && ProviderTokens > 0 && ProviderBound.MaximumTotalTokens is null
        ? InteractionInvocationResult.Unavailable("INNER_AI_PROVIDER_BOUND_UNAVAILABLE",
            "This provider cannot supply a verified total-token upper bound; no hard-cap dispatch is available.")
        : null;
}

/// <summary>
/// Inert reservation evidence read from plan 04's records, not a transferable execution permit.
/// Root and ancestor debit membership is resolved/persisted by that owner in its existing task
/// storage. A matching record shape never proves persistence, authority, or a current lease.
/// A descendant normally has a different command from its root; these references alone cannot
/// establish that ancestry, even when their command IDs happen to match.
/// </summary>
public sealed record SystemInnerWorkerAiReservationEvidence
{
    [JsonConstructor]
    public SystemInnerWorkerAiReservationEvidence(string recordReference, string reservationId,
        SystemTaskDurableHandle rootTask, SystemTaskDurableHandle task, SystemTaskAttemptIdentity attempt,
        int providerTokens, int toolCalls, SystemInnerWorkerTokenBudgetMode mode = SystemInnerWorkerTokenBudgetMode.MeasuredStop)
    {
        RecordReference = InteractionGuard.Identifier(recordReference, nameof(recordReference));
        ReservationId = InteractionGuard.IdempotencyKey(reservationId);
        RootTask = rootTask ?? throw new ArgumentNullException(nameof(rootTask));
        Task = task ?? throw new ArgumentNullException(nameof(task));
        Attempt = attempt ?? throw new ArgumentNullException(nameof(attempt));
        if (task.CommandId != attempt.StableCommandId)
            throw new InteractionContractException("INNER_AI_RESERVATION_IDENTITY_MISMATCH", "Reservation command and attempt identities differ.");
        if (providerTokens is < 0 or > SystemInnerWorkerAiBudget.MaximumProviderTokens
            || toolCalls is < 0 or > SystemInnerWorkerAiBudget.MaximumToolCalls
            || (providerTokens == 0 && toolCalls == 0))
            throw new InteractionContractException("INNER_AI_RESERVATION_INVALID", "The recorded reservation is outside the closed limits.");
        if (!Enum.IsDefined(mode))
            throw new InteractionContractException("INNER_AI_BUDGET_MODE_INVALID", "The AI token budget mode is unsupported.");
        ProviderTokens = providerTokens;
        ToolCalls = toolCalls;
        Mode = mode;
    }

    public string RecordReference { get; }
    public string ReservationId { get; }
    public SystemTaskDurableHandle RootTask { get; }
    public SystemTaskDurableHandle Task { get; }
    public SystemTaskAttemptIdentity Attempt { get; }
    public int ProviderTokens { get; }
    public int ToolCalls { get; }
    public SystemInnerWorkerTokenBudgetMode Mode { get; }
}

/// <summary>
/// Inert retained usage from host/provider evidence, never authority supplied by authored/model
/// JSON. Deserialization does not establish provenance: the accounting owner must verify these
/// counts against its recorded provider response/host dispatch evidence before settlement.
/// Null input or output tokens means
/// unknown usage, not zero. AiResponse zero defaults are insufficient to establish known usage.
/// ToolCalls is the host-observed number of dispatch attempts; rejected pre-dispatch suggestions
/// are activity, not dispatched tools. Actual overrun must be retained without clamping.
/// </summary>
public sealed record SystemInnerWorkerAiUsageReport
{
    [JsonConstructor]
    public SystemInnerWorkerAiUsageReport(string reservationRecordReference, SystemTaskAttemptIdentity attempt,
        long? inputTokens, long? outputTokens, long toolCalls)
    {
        ReservationRecordReference = InteractionGuard.Identifier(reservationRecordReference, nameof(reservationRecordReference));
        Attempt = attempt ?? throw new ArgumentNullException(nameof(attempt));
        if (inputTokens < 0 || outputTokens < 0 || toolCalls < 0)
            throw new InteractionContractException("INNER_AI_USAGE_INVALID", "Reported usage cannot be negative.");
        try { _ = checked(inputTokens.GetValueOrDefault() + outputTokens.GetValueOrDefault()); }
        catch (OverflowException)
        {
            throw new InteractionContractException("INNER_AI_USAGE_INVALID", "Reported token usage cannot fit the accounting counter.");
        }
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        ToolCalls = toolCalls;
    }

    public string ReservationRecordReference { get; }
    public SystemTaskAttemptIdentity Attempt { get; }
    public long? InputTokens { get; }
    public long? OutputTokens { get; }
    public long ToolCalls { get; }
}

/// <summary>
/// Pure accounting projection, not a ledger mutation or task outcome. Unknown usage retains the
/// entire reserved amount (or a larger observed lower bound); no automatic retry/new reservation
/// may proceed in the affected root until the owner reconciles it. Excess actual usage also blocks
/// further dispatch. Cancellation/provider failure do not release an unknown reservation.
/// </summary>
public sealed record SystemInnerWorkerAiUsageReconciliation(
    long ChargedProviderTokens, long ChargedToolCalls, int ReleasedProviderTokens, int ReleasedToolCalls,
    bool UsageKnown, bool ExceededReservation, bool RequiresReconciliation,
    SystemInnerWorkerTokenBudgetMode Mode = SystemInnerWorkerTokenBudgetMode.MeasuredStop)
{
    public string Code => !UsageKnown ? "INNER_AI_USAGE_UNKNOWN"
        : ExceededReservation ? "INNER_AI_RESERVATION_EXCEEDED" : "INNER_AI_USAGE_KNOWN";

    public static SystemInnerWorkerAiUsageReconciliation Calculate(SystemInnerWorkerAiReservationEvidence reservation,
        SystemInnerWorkerAiUsageReport report)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(report);
        if (reservation.RecordReference != report.ReservationRecordReference || reservation.Attempt != report.Attempt)
            throw new InteractionContractException("INNER_AI_USAGE_IDENTITY_MISMATCH", "Usage does not refer to the reserved attempt and fence.");
        var known = report.InputTokens.HasValue && report.OutputTokens.HasValue;
        var observed = checked(report.InputTokens.GetValueOrDefault() + report.OutputTokens.GetValueOrDefault());
        var tokens = known ? observed : Math.Max(reservation.ProviderTokens, observed);
        var calls = known ? report.ToolCalls : Math.Max(reservation.ToolCalls, report.ToolCalls);
        var exceeded = observed > reservation.ProviderTokens || report.ToolCalls > reservation.ToolCalls;
        return new(tokens, calls,
            known ? (int)Math.Max(0, reservation.ProviderTokens - tokens) : 0,
            known ? (int)Math.Max(0, reservation.ToolCalls - calls) : 0,
            known, exceeded, !known || exceeded, reservation.Mode);
    }
}

/// <summary>
/// PROPOSAL ONLY; plan 04 implements this over its existing persisted root/ancestor reservations,
/// never a second task state/history. Reserve atomically debits every ancestor and checks current
/// authority, ceilings, remaining operations/deadline and lease before returning completed
/// computation with reservation evidence. Missing accounting/provider upper-bound support returns
/// unavailable (including INNER_AI_PROVIDER_BOUND_UNAVAILABLE from ProviderBoundFailure).
/// Reusing reservation identity with changed payload conflicts; unchanged replay does
/// not debit again. Retry attempts/descendants use fresh reservations against the same ancestors.
/// Reconcile atomically settles once from trusted usage evidence; identical redelivery is inert,
/// conflicting known reports fail. Later evidence may resolve unknown usage via an explicit audited
/// reconciliation. Late usage remains accountable after lease expiry without authorizing a stale
/// worker to publish a task result. Neither method invokes a provider or waits in an ECS writer.
/// Unknown usage/overrun must return failed with INNER_AI_USAGE_UNKNOWN/INNER_AI_RESERVATION_EXCEEDED
/// after retaining the debit and evidence. Host-to-owner evidence may carry a lease token; public
/// task readback must expose only safe record references and never copy the lease into OUTER/web data.
/// </summary>
public interface ISystemInnerWorkerAiBudgetAccounting
{
    Task<InteractionInvocationResult> ReserveAsync(SystemInnerWorkerAiReservationRequest request,
        CancellationToken cancellationToken = default);
    Task<InteractionInvocationResult> ReconcileAsync(InteractionInvocationHost host,
        SystemInnerWorkerAiUsageReport report, CancellationToken cancellationToken = default);
}
