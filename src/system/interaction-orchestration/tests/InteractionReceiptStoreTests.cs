using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class InteractionReceiptStoreTests : IDisposable
{
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Resolution_receipts_are_immutable_redacted_and_replay_safe()
    {
        await using var db = _fixture.CreateContext();
        var store = Store(db);
        var draft = Resolution("plan.1", "Inspect the private sealed container");

        var appended = await store.AppendResolutionAsync(draft);
        var replay = await store.AppendResolutionAsync(draft);

        Assert.Equal(InteractionReceiptWriteDisposition.Appended, appended.Disposition);
        Assert.Equal(InteractionReceiptWriteDisposition.Replay, replay.Disposition);
        Assert.Equal(appended.Receipt!.Id, replay.Receipt!.Id);
        Assert.Equal("resolution", appended.Receipt.Kind);
        Assert.Equal("resolved", appended.Receipt.Status);
        Assert.Equal(draft.Result.Proposal!.Fingerprint, appended.Receipt.ProposalFingerprint);
        Assert.Single(db.InteractionResolutionReceipts);
        var stored = db.InteractionResolutionReceipts.Single();
        var serialized = JsonSerializer.Serialize(stored);
        Assert.DoesNotContain("Inspect the private sealed container", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("player message", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HashB, stored.QueryFingerprint);

        var changed = await store.AppendResolutionAsync(Resolution("plan.1", "Different intent"));
        Assert.Equal(InteractionReceiptWriteDisposition.Conflict, changed.Disposition);
        Assert.Single(db.InteractionResolutionReceipts);
    }

    [Fact]
    public async Task Every_typed_non_resolution_persists_without_a_proposal()
    {
        await using var db = _fixture.CreateContext();
        var store = Store(db);
        var statuses = Enum.GetValues<InteractionResolutionStatus>().Where(status => status != InteractionResolutionStatus.Resolved).ToArray();

        foreach (var (status, index) in statuses.Select((status, index) => (status, index)))
        {
            var envelope = Envelope($"non-resolution.{index}", "Need more information");
            var result = InteractionResolutionResult.NonResolution(status, "SAFE_REASON", "A safe explanation.", ["feature.count:0"]);
            var written = await store.AppendResolutionAsync(new InteractionResolutionReceiptDraft(envelope, result));
            Assert.Equal(InteractionResolutionStatusNames.Get(status), written.Receipt!.Status);
            Assert.Null(written.Receipt.ProposalFingerprint);
        }

        Assert.Equal(statuses.Length, db.InteractionResolutionReceipts.Count());
    }

    [Fact]
    public async Task Receipt_reads_require_exact_fresh_authorization_scope_without_existence_disclosure()
    {
        await using var db = _fixture.CreateContext();
        var policy = new TestAuthorizationPolicy();
        var store = new InteractionReceiptStore(db, policy);
        var receipt = (await store.AppendResolutionAsync(Resolution("read.1", "Inspect"))).Receipt!;

        var allowed = ReadRequest(App(), "state.1");
        var found = await store.GetAsync(allowed, receipt.Id);
        var wrongState = ReadRequest(App(), "state.other");
        var denied = ReadRequest(App(), "state.1", "deny.read");

        Assert.NotNull(found);
        Assert.Equal(receipt.Id, found!.Id);
        Assert.Null(await store.GetAsync(wrongState, receipt.Id));
        Assert.Null(await store.GetAsync(denied, receipt.Id));
        Assert.Null(await store.GetAsync(allowed, InteractionReceiptIds.New()));
        Assert.Equal(4, policy.EvaluationCount);
    }

    [Fact]
    public async Task Recent_receipt_context_is_session_scoped_revision_bound_and_reauthorized()
    {
        await using var db = _fixture.CreateContext();
        var policy = new TestAuthorizationPolicy();
        var store = new InteractionReceiptStore(db, policy);
        var resolution = (await store.AppendResolutionAsync(Resolution("recent.1", "Inspect"))).Receipt!;
        var consent = new InteractionExecutionConsentReference(resolution.Id,
            resolution.ProposalFingerprint!, Principal, App(), "state.1", "recent.execute.1");
        await store.AppendExecutionAsync(new(consent, HashB,
            InteractionExecutionReceiptDisposition.Succeeded, "Completed.", [],
            [new(1, "step.1", InteractionExecutionStepDisposition.Succeeded)]));

        var recent = await store.ReadRecentAsync(ReadRequest(App(), "state.1"), "session.1", 6);
        var otherSession = await store.ReadRecentAsync(ReadRequest(App(), "state.1"), "session.other", 6);

        Assert.Equal(2, recent.Count);
        Assert.Contains(recent, value => value.Receipt.Kind == "resolution");
        Assert.Contains(recent, value => value.Receipt.Kind == "execution");
        Assert.All(recent, value =>
        {
            Assert.Equal("session.1", value.SessionContextId);
            Assert.Equal(HashA, value.ApplicationFingerprint);
            Assert.Equal("revision.1", value.StateRevision);
            Assert.Equal(HashB, value.EffectiveSetFingerprint);
            Assert.StartsWith("receipt:interaction-receipt.", value.Reference, StringComparison.Ordinal);
        });
        Assert.Empty(otherSession);
        Assert.Equal(4, policy.EvaluationCount);
    }

    [Fact]
    public async Task Future_execution_receipts_link_existing_operations_without_executing_an_action()
    {
        await using var db = _fixture.CreateContext();
        var store = Store(db);
        var resolution = (await store.AppendResolutionAsync(Resolution("execute-plan.1", "Inspect"))).Receipt!;
        db.Operations.Add(new Operation { Id = "operation.1", Timestamp = DateTime.UtcNow, Tool = "action", Summary = "Existing authoritative action audit.", Success = true });
        await db.SaveChangesAsync();
        var consent = new InteractionExecutionConsentReference(resolution.Id, resolution.ProposalFingerprint!, Principal, App(), "state.1", "execute.1");
        var draft = new InteractionExecutionReceiptDraft(consent, HashB, InteractionExecutionReceiptDisposition.Succeeded,
            "The server recorded one completed step.", ["operation.1"], [new(1, "step.1", InteractionExecutionStepDisposition.Succeeded, "operation.1")]);

        var appended = await store.AppendExecutionAsync(draft);
        var replay = await store.AppendExecutionAsync(draft);

        Assert.Equal(InteractionReceiptWriteDisposition.Appended, appended.Disposition);
        Assert.Equal(InteractionReceiptWriteDisposition.Replay, replay.Disposition);
        Assert.Equal("execution", appended.Receipt!.Kind);
        Assert.Equal(resolution.Id, appended.Receipt.ResolutionReceiptId);
        var step = Assert.Single(appended.Receipt.Steps!);
        Assert.Equal("operation.1", step.OperationId);
        Assert.Single(db.Operations);
        Assert.Single(db.InteractionExecutionReceipts);
        Assert.Single(db.InteractionExecutionReceiptSteps);
    }

    [Fact]
    public async Task Query_receipts_persist_visible_output_redact_binding_only_values_and_replay_without_work()
    {
        await using var db = _fixture.CreateContext();
        var store = Store(db);
        var resolution = (await store.AppendResolutionAsync(Resolution("query-plan.1", "Read safe facts"))).Receipt!;
        var consent = new InteractionExecutionConsentReference(resolution.Id,
            resolution.ProposalFingerprint!, Principal, App(), "state.1", "query-execute.1");
        var draft = new InteractionExecutionReceiptDraft(consent, HashB,
            InteractionExecutionReceiptDisposition.Succeeded, "Two queries completed.", [],
            [
                new(1, "query.visible", InteractionExecutionStepDisposition.Succeeded),
                new(2, "query.hidden", InteractionExecutionStepDisposition.Succeeded)
            ],
            [
                new(1, "query.visible", "sample-app.query.visible", HashA, HashB, HashA,
                    ApplicationQueryExposure.ModelVisible, "{\"answer\":42}"),
                new(2, "query.hidden", "sample-app.query.hidden", HashB, HashA, HashB,
                    ApplicationQueryExposure.BindingOnly, null)
            ]);

        var appended = await store.AppendExecutionAsync(draft);
        var found = await store.GetAsync(ReadRequest(App(), "state.1"), appended.Receipt!.Id);
        var replay = await store.FindExecutionAsync(consent, HashB);

        Assert.Equal(2, db.InteractionExecutionQueryResults.Count());
        Assert.Equal("{\"answer\":42}", db.InteractionExecutionQueryResults.Single(
            value => value.Exposure == "model-visible").OutputJson);
        Assert.Null(db.InteractionExecutionQueryResults.Single(
            value => value.Exposure == "binding-only").OutputJson);
        Assert.Equal(2, found!.QueryResults!.Count);
        Assert.Equal(42, found.QueryResults.Single(value => value.Output is not null)
            .Output!.Value.GetProperty("answer").GetInt32());
        Assert.Null(found.QueryResults.Single(value => value.QualifiedId.EndsWith("hidden", StringComparison.Ordinal)).Output);
        Assert.Equal(InteractionReceiptWriteDisposition.Replay, replay!.Disposition);
        Assert.Equal(found.Id, replay.Receipt!.Id);
    }

    [Fact]
    public async Task Invalid_execution_link_or_scope_leaves_no_partial_receipt()
    {
        await using var db = _fixture.CreateContext();
        var store = Store(db);
        var resolution = (await store.AppendResolutionAsync(Resolution("invalid-execute.1", "Inspect"))).Receipt!;
        var consent = new InteractionExecutionConsentReference(resolution.Id, resolution.ProposalFingerprint!, Principal, App(), "state.1", "execute.invalid");
        var draft = new InteractionExecutionReceiptDraft(consent, HashB, InteractionExecutionReceiptDisposition.Failed,
            "The operation reference was unavailable.", [], [new(1, "step.1", InteractionExecutionStepDisposition.Failed, "operation.missing")]);

        var exception = await Assert.ThrowsAsync<InteractionContractException>(() => store.AppendExecutionAsync(draft));

        Assert.Equal("EXECUTION_OPERATION_NOT_FOUND", exception.Code);
        Assert.Empty(db.InteractionExecutionReceipts);
        Assert.Empty(db.InteractionExecutionReceiptSteps);
        Assert.Single(db.InteractionResolutionReceipts);
    }

    [Fact]
    public async Task A_database_failure_after_execution_receipt_insert_rolls_back_the_header_and_steps()
    {
        await using var db = _fixture.CreateContext();
        var store = Store(db);
        var resolution = (await store.AppendResolutionAsync(Resolution("rollback.1", "Inspect"))).Receipt!;
        db.Operations.Add(new Operation { Id = "operation.rollback", Timestamp = DateTime.UtcNow, Tool = "action", Summary = "Existing audit.", Success = true });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER interaction_execution_step_failure BEFORE INSERT ON interaction_execution_receipt_step BEGIN SELECT RAISE(ABORT, 'injected execution-step failure'); END;");
        var consent = new InteractionExecutionConsentReference(resolution.Id, resolution.ProposalFingerprint!, Principal, App(), "state.1", "execute.rollback");
        var draft = new InteractionExecutionReceiptDraft(consent, HashB, InteractionExecutionReceiptDisposition.Failed,
            "The execution storage failed safely.", [], [new(1, "step.1", InteractionExecutionStepDisposition.Failed, "operation.rollback")]);

        await Assert.ThrowsAsync<DbUpdateException>(() => store.AppendExecutionAsync(draft));

        Assert.Empty(db.InteractionExecutionReceipts);
        Assert.Empty(db.InteractionExecutionReceiptSteps);
        Assert.Single(db.InteractionResolutionReceipts);
        Assert.Single(db.Operations);
    }

    [Fact]
    public async Task SQLite_independently_rejects_a_receipt_that_bypasses_domain_bounds()
    {
        await using var db = _fixture.CreateContext();
        var store = Store(db);
        await store.AppendResolutionAsync(Resolution("database-bounds.1", "Inspect"));
        db.ChangeTracker.Clear();
        var forged = await db.InteractionResolutionReceipts.AsNoTracking().SingleAsync();
        forged.Id = InteractionReceiptIds.New();
        forged.IdempotencyKey = "database-bounds.forged";
        forged.SafeSummary = new string('x', 1_001);
        db.InteractionResolutionReceipts.Add(forged);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        Assert.Single(db.InteractionResolutionReceipts);
    }

    [Fact]
    public void Default_composition_resolves_the_store_and_denies_reads_until_a_host_policy_is_configured()
    {
        var services = new ServiceCollection();
        services.AddDantesRoleplayDataAccess("Filename=:memory:");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInteractionReceiptStore>();
        var recent = scope.ServiceProvider.GetRequiredService<IInteractionRecentReceiptReader>();
        var policy = scope.ServiceProvider.GetRequiredService<IInteractionAuthorizationPolicy>();

        Assert.NotNull(store);
        Assert.Same(store, recent);
        var decision = policy.Evaluate(ReadRequest(App(), "state.1"));
        Assert.False(decision.Allowed);
        Assert.Equal("INTERACTION_AUTHORIZATION_NOT_CONFIGURED", decision.Code);
    }

    [Fact]
    public void Default_composition_registers_both_query_executor_kinds_once()
    {
        var services = new ServiceCollection();
        services.AddDantesRoleplayDataAccess("Filename=:memory:");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var executors = scope.ServiceProvider.GetServices<IInteractionQueryExecutor>().ToArray();
        Assert.Equal(new[]
            {
                ApplicationQueryContract.MechanicProjectionExecutor,
                ApplicationQueryContract.ObjectProjectionExecutor,
                ApplicationQueryContract.ProjectionExecutor
            },
            executors.Select(value => value.Kind).Order(StringComparer.Ordinal));
        var registry = scope.ServiceProvider.GetRequiredService<IInteractionQueryExecutorRegistry>();
        foreach (var executor in executors)
            Assert.True(registry.TryGet(executor.Kind, out _));
    }

    private static InteractionResolutionReceiptDraft Resolution(string key, string intent)
    {
        var envelope = Envelope(key, intent);
        return new(envelope, InteractionResolutionResult.Resolved(Proposal(envelope)), HashB);
    }

    private static AuthorizedInteractionEnvelope Envelope(string key, string intent) => AuthorizedInteractionEnvelope.Create(
        InteractionIntent.Parse(JsonSerializer.Serialize(new { idempotencyKey = key, intentText = intent, maximumPlanSteps = 1 })), Host());

    private static InteractionProposal Proposal(AuthorizedInteractionEnvelope envelope) => InteractionProposal.Create(envelope,
        [new InteractionPlanStep("step.1", InteractionPlanStepKind.Action,
            new InteractionContractReference(InteractionFeatureScope.Application, App(), "sample-app.action", "contract.1", 1, HashA),
            [], new Dictionary<string, string>(), "{}", "revision.1")]);

    private static InteractionHostContext Host()
    {
        var request = new InteractionAuthorizationRequest(TrustedPrincipalContext.VerifiedPrincipal(Principal, "local-loopback"), App(), "state.1", InteractionCapability.Plan, "plan.request");
        return new InteractionHostContext(request.Principal, new ApplicationRevision(App(), 1, HashA, Array.Empty<ApplicationIdentifier>()), "state.1", "session.1", "revision.1", HashB,
            InteractionRoleProfile.Inner, new InteractionBudgets(1, 4096, 4096), InteractionAuthorizationDecision.Allow(request, "plan.evidence"));
    }

    private static InteractionAuthorizationRequest ReadRequest(ApplicationIdentifier application, string stateSpace, string correlation = "read.request") => new(
        TrustedPrincipalContext.VerifiedPrincipal(Principal, "local-loopback"), application, stateSpace, InteractionCapability.ReadReceipt, correlation);

    private static ApplicationIdentifier App() => ApplicationIdentifier.Parse("sample-app");

    private static InteractionReceiptStore Store(DantesRoleplayDbContext db) => new(db, new TestAuthorizationPolicy());

    private sealed class TestAuthorizationPolicy : IInteractionAuthorizationPolicy
    {
        public int EvaluationCount { get; private set; }

        public InteractionAuthorizationDecision Evaluate(InteractionAuthorizationRequest request)
        {
            EvaluationCount++;
            return request.CorrelationId == "deny.read"
                ? InteractionAuthorizationDecision.Deny(request, "DENIED", "read.evidence")
                : InteractionAuthorizationDecision.Allow(request, "read.evidence");
        }
    }
}
