using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Retained_task_origin_survives_unrelated_application_revision_but_rechecks_source_ownership()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        var oldHost = Host(setup);
        var selected = await setup.Resolver.ResolveCurrentAsync(oldHost, "demo.runtime.inspect", CatalogNamespaceKinds.Procedure);
        var origin = Assert.IsType<StandingGrantActivationOrigin>(selected.CurrentActivation);
        var oldTarget = Assert.IsType<StandingGrantDefinitionTarget>(selected.Target);
        var baseApp = ApplicationIdentifier.Parse("base-demo");
        setup.Applications.Register(new(baseApp, "Base", "Unrelated registration evolution fixture.", []));
        var next = setup.Applications.ReviseBaseApplications(Application, [baseApp], oldHost.ApplicationRevision.Revision,
            oldHost.ApplicationRevision.Fingerprint);
        var stateSpaceId = Assert.IsType<string>(oldHost.StateSpaceId);
        var stateRevision = Assert.IsType<string>(oldHost.StateRevision);
        var host = new InteractionInvocationHost(oldHost.Principal, next, stateSpaceId, oldHost.GrantReference,
            "historical-after-registration", stateRevision, InteractionExecutionProfile.ReadOnly,
            new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));

        var result = await setup.Resolver.ResolveRetainedAsync(host, origin, Selection(oldTarget));
        Assert.Equal(StandingGrantTargetResolutionStatus.Available, result.Status);
        Assert.Equal(origin, result.Target!.RetainedActivation);
        Assert.Equal(oldTarget.ContentFingerprint, result.Target.ContentFingerprint);
        Assert.Null(result.CurrentActivation);

        var staleOrigin = origin with { ActivationFingerprint = new string('B', 64) };
        var stale = await setup.Resolver.ResolveRetainedAsync(host, staleOrigin, Selection(oldTarget));
        Assert.Equal(StandingGrantTargetResolutionStatus.Denied, stale.Status);
        Assert.Equal("STANDING_GRANT_RETAINED_ORIGIN_STALE", stale.Code);
        Assert.Null(stale.Target);

        setup.Sources.Retire(Application, "catalog", "Source ownership was withdrawn.");
        var denied = await setup.Resolver.ResolveRetainedAsync(host, origin, Selection(oldTarget));
        Assert.Equal(StandingGrantTargetResolutionStatus.Denied, denied.Status);
        Assert.Equal("STANDING_GRANT_SOURCE_DRIFT", denied.Code);
        Assert.Null(denied.Target);
    }
}
