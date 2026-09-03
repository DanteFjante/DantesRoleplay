using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Tests;

public sealed class InteractionPlanningTests
{
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ActivationFingerprint = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string CatalogFingerprint = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

    [Fact]
    public void Planner_commands_are_closed_and_model_selected_statuses_are_limited()
    {
        var search = Assert.IsType<InteractionPlannerSearchCommand>(InteractionPlannerCommand.Parse(
            """{"command":"search","query":"apply a change","kinds":["mechanic"],"limit":4}"""));
        Assert.Equal("apply a change", search.Input.Query);
        Assert.Equal(["mechanic"], search.Input.Kinds);

        var inspect = Assert.IsType<InteractionPlannerInspectCommand>(InteractionPlannerCommand.Parse(
            $$"""{"command":"inspect","qualifiedId":"sample-app.mechanic.fixture","version":1,"fingerprint":"{{Hash("record")}}"}"""));
        Assert.Equal(1, inspect.Version);

        var proposal = Assert.IsType<InteractionPlannerProposalCommand>(InteractionPlannerCommand.Parse(ProposalJson(Hash("record"))));
        Assert.Equal(["actor", "target"], proposal.Steps.Single().RoleBindings.Keys);
        Assert.Equal("{}", proposal.Steps.Single().InputJson);

        Assert.Equal("PLANNER_PROPERTY_FORBIDDEN", Assert.Throws<InteractionContractException>(() =>
            InteractionPlannerCommand.Parse("""{"command":"search","query":"x","tools":["shell"]}""")).Code);
        Assert.Equal("PLANNER_STATUS_FORBIDDEN", Assert.Throws<InteractionContractException>(() =>
            InteractionPlannerCommand.Parse("""{"command":"non-resolution","status":"unsafe","summary":"x","evidence":[]}""")).Code);
        Assert.Equal("PLANNER_PROPOSAL_INVALID", Assert.Throws<InteractionContractException>(() =>
            InteractionPlannerCommand.Parse(ProposalJson(Hash("record")).Replace("\"input\":{}", "\"input\":[]"))).Code);
        Assert.Equal("PLANNER_COMMAND_INVALID", Assert.Throws<InteractionContractException>(() =>
            InteractionPlannerCommand.Parse("""{"command":"search","query":"x","kinds":[],"limit":13}""")).Code);
    }

    [Fact]
    public async Task Local_and_remote_planners_use_the_same_search_inspect_and_verifier_path()
    {
        var fixture = Fixture();
        var local = await RunAsync(fixture, InteractionPlannerKind.Local);
        var remote = await RunAsync(fixture, InteractionPlannerKind.Remote);

        Assert.Equal(InteractionResolutionStatus.Resolved, local.Result.Status);
        Assert.Equal(InteractionResolutionStatus.Resolved, remote.Result.Status);
        Assert.Equal(local.Result.Proposal!.Fingerprint, remote.Result.Proposal!.Fingerprint);
        Assert.Equal(local.TraceFingerprint, remote.TraceFingerprint);
        Assert.Equal(new InteractionPlannerUsage(3, 1, 1, 1, local.Usage.ElapsedMilliseconds), local.Usage);
        Assert.Equal(InteractionReceiptWriteDisposition.Appended, local.Receipt.Disposition);
        Assert.All(local.Result.Proposal.Steps, step => Assert.Equal(fixture.Envelope.Host.StateRevision, step.ExpectedStateRevision));
        Assert.Equal("mechanic.fixture", local.Result.Proposal.Steps.Single().Contract.AuthoritativeId);
        Assert.Equal(0, fixture.ReceiptStore.NonReceiptMutationCount);
    }

