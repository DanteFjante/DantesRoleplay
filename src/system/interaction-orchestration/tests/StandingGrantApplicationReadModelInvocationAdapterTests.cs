using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.Interactions.Tests;

public sealed class StandingGrantApplicationReadModelInvocationAdapterTests
{
    [Fact]
    public async Task Current_standing_read_executes_the_real_query_and_consumes_one_shared_operation()
    {
        using var fixture = await InteractionInvocationAdapterTests.InvocationFixture.CreateAsync();
        var request = fixture.ReadRequest("command.standing.current", "grant.read.1", maximumOperations: 1);
        var resolver = new LabeledTargetResolver(fixture.QueryTarget);
        var policy = new LabeledStandingGrantPolicy(Grant(request, fixture.QueryTarget));
        IStandingGrantApplicationReadModelInvocationAdapter adapter = new StandingGrantApplicationReadModelInvocationAdapter(
            policy,
            resolver,
            fixture.StateSpaces,
            fixture.ReadModels);

        var result = await adapter.ReadAsync(request);

        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Equal("{\"entityId\":\"subject\",\"value\":1}", result.DataJson);
        Assert.NotNull(result.ReadEvidence);
        Assert.Equal(0, request.Host.Budget.RemainingOperations);
        Assert.Equal(2, policy.Calls);
        Assert.Equal(2, resolver.CurrentCalls);
        Assert.Equal(0, resolver.LegacyCalls);
        Assert.All(policy.Requirements, requirement =>
        {
            Assert.Equal(StandingGrantCapability.Read, requirement.Capability);
            Assert.Equal(StandingGrantScope.StateSpace, requirement.Scope);
            Assert.Equal(fixture.QueryTarget, Assert.Single(requirement.Definitions));
            Assert.Empty(requirement.EffectKinds);
        });
        Assert.All(policy.Hosts, host => Assert.Same(request.Host, host));
        Assert.All(resolver.Hosts, host => Assert.Same(request.Host, host));
        Assert.All(resolver.DefinitionIds, id => Assert.Equal(request.QualifiedQueryId, id));
        Assert.All(resolver.Kinds, kind => Assert.Equal("query", kind));
    }

    [Fact]
    public async Task Forbidden_standing_read_never_executes_the_query_or_falls_back_to_legacy_grant_names()
    {
        using var fixture = await InteractionInvocationAdapterTests.InvocationFixture.CreateAsync();
        var request = fixture.ReadRequest(
            "command.standing.forbidden",
            "interaction.private-host.read",
            maximumOperations: 1);
        var resolver = new LabeledTargetResolver(fixture.QueryTarget);
        var policy = new LabeledStandingGrantPolicy(Grant(request, fixture.QueryTarget))
        {
            Label = StandingPolicyLabel.Forbidden
        };
        var reads = new CountingReadModelService(fixture.ReadModels);
        var adapter = new StandingGrantApplicationReadModelInvocationAdapter(
            policy,
            resolver,
            fixture.StateSpaces,
            reads);

        var result = await adapter.ReadAsync(request);

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", result.Code);
        Assert.Null(result.DataJson);
        Assert.Equal(0, reads.Calls);
        Assert.Equal(1, policy.Calls);
        Assert.Equal(1, resolver.CurrentCalls);
        Assert.Equal(0, resolver.LegacyCalls);
        Assert.Equal(0, request.Host.Budget.RemainingOperations);
    }

