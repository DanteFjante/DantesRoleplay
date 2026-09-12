using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.StateSpaceAdministration;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Compatible_publication_atomically_rebinds_exact_spaces_and_new_actions_use_the_new_generation()
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db,
            "return { data: { count: ctx.input.count + 7 } }; ");
        await AllowPurePublicationAsync(db, execute: true);
        db.ChangeTracker.Clear();
        var before = data.Setup.Activation.Current(Application)!;
        var application = data.Setup.Applications.Get(Application)!;
        var spaces = new SqliteStateSpaceRegistry(db, data.Setup.Applications);
        var live = spaces.Create(new("live-state", application, before.ActivationFingerprint,
            before.ResolutionFingerprint));
        _ = spaces.Create(new("publication-state", application, before.ActivationFingerprint,
            before.ResolutionFingerprint, EcsStateSpaceScope.ApplicationPublication));
        var mixedResolution = new string('F', 64);
        _ = spaces.Create(new("mixed-state", application, before.ActivationFingerprint, mixedResolution));

        data.Setup.Namespaces.Register(new CatalogNamespaceRegistration("demo.state", "human-domain-label",
            "State preservation fixture.", [CatalogNamespaceKinds.ComponentType],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed fixture."));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var type = types.Define(new ComponentTypeDefinition(Application, "demo.state.value",
            "{\"type\":\"object\",\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}},\"additionalProperties\":false}"));
        var entities = new SqliteEntityComponentStore(db, types, schemas);
        await entities.CreateEntityAsync("live-state", "root", "Root");
        await entities.CreateEntityAsync("live-state", "child", "Child");
        var component = await entities.AddComponentAsync(new("live-state", "child",
            new(type.QualifiedId, type.Version, type.SchemaHash), "{\"value\":3}", 0));
        var edges = new SqliteStateSpaceEdgeStore(db, spaces);
        var containment = await edges.MoveContainmentAsync("live-state", "child", "root", "items", 0);

        var catalogs = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]),
            new ActivatedApplicationCatalogMaterializer(data.Setup.Applications,
                data.Setup.Activation, data.Setup.Sources, data.Setup.Roots, data.Setup.Extensions),
            new CatalogCursorCodec(new byte[32]), data.Setup.Activation);
        Assert.True(catalogs.TryGet(Application, out var oldCatalog));
        var oldRecord = oldCatalog.Inspect(new(Application, Application.Value,
            data.Definition.DefinitionId)).Summary;
        var policy = new SqliteStandingGrantPolicy(db, data.Setup.Resolver);
        var manuals = new InteractionManualContextService(
            new ProcedureStore(db), new InteractionFeatureRetriever(catalogs), policy,
            data.Setup.Resolver, data.Setup.Activation, ["system"]);
        var service = new SqliteApplicationAuthoringService(
            db, data.Setup.Applications, data.Setup.Activation, data.Setup.Activation,
            data.Setup.Sources, policy, data.Setup.Resolver, new OperationLog(db),
            PureRuntimeValidator(db, data.Setup), manuals);
        var validated = await service.ValidateAsync(new ApplicationCandidateValidationRequest(
            data.Candidate, [new(data.Definition, "{\"count\":1}", "{\"count\":8}")]),
            PureRuntimeHost(data.Setup, operations: 3));
        Assert.Equal(InteractionInvocationResultTag.Committed, validated.Tag);
        var validation = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking().SingleAsync();
        Assert.Equal("valid", validation.Outcome);
        var activationRequest = new ApplicationCandidateActivationRequest(data.Candidate, validation.OperationId);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_compatible_state_rebind
            BEFORE INSERT ON system_state_space_binding_revision
            WHEN NEW.BindingRevision = 2
            BEGIN SELECT RAISE(ABORT, 'Injected state-space rebind failure'); END;
            """);
        var failed = await service.ActivateAsync(PureRuntimeHost(data.Setup), activationRequest);
        Assert.True(failed.Tag == InteractionInvocationResultTag.Unavailable, failed.Code);
        Assert.Equal(before.ActivationFingerprint,
            data.Setup.Activation.Current(Application)!.ActivationFingerprint);
        Assert.Equal(live, spaces.Get("live-state"));
        Assert.Empty(await db.Set<StateSpaceBindingRevisionRecord>().AsNoTracking().ToArrayAsync());
        Assert.Empty(await db.Set<ApplicationCandidatePublicationRecord>().AsNoTracking().ToArrayAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_compatible_state_rebind;");

        var activated = await service.ActivateAsync(PureRuntimeHost(data.Setup), activationRequest);
        Assert.Equal(InteractionInvocationResultTag.Committed, activated.Tag);
        var current = data.Setup.Activation.Current(Application)!;
        foreach (var id in new[] { "live-state", "publication-state" })
        {
            var rebound = spaces.Get(id)!;
            Assert.Equal(current.ActivationFingerprint, rebound.ManifestFingerprint);
            Assert.Equal(current.ResolutionFingerprint, rebound.ResolutionFingerprint);
            Assert.Equal(2, rebound.BindingRevision);
            Assert.Equal(2, await db.Set<StateSpaceBindingRevisionRecord>().AsNoTracking()
                .CountAsync(value => value.StateSpaceId == id));
        }
        Assert.Equal(EcsStateSpaceScope.Runtime, spaces.Get("live-state")!.Scope);
        Assert.Equal(EcsStateSpaceScope.ApplicationPublication, spaces.Get("publication-state")!.Scope);
        var liveHistory = await db.Set<StateSpaceBindingRevisionRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == "live-state")
            .OrderBy(value => value.BindingRevision).ToArrayAsync();
        Assert.Equal(liveHistory[0].BindingFingerprint, liveHistory[1].PreviousBindingFingerprint);
        Assert.Equal(current.ActivationFingerprint, liveHistory[1].ActiveFingerprint);
        Assert.Equal(2, liveHistory[1].EntityCount);
        Assert.Equal(1, liveHistory[1].ComponentCount);
        Assert.Null(liveHistory[1].OperationId);
        var mixed = spaces.Get("mixed-state")!;
        Assert.Equal(before.ActivationFingerprint, mixed.ManifestFingerprint);
        Assert.Equal(mixedResolution, mixed.ResolutionFingerprint);
        Assert.Equal(1, mixed.BindingRevision);
        Assert.Empty(await db.Set<StateSpaceBindingRevisionRecord>().AsNoTracking()
            .Where(value => value.StateSpaceId == "mixed-state").ToArrayAsync());
        Assert.Equal(component, await entities.GetComponentAsync("live-state", "child", type.QualifiedId));
        Assert.Equal(containment, await edges.GetContainmentAsync("live-state", "child"));

        var replay = await service.ActivateAsync(PureRuntimeHost(data.Setup), activationRequest);
        Assert.Equal(activated.Receipt, replay.Receipt);
        Assert.Equal(4, await db.Set<StateSpaceBindingRevisionRecord>().AsNoTracking().CountAsync());

        Assert.True(catalogs.TryGet(Application, out var newCatalog));
        var newRecord = newCatalog.Inspect(new(Application, Application.Value,
            data.Definition.DefinitionId)).Summary;
        var runner = new ApplicationActionRunner(catalogs, data.Setup.Activation, spaces, types, entities, edges,
            new ApplicationMechanicProjectionMappingResolver(catalogs, spaces, types, edges),
            new ApplicationMechanicEvaluator(catalogs,
                new ApplicationMechanicProjectionResolver(db, spaces), new JintMechanicEngine()),
            new ApplicationEcsEffectApplier(db, entities, spaces, new OperationLog(db), edges),
            new OperationLog(db));
        var request = new ApplicationActionExecutionRequest("live-state", Application,
            newRecord.QualifiedId, newRecord.Version, newRecord.ContentFingerprint,
            new Dictionary<string, string>(), "{\"count\":5}", 7,
            new("0123456789abcdef0123456789abcdef", new string('A', 64)));
        var executed = await runner.RunAsync(request);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, executed.Disposition);
        Assert.Empty(executed.Narration);

        var staleDefinition = await runner.RunAsync(request with
        {
            ContentFingerprint = oldRecord.ContentFingerprint,
            ExecutionIdentity = new("1123456789abcdef0123456789abcdef", new string('B', 64))
        });
        Assert.Equal(ApplicationActionExecutionDisposition.Stale, staleDefinition.Disposition);
        Assert.Equal("MECHANIC_STALE", Assert.Single(staleDefinition.Problems).Code);
        var stranded = await runner.RunAsync(request with
        {
            StateSpaceId = "mixed-state",
            ExecutionIdentity = new("2123456789abcdef0123456789abcdef", new string('C', 64))
        });
        Assert.Equal(ApplicationActionExecutionDisposition.Stale, stranded.Disposition);
        Assert.Equal("STATE_SPACE_ACTIVATION_STALE", Assert.Single(stranded.Problems).Code);
    }
}