    [Theory]
    [InlineData(InteractionPlannerKind.Local)]
    [InlineData(InteractionPlannerKind.Remote)]
    public async Task Value_free_verified_route_guidance_is_observation_only_and_still_uses_common_verification(
        InteractionPlannerKind kind)
    {
        var fixture = Fixture();
        var provider = new SequenceProvider(kind,
        [
            """{"command":"search","query":"apply a declared change","kinds":["mechanic"],"limit":12}""",
            $$"""{"command":"inspect","qualifiedId":"{{fixture.Record.QualifiedId}}","version":1,"fingerprint":"{{fixture.Record.ContentFingerprint}}"}""",
            ProposalJson(fixture.Record.ContentFingerprint)
        ]);
        var guidance = new VerifiedInteractionRecipeGuidance(
            new("sample-app.recipe." + new string('a', 32), 2, Hash("template")),
            [new("step.1", fixture.Record.QualifiedId, 1, fixture.Record.ContentFingerprint,
                [], ["actor", "target"])]);
        var planner = new InteractionPlanner(fixture.Authorization, fixture.Retriever, fixture.Snapshots,
            fixture.Verifier, new GuidanceResolver(guidance), fixture.ReceiptStore,
            kind == InteractionPlannerKind.Local
                ? [provider, new SequenceProvider(InteractionPlannerKind.Remote, [])]
                : [new SequenceProvider(InteractionPlannerKind.Local, []), provider],
            new FixedTaskContext());

        var outcome = await planner.PlanAsync(fixture.Envelope, fixture.AuthorizationRequest,
            kind);

        Assert.Equal(InteractionResolutionStatus.Resolved, outcome.Result.Status);
        using var observation = JsonDocument.Parse(provider.Requests[0].ObservationJson);
        var route = observation.RootElement.GetProperty("verifiedRoute");
        Assert.Equal(InteractionTaskContextProfiles.Version1,
            observation.RootElement.GetProperty("taskContext").GetProperty("profile").GetString());
        Assert.Equal(fixture.Record.QualifiedId,
            route.GetProperty("steps")[0].GetProperty("qualifiedId").GetString());
        Assert.DoesNotContain("entity.1", route.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(3, provider.Calls);
        Assert.Equal(1, fixture.Retriever.Calls);
    }

    [Fact]
    public async Task Planner_rejects_uninspected_and_stale_references_and_still_writes_safe_evidence()
    {
        var fixture = Fixture();
        var forged = new SequenceProvider(InteractionPlannerKind.Local,
        [
            ProposalJson(Hash("forged"))
        ]);
        var outcome = await Planner(fixture, forged, new SequenceProvider(InteractionPlannerKind.Remote, [])).PlanAsync(
            fixture.Envelope, fixture.AuthorizationRequest, InteractionPlannerKind.Local);

        Assert.Equal(InteractionResolutionStatus.Unsafe, outcome.Result.Status);
        Assert.Equal("CONTRACT_NOT_INSPECTED", outcome.Result.Code);
        Assert.DoesNotContain(outcome.Result.Evidence, value => value.Contains(fixture.Envelope.Intent.IntentText, StringComparison.Ordinal));
        Assert.Equal(InteractionReceiptWriteDisposition.Appended, outcome.Receipt.Disposition);

        var queryDraft = InteractionPlannerCommand.Parse(ProposalJson(fixture.Record.ContentFingerprint)
            .Replace("\"kind\":\"action\"", "\"kind\":\"query\""));
        var verified = fixture.Verifier.Verify(new(fixture.Envelope,
            [new(fixture.Hit, fixture.Record.ContentJson)], Assert.IsType<InteractionPlannerProposalCommand>(queryDraft)));
        Assert.Equal(InteractionResolutionStatus.Unsupported, verified.Status);
        Assert.Equal("CONTRACT_KIND_UNSUPPORTED", verified.Code);

        var staleDraft = Assert.IsType<InteractionPlannerProposalCommand>(InteractionPlannerCommand.Parse(
            ProposalJson(Hash("changed"))));
        var stale = fixture.Verifier.Verify(new(fixture.Envelope,
            [new(fixture.Hit, fixture.Record.ContentJson)], staleDraft));
        Assert.Equal(InteractionResolutionStatus.Stale, stale.Status);
        Assert.Equal("PROPOSAL_CONTRACT_STALE", stale.Code);

        var missingRoleDraft = Assert.IsType<InteractionPlannerProposalCommand>(InteractionPlannerCommand.Parse(
            ProposalJson(fixture.Record.ContentFingerprint).Replace(",\"target\":\"entity.2\"", "")));
        var needsInput = fixture.Verifier.Verify(new(fixture.Envelope,
            [new(fixture.Hit, fixture.Record.ContentJson)], missingRoleDraft));
        Assert.Equal(InteractionResolutionStatus.NeedsInput, needsInput.Status);
        Assert.Contains("role:target", needsInput.Evidence);

        var impersonating = new SequenceProvider(InteractionPlannerKind.Local,
        [
            $$"""{"command":"search","query":"apply a declared change","kinds":["mechanic"],"limit":12}""",
            $$"""{"command":"inspect","qualifiedId":"{{fixture.Record.QualifiedId}}","version":1,"fingerprint":"{{fixture.Record.ContentFingerprint}}"}""",
            ProposalJson(fixture.Record.ContentFingerprint).Replace("entity.1", "entity.impostor", StringComparison.Ordinal)
        ]);
        var rejected = await Planner(fixture, impersonating,
            new SequenceProvider(InteractionPlannerKind.Remote, [])).PlanAsync(
            fixture.Envelope, fixture.AuthorizationRequest, InteractionPlannerKind.Local);
        Assert.Equal(InteractionResolutionStatus.Unsafe, rejected.Result.Status);
        Assert.Equal("ROLE_HINT_BINDING_MISMATCH", rejected.Result.Code);

        var resultBindingImpersonation = new InteractionPlannerProposalCommand([
            new("apply", InteractionPlanStepKind.Action, fixture.Record.QualifiedId, fixture.Record.Version,
                fixture.Record.ContentFingerprint, [], new Dictionary<string, string> { ["target"] = "entity.2" }, "{}",
                [new("unavailable-query", "/entityId", toRole: "actor")])
        ]);
        var resultBindingRejected = fixture.Verifier.Verify(new(fixture.Envelope,
            [new(fixture.Hit, fixture.Record.ContentJson)], resultBindingImpersonation));
        Assert.Equal(InteractionResolutionStatus.Unsafe, resultBindingRejected.Status);
        Assert.Equal("ROLE_HINT_RESULT_BINDING_FORBIDDEN", resultBindingRejected.Code);
    }

    [Theory]
    [InlineData("needs-input", InteractionResolutionStatus.NeedsInput, "PLANNER_NEEDS_INPUT")]
    [InlineData("ambiguous", InteractionResolutionStatus.Ambiguous, "PLANNER_AMBIGUOUS")]
    [InlineData("unknown", InteractionResolutionStatus.Unknown, "PLANNER_UNKNOWN")]
    public async Task Model_selectable_non_resolutions_are_typed_and_receipted(
        string status,
        InteractionResolutionStatus expected,
        string code)
    {
        var fixture = Fixture();
        var provider = new SequenceProvider(InteractionPlannerKind.Local,
        [
            $$"""{"command":"non-resolution","status":"{{status}}","summary":"A bounded result.","evidence":[]}"""
        ]);

        var outcome = await Planner(fixture, provider,
            new SequenceProvider(InteractionPlannerKind.Remote, [])).PlanAsync(
            fixture.Envelope, fixture.AuthorizationRequest, InteractionPlannerKind.Local);

        Assert.Equal(expected, outcome.Result.Status);
        Assert.Equal(code, outcome.Result.Code);
        Assert.Equal(InteractionReceiptWriteDisposition.Appended, outcome.Receipt.Disposition);
    }

    [Fact]
    public async Task Search_limit_stops_before_a_sixth_provider_turn()
    {
        var fixture = Fixture();
        var searches = Enumerable.Repeat(
            """{"command":"search","query":"apply change","kinds":["mechanic"],"limit":12}""", 5);
        var provider = new SequenceProvider(InteractionPlannerKind.Local, searches);

        var outcome = await Planner(fixture, provider,
            new SequenceProvider(InteractionPlannerKind.Remote, [])).PlanAsync(
            fixture.Envelope, fixture.AuthorizationRequest, InteractionPlannerKind.Local);

        Assert.Equal(InteractionResolutionStatus.Unavailable, outcome.Result.Status);
        Assert.Equal("PLANNER_SEARCH_BUDGET_EXCEEDED", outcome.Result.Code);
        Assert.Equal(5, provider.Calls);
        Assert.Equal(4, fixture.Retriever.Calls);
    }

    [Fact]
    public async Task Provider_isolation_and_fresh_authorization_fail_before_model_or_retrieval()
    {
        var fixture = Fixture();
        var provider = new SequenceProvider(InteractionPlannerKind.Local, ["{}"], eligible: false);
        var outcome = await Planner(fixture, provider, new SequenceProvider(InteractionPlannerKind.Remote, [])).PlanAsync(
            fixture.Envelope, fixture.AuthorizationRequest, InteractionPlannerKind.Local);
        Assert.Equal("PROVIDER_ISOLATION_INSUFFICIENT", outcome.Result.Code);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, fixture.Retriever.Calls);

        var deniedFixture = Fixture(allow: false);
        var deniedProvider = new SequenceProvider(InteractionPlannerKind.Local, ["{}"]);
        var denied = await Planner(deniedFixture, deniedProvider, new SequenceProvider(InteractionPlannerKind.Remote, [])).PlanAsync(
            deniedFixture.Envelope, deniedFixture.AuthorizationRequest, InteractionPlannerKind.Local);
        Assert.Equal(InteractionResolutionStatus.Unsafe, denied.Result.Status);
        Assert.Equal("PLAN_NOT_AUTHORIZED", denied.Result.Code);
        Assert.Equal(0, deniedProvider.Calls);
        Assert.Equal(0, deniedFixture.Retriever.Calls);
    }

