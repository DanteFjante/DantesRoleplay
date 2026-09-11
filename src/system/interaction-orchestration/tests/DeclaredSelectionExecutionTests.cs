using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Interactions.Tests;

/// <summary>Regression coverage for selection enforcement below every transport adapter.</summary>
public sealed class DeclaredSelectionExecutionTests
{
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("fixture-app");

    [Fact]
    public async Task Planned_mechanic_query_runs_matching_selector_before_and_after_the_target()
    {
        var fixture = new Fixture(ProofMode.Matches);

        var result = await fixture.Executor.ExecuteAsync(fixture.Request);

        Assert.Equal("{\"value\":\"visible\"}", result.OutputJson);
        Assert.Equal([Fixture.SelectorMechanicId, Fixture.TargetMechanicId, Fixture.SelectorMechanicId],
            fixture.Evaluator.MechanicIds);
        Assert.All(fixture.Evaluator.RoleBindings, roles =>
            Assert.Equal("entity.selected", Assert.Single(roles.Values)));
    }

    [Fact]
    public async Task Planned_mechanic_query_never_evaluates_target_when_selector_does_not_match()
    {
        var fixture = new Fixture(ProofMode.Mismatch);

        var error = await Assert.ThrowsAsync<ApplicationReadModelException>(() =>
            fixture.Executor.ExecuteAsync(fixture.Request));

        Assert.Equal("READ_MODEL_FORBIDDEN", error.Code);
        Assert.Equal([Fixture.SelectorMechanicId], fixture.Evaluator.MechanicIds);
    }

    [Fact]
    public async Task Planned_mechanic_query_rejects_stale_selection_evidence_after_target()
    {
        var fixture = new Fixture(ProofMode.Stale);

        var error = await Assert.ThrowsAsync<ApplicationReadModelException>(() =>
            fixture.Executor.ExecuteAsync(fixture.Request));

        Assert.Equal("READ_MODEL_SOURCE_STALE", error.Code);
        Assert.Equal([Fixture.SelectorMechanicId, Fixture.TargetMechanicId, Fixture.SelectorMechanicId],
            fixture.Evaluator.MechanicIds);
    }

    [Fact]
    public async Task Planned_mechanic_query_rechecks_each_nested_selector_before_and_after_the_target()
    {
        var fixture = new Fixture(ProofMode.Matches, SelectionTopology.Nested);

        var result = await fixture.Executor.ExecuteAsync(fixture.Request);

        Assert.Equal("{\"value\":\"visible\"}", result.OutputJson);
        Assert.Equal([Fixture.NestedSelectorMechanicId, Fixture.SelectorMechanicId,
            Fixture.NestedSelectorMechanicId, Fixture.TargetMechanicId,
            Fixture.NestedSelectorMechanicId, Fixture.SelectorMechanicId,
            Fixture.NestedSelectorMechanicId], fixture.Evaluator.MechanicIds);
    }

    [Fact]
    public async Task Planned_mechanic_query_rejects_a_declared_selector_cycle_before_target_execution()
    {
        var fixture = new Fixture(ProofMode.Matches, SelectionTopology.Cycle);

        var error = await Assert.ThrowsAsync<ApplicationReadModelException>(() =>
            fixture.Executor.ExecuteAsync(fixture.Request));

        Assert.Equal("READ_MODEL_FORBIDDEN", error.Code);
        Assert.Empty(fixture.Evaluator.MechanicIds);
    }

    [Fact]
    public async Task Planned_mechanic_query_rejects_a_selector_chain_beyond_the_bounded_depth_before_target_execution()
    {
        var fixture = new Fixture(ProofMode.Matches, SelectionTopology.ExcessiveDepth);

        var error = await Assert.ThrowsAsync<ApplicationReadModelException>(() =>
            fixture.Executor.ExecuteAsync(fixture.Request));

        Assert.Equal("READ_MODEL_FORBIDDEN", error.Code);
        Assert.Empty(fixture.Evaluator.MechanicIds);
    }

    [Fact]
    public async Task Direct_service_read_rejects_the_retired_campaign_selection_contract_before_evaluation()
    {
        var fixture = new Fixture(ProofMode.Matches, legacyCampaignSelection: true);

        var error = await Assert.ThrowsAsync<ApplicationReadModelException>(() =>
            fixture.Executor.ExecuteAsync(fixture.Request));

        Assert.Equal("READ_MODEL_FORBIDDEN", error.Code);
        Assert.Empty(fixture.Evaluator.MechanicIds);
    }

