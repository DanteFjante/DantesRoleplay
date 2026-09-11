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
        Assert.Equal(new SystemInnerWorkerAiBudget(1_000, 0), budget.NarrowTo(new(1_000, 0)));
        Assert.Throws<InteractionContractException>(() => budget.NarrowTo(new(32_769, 8)));
        Assert.Throws<InteractionContractException>(() => budget.NarrowTo(new(32_768, 9)));
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
        var unavailable = Request().ProviderBoundFailure();
        Assert.NotNull(unavailable);
        Assert.Equal("INNER_AI_PROVIDER_BOUND_UNAVAILABLE", unavailable.Code);
        Assert.Equal(InteractionInvocationResultTag.Unavailable, unavailable.Tag);
        Assert.Null(unavailable.TaskHandle);
        Assert.Null(unavailable.CompletionEvidenceReference);
        Assert.Null(unavailable.Receipt);
        Assert.Null(Request(new(100, "provider.bound.evidence")).ProviderBoundFailure());
        Assert.Throws<InteractionContractException>(() => Request(new(101, "provider.bound.evidence")));
    }

    [Fact]
    public void Tool_only_reservation_needs_no_provider_bound_and_known_zero_tokens_are_valid()
    {
        var request = new SystemInnerWorkerAiReservationRequest(Host(), TaskHandle(), Attempt(), "tool.call.1", new(), 0, 1);
        Assert.Null(request.ProviderBoundFailure());
        var evidence = new SystemInnerWorkerAiReservationEvidence("record.tool", "tool.call.1", Root(), TaskHandle(), Attempt(), 0, 1);
        var result = SystemInnerWorkerAiUsageReconciliation.Calculate(evidence, new("record.tool", Attempt(), 0, 0, 1));
        Assert.True(result.UsageKnown);
        Assert.Equal(0, result.ChargedProviderTokens);
        Assert.Equal(1, result.ChargedToolCalls);
        Assert.False(result.RequiresReconciliation);
    }

    [Fact]
    public void Known_usage_releases_only_the_unused_reserved_amount()
    {
        var result = SystemInnerWorkerAiUsageReconciliation.Calculate(Reservation(), new("record.1", Attempt(), 10, 5, 1));
        Assert.Equal(new SystemInnerWorkerAiUsageReconciliation(15, 1, 85, 1, true, false, false), result);
        Assert.Equal(100, result.ChargedProviderTokens + result.ReleasedProviderTokens);
        Assert.Equal(2, result.ChargedToolCalls + result.ReleasedToolCalls);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, 5L)]
    [InlineData(10L, null)]
    public void Unknown_provider_usage_keeps_the_entire_reservation_charged(long? input, long? output)
    {
        var result = SystemInnerWorkerAiUsageReconciliation.Calculate(Reservation(), new("record.1", Attempt(), input, output, 1));
        Assert.Equal(new SystemInnerWorkerAiUsageReconciliation(100, 2, 0, 0, false, false, true), result);
        Assert.Equal("INNER_AI_USAGE_UNKNOWN", result.Code);
    }

    [Theory]
    [InlineData(150L, 10L, true)]
    [InlineData(150L, null, false)]
    public void Observed_overrun_is_never_clamped_to_reservation(long input, long? output, bool known)
    {
        var result = SystemInnerWorkerAiUsageReconciliation.Calculate(Reservation(), new("record.1", Attempt(), input, output, 3));
        Assert.Equal(input + output.GetValueOrDefault(), result.ChargedProviderTokens);
        Assert.Equal(3, result.ChargedToolCalls);
        Assert.Equal(0, result.ReleasedProviderTokens);
        Assert.Equal(0, result.ReleasedToolCalls);
        Assert.Equal(known, result.UsageKnown);
        Assert.True(result.ExceededReservation);
        Assert.True(result.RequiresReconciliation);
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
            new("record.1", Attempt(), 0, 0, SystemInnerWorkerAiBudget.MaximumToolCalls + 1));
        Assert.Equal(17, result.ChargedToolCalls);
        Assert.True(result.ExceededReservation);
        Assert.True(result.RequiresReconciliation);
    }

    private static SystemInnerWorkerAiReservationRequest Request(SystemInnerWorkerAiProviderTokenBound? bound = null) =>
        new(Host(), TaskHandle(), Attempt(), "reservation.1", new(), 100, 2, bound);
    private static SystemTaskDurableHandle TaskHandle() => new("task.1", "command.1");
    private static SystemTaskDurableHandle Root() => new("task.root", "command.root");
    private static SystemTaskAttemptIdentity Attempt() => new("command.1", "attempt.1", "lease.1", 1, new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc));
    private static SystemInnerWorkerAiReservationEvidence Reservation() => new("record.1", "reservation.1", Root(), TaskHandle(), Attempt(), 100, 2);
    private static InteractionInvocationHost Host(InteractionExecutionProfile profile = InteractionExecutionProfile.Workflow) => new(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "fixture"),
        new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []), "state.1", "grant.1", "command.1", "revision.1",
        profile, new InteractionInvocationBudget(2, new DateTime(2026, 9, 11, 12, 1, 0, DateTimeKind.Utc)));
}