    [Fact]
    public async Task Malformed_output_and_cancellation_are_typed_receipted_and_never_execute()
    {
        var malformedFixture = Fixture();
        var malformedProvider = new SequenceProvider(InteractionPlannerKind.Local, ["{\"command\":\"shell\"}"]);
        var malformed = await Planner(malformedFixture, malformedProvider,
            new SequenceProvider(InteractionPlannerKind.Remote, [])).PlanAsync(
            malformedFixture.Envelope, malformedFixture.AuthorizationRequest, InteractionPlannerKind.Local);
        Assert.Equal(InteractionResolutionStatus.Unsafe, malformed.Result.Status);
        Assert.Equal("PLANNER_COMMAND_UNKNOWN", malformed.Result.Code);
        Assert.Single(malformedFixture.ReceiptStore.Drafts);

        var cancelledFixture = Fixture();
        var cancelledProvider = new CancellingProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await Planner(cancelledFixture, cancelledProvider,
            new SequenceProvider(InteractionPlannerKind.Remote, [])).PlanAsync(
            cancelledFixture.Envelope, cancelledFixture.AuthorizationRequest,
            InteractionPlannerKind.Local, cancellation.Token);
        Assert.Equal(InteractionResolutionStatus.Unavailable, cancelled.Result.Status);
        Assert.Equal("PLANNER_CANCELLED", cancelled.Result.Code);
        Assert.Single(cancelledFixture.ReceiptStore.Drafts);
        Assert.Equal(0, cancelledFixture.ReceiptStore.NonReceiptMutationCount);
    }

