using System.Text.Json.Serialization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>
/// PROPOSAL: aggregate input+output provider tokens and host-observed tool dispatches over a
/// task and all descendants/retries. These limits grant no tool authority. Existing shared
/// operation allowances and deadlines still apply independently. No persisted ledger is supplied.
/// </summary>
public sealed record SystemInnerWorkerAiBudget
{
    public const int DefaultProviderTokens = 32_768;
    public const int DefaultToolCalls = 8;
    public const int MaximumProviderTokens = 131_072;
    public const int MaximumToolCalls = 16;

    [JsonConstructor]
    public SystemInnerWorkerAiBudget(int providerTokens = DefaultProviderTokens, int toolCalls = DefaultToolCalls)
    {
        if (providerTokens is < 1 or > MaximumProviderTokens || toolCalls is < 0 or > MaximumToolCalls)
            throw new InteractionContractException("INNER_AI_BUDGET_INVALID", "The aggregate AI budget exceeds the closed limits.");
        ProviderTokens = providerTokens;
        ToolCalls = toolCalls;
    }

    public int ProviderTokens { get; }
    public int ToolCalls { get; }

    /// <summary>Shape check only. Plan 04 must enforce remaining balances at every persisted ancestor.</summary>
    public SystemInnerWorkerAiBudget NarrowTo(SystemInnerWorkerAiBudget child) =>
        child.ProviderTokens > ProviderTokens || child.ToolCalls > ToolCalls
            ? throw new InteractionContractException("INNER_AI_BUDGET_EXPANDED", "A child AI ceiling cannot exceed its ancestor ceiling.")
            : child;
}

/// <summary>
/// Host-verified provider capability evidence for a total input+output token upper bound, including
/// provider overhead. Both fields null means unavailable. A model name, output-token setting or
/// token estimate is not proof of this bound. No measured-only fallback is implied by this contract.
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
/// PROPOSAL: reserve before dispatch, using a trusted upper bound for total provider input+output
/// tokens, including system/context/tool overhead. No safe provider bound means unavailable and
/// no dispatch. Tool calls are host-counted dispatch attempts (including failed calls), not model
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

    /// <summary>Null means only that this prerequisite is present; it is not a reservation or permission.</summary>
    public InteractionInvocationResult? ProviderBoundFailure() => ProviderTokens > 0 && ProviderBound.MaximumTotalTokens is null
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
        int providerTokens, int toolCalls)
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
        ProviderTokens = providerTokens;
        ToolCalls = toolCalls;
    }

    public string RecordReference { get; }
    public string ReservationId { get; }
    public SystemTaskDurableHandle RootTask { get; }
    public SystemTaskDurableHandle Task { get; }
    public SystemTaskAttemptIdentity Attempt { get; }
    public int ProviderTokens { get; }
    public int ToolCalls { get; }
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
    bool UsageKnown, bool ExceededReservation, bool RequiresReconciliation)
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
            known, exceeded, !known || exceeded);
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
