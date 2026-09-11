using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class RegisteredSelectionMcpTests
{
    [Theory]
    [InlineData(ProofMode.Matches, true, null, 1)]
    [InlineData(ProofMode.Mismatch, false, "APPLICATION_OBJECT_FORBIDDEN", 0)]
    [InlineData(ProofMode.Stale, false, "APPLICATION_OBJECT_STALE", 1)]
    public async Task Direct_registered_object_read_cannot_bypass_declared_selector(
        ProofMode mode, bool succeeds, string? expectedError, int expectedTargetReads)
    {
        var fixture = new Fixture(mode);

        var result = await new QueryMcpTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: new Log(), notifications: null!,
            kind: "system.application-object", applicationId: Fixture.App.Value,
            stateSpaceId: Fixture.StateSpaceId, id: Fixture.TargetQueryId, entityId: Fixture.SelectedEntityId,
            request: "{}", privateOperator: new Authorizer(), publicCatalogs: fixture.Catalogs,
            applicationEntityStateSpaces: fixture.StateSpaces, applicationEntities: fixture.Entities,
            applicationReadModels: fixture.Service,
            applicationRoleBindings: new ApplicationQueryRoleBindingResolver(new BoundedJsonSchemaValidator()));

        Assert.Equal(succeeds, result.Ok);
        Assert.Equal(expectedError, result.Error?.Code);
        Assert.Equal(expectedTargetReads, fixture.Roots.Calls);
        Assert.Equal(mode == ProofMode.Mismatch ? [Fixture.SelectorMechanicId]
            : [Fixture.SelectorMechanicId, Fixture.SelectorMechanicId], fixture.Evaluator.MechanicIds);
        if (succeeds)
        {
            using var data = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
            Assert.Equal("object-visible", data.RootElement.GetProperty("data").GetProperty("value").GetString());
        }
        else Assert.Null(result.Data);
    }

    [Theory]
    [InlineData(ProofMode.Matches, true, null, 1)]
    [InlineData(ProofMode.Mismatch, false, "READ_MODEL_FORBIDDEN", 0)]
    [InlineData(ProofMode.Stale, false, "READ_MODEL_SOURCE_STALE", 1)]
    public async Task Planned_registered_object_read_uses_the_same_declared_selector_proof(
        ProofMode mode, bool succeeds, string? expectedError, int expectedTargetReads)
    {
        var fixture = new Fixture(mode);
        Assert.True(fixture.Catalogs.TryGet(Fixture.App, out var catalog));
        var contract = ApplicationQueryContract.Parse(catalog.Inspect(new(Fixture.App, Fixture.App.Value,
            Fixture.TargetQueryId)).ContentJson, Fixture.App);
        var executor = new RegisteredObjectProjectionInteractionQueryExecutor(fixture.Service);
        var request = new InteractionQueryExecutionRequest(Fixture.StateSpaceId, Fixture.App, Fixture.TargetQueryId,
            new(contract.Executor, contract.ProjectionQualifiedId, contract.ProjectionVersion,
                contract.ProjectionContentHash, contract.OutputSchemaHash, contract.OutputSchemaJson,
                contract.Exposure, contract.Roles.Keys, contract.ObjectCollectionId),
            new Dictionary<string, string> { ["subject"] = Fixture.SelectedEntityId },
            MechanicAudienceContext.GameMaster);

        if (succeeds)
        {
            var result = await executor.ExecuteAsync(request);
            Assert.Equal("{\"value\":\"object-visible\"}", result.OutputJson);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ApplicationReadModelException>(() => executor.ExecuteAsync(request));
            Assert.Equal(expectedError, error.Code);
        }
        Assert.Equal(expectedTargetReads, fixture.Roots.Calls);
        Assert.Equal(mode == ProofMode.Mismatch ? [Fixture.SelectorMechanicId]
            : [Fixture.SelectorMechanicId, Fixture.SelectorMechanicId], fixture.Evaluator.MechanicIds);
    }

    [Fact]
    public async Task Planned_registered_object_read_rejects_an_old_projection_pin_before_selection_or_materialization()
    {
        var fixture = new Fixture(ProofMode.Matches, projectionVersion: 2);
        Assert.True(fixture.Catalogs.TryGet(Fixture.App, out var catalog));
        var active = ApplicationQueryContract.Parse(catalog.Inspect(new(Fixture.App, Fixture.App.Value,
            Fixture.TargetQueryId)).ContentJson, Fixture.App);
        var oldPlan = new InteractionQueryContractReference(active.Executor, active.ProjectionQualifiedId, 1,
            Hash("selected-object-v1"), active.OutputSchemaHash, active.OutputSchemaJson, active.Exposure,
            active.Roles.Keys, active.ObjectCollectionId);
        var request = new InteractionQueryExecutionRequest(Fixture.StateSpaceId, Fixture.App, Fixture.TargetQueryId,
            oldPlan, new Dictionary<string, string> { ["subject"] = Fixture.SelectedEntityId },
            MechanicAudienceContext.GameMaster);
        var executor = new RegisteredObjectProjectionInteractionQueryExecutor(fixture.Service);

        var error = await Assert.ThrowsAsync<ApplicationReadModelException>(() => executor.ExecuteAsync(request));

        Assert.Equal("READ_MODEL_SOURCE_STALE", error.Code);
        Assert.Empty(fixture.Evaluator.MechanicIds);
        Assert.Equal(0, fixture.Roots.Calls);
    }

    public enum ProofMode { Matches, Mismatch, Stale }

    private sealed class Fixture
    {
        public static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("fixture-app");
        public const string StateSpaceId = "space.1";
        public const string SelectedEntityId = "entity.selected";
        public const string TargetQueryId = "fixture-app.query.selected-object";
        public const string SelectorQueryId = "fixture-app.query.object-selection-proof";
        public const string SelectorMechanicId = "fixture-app.mechanic.object-selection-proof.project";
        public const string ObjectId = "fixture-app.object.selected";

        public Fixture(ProofMode mode, int projectionVersion = 1)
        {
            var applications = new InMemoryApplicationRegistry();
            var revision = applications.Register(new(App, "Fixture", "Registered selector fixture.", []));
            var activationFingerprint = Hash("registered-selector-activation");
            var selectorSchema = "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"selectedId\"],\"properties\":{\"selectedId\":{\"type\":\"string\"}}}";
            var validator = new BoundedJsonSchemaValidator();
            var selectorSchemaHash = validator.Compile(selectorSchema).SchemaHash;
            var selectorMechanic = Record("mechanic", SelectorMechanicId, Mechanic(SelectorMechanicId));
            var selectorQuery = Record("query", SelectorQueryId, MechanicQuery(SelectorQueryId,
                SelectorMechanicId, selectorMechanic.ContentFingerprint, selectorSchema, selectorSchemaHash));
            var objectHash = Hash($"selected-object-v{projectionVersion}");
            var targetQuery = Record("query", TargetQueryId, ObjectQuery(objectHash, projectionVersion));
            var manifest = CatalogNavigationManifest.Create(App, Hash("registered-selector-catalog"),
                "catalog-lexical-v1", [new(App.Value, "Fixture", "Registered selector fixture.")],
                [new(App.Value, "", "Fixture", "Registered selector fixture.", CatalogDescriptionStatus.Authored),
                    new(App.Value, "fixture", "Fixture", "Registered selector fixture.", CatalogDescriptionStatus.Authored)],
                [targetQuery, selectorQuery, selectorMechanic]);
            Catalogs = new InMemoryPublicApplicationCatalogProvider(
                new Dictionary<ApplicationIdentifier, ICatalogNavigator>
                {
                    [App] = new InMemoryCatalogNavigator(manifest,
                        new CatalogCursorCodec(Encoding.UTF8.GetBytes("registered-selector-cursor-key-32")))
                });
            var activation = new ActiveApplicationManifest(App, 1, revision.Revision, revision.Fingerprint,
                Hash("preview"), Hash("scan"), Hash("candidate"), Hash("dependencies"), activationFingerprint,
                "coverage-v1", true, [], [], "operation.activation", DateTime.UtcNow);
            var state = new StateSpaceView(StateSpaceId, revision, activationFingerprint, 1,
                DateTime.UtcNow, DateTime.UtcNow);
            StateSpaces = new StateSpaces(state);
            Entities = new Entities();
            Evaluator = new(mode);
            var definition = new RegisteredProjectionDefinition(App, ObjectId, projectionVersion,
                RegisteredApplicationObjectContract.FieldBasedContractProfileId,
                RegisteredApplicationObjectContract.TransportSchemaJson,
                RegisteredApplicationObjectContract.TransportSchemaHash, objectHash, [], [], [], DateTime.UtcNow,
                new(RegisteredApplicationObjectContract.FieldBasedContractProfileId, [new("subject", true)], [], [], [], [],
                    new(1, 1, 4_096, 4), new(["dm"], []), null, []));
            Roots = new Roots(definition.Reference, "{\"value\":\"object-visible\"}");
            var objects = new ObjectProjectionInteractionQueryExecutor(new Collections(), new Definitions(definition), Roots);
            Service = new ApplicationReadModelService(Catalogs, new Activation(activation), StateSpaces,
                new MappingResolver(), Evaluator, validator, objects);
        }

        public InMemoryPublicApplicationCatalogProvider Catalogs { get; }
        public StateSpaces StateSpaces { get; }
        public Entities Entities { get; }
        public Evaluator Evaluator { get; }
        public Roots Roots { get; }
        public ApplicationReadModelService Service { get; }
    }

    private static string ObjectQuery(string objectHash, int projectionVersion) => JsonSerializer.Serialize(new
    {
        id = Fixture.TargetQueryId,
        category = "fixture.selection",
        name = "Selected object",
        description = "Reads exactly one selected object.",
        matches = new[] { "selected object" },
        roles = new Dictionary<string, string> { ["subject"] = "The selected route entity." },
        roleBindings = new Dictionary<string, object> { ["subject"] = new { source = "route-entity" } },
        selection = new
        {
            queryId = Fixture.SelectorQueryId,
            targetRole = "subject",
            resultPointer = "/selectedId",
            roleBindings = new Dictionary<string, string> { ["proof"] = "subject" }
        },
        executor = ApplicationQueryContract.ObjectProjectionExecutor,
        profile = RegisteredApplicationObjectContract.FieldBasedContractProfileId,
        @object = new { qualifiedId = Fixture.ObjectId, version = projectionVersion, contentFingerprint = objectHash },
        exposure = "model-visible",
        status = "active"
    });

    private static string MechanicQuery(string queryId, string mechanicId, string mechanicHash,
        string schema, string schemaHash) => JsonSerializer.Serialize(new
    {
        id = queryId,
        category = "fixture.selection",
        name = "Selection proof",
        description = "Proves the selected entity.",
        matches = new[] { "selection proof" },
        roles = new Dictionary<string, string> { ["proof"] = "The selector role." },
        roleBindings = new Dictionary<string, object> { ["proof"] = new { source = "route-entity" } },
        executor = ApplicationQueryContract.MechanicProjectionExecutor,
        projection = new { qualifiedId = mechanicId, version = 1, contentHash = mechanicHash, outputSchemaHash = schemaHash },
        outputSchema = JsonDocument.Parse(schema).RootElement.Clone(),
        exposure = "binding-only",
        status = "active"
    });

    private static string Mechanic(string id) => JsonSerializer.Serialize(new
    {
        id,
        requirements = "{\"roles\":{\"proof\":{\"components\":[]}}}",
        source = "return { data: {} };"
    });

    private static CatalogRecordDefinition Record(string kind, string id, string content) =>
        new(Fixture.App.Value, kind, id, id, id, [], [], "fixture", "active", 1, content,
            Hash(content), "fixture", "fixture.json");

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class Evaluator(ProofMode mode) : IApplicationMechanicEvaluator
    {
        public List<string> MechanicIds { get; } = [];

        public Task<ApplicationMechanicEvaluationResult> EvaluateAsync(ApplicationMechanicEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            MechanicIds.Add(request.QualifiedMechanicId);
            var second = MechanicIds.Count == 2;
            var selectedId = mode == ProofMode.Mismatch ? "entity.other" : Fixture.SelectedEntityId;
            var revision = mode == ProofMode.Stale && second ? "proof-revision-2" : "proof-revision-1";
            return Task.FromResult(new ApplicationMechanicEvaluationResult(request.QualifiedMechanicId,
                request.ContentFingerprint, new MechanicProjection { StateSpaceId = request.StateSpaceId,
                    AuthorizedSourceRevision = revision }, new MechanicRunResult
                {
                    Ok = true,
                    Output = new MechanicOutput { HasData = true, Data = JsonSerializer.Serialize(new { selectedId }) }
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
            applicationId == Fixture.App ? value : null;
    }

    private sealed class StateSpaces(StateSpaceView value) : IStateSpaceRegistry
    {
        public StateSpaceView? Get(string stateSpaceId) => stateSpaceId == Fixture.StateSpaceId ? value : null;
        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId, string? afterStateSpaceId,
            int limit) => throw new NotSupportedException();
    }

    private sealed class Entities : IEntityComponentStore
    {
        public Task<EcsEntityView?> GetEntityAsync(string stateSpaceId, string entityId,
            CancellationToken cancellationToken = default) => Task.FromResult<EcsEntityView?>(
            stateSpaceId == Fixture.StateSpaceId && entityId == Fixture.SelectedEntityId
                ? new(stateSpaceId, entityId, "Selected", 1, DateTime.UtcNow, null) : null);
        public Task<EcsEntityView> CreateEntityAsync(string stateSpaceId, string entityId, string name,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsEntityDiscoveryPage> ListEntitiesAsync(string stateSpaceId, string? afterEntityId,
            int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteEntityAsync(string stateSpaceId, string entityId, int expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView?> GetComponentAsync(string stateSpaceId, string entityId,
            string qualifiedTypeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EcsComponentView>> GetComponentsAsync(string stateSpaceId,
            IReadOnlyList<EcsComponentLocator> locators, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsComponentDiscoveryPage> ListComponentsAsync(string stateSpaceId, string entityId,
            string? afterQualifiedTypeId, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsComponentView> AddComponentAsync(EcsComponentWrite write,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView> SetComponentAsync(EcsComponentWrite write,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView> MergeComponentAsync(EcsComponentWrite write,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveComponentAsync(string stateSpaceId, string entityId, EcsComponentReference type,
            int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Definitions(RegisteredProjectionDefinition value) : IProjectionDefinitionRegistry
    {
        public RegisteredProjectionDefinition? Get(string qualifiedId, int version) =>
            qualifiedId == value.QualifiedId && version == value.Version ? value : null;
        public RegisteredProjectionDefinition Define(ProjectionDefinitionRequest definition) => throw new NotSupportedException();
        public ProjectionImpactGraph GetImpactGraph(ApplicationIdentifier owner) =>
            new(new Dictionary<string, IReadOnlyList<string>>(), new Dictionary<string, IReadOnlyList<string>>());
    }

    private sealed class Roots(ProjectionReference reference, string output) : IProjectionMaterializer
    {
        public int Calls { get; private set; }
        public Task<ProjectionMaterializationResult> MaterializeAsync(ProjectionMaterializationRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ProjectionMaterializationResult(reference, output, []));
        }
    }

    private sealed class Collections : IProjectionCollectionMaterializer
    {
        public Task<ProjectionCollectionMaterializationResult> MaterializeAsync(
            ProjectionCollectionMaterializationRequest request, CancellationToken cancellationToken = default) =>
            throw new Xunit.Sdk.XunitException("The selected scalar object must not read a collection.");
    }

    private sealed class Authorizer : IPrivateOperatorRequestAuthorizer
    {
        public PrivateOperatorAuthorizationDecision Authorize(PrivateOperatorCapability capability) =>
            new PrivateOperatorAuthorizationPolicy().Evaluate(new(PrivateOperatorPrincipal.Create("test", "selection"),
                capability, PrivateOperatorAuthorizationPolicy.PrivateHostScope, "selection-test"));
    }

    private sealed class Log : IOperationLog
    {
        public Task<Operation?> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Operation?>(null);
        public Task<Operation> RecordAsync(string tool, string summary, bool success, string intent = "", string subject = "",
            IEnumerable<string>? proceduresCited = null, string error = "", bool consumesReadEvidence = false,
            CancellationToken cancellationToken = default, string mechanicId = "", int? mechanicVersion = null,
            long? seed = null, string projectionJson = "", string guardEvidenceJson = "", string id = "") =>
            Task.FromResult(new Operation { Id = string.IsNullOrEmpty(id) ? Operation.NewId() : id,
                Timestamp = DateTime.UtcNow, Tool = tool, Summary = summary, Success = success, Subject = subject, Error = error });
        public Task<IReadOnlyList<Operation>> RecentAsync(int limit = 20, bool failuresOnly = false, string? tool = null,
            string? subject = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Operation>>([]);
        public Task<IReadOnlyList<string>> RecentlyReadProceduresAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
