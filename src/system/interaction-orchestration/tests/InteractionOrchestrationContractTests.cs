using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.Tests;

public sealed class InteractionOrchestrationContractTests
{
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public void Closed_caller_intent_is_bounded_copied_and_canonical()
    {
        var left = InteractionIntent.Parse("""
            {
              "intentText": "Inspect the sealed container",
              "idempotencyKey": "intent-01",
              "roleHints": { "target": "entity.2", "actor": "entity.1" },
              "conversationFactReferences": ["fact.2", "fact.1"],
              "maximumPlanSteps": 4,
              "plannerPreference": "automatic"
            }
            """);
        var right = InteractionIntent.Parse("""{"plannerPreference":"automatic","maximumPlanSteps":4,"conversationFactReferences":["fact.1","fact.2"],"roleHints":{"actor":"entity.1","target":"entity.2"},"idempotencyKey":"other-replay-key","intentText":"Inspect the sealed container"}""");

        var leftEnvelope = AuthorizedInteractionEnvelope.Create(left, Host());
        var rightEnvelope = AuthorizedInteractionEnvelope.Create(right, Host());

        Assert.Equal(["fact.1", "fact.2"], left.ConversationFactReferences);
        Assert.Equal(["actor", "target"], left.RoleHints.Keys);
        Assert.Equal(leftEnvelope.Fingerprint, rightEnvelope.Fingerprint);
        Assert.Equal(64, leftEnvelope.Fingerprint.Length);
        Assert.DoesNotContain(left.IntentText, leftEnvelope.Fingerprint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("applicationId")]
    [InlineData("principal")]
    [InlineData("roleProfile")]
    [InlineData("stateRevision")]
    [InlineData("effects")]
    [InlineData("execute")]
    [InlineData("learn")]
    public void Caller_cannot_supply_host_authority_or_execution_fields(string forbiddenField)
    {
        var exception = Assert.Throws<InteractionContractException>(() => InteractionIntent.Parse(
            $$"""{"idempotencyKey":"intent-02","intentText":"Inspect","maximumPlanSteps":1,"{{forbiddenField}}":"forged"}"""));

        Assert.Equal("CALLER_AUTHORITY_FORBIDDEN", exception.Code);
    }

    [Fact]
    public void Intent_and_json_bounds_fail_closed()
    {
        Assert.Equal("INVALID_MAXIMUM_PLAN_STEPS", Assert.Throws<InteractionContractException>(() =>
            InteractionIntent.Parse("""{"idempotencyKey":"k","intentText":"x","maximumPlanSteps":"four"}""")).Code);
        Assert.Equal("INVALID_PLANNER_PREFERENCE", Assert.Throws<InteractionContractException>(() =>
            InteractionIntent.Parse("""{"idempotencyKey":"k","intentText":"x","plannerPreference":"fallback"}""")).Code);
        Assert.Equal("DUPLICATE_JSON_PROPERTY", Assert.Throws<InteractionContractException>(() =>
            InteractionIntent.Parse("""{"idempotencyKey":"k","idempotencyKey":"again","intentText":"x"}""")).Code);
        Assert.Equal("JSON_OBJECT_REQUIRED", Assert.Throws<InteractionContractException>(() =>
            InteractionCanonicalJson.CanonicalizeObject("[]")).Code);
    }

    [Fact]
    public void Host_context_requires_exact_verified_plan_authority()
    {
        var application = App();
        var other = ApplicationIdentifier.Parse("other-app");
        var principal = TrustedPrincipalContext.VerifiedPrincipal(Principal, "local-loopback");
        var wrongRequest = new InteractionAuthorizationRequest(principal, other, "state.1", InteractionCapability.Plan, "request.1");

        var exception = Assert.Throws<InteractionContractException>(() => new InteractionHostContext(
            principal, Revision(application), "state.1", "session.1", "revision.4", HashB,
            InteractionRoleProfile.Inner, new(4, 4096, 4096),
            InteractionAuthorizationDecision.Allow(wrongRequest, "evidence.1")));

        Assert.Equal("AUTHORIZATION_SCOPE_MISMATCH", exception.Code);
        Assert.Equal("PLAN_NOT_AUTHORIZED", Assert.Throws<InteractionContractException>(() => new InteractionHostContext(
            principal, Revision(application), "state.1", "session.1", "revision.4", HashB,
            InteractionRoleProfile.Inner, new(4, 4096, 4096),
            InteractionAuthorizationDecision.Deny(Request(application), "DENIED", "evidence.2"))).Code);
    }

    [Fact]
    public void Host_context_copies_application_base_ownership()
    {
        var bases = new[] { ApplicationIdentifier.Parse("base-app") };
        var revision = new ApplicationRevision(App(), 2, HashA, bases);
        var host = Host(revision);

        bases[0] = ApplicationIdentifier.Parse("mutated-app");

        Assert.Equal("base-app", host.ApplicationRevision.BaseApplications.Single().Value);
        Assert.Equal("inner:gpt-5.6-luna:low", InteractionRoleProfile.Inner.StableKey);
        Assert.Equal("outer:gpt-5.6-luna:high", InteractionRoleProfile.Outer.StableKey);
        InteractionRoleProfile.EnsureResumeCompatible(InteractionRoleProfile.Inner, InteractionRoleProfile.Inner);
        AssertCode("ROLE_PROFILE_CHANGED", () =>
            InteractionRoleProfile.EnsureResumeCompatible(InteractionRoleProfile.Inner, InteractionRoleProfile.Outer));
        Assert.Throws<InteractionContractException>(() => InteractionRoleProfile.For((InteractionAiRole)99));
    }

    [Fact]
    public void Inert_proposal_accepts_an_ordered_application_and_system_dag()
    {
        var envelope = Envelope();
        var proposal = InteractionProposal.Create(envelope,
        [
            Step("lookup", InteractionPlanStepKind.Query, AppReference("sample-app.lookup"), []),
            Step("apply", InteractionPlanStepKind.Action, AppReference("sample-app.apply"), ["lookup"]),
            Step("record", InteractionPlanStepKind.Action, SystemReference("system.record"), ["apply"])
        ]);

        Assert.Equal(3, proposal.Steps.Count);
        Assert.Equal(64, proposal.Fingerprint.Length);
        Assert.Equal("{\"a\":1,\"b\":2}", proposal.Steps[0].InputJson);
        Assert.Equal(InteractionResolutionStatus.Resolved, InteractionResolutionResult.Resolved(proposal).Status);
    }

    [Fact]
    public void Equivalent_proposals_have_identical_fingerprints()
    {
        var left = InteractionProposal.Create(Envelope(),
        [Step("lookup", InteractionPlanStepKind.Query, AppReference("sample-app.lookup"), [],
            new Dictionary<string, string> { ["target"] = "entity.2", ["actor"] = "entity.1" }, "{\"b\":2,\"a\":1}")]);
        var right = InteractionProposal.Create(Envelope(),
        [Step("lookup", InteractionPlanStepKind.Query, AppReference("sample-app.lookup"), [],
            new Dictionary<string, string> { ["actor"] = "entity.1", ["target"] = "entity.2" }, " { \"a\" : 1, \"b\" : 2 }")]);

        Assert.Equal(left.Fingerprint, right.Fingerprint);
    }

    [Fact]
    public void Proposal_rejects_duplicate_missing_forward_self_cross_application_and_stale_steps()
    {
        var envelope = Envelope();
        AssertCode("DUPLICATE_PLAN_STEP", () => InteractionProposal.Create(envelope,
            [Step("same", InteractionPlanStepKind.Query, AppReference("sample-app.one"), []),
             Step("same", InteractionPlanStepKind.Action, AppReference("sample-app.two"), [])]));
        AssertCode("MISSING_OR_FORWARD_DEPENDENCY", () => InteractionProposal.Create(envelope,
            [Step("first", InteractionPlanStepKind.Query, AppReference("sample-app.one"), ["later"]),
             Step("later", InteractionPlanStepKind.Action, AppReference("sample-app.two"), [])]));
        AssertCode("MISSING_OR_FORWARD_DEPENDENCY", () => InteractionProposal.Create(envelope,
            [Step("first", InteractionPlanStepKind.Query, AppReference("sample-app.one"), ["second"]),
             Step("second", InteractionPlanStepKind.Action, AppReference("sample-app.two"), ["first"])]));
        AssertCode("SELF_DEPENDENCY", () => InteractionProposal.Create(envelope,
            [Step("self", InteractionPlanStepKind.Query, AppReference("sample-app.one"), ["self"])]));
        AssertCode("CROSS_APPLICATION_REFERENCE", () => InteractionProposal.Create(envelope,
            [Step("cross", InteractionPlanStepKind.Query,
                new(InteractionFeatureScope.Application, ApplicationIdentifier.Parse("other-app"), "other-app.lookup", "contract.1", 1, HashA), [])]));
        AssertCode("STALE_PROPOSAL_REVISION", () => InteractionProposal.Create(envelope,
            [new InteractionPlanStep("stale", InteractionPlanStepKind.Query, AppReference("sample-app.one"), [],
                new Dictionary<string, string>(), "{}", "revision.3",
                queryContract: QueryReference(App(), []))]));
        AssertCode("INVALID_PROPOSAL_SIZE", () => InteractionProposal.Create(envelope,
            Enumerable.Range(1, 5).Select(index => Step($"step.{index}", InteractionPlanStepKind.Query,
                AppReference("sample-app.one"), []))));
        AssertCode("INVALID_STEP_DEPENDENCIES", () => Step("too-many-dependencies", InteractionPlanStepKind.Query,
            AppReference("sample-app.one"), Enumerable.Range(1, 17).Select(index => $"step.{index}")));
    }

    [Fact]
    public void Proposal_obeys_the_host_owned_total_output_budget()
    {
        var request = Request(App());
        var host = new InteractionHostContext(request.Principal, Revision(App()), "state.1", "session.1", "revision.4", HashB,
            InteractionRoleProfile.Inner, new(1, 4096, 200), InteractionAuthorizationDecision.Allow(request, "authorization.1"));
        var envelope = AuthorizedInteractionEnvelope.Create(
            InteractionIntent.Parse("""{"idempotencyKey":"intent.1","intentText":"Inspect","maximumPlanSteps":1}"""), host);

        AssertCode("MODEL_OUTPUT_BUDGET_EXCEEDED", () => InteractionProposal.Create(envelope,
            [Step("lookup", InteractionPlanStepKind.Query, AppReference("sample-app.lookup"), [])]));
    }

    [Fact]
    public void Namespace_contract_separates_system_from_application_owners()
    {
        Assert.Throws<ArgumentException>(() => ApplicationIdentifier.Parse("system"));
        AssertCode("CONTRACT_NAMESPACE_MISMATCH", () =>
            new InteractionContractReference(InteractionFeatureScope.Application, App(), "system.lookup", "contract.1", 1, HashA));
        AssertCode("CONTRACT_NAMESPACE_MISMATCH", () =>
            new InteractionContractReference(InteractionFeatureScope.System, App(), "sample-app.lookup", "contract.1", 1, HashA));
    }

    [Fact]
    public void Every_non_resolution_is_a_normal_bounded_result_without_a_proposal()
    {
        var statuses = Enum.GetValues<InteractionResolutionStatus>().Where(x => x != InteractionResolutionStatus.Resolved).ToArray();
        var expected = new[] { "needs-input", "ambiguous", "unknown", "unsupported", "unavailable", "unsafe", "stale" };

        var results = statuses.Select(status => InteractionResolutionResult.NonResolution(
            status, "SAFE_CODE", "A bounded safe explanation.", ["reference.1"])).ToArray();

        Assert.Equal(expected, statuses.Select(InteractionResolutionStatusNames.Get));
        Assert.All(results, result => Assert.Null(result.Proposal));
        Assert.Throws<InteractionContractException>(() => InteractionResolutionResult.NonResolution(
            InteractionResolutionStatus.Resolved, "BAD", "Not valid.", []));
        Assert.Throws<InteractionContractException>(() => InteractionResolutionStatusNames.Get((InteractionResolutionStatus)99));
    }

    [Fact]
    public void Provider_is_eligible_only_when_every_power_is_denied()
    {
        var eligible = new InteractionProviderAttestation("responses.closed", InteractionRoleProfile.Inner,
            new(true, true, true, true, true, true)).EvaluateEligibility();
        Assert.True(eligible.IsEligible);
        Assert.Null(eligible.Failure);

        var variants = new[]
        {
            new InteractionProviderIsolation(false, true, true, true, true, true),
            new InteractionProviderIsolation(true, false, true, true, true, true),
            new InteractionProviderIsolation(true, true, false, true, true, true),
            new InteractionProviderIsolation(true, true, true, false, true, true),
            new InteractionProviderIsolation(true, true, true, true, false, true),
            new InteractionProviderIsolation(true, true, true, true, true, false)
        };
        foreach (var isolation in variants)
        {
            var result = new InteractionProviderAttestation("provider", InteractionRoleProfile.Outer, isolation).EvaluateEligibility();
            Assert.False(result.IsEligible);
            Assert.Equal(InteractionResolutionStatus.Unavailable, result.Failure!.Status);
            Assert.Null(result.Failure.Proposal);
        }
    }

    [Fact]
    public void Replay_and_execution_consent_bind_exact_authority()
    {
        var proposal = InteractionProposal.Create(Envelope(),
            [Step("lookup", InteractionPlanStepKind.Query, AppReference("sample-app.lookup"), [])]);
        var consent = new InteractionExecutionConsentReference(
            "receipt.1", proposal.Fingerprint, Principal, App(), "state.1", "execute.1");

        Assert.Equal(proposal.Fingerprint, consent.ProposalFingerprint);
        Assert.Equal(InteractionReplayDisposition.New, InteractionReplay.Decide(null, null, "intent.1", HashA));
        Assert.Equal(InteractionReplayDisposition.Replay, InteractionReplay.Decide("intent.1", HashA, "intent.1", HashA));
        Assert.Equal(InteractionReplayDisposition.Conflict, InteractionReplay.Decide("intent.1", HashA, "intent.1", HashB));
        Assert.Equal(InteractionReplayDisposition.New, InteractionReplay.Decide("intent.1", HashA, "intent.2", HashB));
        AssertCode("INVALID_PRINCIPAL_REFERENCE", () => new InteractionExecutionConsentReference(
            "receipt.1", HashA, "principal.forged", App(), "state.1", "execute.1"));
    }

    [Fact]
    public void Authorization_port_can_be_exercised_without_storage_or_runtime_services()
    {
        IInteractionAuthorizationPolicy policy = new FakeAuthorizationPolicy();
        var decision = policy.Evaluate(Request(App()));

        Assert.True(decision.Allowed);
        Assert.Equal(InteractionCapability.Plan, decision.Capability);
        Assert.Equal(Principal, decision.PrincipalReference);
    }

    private static InteractionHostContext Host(ApplicationRevision? revision = null)
    {
        var appRevision = revision ?? Revision(App());
        var request = Request(appRevision.ApplicationId);
        return new(request.Principal, appRevision, request.StateSpaceId, "session.1", "revision.4", HashB,
            InteractionRoleProfile.Inner, new(4, 4096, 4096),
            InteractionAuthorizationDecision.Allow(request, "authorization.1"), "conversation.1", "delegation.1");
    }

    private static AuthorizedInteractionEnvelope Envelope() => AuthorizedInteractionEnvelope.Create(
        InteractionIntent.Parse("""{"idempotencyKey":"intent.1","intentText":"Inspect","maximumPlanSteps":4,"roleHints":{"actor":"entity.1"}}"""),
        Host());

    private static ApplicationIdentifier App() => ApplicationIdentifier.Parse("sample-app");

    private static ApplicationRevision Revision(ApplicationIdentifier application) =>
        new(application, 3, HashA, Array.Empty<ApplicationIdentifier>());

    private static InteractionAuthorizationRequest Request(ApplicationIdentifier application) => new(
        TrustedPrincipalContext.VerifiedPrincipal(Principal, "local-loopback"), application, "state.1",
        InteractionCapability.Plan, "request.1");

    private static InteractionContractReference AppReference(string key) =>
        new(InteractionFeatureScope.Application, App(), key, "contract.1", 1, HashA);

    private static InteractionContractReference SystemReference(string key) =>
        new(InteractionFeatureScope.System, App(), key, "contract.2", 1, HashB);

    private static InteractionPlanStep Step(
        string id,
        InteractionPlanStepKind kind,
        InteractionContractReference contract,
        IEnumerable<string> dependencies,
        IReadOnlyDictionary<string, string>? bindings = null,
        string input = "{\"b\":2,\"a\":1}") =>
        new(id, kind, contract, dependencies, bindings ?? new Dictionary<string, string>(), input, "revision.4",
            queryContract: kind == InteractionPlanStepKind.Query
                ? QueryReference(contract.ApplicationId, (bindings ?? new Dictionary<string, string>()).Keys)
                : null);

    private static InteractionQueryContractReference QueryReference(
        ApplicationIdentifier applicationId,
        IEnumerable<string> roles) => new("projection", applicationId.Value + ".projection.fixture",
            1, HashA, HashB, "{\"type\":\"object\"}",
            DantesRoleplay.CatalogNavigation.ApplicationQueryExposure.BindingOnly, roles);

    private static void AssertCode(string code, Action action) =>
        Assert.Equal(code, Assert.Throws<InteractionContractException>(action).Code);

    private sealed class FakeAuthorizationPolicy : IInteractionAuthorizationPolicy
    {
        public InteractionAuthorizationDecision Evaluate(InteractionAuthorizationRequest request) =>
            InteractionAuthorizationDecision.Allow(request, "fake.authorization");
    }
}