    [Fact]
    public async Task Receipt_replay_with_a_different_terminal_result_is_not_accepted()
    {
        var fixture = Fixture();
        fixture.ReceiptStore.ReplayAs = new InteractionReceiptProjection(
            "interaction-receipt." + new string('b', 32), "resolution", Principal,
            fixture.Envelope.Host.ApplicationRevision.ApplicationId,
            fixture.Envelope.Host.StateSpaceId,
            fixture.Envelope.Intent.IdempotencyKey,
            fixture.Envelope.Fingerprint,
            "unknown", "PLANNER_UNKNOWN", null, "Prior result.", [], DateTime.UnixEpoch);

        var outcome = await RunAsync(fixture, InteractionPlannerKind.Local);

        Assert.Equal(InteractionResolutionStatus.Unsafe, outcome.Result.Status);
        Assert.Equal("INTERACTION_RECEIPT_IDEMPOTENCY_CONFLICT", outcome.Result.Code);
        Assert.Equal(InteractionReceiptWriteDisposition.Conflict, outcome.Receipt.Disposition);
    }

    [Fact]
    public async Task Local_adapter_uses_only_the_fixed_task_schema_and_reports_real_local_identity()
    {
        var local = new CapturingLocalProvider();
        var adapter = new LocalInteractionPlanningProvider(local);

        var result = await adapter.CompleteAsync(new(InteractionRoleProfile.Inner, "{}", 4096));

        Assert.True(result.Ok);
        Assert.Equal(InteractionPlannerKind.Local, result.Identity!.Kind);
        Assert.Equal("ollama", result.Identity.Provider);
        Assert.Equal("fixture-local", result.Identity.Model);
        Assert.Equal(string.Empty, result.Identity.ReasoningEffort);
        Assert.Equal(InteractionPlannerProtocol.TaskClass, local.Request!.TaskClass);
        Assert.Equal(InteractionPlannerProtocol.SystemPrompt, local.Request.SystemPrompt);
        Assert.Equal(InteractionPlannerProtocol.ResponseSchema, local.Request.ResponseSchema);
    }

