using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.Tests;

public sealed class ResourceStandingGrantTargetResolverTests
{
    private const string Kind = "web-page";
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string OtherHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("demo");
    private static readonly StandingGrantDefinitionTarget Target =
        new("demo.pages.home", Kind, App, "demo.pages", "web-page-owner:home@2", 2, Hash);
    private static readonly StandingGrantDefinitionReference Selection =
        new(Target.DefinitionId, Target.Kind, Target.Revision, Target.ContentFingerprint);

    [Fact]
    public async Task Resource_exact_and_current_resolution_route_only_to_the_fixed_owner()
    {
        var definitions = new DefinitionResolver();
        var owner = new ResourceOwner { ExactResult = Available(Target), CurrentResult = Available(Target) };
        var resolver = new ResourceStandingGrantTargetResolver(definitions, [owner]);

        Assert.Same(owner.ExactResult, await resolver.ResolveAsync(Host(), Selection));
        Assert.Same(owner.CurrentResult, await resolver.ResolveCurrentAsync(Host(), Target.DefinitionId, Kind));
        Assert.Equal(Selection, Assert.Single(owner.ExactSelections));
        Assert.Equal(Target.DefinitionId, Assert.Single(owner.CurrentIds));
        Assert.Equal(0, definitions.TotalCalls);
    }

    [Theory]
    [InlineData(StandingGrantTargetResolutionStatus.Denied)]
    [InlineData(StandingGrantTargetResolutionStatus.Unavailable)]
    public async Task Resource_denial_or_unavailability_never_falls_back_to_catalog(
        StandingGrantTargetResolutionStatus status)
    {
        var definitions = new DefinitionResolver();
        var owner = new ResourceOwner { ExactResult = new(status, "RESOURCE_RESULT", null) };
        var resolver = new ResourceStandingGrantTargetResolver(definitions, [owner]);

        var result = await resolver.ResolveAsync(Host(), Selection);

        Assert.Equal(status, result.Status);
        Assert.Equal(0, definitions.TotalCalls);
    }

    [Fact]
    public async Task Exact_resource_selection_rejects_a_substituted_revision_or_fingerprint()
    {
        var owner = new ResourceOwner { ExactResult = Available(Target with { Revision = 3, ContentFingerprint = OtherHash }) };
        var definitions = new DefinitionResolver();
        var result = await new ResourceStandingGrantTargetResolver(definitions, [owner])
            .ResolveAsync(Host(), Selection);

        Assert.Equal(StandingGrantTargetResolutionStatus.Denied, result.Status);
        Assert.Equal("STANDING_GRANT_RESOURCE_OWNER_EVIDENCE_INVALID", result.Code);
        Assert.Equal(Selection, Assert.Single(owner.ExactSelections));
        Assert.Equal(0, definitions.TotalCalls);
    }

    [Fact]
    public async Task Resource_revalidation_reads_the_exact_revision_and_compares_the_complete_target()
    {
        var changed = Target with { OwnershipEvidenceReference = "web-page-owner:home@2-changed" };
        var owner = new ResourceOwner { ExactResult = Available(changed) };
        var resolver = new ResourceStandingGrantTargetResolver(new DefinitionResolver(), [owner]);

        var result = await resolver.RevalidateAsync(Host(), Target);

        Assert.Equal(StandingGrantTargetResolutionStatus.Denied, result.Status);
        Assert.Equal("STANDING_GRANT_RESOURCE_OWNER_EVIDENCE_STALE", result.Code);
        Assert.Equal(Selection, Assert.Single(owner.ExactSelections));
        Assert.Empty(owner.CurrentIds);
    }

    [Fact]
    public async Task Resource_results_reject_catalog_origins_and_current_activation_evidence()
    {
        var retained = Target with { RetainedActivation = new(1, Hash, 1, Hash) };
        var owner = new ResourceOwner
        {
            ExactResult = new(StandingGrantTargetResolutionStatus.Available, "AVAILABLE", Target,
                new StandingGrantActivationOrigin(1, Hash, 1, Hash))
        };
        var resolver = new ResourceStandingGrantTargetResolver(new DefinitionResolver(), [owner]);

        Assert.Equal(StandingGrantTargetResolutionStatus.Denied,
            (await resolver.ResolveAsync(Host(), Selection)).Status);
        Assert.Equal(StandingGrantTargetResolutionStatus.Denied,
            (await resolver.RevalidateAsync(Host(), retained)).Status);
    }

    [Fact]
    public void Registration_rejects_duplicate_or_non_web_resource_owners()
    {
        var definitions = new DefinitionResolver();
        Assert.Throws<ArgumentException>(() => new ResourceStandingGrantTargetResolver(
            definitions, [new ResourceOwner(), new ResourceOwner()]));
        Assert.Throws<ArgumentException>(() => new ResourceStandingGrantTargetResolver(
            definitions, [new ResourceOwner("mechanic")]));
        Assert.Throws<ArgumentException>(() => new ResourceStandingGrantTargetResolver(
            definitions, [new ResourceOwner("information-source")]));
    }

