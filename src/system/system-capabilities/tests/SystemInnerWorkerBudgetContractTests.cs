using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.Tests;

/// <summary>DTO and arithmetic fixtures only; no persisted reservation/execution claim.</summary>
public sealed class SystemInnerWorkerBudgetContractTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Defaults_and_narrowing_do_not_replace_operation_or_deadline_budgets()
    {
        var budget = new SystemInnerWorkerAiBudget();
        Assert.Equal(32_768, budget.ProviderTokens);
        Assert.Equal(8, budget.ToolCalls);
        Assert.Equal(SystemInnerWorkerTokenBudgetMode.MeasuredStop, budget.Mode);
        Assert.Equal(2, budget.MaxConcurrentProviderRequests);
        Assert.Equal(new SystemInnerWorkerAiBudget(1_000, 0), budget.NarrowTo(new(1_000, 0)));
        Assert.Throws<InteractionContractException>(() => budget.NarrowTo(new(32_769, 8)));
        Assert.Throws<InteractionContractException>(() => budget.NarrowTo(new(32_768, 9)));
        Assert.Equal("INNER_AI_BUDGET_MODE_CHANGED", Assert.Throws<InteractionContractException>(() =>
            budget.NarrowTo(new(1_000, 1, SystemInnerWorkerTokenBudgetMode.HardCap))).Code);
        Assert.Equal("INNER_AI_BUDGET_EXPANDED", Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerAiBudget(1_000, 1, SystemInnerWorkerTokenBudgetMode.MeasuredStop, 1)
                .NarrowTo(new(500, 1, SystemInnerWorkerTokenBudgetMode.MeasuredStop, 2))).Code);
        var request = Request();
        Assert.Equal(2, request.Host.Budget.RemainingOperations);
        Assert.Equal(100, request.ProviderTokens);
        Assert.Equal(2, request.ToolCalls);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(131_073, 8)]
    [InlineData(32_768, 17)]
    [InlineData(32_768, -1)]
    public void Aggregate_ceilings_reject_invalid_counts(int tokens, int calls) =>
        Assert.Equal("INNER_AI_BUDGET_INVALID", Assert.Throws<InteractionContractException>(() => new SystemInnerWorkerAiBudget(tokens, calls)).Code);

    [Fact]
    public void Reservation_must_fit_selected_ceiling_and_current_command_attempt()
    {
        Assert.Equal("INNER_AI_RESERVATION_INVALID", Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerAiReservationRequest(Host(), TaskHandle(), Attempt(), "reservation.1", new(10, 1), 11, 1)).Code);
        Assert.Equal("INNER_AI_RESERVATION_INVALID", Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerAiReservationRequest(Host(), TaskHandle(), Attempt(), "reservation.1", new(), 0, 0)).Code);
        Assert.Equal("INNER_AI_RESERVATION_IDENTITY_MISMATCH", Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerAiReservationRequest(Host(), new("task.other", "command.other"), Attempt(), "reservation.1", new(), 100, 1)).Code);
        Assert.Equal("INNER_AI_ATOMIC_UNSUPPORTED", Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerAiReservationRequest(Host(InteractionExecutionProfile.Atomic), TaskHandle(), Attempt(), "reservation.1", new(), 100, 1)).Code);
    }

    [Fact]
    public void Missing_provider_upper_bound_is_explicit_unavailable_not_a_hard_cap_claim()
    {
        var unavailable = Request(mode: SystemInnerWorkerTokenBudgetMode.HardCap).ProviderBoundFailure();
        Assert.NotNull(unavailable);
        Assert.Equal("INNER_AI_PROVIDER_BOUND_UNAVAILABLE", unavailable.Code);
        Assert.Equal(InteractionInvocationResultTag.Unavailable, unavailable.Tag);
        Assert.Null(unavailable.TaskHandle);
        Assert.Null(unavailable.CompletionEvidenceReference);
        Assert.Null(unavailable.Receipt);
        Assert.Null(Request(new(100, "provider.bound.evidence"), SystemInnerWorkerTokenBudgetMode.HardCap).ProviderBoundFailure());
        Assert.Throws<InteractionContractException>(() => Request(new(101, "provider.bound.evidence"), SystemInnerWorkerTokenBudgetMode.HardCap));
        var measured = Request();
        Assert.Equal(SystemInnerWorkerTokenBudgetMode.MeasuredStop, measured.Ceiling.Mode);
        Assert.Null(measured.ProviderBoundFailure());
    }

    [Fact]
    public void Tool_only_reservation_needs_no_provider_bound_and_known_zero_tokens_are_valid()
    {
        var request = new SystemInnerWorkerAiReservationRequest(Host(), TaskHandle(), Attempt(), "tool.call.1", new(), 0, 1);
        Assert.Null(request.ProviderBoundFailure());
        var evidence = new SystemInnerWorkerAiReservationEvidence("record.tool", "tool.call.1", Root(), TaskHandle(), Attempt(), 0, 1);
        var result = SystemInnerWorkerAiUsageReconciliation.Calculate(evidence, new("record.tool", Attempt(), 0, 0, 1, 0, true));
        Assert.True(result.UsageKnown);
        Assert.Equal(0, result.ChargedProviderTokens);
        Assert.Equal(1, result.ChargedToolCalls);
        Assert.False(result.RequiresReconciliation);
    }

    [Fact]
    public void Known_usage_releases_only_the_unused_reserved_amount()
    {
        var result = SystemInnerWorkerAiUsageReconciliation.Calculate(Reservation(), new("record.1", Attempt(), 10, 5, 1, 15, true));
        Assert.Equal(new SystemInnerWorkerAiUsageReconciliation(15, 1, 85, 1, true, false, false, SystemInnerWorkerTokenBudgetMode.MeasuredStop), result);
        Assert.Equal(100, result.ChargedProviderTokens + result.ReleasedProviderTokens);
        Assert.Equal(2, result.ChargedToolCalls + result.ReleasedToolCalls);
    }

    [Theory]
    [InlineData(true, 20L, 80)]
    [InlineData(false, 100L, 0)]
    public void Independent_total_includes_overhead_and_partial_evidence_cannot_release_holds(bool complete, long charged, int released)
    {
        var report = new SystemInnerWorkerAiUsageReport("record.1", Attempt(), 10, 5, 1, 20, complete);
        var result = SystemInnerWorkerAiUsageReconciliation.Calculate(Reservation(), report);
        Assert.Equal(charged, result.ChargedProviderTokens);
        Assert.Equal(released, result.ReleasedProviderTokens);
        Assert.Equal(complete, result.UsageKnown);
    }

    [Fact]
    public void Complete_components_without_total_remain_unknown_and_partial_overrun_is_retained()
    {
        var missing = SystemInnerWorkerAiUsageReconciliation.Calculate(Reservation(), new("record.1", Attempt(), 10, 5, 1, isComplete: true));
        Assert.False(missing.UsageKnown);
        Assert.Equal(100, missing.ChargedProviderTokens);
        var partial = SystemInnerWorkerAiUsageReconciliation.Calculate(Reservation(), new("record.1", Attempt(), 10, 5, 1, 130));
        Assert.Equal(130, partial.ChargedProviderTokens);
        Assert.Equal(0, partial.ReleasedProviderTokens);
        Assert.True(partial.RequiresReconciliation);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, 5L)]
    [InlineData(10L, null)]
    public void Unknown_provider_usage_keeps_the_entire_reservation_charged(long? input, long? output)
    {
        var result = SystemInnerWorkerAiUsageReconciliation.Calculate(Reservation(), new("record.1", Attempt(), input, output, 1));
        Assert.Equal(new SystemInnerWorkerAiUsageReconciliation(100, 2, 0, 0, false, false, true, SystemInnerWorkerTokenBudgetMode.MeasuredStop), result);
        Assert.Equal("INNER_AI_USAGE_UNKNOWN", result.Code);
    }

    [Theory]
    [InlineData(150L, 10L, true)]
    [InlineData(150L, null, false)]
    public void Observed_overrun_is_never_clamped_to_reservation(long input, long? output, bool known)
    {
        var result = SystemInnerWorkerAiUsageReconciliation.Calculate(Reservation(), new("record.1", Attempt(), input, output, 3,
            known ? input + output : null, known));
        Assert.Equal(input + output.GetValueOrDefault(), result.ChargedProviderTokens);
        Assert.Equal(3, result.ChargedToolCalls);
        Assert.Equal(0, result.ReleasedProviderTokens);
        Assert.Equal(0, result.ReleasedToolCalls);
        Assert.Equal(known, result.UsageKnown);
        Assert.True(result.ExceededReservation);
        Assert.Equal(!known, result.RequiresReconciliation);
        Assert.Equal(known ? "INNER_AI_RESERVATION_EXCEEDED" : "INNER_AI_USAGE_UNKNOWN", result.Code);
    }

    [Theory]
    [InlineData("record")]
    [InlineData("attempt")]
    [InlineData("fence")]
    [InlineData("lease")]
    public void Usage_cannot_settle_a_different_reservation_attempt_or_fence(string change)
    {
        var attempt = new SystemTaskAttemptIdentity("command.1", change == "attempt" ? "attempt.other" : "attempt.1",
            change == "lease" ? "lease.other" : "lease.1", change == "fence" ? 2 : 1, Attempt().LeaseExpiresAtUtc);
        var report = new SystemInnerWorkerAiUsageReport(change == "record" ? "record.other" : "record.1", attempt, 10, 5, 1);
        Assert.Equal("INNER_AI_USAGE_IDENTITY_MISMATCH", Assert.Throws<InteractionContractException>(() =>
            SystemInnerWorkerAiUsageReconciliation.Calculate(Reservation(), report)).Code);
    }

    [Fact]
    public void Inert_evidence_round_trips_but_host_authority_cannot_be_imported_or_exported()
    {
        var reservation = Reservation();
        var json = JsonSerializer.Serialize(reservation, Wire);
        Assert.Contains("\"rootTask\":", json);
        Assert.Equal(reservation, JsonSerializer.Deserialize<SystemInnerWorkerAiReservationEvidence>(json, Wire));
        var report = new SystemInnerWorkerAiUsageReport("record.1", Attempt(), null, 0, 0);
        var usageJson = JsonSerializer.Serialize(report, Wire);
        Assert.Contains("\"inputTokens\":null", usageJson);
        Assert.Equal(report, JsonSerializer.Deserialize<SystemInnerWorkerAiUsageReport>(usageJson, Wire));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(Request(), Wire));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SystemInnerWorkerAiReservationRequest>("{\"host\":{}}", Wire));
    }

    [Fact]
    public void Invalid_provider_reports_require_correction_instead_of_overflow_or_negative_credit()
    {
        Assert.Throws<InteractionContractException>(() => new SystemInnerWorkerAiUsageReport("record.1", Attempt(), -1, 0, 0));
        Assert.Throws<InteractionContractException>(() => new SystemInnerWorkerAiUsageReport("record.1", Attempt(), long.MaxValue, 1, 0));
        Assert.Throws<InteractionContractException>(() => new SystemInnerWorkerAiUsageReport("record.1", Attempt(), 0, 0, -1));
    }

    [Fact]
    public void Actual_tool_count_above_the_global_ceiling_is_retained_as_an_overrun()
    {
        var result = SystemInnerWorkerAiUsageReconciliation.Calculate(Reservation(),
            new("record.1", Attempt(), 0, 0, SystemInnerWorkerAiBudget.MaximumToolCalls + 1, 0, true));
        Assert.Equal(17, result.ChargedToolCalls);
        Assert.True(result.ExceededReservation);
        Assert.False(result.RequiresReconciliation);
    }

    [Fact]
    public void Complete_overrun_settles_but_blocks_new_admission_at_the_threshold()
    {
        var settled = SystemInnerWorkerAiUsageReconciliation.Calculate(Reservation(), new("record.1", Attempt(), 150, 10, 3, 160, true));
        Assert.False(settled.RequiresReconciliation);
        Assert.Equal(160, settled.ChargedProviderTokens);
        Assert.Equal("INNER_AI_TOKEN_THRESHOLD_REACHED", Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerAiBudget(100, 1).ProviderReservationAmount(1, settled.ChargedProviderTokens, 0, 0, false)).Code);
    }

    [Fact]
    public void Mode_uses_stable_strings_and_rejects_numeric_or_unknown_values()
    {
        var json = JsonSerializer.Serialize(new SystemInnerWorkerAiBudget(100, 1, SystemInnerWorkerTokenBudgetMode.HardCap), Wire);
        Assert.Contains("\"mode\":\"hard-cap\"", json);
        Assert.Equal(SystemInnerWorkerTokenBudgetMode.HardCap, JsonSerializer.Deserialize<SystemInnerWorkerAiBudget>(json, Wire)!.Mode);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SystemInnerWorkerAiBudget>("{\"providerTokens\":100,\"toolCalls\":1,\"mode\":1}", Wire));
        Assert.Throws<InteractionContractException>(() => SystemInnerWorkerTokenBudgetModeNames.Parse("unknown"));
    }

    [Fact]
    public void Measured_stop_admission_holds_partial_remaining_and_blocks_busy_or_unknown_roots()
    {
        var budget = new SystemInnerWorkerAiBudget(100, 1);
        Assert.Equal(10, budget.ProviderReservationAmount(50, 90, 0, 0, false));
        Assert.Equal(5, budget.ProviderReservationAmount(50, 90, 5, 1, false));
        Assert.Equal("INNER_AI_PROVIDER_IN_FLIGHT", Assert.Throws<InteractionContractException>(() =>
            budget.ProviderReservationAmount(10, 10, 1, 2, false)).Code);
        Assert.Equal("INNER_AI_USAGE_UNKNOWN", Assert.Throws<InteractionContractException>(() =>
            budget.ProviderReservationAmount(10, 10, 0, 0, true)).Code);
        Assert.Equal("INNER_AI_TOKEN_THRESHOLD_REACHED", Assert.Throws<InteractionContractException>(() =>
            budget.ProviderReservationAmount(1, 100, 0, 0, false)).Code);
    }

    [Fact]
    public void Hard_cap_admission_never_shrinks_a_reservation()
    {
        var budget = new SystemInnerWorkerAiBudget(100, 1, SystemInnerWorkerTokenBudgetMode.HardCap);
        Assert.Equal(10, budget.ProviderReservationAmount(10, 90, 0, 0, false));
        Assert.Equal("INNER_AI_TOKEN_THRESHOLD_REACHED", Assert.Throws<InteractionContractException>(() =>
            budget.ProviderReservationAmount(11, 90, 0, 0, false)).Code);
    }

    [Fact]
    public void Reservation_fingerprint_binds_mode_and_concurrency_without_serializing_host_authority()
    {
        var measured = Request(mode: SystemInnerWorkerTokenBudgetMode.MeasuredStop);
        var hard = Request(mode: SystemInnerWorkerTokenBudgetMode.HardCap);
        var concurrent = new SystemInnerWorkerAiReservationRequest(Host(), TaskHandle(), Attempt(), "reservation.1",
            new(32_768, 8, SystemInnerWorkerTokenBudgetMode.MeasuredStop, 3), 100, 2);
        Assert.NotEqual(measured.ReservationFingerprint, hard.ReservationFingerprint);
        Assert.NotEqual(measured.ReservationFingerprint, concurrent.ReservationFingerprint);
        Assert.Equal(measured.ReservationFingerprint, Request().ReservationFingerprint);
    }

    [Theory]
    [InlineData(SystemInnerWorkerTokenBudgetMode.HardCap)]
    [InlineData(SystemInnerWorkerTokenBudgetMode.MeasuredStop)]
    public void Unknown_and_overrun_usage_remain_unclamped_in_both_modes(SystemInnerWorkerTokenBudgetMode mode)
    {
        var evidence = new SystemInnerWorkerAiReservationEvidence("record.1", "reservation.1", Root(), TaskHandle(), Attempt(), 100, 2, mode);
        var unknown = SystemInnerWorkerAiUsageReconciliation.Calculate(evidence, new("record.1", Attempt(), null, 0, 1));
        var overrun = SystemInnerWorkerAiUsageReconciliation.Calculate(evidence, new("record.1", Attempt(), 150, 10, 3, 160, true));
        Assert.Equal(mode, unknown.Mode);
        Assert.Equal(mode, overrun.Mode);
        Assert.True(unknown.RequiresReconciliation);
        Assert.Equal(100, unknown.ChargedProviderTokens);
        Assert.True(overrun.ExceededReservation);
        Assert.False(overrun.RequiresReconciliation);
        Assert.Equal(160, overrun.ChargedProviderTokens);
    }

    private static SystemInnerWorkerAiReservationRequest Request(SystemInnerWorkerAiProviderTokenBound? bound = null,
        SystemInnerWorkerTokenBudgetMode mode = SystemInnerWorkerTokenBudgetMode.MeasuredStop) =>
        new(Host(), TaskHandle(), Attempt(), "reservation.1", new(32_768, 8, mode), 100, 2, bound);
    private static SystemTaskDurableHandle TaskHandle() => new("task.1", "command.1");
    private static SystemTaskDurableHandle Root() => new("task.root", "command.root");
    private static SystemTaskAttemptIdentity Attempt() => new("command.1", "attempt.1", "lease.1", 1, new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc));
    private static SystemInnerWorkerAiReservationEvidence Reservation() => new("record.1", "reservation.1", Root(), TaskHandle(), Attempt(), 100, 2);
    private static InteractionInvocationHost Host(InteractionExecutionProfile profile = InteractionExecutionProfile.Workflow) => new(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "fixture"),
        new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []), "state.1", "grant.1", "command.1", "revision.1",
        profile, new InteractionInvocationBudget(2, new DateTime(2026, 9, 11, 12, 1, 0, DateTimeKind.Utc)));
}