    [Fact]
    public async Task Local_outer_planning_uses_the_dedicated_outer_completion_not_the_inner_profile()
    {
        var inner = new CapturingLocalProvider();
        var outerCompletion = new CapturingLocalProvider();
        var outer = new InteractionOuterLocalCompletionProvider(outerCompletion, new()
        {
            Model = "fixture-local", Profile = "profile", MaximumOutputBytes = 4096
        });
        var adapter = new LocalInteractionPlanningProvider(inner, outer);

        var result = await adapter.CompleteAsync(new(InteractionRoleProfile.Outer, "{}", 4096));

        Assert.True(result.Ok);
        Assert.Null(inner.Request);
        Assert.Equal(InteractionPlannerProtocol.TaskClass, outerCompletion.Request!.TaskClass);
        Assert.Equal("profile", result.Identity!.Profile);
    }

    [Theory]
    [InlineData(InteractionAiRole.Inner, "low")]
    [InlineData(InteractionAiRole.Outer, "high")]
    public async Task Responses_adapter_fixes_role_schema_and_no_tools(InteractionAiRole role, string effort)
    {
        string? requestBody = null;
        string? authorization = null;
        var handler = new DelegateHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            authorization = request.Headers.Authorization?.ToString();
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                    {"status":"completed","model":"gpt-5.6-luna","output":[{"type":"reasoning"},{"type":"message","content":[{"type":"output_text","text":"{\"command\":\"non-resolution\",\"status\":\"unknown\",\"summary\":\"No route.\",\"evidence\":[]}"}]}]}
                    """, Encoding.UTF8, "application/json")
            };
        });
        var provider = new OpenAiResponsesInteractionPlanningProvider(new HttpClient(handler), new()
        {
            Enabled = true,
            ApiKey = "test-secret",
            Timeout = TimeSpan.FromSeconds(5)
        });

        var result = await provider.CompleteAsync(new(
            InteractionRoleProfile.For(role), "{}", 4096));

        Assert.True(result.Ok);
        Assert.Equal(effort, result.Identity!.ReasoningEffort);
        Assert.Equal("Bearer test-secret", authorization);
        using var request = JsonDocument.Parse(requestBody!);
        Assert.Equal("gpt-5.6-luna", request.RootElement.GetProperty("model").GetString());
        Assert.Equal(effort, request.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("none", request.RootElement.GetProperty("tool_choice").GetString());
        Assert.Empty(request.RootElement.GetProperty("tools").EnumerateArray());
        Assert.False(request.RootElement.GetProperty("parallel_tool_calls").GetBoolean());
        Assert.False(request.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal(InteractionPlannerProtocol.ResponseSchemaName,
            request.RootElement.GetProperty("text").GetProperty("format").GetProperty("name").GetString());
        Assert.DoesNotContain("test-secret", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Responses_adapter_rejects_tool_output_wrong_model_oversize_and_disabled_configuration()
    {
        async Task<InteractionPlanningCompletionResult> Respond(string body, OpenAiInteractionPlanningOptions? options = null)
        {
            var provider = new OpenAiResponsesInteractionPlanningProvider(new HttpClient(new DelegateHandler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                }))), options ?? new() { Enabled = true, ApiKey = "test-secret", Timeout = TimeSpan.FromSeconds(5) });
            return await provider.CompleteAsync(new(InteractionRoleProfile.Inner, "{}", 4096));
        }

        var tool = await Respond("""{"status":"completed","model":"gpt-5.6-luna","output":[{"type":"function_call"}]}""");
        Assert.Equal("REMOTE_MODEL_TOOL_OUTPUT_FORBIDDEN", tool.ErrorCode);
        var wrong = await Respond("""{"status":"completed","model":"other-model","output":[]}""");
        Assert.Equal("REMOTE_MODEL_RESPONSE_INVALID", wrong.ErrorCode);
        var disabled = await Respond("{}", new());
        Assert.Equal("REMOTE_MODEL_DISABLED", disabled.ErrorCode);
        var oversizedText = new string('x', 5000);
        var oversized = await Respond(JsonSerializer.Serialize(new
        {
            status = "completed", model = "gpt-5.6-luna",
            output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = oversizedText } } } }
        }));
        Assert.Equal("REMOTE_MODEL_OUTPUT_BUDGET_EXCEEDED", oversized.ErrorCode);
    }

    private static async Task<InteractionPlanningOutcome> RunAsync(FixtureData fixture, InteractionPlannerKind kind)
    {
        var commands = new[]
        {
            """{"command":"search","query":"apply a declared change","kinds":["mechanic"],"limit":12}""",
            $$"""{"command":"inspect","qualifiedId":"{{fixture.Record.QualifiedId}}","version":1,"fingerprint":"{{fixture.Record.ContentFingerprint}}"}""",
            ProposalJson(fixture.Record.ContentFingerprint)
        };
        var local = new SequenceProvider(InteractionPlannerKind.Local, commands);
        var remote = new SequenceProvider(InteractionPlannerKind.Remote, commands);
        return await Planner(fixture, local, remote).PlanAsync(fixture.Envelope, fixture.AuthorizationRequest, kind);
    }

    private static InteractionPlanner Planner(
        FixtureData fixture,
        IInteractionPlanningCompletionProvider local,
        IInteractionPlanningCompletionProvider remote) => new(
            fixture.Authorization,
            fixture.Retriever,
            fixture.Snapshots,
            fixture.Verifier,
            new EmptyVerifiedInteractionRecipeResolver(),
            fixture.ReceiptStore,
            [local, remote]);

    private static FixtureData Fixture(bool allow = true)
    {
        var app = ApplicationIdentifier.Parse("sample-app");
        var registry = new InMemoryApplicationRegistry();
        var revision = registry.Register(new(app, "Sample", "A neutral fixture.", []));
        var content = JsonSerializer.Serialize(new
        {
            id = "mechanic.fixture",
            category = "change",
            name = "Fixture change",
            description = "Apply a declared neutral change.",
            matches = "apply change",
            requirements = "{\"roles\":{\"actor\":{\"components\":[]},\"target\":{\"components\":[]}}}",
            source = "return { effects: [] };",
            scope = "fixture",
            status = "active"
        });
        var record = new CatalogRecordDefinition(
            app.Value, "mechanic", app.Value + ".mechanic.fixture", "Fixture change",
            "Apply a declared neutral change.", [], ["apply change"], "mechanics/change", "active", 1,
            content, Hash(content), "trusted-source", "mechanics/change/mechanic.fixture.md");
        var manifest = CatalogNavigationManifest.Create(app, CatalogFingerprint, "catalog-lexical-v1",
            [new(app.Value, "Sample", "A neutral fixture.")],
            [
                new(app.Value, "", "Sample", "A neutral fixture.", CatalogDescriptionStatus.Authored),
                new(app.Value, "mechanics", "Mechanics", "Executable contracts.", CatalogDescriptionStatus.Authored),
                new(app.Value, "mechanics/change", "Change", "Neutral changes.", CatalogDescriptionStatus.Authored)
            ], [record]);
        var snapshot = new ActiveCatalogFeatureSnapshot(manifest, [new(record, SourceTrust.Trusted)]);
        var snapshots = new SnapshotProvider(app, snapshot);
        var hit = InteractionFeatureHit.Create(
            InteractionFeatureReference.Create(app, InteractionRetrievalLane.TrustedFeature, manifest.Fingerprint, record),
            record, 1, null, false);
        var retriever = new FakeRetriever(hit);
        var activation = new FakeActivationReader(new(
            app, 1, revision.Revision, revision.Fingerprint, Hash("preview"), Hash("scan"), Hash("candidate"),
            Hash("dependencies"), ActivationFingerprint, "coverage-v1", true, [], [], "operation.fixture", DateTime.UnixEpoch));
        var verifier = new InteractionProposalVerifier(registry, activation, snapshots);
        var principal = TrustedPrincipalContext.VerifiedPrincipal(Principal, "local-loopback");
        var request = new InteractionAuthorizationRequest(principal, app, "state.1", InteractionCapability.Plan, "request.1");
        var initialDecision = InteractionAuthorizationDecision.Allow(request, "authorization.initial");
        var host = new InteractionHostContext(principal, revision, "state.1", "session.1", "revision.1",
            ActivationFingerprint, InteractionRoleProfile.Inner, new(4, 65_536, 65_536), initialDecision);
        var envelope = AuthorizedInteractionEnvelope.Create(InteractionIntent.Parse(
            """{"idempotencyKey":"intent.1","intentText":"Apply a declared change","maximumPlanSteps":2,"roleHints":{"actor":"entity.1","target":"entity.2"}}"""), host);
        var policy = new FakeAuthorizationPolicy(allow);
        return new(envelope, request, record, hit, snapshots, retriever, verifier, policy, new FakeReceiptStore());
    }

    private static string ProposalJson(string fingerprint) => JsonSerializer.Serialize(new
    {
        command = "propose",
        steps = new[]
        {
            new
            {
                stepId = "apply",
                kind = "action",
                qualifiedId = "sample-app.mechanic.fixture",
                version = 1,
                fingerprint,
                dependsOn = Array.Empty<string>(),
                roleBindings = new Dictionary<string, string>
                {
                    ["actor"] = "entity.1",
                    ["target"] = "entity.2"
                },
                input = new { }
            }
        }
    });

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record FixtureData(
        AuthorizedInteractionEnvelope Envelope,
        InteractionAuthorizationRequest AuthorizationRequest,
        CatalogRecordDefinition Record,
        InteractionFeatureHit Hit,
        SnapshotProvider Snapshots,
        FakeRetriever Retriever,
        InteractionProposalVerifier Verifier,
        FakeAuthorizationPolicy Authorization,
        FakeReceiptStore ReceiptStore);

    private sealed class SnapshotProvider(ApplicationIdentifier app, ActiveCatalogFeatureSnapshot snapshot)
        : IActiveCatalogFeatureSnapshotProvider
    {
        public bool TryGetSnapshot(ApplicationIdentifier applicationId, out ActiveCatalogFeatureSnapshot value)
        {
            value = snapshot;
            return applicationId == app;
        }
    }

    private sealed class FakeActivationReader(ActiveApplicationManifest current) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            applicationId == current.ApplicationId ? current : null;
    }

    private sealed class FakeRetriever(InteractionFeatureHit hit) : IInteractionFeatureRetriever
    {
        public int Calls { get; private set; }
        public Task<InteractionFeatureSearchResult> SearchAsync(
            InteractionFeatureRetrievalScope scope,
            InteractionFeatureSearchInput input,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(InteractionFeatureSearchResult.Create(InteractionRetrievalMode.Lexical, [hit]));
        }
        public Task<InteractionFeatureRebuildResult> RebuildAsync(
            InteractionFeatureRetrievalScope scope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SequenceProvider(
        InteractionPlannerKind kind,
        IEnumerable<string> commands,
        bool eligible = true) : IInteractionPlanningCompletionProvider
    {
        private readonly Queue<string> values = new(commands);
        public int Calls { get; private set; }
        public List<InteractionPlanningCompletionRequest> Requests { get; } = [];
        public InteractionPlannerKind Kind => kind;
        public InteractionProviderIsolation Isolation { get; } = eligible
            ? new(true, true, true, true, true, true)
            : new(false, true, true, true, true, true);

        public Task<InteractionPlanningCompletionResult> CompleteAsync(
            InteractionPlanningCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Requests.Add(request);
            if (!values.TryDequeue(out var command))
                return Task.FromResult(InteractionPlanningCompletionResult.Failure("FAKE_EXHAUSTED", "No command remains."));
            var profile = request.RoleProfile;
            return Task.FromResult(new InteractionPlanningCompletionResult(new(
                kind,
                kind == InteractionPlannerKind.Local ? "fake-local" : "fake-remote",
                kind == InteractionPlannerKind.Local ? "fixture-model" : profile.Model,
                "fixture-revision",
                "test",
                kind == InteractionPlannerKind.Remote ? profile.ReasoningEffort : ""), command));
        }
    }

    private sealed class GuidanceResolver(VerifiedInteractionRecipeGuidance guidance)
        : IVerifiedInteractionRecipeResolver
    {
        public Task<VerifiedInteractionRecipeResolution?> ResolveAsync(
            AuthorizedInteractionEnvelope envelope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<VerifiedInteractionRecipeResolution?>(null);

        public Task<VerifiedInteractionRecipeGuidance?> GuideAsync(
            AuthorizedInteractionEnvelope envelope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<VerifiedInteractionRecipeGuidance?>(guidance);
    }

    private sealed class CancellingProvider : IInteractionPlanningCompletionProvider
    {
        public InteractionPlannerKind Kind => InteractionPlannerKind.Local;
        public InteractionProviderIsolation Isolation { get; } = new(true, true, true, true, true, true);
        public Task<InteractionPlanningCompletionResult> CompleteAsync(
            InteractionPlanningCompletionRequest request,
            CancellationToken cancellationToken = default) => Task.FromCanceled<InteractionPlanningCompletionResult>(cancellationToken);
    }

    private sealed class CapturingLocalProvider : DantesRoleplay.Retrieval.ILocalStructuredCompletionProvider
    {
        public DantesRoleplay.Retrieval.StructuredCompletionRequest? Request { get; private set; }
        public Task<DantesRoleplay.Retrieval.LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DantesRoleplay.Retrieval.LocalModelStatus(true,
                new("ollama", "fixture-local", "revision", "profile")));
        public Task<DantesRoleplay.Retrieval.StructuredCompletionResult> CompleteAsync(
            DantesRoleplay.Retrieval.StructuredCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new DantesRoleplay.Retrieval.StructuredCompletionResult(
                new("ollama", "fixture-local", "revision", "profile"),
                """{"command":"non-resolution","status":"unknown","summary":"No route.","evidence":[]}""", 1));
        }
    }

    private sealed class FakeAuthorizationPolicy(bool allow) : IInteractionAuthorizationPolicy
    {
        public InteractionAuthorizationDecision Evaluate(InteractionAuthorizationRequest request) => allow
            ? InteractionAuthorizationDecision.Allow(request, "authorization.fresh")
            : InteractionAuthorizationDecision.Deny(request, "DENIED", "authorization.denied");
    }

    private sealed class FakeReceiptStore : IInteractionReceiptStore
    {
        public List<InteractionResolutionReceiptDraft> Drafts { get; } = [];
        public InteractionReceiptProjection? ReplayAs { get; set; }
        public int NonReceiptMutationCount => 0;
        public Task<InteractionReceiptWriteResult> AppendResolutionAsync(
            InteractionResolutionReceiptDraft draft,
            CancellationToken cancellationToken = default)
        {
            Drafts.Add(draft);
            if (ReplayAs is not null)
                return Task.FromResult(InteractionReceiptWriteResult.Replay(ReplayAs));
            var projection = new InteractionReceiptProjection(
                "interaction-receipt." + new string('a', 32), "resolution",
                draft.Envelope.Host.Principal.PrincipalId,
                draft.Envelope.Host.ApplicationRevision.ApplicationId,
                draft.Envelope.Host.StateSpaceId,
                draft.Envelope.Intent.IdempotencyKey,
                draft.Envelope.Fingerprint,
                InteractionResolutionStatusNames.Get(draft.Result.Status),
                draft.Result.Code,
                draft.Result.Proposal?.Fingerprint,
                draft.Result.SafeSummary,
                draft.Result.Evidence,
                DateTime.UnixEpoch);
            return Task.FromResult(InteractionReceiptWriteResult.Appended(projection));
        }
        public Task<InteractionReceiptWriteResult> AppendExecutionAsync(InteractionExecutionReceiptDraft draft, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Execution is forbidden in Slice 12E.");
        public Task<InteractionReceiptProjection?> GetAsync(InteractionAuthorizationRequest authorizationRequest, string receiptId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTaskContext : IInteractionTaskContextMaterializer
    {
        private const string Json = "{\"profile\":\"interaction-task-context/v1\",\"scope\":[]}";
        public Task<InteractionTaskContextPack> MaterializeAsync(
            AuthorizedInteractionEnvelope envelope,
            InteractionAuthorizationRequest authorizationRequest,
            CancellationToken cancellationToken = default) => Task.FromResult(new InteractionTaskContextPack(
            InteractionTaskContextProfiles.Version1, Json, Hash(Json), []));
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
