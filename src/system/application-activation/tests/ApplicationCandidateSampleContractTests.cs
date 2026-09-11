using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationActivation.Tests;

public sealed class ApplicationCandidateSampleContractTests
{
    [Fact]
    public async Task Request_first_preparation_default_rejects_samples_without_calling_legacy_snapshot_validation()
    {
        IApplicationCandidatePreparation preparation = new LegacyPreparation();
        var legacy = (LegacyPreparation)preparation;

        var result = await preparation.ValidateAsync(Request(), Host());

        Assert.Equal(ApplicationCandidateRuntimeStatus.Unavailable, result.Status);
        Assert.Equal("RUNTIME_PREPARATION_UNAVAILABLE", Assert.Single(result.Diagnostics).Code);
        Assert.Equal(0, legacy.Calls);
    }

    [Fact]
    public async Task Request_first_authoring_default_rejects_samples_without_calling_legacy_candidate_validation()
    {
        IApplicationAuthoringService authoring = new LegacyAuthoringService();
        var legacy = (LegacyAuthoringService)authoring;

        var result = await authoring.ValidateAsync(Request(), Host());

        Assert.Equal(InteractionInvocationResultTag.Unavailable, result.Tag);
        Assert.Equal("APPLICATION_CANDIDATE_SAMPLES_UNAVAILABLE", result.Code);
        Assert.Equal(0, legacy.Calls);
    }

    [Fact]
    public async Task Legacy_preparation_implementation_keeps_host_first_target_typed_snapshot_call_unambiguous()
    {
        IApplicationCandidatePreparation preparation = new LegacyPreparation();
        var legacy = (LegacyPreparation)preparation;

        var result = await preparation.ValidateAsync(Host(), new(
            Candidate(), 1, Hash, null, "runtime", null, "Needed.", "grant", "source-operation", [], []));

        Assert.Equal(ApplicationCandidateCheckStatus.Unavailable, result.Status);
        Assert.Equal(1, legacy.Calls);
    }

    private static ApplicationCandidateValidationRequest Request() => new(Candidate(),
        [new(new StandingGrantDefinitionReference("demo.runtime.sample", "procedure", 1, Hash), "{}", "{}")]);

    private static ApplicationCandidateReference Candidate() =>
        new(ApplicationIdentifier.Parse("demo"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 1, Hash);

    private static InteractionInvocationHost Host() => InteractionInvocationHost.ForApplication(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
        new ApplicationRevision(ApplicationIdentifier.Parse("demo"), 1, Hash, []), "grant", "command",
        InteractionExecutionProfile.Atomic, new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));

    private sealed class LegacyPreparation : IApplicationCandidatePreparation
    {
        public int Calls { get; private set; }

        public Task<ApplicationCandidateCheckResult> ValidateAsync(InteractionInvocationHost host,
            ApplicationCandidateSnapshot candidate, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ApplicationCandidateCheckResult(ApplicationCandidateCheckStatus.Unavailable,
                candidate.Candidate.ContentFingerprint, Hash, null, null, []));
        }
    }

    private sealed class LegacyAuthoringService : IApplicationAuthoringService
    {
        public int Calls { get; private set; }

        public Task<InteractionInvocationResult> WriteCandidateAsync(InteractionInvocationHost host,
            ApplicationCandidateWriteRequest request, CancellationToken cancellationToken = default) => Unavailable();
        public Task<InteractionInvocationResult> InspectAsync(InteractionInvocationHost host,
            ApplicationCandidateLookup candidate, CancellationToken cancellationToken = default) => Unavailable();
        public Task<InteractionInvocationResult> ValidateAsync(InteractionInvocationHost host,
            ApplicationCandidateReference candidate, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Unavailable();
        }
        public Task<InteractionInvocationResult> ActivateAsync(InteractionInvocationHost host,
            ApplicationCandidateActivationRequest request, CancellationToken cancellationToken = default) => Unavailable();
        public Task<InteractionInvocationResult> RecoverAsync(InteractionInvocationHost host, int activationRevision,
            string? expectedActiveFingerprint, CancellationToken cancellationToken = default) => Unavailable();

        private static Task<InteractionInvocationResult> Unavailable() =>
            Task.FromResult(InteractionInvocationResult.Unavailable("LEGACY_UNAVAILABLE", "Legacy fixture."));
    }

    private static readonly string Hash = new('A', 64);
}