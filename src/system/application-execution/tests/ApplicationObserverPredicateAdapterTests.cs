using System.Text;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.TriggerScheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public void Production_host_registers_one_scoped_catalog_javascript_predicate_adapter()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddDantesRoleplayDataAccess("Filename=:memory:");
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var capture = scope.ServiceProvider.GetRequiredService<IApplicationObserverPredicateInputCapture>();
        var evaluator = scope.ServiceProvider.GetRequiredService<IApplicationObserverPredicateEvaluator>();

        Assert.IsType<CatalogJavaScriptObserverPredicateAdapter>(capture);
        Assert.Same(capture, evaluator);
    }

    [Theory]
    [InlineData(3, "allowed", true)]
    [InlineData(2, "allowed", false)]
    [InlineData(3, "ignored", false)]
    public async Task Retained_catalog_javascript_predicate_runs_actual_jint_on_the_frozen_admitted_input(
        int value, string eventKind, bool expected)
    {
        await using var db = fixture.CreateContext();
        var data = await ObserverSetupAsync(db,
            "return { data: { matches: ctx.input.value === 3 && ctx.event.kind === 'allowed' } };");
        var captured = await CaptureObserverAsync(db, data, value, eventKind);

        var result = await data.Adapter.EvaluateAsync(data.Host, captured);

        Assert.True(result.Evaluated, result.Code);
        Assert.Equal(expected, result.Matches);
        Assert.NotNull(result.Evidence);
        Assert.Equal(data.Selection.Mechanic, result.Evidence!.Selection.Mechanic);
        Assert.Equal(captured.InputFingerprint, result.Evidence.InputFingerprint);
        Assert.Equal(captured.ProjectionFingerprint, result.Evidence.ProjectionFingerprint);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Queued_predicate_uses_retained_source_after_current_replacement()
    {
        await using var db = fixture.CreateContext();
        var data = await ObserverSetupAsync(db, "return { data: { matches: true } };");
        var captured = await CaptureObserverAsync(db, data, 3, "allowed");
        WritePureAction("return { data: { matches: false } };", ObserverRequirements);
        await ActivateAsync(data.Setup, data.Selection.CatalogOrigin.ActivationFingerprint);
        db.ChangeTracker.Clear();

        var result = await data.Adapter.EvaluateAsync(data.Host, captured);

        Assert.True(result.Evaluated, result.Code);
        Assert.True(result.Matches);
        Assert.Equal(data.Selection.CatalogOrigin, result.Evidence!.Selection.CatalogOrigin);
    }

    [Fact]
    public async Task Predicate_rejects_a_changed_frozen_projection_before_jint()
    {
        await using var db = fixture.CreateContext();
        var data = await ObserverSetupAsync(db, "return { data: { matches: true } };");
        var captured = await CaptureObserverAsync(db, data, 3, "allowed");
        captured = captured with { Projection = captured.Projection with { Input = "{\"value\":4}" } };

        var result = await data.Adapter.EvaluateAsync(data.Host, captured);

        Assert.False(result.Evaluated);
        Assert.Equal("OBSERVER_PREDICATE_CAPTURE_INVALID", result.Code);
        Assert.Null(result.Evidence);
    }

    [Theory]
    [InlineData("effect", "OBSERVER_PREDICATE_OUTPUT_INVALID")]
    [InlineData("service", "OBSERVER_PREDICATE_EVALUATION_FAILED")]
    public async Task Predicate_denies_effects_and_unavailable_service_access(string failure, string expectedCode)
    {
        var source = failure == "effect"
            ? "return { data: { matches: true }, effects: [{ type: 'invented' }] };"
            : "return { data: { matches: ctx.services.invoke() } };";
        await using var db = fixture.CreateContext();
        var data = await ObserverSetupAsync(db, source);
        var captured = await CaptureObserverAsync(db, data, 3, "allowed");

        var result = await data.Adapter.EvaluateAsync(data.Host, captured);

        Assert.False(result.Evaluated);
        Assert.False(result.Matches);
        Assert.Equal(expectedCode, result.Code);
        Assert.Null(result.Evidence);
    }

    [Theory]
    [InlineData("source", "STANDING_GRANT_SOURCE_DRIFT")]
    [InlineData("grant", "STANDING_GRANT_NOT_CURRENT")]
    public async Task Predicate_rechecks_retained_source_registration_and_current_read_grant(
        string drift, string expectedCode)
    {
        await using var db = fixture.CreateContext();
        var data = await ObserverSetupAsync(db, "return { data: { matches: true } };");
        var captured = await CaptureObserverAsync(db, data, 3, "allowed");
        if (drift == "source") data.Setup.Sources.Retire(Application, "catalog", "Withdraw fixture source.");
        else await RevokeObserverGrantAsync(db);
        db.ChangeTracker.Clear();

        var result = await data.Adapter.EvaluateAsync(data.Host, captured);

        Assert.False(result.Evaluated);
        Assert.False(result.Matches);
        Assert.Equal(expectedCode, result.Code);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task Retained_mechanic_read_is_state_scoped_and_current_grant_authorized()
    {
        await using var db = fixture.CreateContext();
        var data = await ObserverSetupAsync(db, "return { data: { matches: true } };");
        var retained = await data.Setup.Resolver.ResolveRetainedAsync(data.Host,
            data.Selection.CatalogOrigin, data.Selection.Mechanic);

        Assert.Equal(StandingGrantTargetResolutionStatus.Available, retained.Status);
        var allowed = await new SqliteStandingGrantPolicy(db, data.Setup.Resolver).EvaluateAsync(data.Host,
            new(StandingGrantCapability.Read, StandingGrantScope.StateSpace, [retained.Target!], []));
        Assert.True(allowed.Allowed, allowed.Code);
        var execute = Assert.Throws<InteractionContractException>(() =>
            StandingGrantContractRules.ValidateRequirement(data.Host,
                new(StandingGrantCapability.Execute, StandingGrantScope.StateSpace, [retained.Target!], [])));
        Assert.Equal("STANDING_GRANT_RETAINED_SCOPE_DENIED", execute.Code);
    }

    private const string ObserverRequirements =
        "{\"inputSchema\":{\"additionalProperties\":false,\"properties\":{\"value\":{\"type\":\"integer\"}},\"required\":[\"value\"],\"type\":\"object\"}}";

    private async Task<ObserverSetup> ObserverSetupAsync(DantesRoleplayDbContext db, string source)
    {
        var setup = Setup(db);
        setup.Namespaces.Register(new CatalogNamespaceRegistration(
            "demo.runtime.pure", "human-domain-label", "Observer predicate fixtures.",
            [CatalogNamespaceKinds.Mechanic], ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed observer predicate fixture."));
        WritePureAction(source, ObserverRequirements);
        await ActivateAsync(setup);
        var activation = setup.Activation.Current(Application)!;
        var stateSpaces = new SqliteStateSpaceRegistry(db, setup.Applications);
        var state = stateSpaces.Create(new("observer-state", setup.Applications.Get(Application)!,
            activation.ActivationFingerprint, activation.ResolutionFingerprint));
        await SeedObserverGrantAsync(db);
        var host = new InteractionInvocationHost(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            setup.Applications.Get(Application)!, state.StateSpaceId, "observer-grant@1", "observer-evaluate",
            InteractionStateRevision.From(state), InteractionExecutionProfile.ReadOnly,
            new(8, DateTime.UtcNow.AddMinutes(2)));
        var selected = await setup.Resolver.ResolveCurrentAsync(host, PureActionId, CatalogNamespaceKinds.Mechanic);
        var target = Assert.IsType<StandingGrantDefinitionTarget>(selected.Target);
        var origin = Assert.IsType<StandingGrantActivationOrigin>(selected.CurrentActivation);
        var canonicalRequirements = InteractionCanonicalJson.CanonicalizeObject(ObserverRequirements);
        var selection = new ApplicationObserverPredicateSelection(Application,
            new(target.DefinitionId, target.Kind, target.Revision, target.ContentFingerprint), origin,
            Assert.Single(activation.Sources, value => value.SourceId == "catalog").RegistrationFingerprint,
            canonicalRequirements, PredicateProof.Requirements(canonicalRequirements),
            new Dictionary<string, string>(StringComparer.Ordinal));
        var materializer = new ActivatedApplicationCatalogMaterializer(setup.Applications, setup.Activation,
            setup.Sources, setup.Roots, setup.Extensions).UsePreparationCache(
                new ActivatedApplicationCatalogSnapshotCache(), new ActivatedApplicationCatalogCacheAuthority());
        var publicCatalogs = Catalogs(setup);
        var types = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
        var edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);
        var mapping = new ApplicationMechanicProjectionMappingResolver(publicCatalogs, stateSpaces, types, edges);
        var projection = new ApplicationMechanicProjectionResolver(db, stateSpaces);
        var evaluator = new ApplicationMechanicEvaluator(publicCatalogs, projection, new JintMechanicEngine());
        var adapter = new CatalogJavaScriptObserverPredicateAdapter(db, setup.Activation, materializer,
            setup.Resolver, new SqliteStandingGrantPolicy(db, setup.Resolver), mapping, projection,
            evaluator, new BoundedJsonSchemaValidator());
        return new(setup, host, selection, adapter);
    }

    private static async Task<ApplicationObserverPredicateCapturedInput> CaptureObserverAsync(
        DantesRoleplayDbContext db, ObserverSetup setup, int value, string eventKind)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var captured = await setup.Adapter.CaptureAsync(setup.Host, setup.Selection,
            "{\"value\":" + value + "}", "{\"kind\":\"" + eventKind + "\"}", 17);
        await transaction.CommitAsync();
        return captured;
    }

    private static async Task SeedObserverGrantAsync(DantesRoleplayDbContext db)
    {
        var grant = new StandingGrantRevision("observer-grant@1", "observer-grant", 1, new string('0', 64),
            "principal." + new string('a', 64), Application, StandingGrantScope.StateSpace, "observer-state",
            [StandingGrantCapability.Read],
            new(StandingGrantDefinitionMode.ApplicationOwned, [],
                [new("demo.runtime.pure", false, [CatalogNamespaceKinds.Mechanic])]),
            [], 8, DateTime.UtcNow.AddMinutes(10), false, "observer-grant-operation");
        db.Add(new Operation { Id = grant.IssuedByOperationId, Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = grant.GrantId, Revision = grant.Revision, GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference, ApplicationId = Application.Value,
            Scope = "stateSpace", StateSpaceId = "observer-state",
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant),
            MaximumOperations = grant.MaximumOperations, ExpiresAtUtc = grant.ExpiresAtUtc,
            IssuedByOperationId = grant.IssuedByOperationId
        });
        db.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = 1 });
        await db.SaveChangesAsync();
    }

    private static async Task RevokeObserverGrantAsync(DantesRoleplayDbContext db)
    {
        var current = await db.Set<StandingGrantCurrentRecord>().SingleAsync(value => value.GrantId == "observer-grant");
        var prior = await db.Set<StandingGrantRevisionRecord>().SingleAsync(value =>
            value.GrantId == "observer-grant" && value.Revision == current.Revision);
        var grant = SqliteStandingGrantPolicy.Parse(prior) with
        { GrantReference = "observer-grant@2", Revision = 2, Revoked = true };
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = grant.GrantId, Revision = grant.Revision, GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference, ApplicationId = grant.ApplicationId.Value,
            Scope = "stateSpace", StateSpaceId = grant.StateSpaceId,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant),
            MaximumOperations = grant.MaximumOperations, ExpiresAtUtc = grant.ExpiresAtUtc,
            Revoked = true, IssuedByOperationId = grant.IssuedByOperationId
        });
        current.Revision = 2;
        await db.SaveChangesAsync();
    }

    private sealed record ObserverSetup(SetupState Setup, InteractionInvocationHost Host,
        ApplicationObserverPredicateSelection Selection, CatalogJavaScriptObserverPredicateAdapter Adapter);
}