    [Theory]
    [InlineData("revoked-grant")]
    [InlineData("replaced-grant")]
    [InlineData("replaced-query")]
    public async Task Authority_changed_during_the_real_read_suppresses_completed_data(string change)
    {
        using var fixture = await InteractionInvocationAdapterTests.InvocationFixture.CreateAsync();
        var request = fixture.ReadRequest("command.standing.changed." + change, "grant.read.1", maximumOperations: 1);
        var resolver = new LabeledTargetResolver(fixture.QueryTarget);
        var policy = new LabeledStandingGrantPolicy(Grant(request, fixture.QueryTarget));
        var gated = new GatedReadModelService(fixture.ReadModels);
        var adapter = new StandingGrantApplicationReadModelInvocationAdapter(
            policy,
            resolver,
            fixture.StateSpaces,
            gated);

        var pending = adapter.ReadAsync(request);
        await gated.CompletedRead;
        if (change == "replaced-query")
        {
            resolver.CurrentTarget = fixture.QueryTarget with
            {
                Revision = fixture.QueryTarget.Revision + 1,
                ContentFingerprint = new string('B', 64)
            };
        }
        else
        {
            policy.CurrentGrant = policy.CurrentGrant with
            {
                GrantReference = "grant.read.2",
                Revision = policy.CurrentGrant.Revision + 1,
                ContentFingerprint = new string('D', 64),
                Revoked = change == "revoked-grant"
            };
        }
        gated.Release();

        var result = await pending;

        Assert.NotEqual(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Null(result.DataJson);
        Assert.Null(result.ReadEvidence);
        Assert.Equal(1, gated.Calls);
        Assert.Equal(0, request.Host.Budget.RemainingOperations);
    }

    [Theory]
    [InlineData("resolver")]
    [InlineData("policy")]
    public async Task Missing_standing_authority_dependency_is_unavailable_without_query_execution(string missing)
    {
        using var fixture = await InteractionInvocationAdapterTests.InvocationFixture.CreateAsync();
        var request = fixture.ReadRequest("command.standing.no-" + missing, "grant.read.1", maximumOperations: 1);
        var policy = new LabeledStandingGrantPolicy(Grant(request, fixture.QueryTarget));
        var resolver = new LabeledTargetResolver(fixture.QueryTarget);
        var reads = new CountingReadModelService(fixture.ReadModels);
        var adapter = new StandingGrantApplicationReadModelInvocationAdapter(
            missing == "policy" ? null : policy,
            missing == "resolver" ? null : resolver,
            fixture.StateSpaces,
            reads);

        var result = await adapter.ReadAsync(request);

        Assert.Equal(InteractionInvocationResultTag.Unavailable, result.Tag);
        Assert.Equal(
            missing == "resolver"
                ? "STANDING_GRANT_TARGET_RESOLVER_UNAVAILABLE"
                : "STANDING_GRANT_POLICY_UNAVAILABLE",
            result.Code);
        Assert.Null(result.DataJson);
        Assert.Equal(0, reads.Calls);
        Assert.Equal(0, policy.Calls);
        Assert.Equal(0, resolver.CurrentCalls);
        Assert.Equal(0, request.Host.Budget.RemainingOperations);
    }

    [Theory]
    [InlineData("principal")]
    [InlineData("application")]
    [InlineData("state")]
    [InlineData("selector")]
    [InlineData("resolved-target")]
    public async Task Allowed_policy_output_must_match_the_exact_host_and_selected_query(string mismatch)
    {
        using var fixture = await InteractionInvocationAdapterTests.InvocationFixture.CreateAsync();
        var request = fixture.ReadRequest("command.standing.mismatch." + mismatch, "grant.read.1", maximumOperations: 1);
        var resolver = new LabeledTargetResolver(fixture.QueryTarget);
        var policy = new LabeledStandingGrantPolicy(Grant(request, fixture.QueryTarget));
        if (mismatch == "resolved-target")
        {
            resolver.CurrentTarget = fixture.QueryTarget with { DefinitionId = "foundation-fixture.query.other" };
        }
        else
        {
            policy.CurrentGrant = mismatch switch
            {
                "principal" => policy.CurrentGrant with
                {
                    PrincipalReference = "principal." + new string('b', 64)
                },
                "application" => policy.CurrentGrant with
                {
                    ApplicationId = ApplicationIdentifier.Parse("other")
                },
                "state" => policy.CurrentGrant with { StateSpaceId = "other-state" },
                _ => policy.CurrentGrant with
                {
                    Definitions = new(
                        StandingGrantDefinitionMode.ExactIds,
                        ["foundation-fixture.query.other"],
                        [])
                }
            };
        }
        var reads = new CountingReadModelService(fixture.ReadModels);
        var adapter = new StandingGrantApplicationReadModelInvocationAdapter(
            policy,
            resolver,
            fixture.StateSpaces,
            reads);

        var result = await adapter.ReadAsync(request);

        Assert.NotEqual(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Null(result.DataJson);
        Assert.Equal(0, reads.Calls);
    }

    private static StandingGrantRevision Grant(
        ApplicationReadModelInvocationRequest request,
        StandingGrantDefinitionTarget target) =>
        new(
            request.Host.GrantReference,
            "grant.read",
            1,
            new string('C', 64),
            request.Host.Principal.PrincipalId,
            request.Host.ApplicationRevision.ApplicationId,
            StandingGrantScope.StateSpace,
            request.Host.StateSpaceId,
            [StandingGrantCapability.Read],
            new(StandingGrantDefinitionMode.ExactIds, [target.DefinitionId], []),
            [],
            1,
            request.Host.Budget.DeadlineUtc.AddMinutes(1),
            false,
            "operation.issue");

    private enum StandingPolicyLabel
    {
        Current,
        Forbidden
    }

    private sealed class LabeledStandingGrantPolicy(StandingGrantRevision currentGrant) : IStandingGrantPolicy
    {
        public StandingPolicyLabel Label { get; set; } = StandingPolicyLabel.Current;
        public StandingGrantRevision CurrentGrant { get; set; } = currentGrant;
        public int Calls { get; private set; }
        public List<InteractionInvocationHost> Hosts { get; } = [];
        public List<StandingGrantRequirement> Requirements { get; } = [];

        public Task<StandingGrantDecision> EvaluateAsync(
            InteractionInvocationHost host,
            StandingGrantRequirement requirement,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Hosts.Add(host);
            Requirements.Add(requirement);
            var allowed = Label == StandingPolicyLabel.Current && !CurrentGrant.Revoked;
            var evidence = new AuthorizationAuditEvidence(
                host.Principal.PrincipalId,
                host.Principal.AuthenticationMethod,
                "read",
                host.StateSpaceId,
                host.CommandId,
                allowed,
                allowed ? "STANDING_GRANT_ALLOWED" : "STANDING_GRANT_FORBIDDEN");
            return Task.FromResult(new StandingGrantDecision(
                allowed,
                allowed ? "STANDING_GRANT_ALLOWED" : "STANDING_GRANT_FORBIDDEN",
                allowed ? CurrentGrant : null,
                evidence));
        }
    }

    private sealed class LabeledTargetResolver(StandingGrantDefinitionTarget target) : IStandingGrantTargetResolver
    {
        public StandingGrantDefinitionTarget CurrentTarget { get; set; } = target;
        public int CurrentCalls { get; private set; }
        public int LegacyCalls { get; private set; }
        public List<InteractionInvocationHost> Hosts { get; } = [];
        public List<string> DefinitionIds { get; } = [];
        public List<string> Kinds { get; } = [];

        public Task<StandingGrantTargetResolution> ResolveCurrentAsync(
            InteractionInvocationHost host,
            string exactDefinitionId,
            string kind,
            CancellationToken cancellationToken = default)
        {
            CurrentCalls++;
            Hosts.Add(host);
            DefinitionIds.Add(exactDefinitionId);
            Kinds.Add(kind);
            return Task.FromResult(new StandingGrantTargetResolution(
                StandingGrantTargetResolutionStatus.Available,
                "STANDING_GRANT_TARGET_AVAILABLE",
                CurrentTarget));
        }

        public Task<StandingGrantTargetResolution> ResolveAsync(
            InteractionInvocationHost host,
            StandingGrantDefinitionReference selection,
            CancellationToken cancellationToken = default)
        {
            LegacyCalls++;
            throw new InvalidOperationException("The standing read adapter must use current exact-query resolution.");
        }

        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(
            InteractionInvocationHost host,
            ApplicationCandidateSnapshot candidate,
            StandingGrantDefinitionReference selection,
            CancellationToken cancellationToken = default)
        {
            LegacyCalls++;
            throw new InvalidOperationException("The standing read adapter cannot resolve a retained candidate.");
        }
    }

    private sealed class CountingReadModelService(IApplicationReadModelService inner) : IApplicationReadModelService
    {
        public int Calls { get; private set; }

        public Task<ApplicationReadModelResult> ReadAsync(
            ApplicationReadModelRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.ReadAsync(request, cancellationToken);
        }
    }

    private sealed class GatedReadModelService(IApplicationReadModelService inner) : IApplicationReadModelService
    {
        private readonly TaskCompletionSource completedRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }
        public Task CompletedRead => completedRead.Task;

        public async Task<ApplicationReadModelResult> ReadAsync(
            ApplicationReadModelRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var result = await inner.ReadAsync(request, cancellationToken);
            completedRead.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return result;
        }

        public void Release() => release.TrySetResult();
    }
}
