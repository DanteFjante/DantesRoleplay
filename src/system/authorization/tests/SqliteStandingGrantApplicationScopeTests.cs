using DantesRoleplay.CatalogNamespaces;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Application_only_authority_records_application_scope_and_cannot_supply_a_state_scope()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Read]);
        var host = ApplicationHost(setup);
        Assert.Null(host.StateSpaceId);
        Assert.Null(host.StateRevision);
        var target = await setup.Resolver.ResolveCurrentAsync(host, "demo.runtime.inspect", CatalogNamespaceKinds.Procedure);
        Assert.NotNull(target.Target);
        var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
        var allowed = await policy.EvaluateAsync(host, new(StandingGrantCapability.Read, StandingGrantScope.Application, [target.Target!], []));
        Assert.True(allowed.Allowed);
        Assert.Equal(Application.Value, allowed.Evidence.Scope);
        var state = await policy.EvaluateAsync(host, new(StandingGrantCapability.Read, StandingGrantScope.StateSpace, [target.Target!], []));
        Assert.False(state.Allowed);
        Assert.Equal("STANDING_GRANT_STATE_SCOPE_REQUIRED", state.Code);
        Assert.Null(state.Grant);
    }
}