    [Fact]
    public void Default_composition_routes_planned_object_queries_through_the_service_backed_executor()
    {
        var services = new ServiceCollection();
        services.AddDantesRoleplayDataAccess("Filename=:memory:");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var executors = scope.ServiceProvider.GetServices<IInteractionQueryExecutor>().ToArray();
        var registered = Assert.Single(executors,
            value => value.Kind == ApplicationQueryContract.ObjectProjectionExecutor);
        Assert.IsType<RegisteredObjectProjectionInteractionQueryExecutor>(registered);
        var registry = scope.ServiceProvider.GetRequiredService<IInteractionQueryExecutorRegistry>();
        Assert.True(registry.TryGet(ApplicationQueryContract.ObjectProjectionExecutor, out var routed));
        Assert.Same(registered, routed);
    }

    private sealed class Fixture
    {
        private const string TargetQueryId = "fixture-app.query.target";
        private const string SelectorQueryId = "fixture-app.query.selector";
        private const string NestedSelectorQueryId = "fixture-app.query.nested-selector";
        private const string ThirdSelectorQueryId = "fixture-app.query.third-selector";
        private const string FourthSelectorQueryId = "fixture-app.query.fourth-selector";
        private const string FifthSelectorQueryId = "fixture-app.query.fifth-selector";
        public const string TargetMechanicId = "fixture-app.mechanic.target.project";
        public const string SelectorMechanicId = "fixture-app.mechanic.selector.project";
        public const string NestedSelectorMechanicId = "fixture-app.mechanic.nested-selector.project";
        public const string ThirdSelectorMechanicId = "fixture-app.mechanic.third-selector.project";
        public const string FourthSelectorMechanicId = "fixture-app.mechanic.fourth-selector.project";
        public const string FifthSelectorMechanicId = "fixture-app.mechanic.fifth-selector.project";

