using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Ecs;
using DantesRoleplay.MCPServer;

namespace DantesRoleplay.Interactions.Tests;

public sealed class PrivateHostInteractionAuthorizationTests
{
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("fixture-app");

    [Theory]
    [InlineData(InteractionCapability.Plan)]
    [InlineData(InteractionCapability.Execute)]
    [InlineData(InteractionCapability.ReadReceipt)]
    public void Verified_operator_is_allowed_only_for_the_application_bound_state_space(
        InteractionCapability capability)
    {
        var policy = new PrivateHostInteractionAuthorizationPolicy(new Spaces());
        var principal = PrivateOperatorPrincipal.Create("local-loopback", "fixture");

        var allowed = policy.Evaluate(new(principal, App, "state.1", capability, "request.1"));
        var mismatched = policy.Evaluate(new(principal, ApplicationIdentifier.Parse("other-app"),
            "state.1", capability, "request.2"));
        var unverified = policy.Evaluate(new(TrustedPrincipalContext.Unauthenticated("NO_IDENTITY"),
            App, "state.1", capability, "request.3"));

        Assert.True(allowed.Allowed);
        Assert.False(mismatched.Allowed);
        Assert.Equal("INTERACTION_SCOPE_MISMATCH", mismatched.Code);
        Assert.False(unverified.Allowed);
        Assert.Equal("VERIFIED_OPERATOR_REQUIRED", unverified.Code);
    }

    private sealed class Spaces : IStateSpaceRegistry
    {
        private readonly StateSpaceView value = new("state.1", new(App, 1, new string('A', 64), []),
            new string('B', 64), 1, DateTime.UtcNow, DateTime.UtcNow);
        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceView? Get(string stateSpaceId) => stateSpaceId == value.StateSpaceId ? value : null;
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId, string? afterStateSpaceId, int limit) => new([value], null);
    }
}