    [Fact]
    public async Task Catalog_candidate_historical_and_current_paths_delegate_unchanged()
    {
        var definitions = new DefinitionResolver();
        var resolver = new ResourceStandingGrantTargetResolver(definitions, []);
        var catalogSelection = Selection with { Kind = "procedure" };
        var catalogTarget = Target with { Kind = "procedure" };
        var origin = new StandingGrantActivationOrigin(1, Hash, 1, Hash);

        Assert.Same(definitions.ExactResult, await resolver.ResolveAsync(Host(), catalogSelection));
        Assert.Same(definitions.CurrentResult, await resolver.ResolveCurrentAsync(Host(), catalogSelection.DefinitionId, catalogSelection.Kind));
        Assert.Same(definitions.CandidateResult, await resolver.ResolveCandidateAsync(Host(), null!, catalogSelection));
        Assert.Same(definitions.RetainedResult, await resolver.ResolveRetainedAsync(Host(), origin, catalogSelection));
        Assert.Same(definitions.RevalidateResult, await resolver.RevalidateAsync(Host(), catalogTarget));
        Assert.Equal(5, definitions.TotalCalls);
    }

    [Fact]
    public async Task Owner_exceptions_are_unavailable_but_cancellation_propagates()
    {
        var unavailable = new ResourceStandingGrantTargetResolver(new DefinitionResolver(),
            [new ResourceOwner { Failure = new InvalidOperationException("private") }]);
        Assert.Equal("STANDING_GRANT_RESOURCE_OWNER_UNAVAILABLE",
            (await unavailable.ResolveAsync(Host(), Selection)).Code);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = new ResourceStandingGrantTargetResolver(new DefinitionResolver(), [new ResourceOwner()]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelled.ResolveAsync(Host(), Selection, cancellation.Token));
    }

    private static StandingGrantTargetResolution Available(StandingGrantDefinitionTarget target) =>
        new(StandingGrantTargetResolutionStatus.Available, "STANDING_GRANT_TARGET_AVAILABLE", target);

    private static InteractionInvocationHost Host() => new(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
        new ApplicationRevision(App, 1, Hash, []), "state", "grant@1", "command", "state@1",
        InteractionExecutionProfile.Atomic, new InteractionInvocationBudget(4, DateTime.UtcNow.AddMinutes(1)));

    private sealed class ResourceOwner(string kind = Kind) : IStandingGrantResourceTargetOwner
    {
        public string Kind { get; } = kind;
        public StandingGrantTargetResolution ExactResult { get; init; } =
            new(StandingGrantTargetResolutionStatus.Unavailable, "RESOURCE_UNAVAILABLE", null);
        public StandingGrantTargetResolution CurrentResult { get; init; } =
            new(StandingGrantTargetResolutionStatus.Unavailable, "RESOURCE_UNAVAILABLE", null);
        public Exception? Failure { get; init; }
        public List<StandingGrantDefinitionReference> ExactSelections { get; } = [];
        public List<string> CurrentIds { get; } = [];

        public Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default)
        {
            ExactSelections.Add(selection);
            if (Failure is not null) throw Failure;
            return Task.FromResult(ExactResult);
        }

        public Task<StandingGrantTargetResolution> ResolveCurrentAsync(InteractionInvocationHost host,
            string exactDefinitionId, CancellationToken cancellationToken = default)
        {
            CurrentIds.Add(exactDefinitionId);
            if (Failure is not null) throw Failure;
            return Task.FromResult(CurrentResult);
        }
    }

    private sealed class DefinitionResolver : IStandingGrantTargetResolver
    {
        public int TotalCalls { get; private set; }
        public StandingGrantTargetResolution ExactResult { get; } =
            new(StandingGrantTargetResolutionStatus.Unavailable, "DEFINITION_EXACT", null);
        public StandingGrantTargetResolution CurrentResult { get; } =
            new(StandingGrantTargetResolutionStatus.Unavailable, "DEFINITION_CURRENT", null);
        public StandingGrantTargetResolution CandidateResult { get; } =
            new(StandingGrantTargetResolutionStatus.Unavailable, "DEFINITION_CANDIDATE", null);
        public StandingGrantTargetResolution RetainedResult { get; } =
            new(StandingGrantTargetResolutionStatus.Unavailable, "DEFINITION_RETAINED", null);
        public StandingGrantTargetResolution RevalidateResult { get; } =
            new(StandingGrantTargetResolutionStatus.Unavailable, "DEFINITION_REVALIDATE", null);

        public Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default)
        { TotalCalls++; return Task.FromResult(ExactResult); }

        public Task<StandingGrantTargetResolution> ResolveCurrentAsync(InteractionInvocationHost host,
            string exactDefinitionId, string kind, CancellationToken cancellationToken = default)
        { TotalCalls++; return Task.FromResult(CurrentResult); }

        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host,
            ApplicationCandidateSnapshot candidate, StandingGrantDefinitionReference selection,
            CancellationToken cancellationToken = default)
        { TotalCalls++; return Task.FromResult(CandidateResult); }

        public Task<StandingGrantTargetResolution> ResolveRetainedAsync(InteractionInvocationHost host,
            StandingGrantActivationOrigin origin, StandingGrantDefinitionReference selection,
            CancellationToken cancellationToken = default)
        { TotalCalls++; return Task.FromResult(RetainedResult); }

        public Task<StandingGrantTargetResolution> RevalidateAsync(InteractionInvocationHost host,
            StandingGrantDefinitionTarget target, CancellationToken cancellationToken = default)
        { TotalCalls++; return Task.FromResult(RevalidateResult); }
    }
}