        public Fixture(ProofMode mode, SelectionTopology topology = SelectionTopology.Single,
            bool legacyCampaignSelection = false)
        {
            var applications = new InMemoryApplicationRegistry();
            var revision = applications.Register(new(App, "Fixture", "Selection test fixture.", []));
            var activationFingerprint = Hash("selection-activation");
            var targetSchema = "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"string\"}}}";
            var selectorSchema = "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"selectedId\"],\"properties\":{\"selectedId\":{\"type\":\"string\"}}}";
            var validator = new BoundedJsonSchemaValidator();
            var targetSchemaHash = validator.Compile(targetSchema).SchemaHash;
            var selectorSchemaHash = validator.Compile(selectorSchema).SchemaHash;

            var targetMechanic = Record("mechanic", TargetMechanicId, Mechanic(TargetMechanicId, "subject"));
            var selectorMechanic = Record("mechanic", SelectorMechanicId, Mechanic(SelectorMechanicId, "proof"));
            var nestedSelectorMechanic = Record("mechanic", NestedSelectorMechanicId,
                Mechanic(NestedSelectorMechanicId, "proof"));
            var nestedSelection = topology is SelectionTopology.Nested or SelectionTopology.Cycle or SelectionTopology.ExcessiveDepth
                ? new
                {
                    queryId = NestedSelectorQueryId,
                    targetRole = "proof",
                    resultPointer = "/selectedId",
                    roleBindings = new Dictionary<string, string> { ["proof"] = "proof" }
                }
                : null;
            var selectorQuery = Record("query", SelectorQueryId, Query(
                SelectorQueryId, SelectorMechanicId, selectorMechanic.ContentFingerprint, selectorSchema,
                selectorSchemaHash, "proof", nestedSelection));
            var thirdSelectorMechanic = Record("mechanic", ThirdSelectorMechanicId,
                Mechanic(ThirdSelectorMechanicId, "proof"));
            var fourthSelectorMechanic = Record("mechanic", FourthSelectorMechanicId,
                Mechanic(FourthSelectorMechanicId, "proof"));
            var fifthSelectorMechanic = Record("mechanic", FifthSelectorMechanicId,
                Mechanic(FifthSelectorMechanicId, "proof"));
            object? directSelection(string queryId) => new
            {
                queryId,
                targetRole = "proof",
                resultPointer = "/selectedId",
                roleBindings = new Dictionary<string, string> { ["proof"] = "proof" }
            };
            var nestedSelectorQuery = Record("query", NestedSelectorQueryId, Query(
                NestedSelectorQueryId, NestedSelectorMechanicId, nestedSelectorMechanic.ContentFingerprint, selectorSchema,
                selectorSchemaHash, "proof", topology == SelectionTopology.Cycle
                    ? directSelection(SelectorQueryId)
                    : topology == SelectionTopology.ExcessiveDepth ? directSelection(ThirdSelectorQueryId) : null));
            var thirdSelectorQuery = Record("query", ThirdSelectorQueryId, Query(
                ThirdSelectorQueryId, ThirdSelectorMechanicId, thirdSelectorMechanic.ContentFingerprint, selectorSchema,
                selectorSchemaHash, "proof", topology == SelectionTopology.ExcessiveDepth ? directSelection(FourthSelectorQueryId) : null));
            var fourthSelectorQuery = Record("query", FourthSelectorQueryId, Query(
                FourthSelectorQueryId, FourthSelectorMechanicId, fourthSelectorMechanic.ContentFingerprint, selectorSchema,
                selectorSchemaHash, "proof", topology == SelectionTopology.ExcessiveDepth ? directSelection(FifthSelectorQueryId) : null));
            var fifthSelectorQuery = Record("query", FifthSelectorQueryId, Query(
                FifthSelectorQueryId, FifthSelectorMechanicId, fifthSelectorMechanic.ContentFingerprint, selectorSchema,
                selectorSchemaHash, "proof", null));
            var targetQuery = Record("query", TargetQueryId, Query(
                TargetQueryId, TargetMechanicId, targetMechanic.ContentFingerprint, targetSchema,
                targetSchemaHash, "subject", legacyCampaignSelection ? null : new
                {
                    queryId = SelectorQueryId,
                    targetRole = "subject",
                    resultPointer = "/selectedId",
                    roleBindings = new Dictionary<string, string> { ["proof"] = "subject" }
                }, legacyCampaignSelection ? new
                {
                    queryId = SelectorQueryId,
                    entityIdField = "selectedId"
                } : null, legacyCampaignSelection));
            var manifest = CatalogNavigationManifest.Create(App, Hash("selection-catalog"), "catalog-lexical-v1",
                [new(App.Value, "Fixture", "Selection test fixture.")],
                [new(App.Value, "", "Fixture", "Selection test fixture.", CatalogDescriptionStatus.Authored),
                    new(App.Value, "fixture", "Fixture", "Selection test fixture.", CatalogDescriptionStatus.Authored)],
                [targetQuery, selectorQuery, nestedSelectorQuery, thirdSelectorQuery, fourthSelectorQuery, fifthSelectorQuery,
                    targetMechanic, selectorMechanic, nestedSelectorMechanic, thirdSelectorMechanic, fourthSelectorMechanic,
                    fifthSelectorMechanic]);
            var catalogs = new InMemoryPublicApplicationCatalogProvider(
                new Dictionary<ApplicationIdentifier, ICatalogNavigator>
                {
                    [App] = new InMemoryCatalogNavigator(manifest,
                        new CatalogCursorCodec(Encoding.UTF8.GetBytes("selection-test-cursor-key-32bytes")))
                });
            var activation = new ActiveApplicationManifest(App, 1, revision.Revision, revision.Fingerprint,
                Hash("preview"), Hash("scan"), Hash("candidate"), Hash("dependencies"), activationFingerprint,
                "coverage-v1", true, [], [], "operation.activation", DateTime.UtcNow);
            var state = new StateSpaceView("space.1", revision, activationFingerprint, 1,
                DateTime.UtcNow, DateTime.UtcNow);
            Evaluator = new(mode);
            var service = new ApplicationReadModelService(catalogs, new Activation(activation), new Spaces(state),
                new MappingResolver(), Evaluator, validator);
            Executor = new MechanicProjectionInteractionQueryExecutor(service);
            var targetContract = ApplicationQueryContract.Parse(targetQuery.ContentJson, App);
            Request = new InteractionQueryExecutionRequest(state.StateSpaceId, App, TargetQueryId,
                new(targetContract.Executor, targetContract.ProjectionQualifiedId, targetContract.ProjectionVersion,
                    targetContract.ProjectionContentHash, targetContract.OutputSchemaHash, targetContract.OutputSchemaJson,
                    targetContract.Exposure, targetContract.Roles.Keys),
                new Dictionary<string, string> { ["subject"] = "entity.selected" },
                MechanicAudienceContext.Player);
        }

