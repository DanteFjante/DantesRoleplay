using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationExecution.Tests;

/// <summary>
/// Adversarial unit checks for decisions returned by the authority seam. These doubles establish
/// fail-closed validation only; production availability is covered by the SQLite integration tests.
/// </summary>
public sealed class ApplicationReadOnlyServiceAuthorityTests
{
    [Fact]
    public async Task Stable_allowed_authority_completes_the_no_read_control()
    {
        using var fixture = await Interactions.Tests.InteractionInvocationAdapterTests.InvocationFixture.CreateAsync(
            "return {data:{entityId:'subject',value:7}};");
        var request = fixture.ServiceRequest("command.service.authority-control");
        var authority = new AdversarialAuthority(request, AuthorityMutation.None);

        var result = await fixture.CreateServiceAdapter(authority, authority).InvokeAsync(request);

        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Equal("{\"entityId\":\"subject\",\"value\":7}", result.DataJson);
        Assert.Equal(2, authority.PolicyCalls);
        Assert.Equal(2, authority.ResolveCalls);
    }

    [Theory]
    [InlineData(AuthorityMutation.MisboundGrant)]
    [InlineData(AuthorityMutation.MisboundEvidencePrincipal)]
    [InlineData(AuthorityMutation.MisboundEvidenceScope)]
    [InlineData(AuthorityMutation.MisboundEvidenceCorrelation)]
    public async Task Misbound_allowed_decision_is_rejected(AuthorityMutation mutation)
    {
        using var fixture = await Interactions.Tests.InteractionInvocationAdapterTests.InvocationFixture.CreateAsync(
            "return {data:{entityId:'subject',value:7}};");
        var request = fixture.ServiceRequest("command.service.misbound");
        var authority = new AdversarialAuthority(request, mutation);

        var result = await fixture.CreateServiceAdapter(authority, authority).InvokeAsync(request);

        Assert.NotEqual(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Null(result.DataJson);
        Assert.Null(result.CompletionEvidenceReference);
        Assert.Null(result.TaskHandle);
        Assert.Equal(1, authority.PolicyCalls);
    }

    [Theory]
    [InlineData(AuthorityMutation.ChangeGrant)]
    [InlineData(AuthorityMutation.ChangeTarget)]
    public async Task Changed_root_authority_identity_is_rejected_after_execution(AuthorityMutation mutation)
    {
        using var fixture = await Interactions.Tests.InteractionInvocationAdapterTests.InvocationFixture.CreateAsync(
            "return {data:{entityId:'subject',value:7}};");
        var request = fixture.ServiceRequest("command.service.authority-change");
        var authority = new AdversarialAuthority(request, mutation);

        var result = await fixture.CreateServiceAdapter(authority, authority).InvokeAsync(request);

        Assert.NotEqual(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Null(result.DataJson);
        Assert.Null(result.CompletionEvidenceReference);
        Assert.Null(result.TaskHandle);
        Assert.Equal("INVOCATION_AUTHORITY_CHANGED", result.Code);
        Assert.Equal(mutation == AuthorityMutation.ChangeTarget ? 1 : 2, authority.PolicyCalls);
        Assert.Equal(2, authority.ResolveCalls);
    }

    public enum AuthorityMutation
    {
        None,
        MisboundGrant,
        MisboundEvidencePrincipal,
        MisboundEvidenceScope,
        MisboundEvidenceCorrelation,
        ChangeGrant,
        ChangeTarget
    }

    private sealed class AdversarialAuthority : IStandingGrantTargetResolver, IStandingGrantPolicy
    {
        private readonly ApplicationReadOnlyServiceInvocationRequest request;
        private readonly AuthorityMutation mutation;

        public AdversarialAuthority(
            ApplicationReadOnlyServiceInvocationRequest request,
            AuthorityMutation mutation)
        {
            this.request = request;
            this.mutation = mutation;
        }

        public int ResolveCalls { get; private set; }
        public int PolicyCalls { get; private set; }

        public Task<StandingGrantTargetResolution> ResolveAsync(
            InteractionInvocationHost host,
            StandingGrantDefinitionReference selection,
            CancellationToken cancellationToken = default)
        {
            ResolveCalls++;
            var target = Target();
            if (mutation == AuthorityMutation.ChangeTarget && ResolveCalls == 2)
                target = target with { OwnershipEvidenceReference = "catalog-owner.changed" };
            return Task.FromResult(new StandingGrantTargetResolution(
                StandingGrantTargetResolutionStatus.Available,
                "STANDING_GRANT_TARGET_AVAILABLE",
                target));
        }

        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(
            InteractionInvocationHost host,
            ApplicationCandidateSnapshot candidate,
            StandingGrantDefinitionReference selection,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StandingGrantTargetResolution(
                StandingGrantTargetResolutionStatus.Unavailable,
                "STANDING_GRANT_CANDIDATE_UNAVAILABLE",
                null));

        public Task<StandingGrantDecision> EvaluateAsync(
            InteractionInvocationHost host,
            StandingGrantRequirement requirement,
            CancellationToken cancellationToken = default)
        {
            PolicyCalls++;
            var principal = mutation == AuthorityMutation.MisboundGrant
                ? "principal.other"
                : host.Principal.PrincipalId;
            var grantId = mutation == AuthorityMutation.ChangeGrant && PolicyCalls == 2
                ? "grant.changed"
                : "grant.stable";
            var grant = Grant(host, principal, grantId);
            var evidencePrincipal = mutation == AuthorityMutation.MisboundEvidencePrincipal
                ? "principal.other"
                : host.Principal.PrincipalId;
            return Task.FromResult(new StandingGrantDecision(
                true,
                "STANDING_GRANT_ALLOWED",
                grant,
                new AuthorizationAuditEvidence(
                    evidencePrincipal,
                    host.Principal.AuthenticationMethod,
                    "standing-grant",
                    mutation == AuthorityMutation.MisboundEvidenceScope ? "state.other" : host.StateSpaceId,
                    mutation == AuthorityMutation.MisboundEvidenceCorrelation ? "command.other" : host.CommandId,
                    true,
                    "STANDING_GRANT_ALLOWED")));
        }

        private StandingGrantDefinitionTarget Target()
        {
            var selected = request.SelectedDefinition;
            return new(
                selected.ExactDefinitionId,
                "mechanic",
                request.Host.ApplicationRevision.ApplicationId,
                CatalogNamespaceIdentity.NamespaceOf(selected.ExactDefinitionId),
                "catalog-owner.service",
                selected.Version,
                selected.Fingerprint);
        }

        private StandingGrantRevision Grant(InteractionInvocationHost host, string principal, string grantId)
        {
            var selected = request.SelectedDefinition;
            var grant = new StandingGrantRevision(
                host.GrantReference,
                grantId,
                1,
                new string('0', 64),
                principal,
                host.ApplicationRevision.ApplicationId,
                StandingGrantScope.StateSpace,
                host.StateSpaceId,
                [StandingGrantCapability.Read],
                new StandingGrantDefinitionAllowance(
                    StandingGrantDefinitionMode.ExactIds,
                    [selected.ExactDefinitionId],
                    []),
                [],
                host.Budget.MaximumOperations,
                host.Budget.DeadlineUtc.AddMinutes(1),
                false,
                "operation.authority-test");
            return grant with
            {
                ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant)
            };
        }
    }
}