        public Evaluator Evaluator { get; }
        public MechanicProjectionInteractionQueryExecutor Executor { get; }
        public InteractionQueryExecutionRequest Request { get; }
    }

    private enum ProofMode { Matches, Mismatch, Stale }
    private enum SelectionTopology { Single, Nested, Cycle, ExcessiveDepth }

    private static string Query(string queryId, string mechanicId, string mechanicHash, string schema,
        string schemaHash, string role, object? selection, object? campaignSelection = null,
        bool omitRoleBindings = false) => JsonSerializer.Serialize(new
    {
        id = queryId,
        category = "fixture.selection",
        name = "Fixture query",
        description = "Reads a fixture.",
        matches = new[] { "fixture" },
        roles = new Dictionary<string, string> { [role] = "A trusted role." },
        roleBindings = omitRoleBindings ? null : new Dictionary<string, object> { [role] = new { source = "route-entity" } },
        selection,
        campaignSelection,
        executor = ApplicationQueryContract.MechanicProjectionExecutor,
        projection = new { qualifiedId = mechanicId, version = 1, contentHash = mechanicHash, outputSchemaHash = schemaHash },
        outputSchema = JsonDocument.Parse(schema).RootElement.Clone(),
        exposure = "model-visible",
        status = "active"
    }, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

    private static string Mechanic(string id, string role) => JsonSerializer.Serialize(new
    {
        id,
        requirements = "{\"roles\":{\"" + role + "\":{\"components\":[]}}}",
        source = "return { data: {} };"
    });

    private static CatalogRecordDefinition Record(string kind, string id, string content) =>
        new(App.Value, kind, id, id, id, [], [], "fixture", "active", 1, content, Hash(content), "fixture", "fixture.json");

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class Evaluator(ProofMode mode) : IApplicationMechanicEvaluator
    {
        public List<string> MechanicIds { get; } = [];
        public List<IReadOnlyDictionary<string, string>> RoleBindings { get; } = [];

        public Task<ApplicationMechanicEvaluationResult> EvaluateAsync(ApplicationMechanicEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            MechanicIds.Add(request.QualifiedMechanicId);
            RoleBindings.Add(new Dictionary<string, string>(request.RoleEntityIds, StringComparer.Ordinal));
            var selector = request.QualifiedMechanicId is Fixture.SelectorMechanicId or Fixture.NestedSelectorMechanicId
                or Fixture.ThirdSelectorMechanicId or Fixture.FourthSelectorMechanicId or Fixture.FifthSelectorMechanicId;
            var secondSelector = selector && MechanicIds.Count(value => value == Fixture.SelectorMechanicId) == 2;
            var data = selector
                ? JsonSerializer.Serialize(new { selectedId = mode == ProofMode.Mismatch ? "entity.other" : "entity.selected" })
                : "{\"value\":\"visible\"}";
            var source = selector && mode == ProofMode.Stale && secondSelector ? "proof-revision-2"
                : selector ? "proof-revision-1" : "target-revision";
            return Task.FromResult(new ApplicationMechanicEvaluationResult(request.QualifiedMechanicId,
                request.ContentFingerprint, new MechanicProjection { StateSpaceId = request.StateSpaceId,
                    AuthorizedSourceRevision = source }, new MechanicRunResult
                {
                    Ok = true,
                    Output = new MechanicOutput { HasData = true, Data = data }
                }, []));
        }
    }

    private sealed class MappingResolver : IApplicationMechanicProjectionMappingResolver
    {
        public Task<ApplicationMechanicProjectionMappingResult> ResolveAsync(string stateSpaceId,
            ApplicationIdentifier applicationId, string qualifiedMechanicId, MechanicRequirements requirements,
            CancellationToken cancellationToken = default) => Task.FromResult(
            new ApplicationMechanicProjectionMappingResult(new(new Dictionary<string, EcsComponentReference>(),
                new Dictionary<string, string>()), []));
    }

    private sealed class Activation(ActiveApplicationManifest value) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            applicationId == App ? value : null;
    }

    private sealed class Spaces(StateSpaceView value) : IStateSpaceRegistry
    {
        public StateSpaceView? Get(string stateSpaceId) => stateSpaceId == value.StateSpaceId ? value : null;
        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId, string? afterStateSpaceId,
            int limit) => throw new NotSupportedException();
    }
}
