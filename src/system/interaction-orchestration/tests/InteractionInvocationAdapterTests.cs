using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.Tests;

namespace DantesRoleplay.Interactions.Tests;

public sealed class InteractionInvocationAdapterTests
{
    [Fact]
    public async Task Rejected_progress_reuse_does_not_close_the_first_invocations_outlet()
    {
        using var fixture = await InvocationFixture.CreateAsync();
        var authority = new GatedMissingStandingAuthority();
        var adapter = fixture.CreateServiceAdapter(authority, authority);
        var progress = new ApplicationServiceProgressChannel();
        var first = adapter.InvokeAsync(fixture.ServiceRequest("command.service.first", progress));
        await authority.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            var second = await adapter.InvokeAsync(fixture.ServiceRequest("command.service.second", progress));
            Assert.Equal("SERVICE_PROGRESS_ALREADY_BOUND", second.Code);
            Assert.False(progress.Reader.Completion.IsCompleted);
        }
        finally { authority.Release(); }
        Assert.Equal(InteractionInvocationResultTag.Unavailable, (await first).Tag);
        await progress.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Service_is_unavailable_when_current_standing_authority_cannot_be_resolved()
    {
        using var fixture = await InvocationFixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        var authority = new MissingStandingAuthority();
        var progress = new ApplicationServiceProgressChannel();
        var adapter = fixture.CreateServiceAdapter(authority, authority);
        var result = await adapter.InvokeAsync(fixture.ServiceRequest("command.service.unavailable", progress));
        Assert.Equal(InteractionInvocationResultTag.Unavailable, result.Tag);
        Assert.Null(result.DataJson);
        Assert.Null(result.CompletionEvidenceReference);
        Assert.Null(result.TaskHandle);
        Assert.Equal(0, authority.PolicyCalls);
        Assert.False(progress.Reader.TryRead(out _));
        await progress.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task Noncanonical_schema_order_preserves_owner_hash_for_cold_and_repeated_reads()
    {
        using var fixture = await InvocationFixture.CreateAsync();
        var cold = await fixture.ReadAdapter.ReadAsync(fixture.ReadRequest("command.schema.cold"));
        var repeated = await fixture.ReadAdapter.ReadAsync(fixture.ReadRequest("command.schema.repeat"));

        Assert.NotEqual(fixture.OriginalOutputSchema, fixture.QueryContract.OutputSchemaJson);
        var compiled = new BoundedJsonSchemaValidator().Compile(fixture.OriginalOutputSchema);
        Assert.Equal(fixture.QueryContract.OutputSchemaHash, compiled.SchemaHash);
        Assert.NotEqual(compiled.SchemaHash,
            new BoundedJsonSchemaValidator().Compile(fixture.QueryContract.OutputSchemaJson).SchemaHash);
        Assert.Equal(InteractionInvocationResultTag.Completed, cold.Tag);
        Assert.Equal(InteractionInvocationResultTag.Completed, repeated.Tag);
        Assert.Equal(cold.DataJson, repeated.DataJson);
        Assert.Equal(cold.ReadEvidence, repeated.ReadEvidence);
        Assert.NotNull(cold.ReadEvidence);
        Assert.Equal(compiled.SchemaHash, cold.ReadEvidence!.OutputSchemaHash);
        Assert.Equal("{\"entityId\":\"subject\",\"value\":1}", cold.DataJson);
    }

    [Fact]
    public async Task Real_read_and_root_atomic_action_return_tracked_evidence_and_one_authoritative_commit()
    {
        using var fixture = await InvocationFixture.CreateAsync();

        var before = await fixture.ReadAdapter.ReadAsync(fixture.ReadRequest("command.read.before"));

        Assert.True(before.Tag == InteractionInvocationResultTag.Completed,
            $"{before.Code}: {before.SafeMessage}");
        Assert.Equal("{\"entityId\":\"subject\",\"value\":1}", before.DataJson);
        Assert.NotNull(before.ReadEvidence);
        Assert.Equal(fixture.ActivationFingerprint, before.ReadEvidence!.StateSpaceFingerprint);
        Assert.Equal(fixture.ResolutionFingerprint, before.ReadEvidence.ResolutionFingerprint);
        Assert.Matches("^[0-9A-F]{64}$", before.ReadEvidence.SourceRevisionFingerprint);

        var action = fixture.ActionRequest("command.action.set", "{\"value\":2}");
        var committed = await fixture.ActionAdapter.ExecuteAsync(action);

        Assert.Equal(InteractionInvocationResultTag.Committed, committed.Tag);
        Assert.NotNull(committed.Receipt);
        Assert.True(committed.Receipt!.EffectDetailsAvailable);
        var receipt = Assert.Single(committed.Receipt.Effects);
        Assert.Equal("component.set", receipt.Type);
        Assert.Equal("subject", receipt.EntityId);
        Assert.Equal(fixture.CounterType.QualifiedId, receipt.QualifiedTypeId);
        Assert.Equal(1, receipt.BeforeRevision);
        Assert.Equal(2, receipt.AfterRevision);
        Assert.Equal("{\"value\":1}", receipt.BeforeJson);
        Assert.Equal("{\"value\":2}", receipt.AfterJson);

        var audit = await fixture.Operations.GetAsync(committed.Receipt.OperationId);
        Assert.NotNull(audit);
        Assert.True(audit!.Success);
        Assert.Equal(ApplicationEcsExecutionIdentity.AuditTool, audit.Tool);
        Assert.Equal("interaction-step:" + committed.Receipt.RequestFingerprint, audit.Subject);
        Assert.Equal(fixture.ActionRecord.QualifiedId, audit.MechanicId);
        Assert.Equal(fixture.ActionRecord.Version, audit.MechanicVersion);
        Assert.False(string.IsNullOrWhiteSpace(audit.ProjectionJson));

        var stored = await fixture.Entities.GetComponentAsync(
            InvocationFixture.SpaceId, "subject", fixture.CounterType.QualifiedId);
        Assert.NotNull(stored);
        Assert.Equal(2, stored!.Revision);
        Assert.Equal("{\"value\":2}", stored.ValueJson);
        var operationCount = (await fixture.Operations.RecentAsync(100)).Count;

        var replay = await fixture.ActionAdapter.ExecuteAsync(
            fixture.ActionRequest("command.action.set", "{ \"value\" : 2 }"));

        Assert.Equal(InteractionInvocationResultTag.Committed, replay.Tag);
        Assert.Equal(committed.Receipt.OperationId, replay.Receipt!.OperationId);
        Assert.Equal(committed.Receipt.RequestFingerprint, replay.Receipt.RequestFingerprint);
        Assert.False(replay.Receipt.EffectDetailsAvailable);
        Assert.Equal(operationCount, (await fixture.Operations.RecentAsync(100)).Count);
        stored = await fixture.Entities.GetComponentAsync(
            InvocationFixture.SpaceId, "subject", fixture.CounterType.QualifiedId);
        Assert.Equal(2, stored!.Revision);

        var conflict = await fixture.ActionAdapter.ExecuteAsync(
            fixture.ActionRequest("command.action.set", "{\"value\":3}"));

        Assert.Equal(InteractionInvocationResultTag.Failed, conflict.Tag);
        Assert.Equal("OPERATION_ID_CONFLICT", conflict.Code);
        Assert.Equal(operationCount, (await fixture.Operations.RecentAsync(100)).Count);
        stored = await fixture.Entities.GetComponentAsync(
            InvocationFixture.SpaceId, "subject", fixture.CounterType.QualifiedId);
        Assert.Equal(2, stored!.Revision);
        Assert.Equal("{\"value\":2}", stored.ValueJson);

        var after = await fixture.ReadAdapter.ReadAsync(fixture.ReadRequest("command.read.after"));
        Assert.Equal(InteractionInvocationResultTag.Completed, after.Tag);
        Assert.Equal("{\"entityId\":\"subject\",\"value\":2}", after.DataJson);
        Assert.NotNull(after.ReadEvidence);
        Assert.NotEqual(before.ReadEvidence.SourceRevisionFingerprint,
            after.ReadEvidence!.SourceRevisionFingerprint);
        Assert.NotEqual(before.ReadEvidence.ResultFingerprint, after.ReadEvidence.ResultFingerprint);
    }

    [Fact]
    public async Task Private_host_grant_and_scope_denials_do_not_mutate_sqlite()
    {
        using var fixture = await InvocationFixture.CreateAsync();
        var initial = await fixture.SnapshotAsync();

        var wrongGrant = await fixture.ActionAdapter.ExecuteAsync(fixture.ActionRequest(
            "command.denied.grant", "{\"value\":2}", grant: "unrelated.grant"));
        var missingScope = await fixture.ActionAdapter.ExecuteAsync(fixture.ActionRequest(
            "command.denied.scope", "{\"value\":2}", stateSpaceId: "missing-space"));
        var staleScope = await fixture.ActionAdapter.ExecuteAsync(fixture.ActionRequest(
            "command.denied.stale", "{\"value\":2}",
            stateRevision: "state-binding.999." + fixture.ActivationFingerprint.ToLowerInvariant()));

        Assert.Equal(InteractionInvocationResultTag.Failed, wrongGrant.Tag);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", wrongGrant.Code);
        Assert.Equal(InteractionInvocationResultTag.Failed, missingScope.Tag);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", missingScope.Code);
        Assert.Equal(InteractionInvocationResultTag.Failed, staleScope.Tag);
        Assert.Equal("INVOCATION_SCOPE_STALE", staleScope.Code);
        Assert.Equal(initial, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task Read_only_atomic_child_and_workflow_actions_are_unavailable_without_implicit_commit()
    {
        using var fixture = await InvocationFixture.CreateAsync();
        var initial = await fixture.SnapshotAsync();

        var readOnly = await fixture.ActionAdapter.ExecuteAsync(fixture.ActionRequest(
            "command.profile.read", "{\"value\":2}", profile: InteractionExecutionProfile.ReadOnly));
        var atomicChild = await fixture.ActionAdapter.ExecuteAsync(fixture.ActionRequest(
            "command.profile.child", "{\"value\":2}", parentCommandId: "command.parent"));
        var workflow = await fixture.ActionAdapter.ExecuteAsync(fixture.ActionRequest(
            "command.profile.workflow", "{\"value\":2}", profile: InteractionExecutionProfile.Workflow));

        Assert.Equal(InteractionInvocationResultTag.Unavailable, readOnly.Tag);
        Assert.Equal("ACTION_PROFILE_UNSUPPORTED", readOnly.Code);
        Assert.Equal(InteractionInvocationResultTag.Unavailable, atomicChild.Tag);
        Assert.Equal("ATOMIC_CHILD_UNSUPPORTED", atomicChild.Code);
        Assert.Equal(InteractionInvocationResultTag.Unavailable, workflow.Tag);
        Assert.Equal("ACTION_PROFILE_UNSUPPORTED", workflow.Code);
        Assert.Equal(initial, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task Post_commit_runner_failure_reconciles_the_real_authoritative_audit()
    {
        using var fixture = await InvocationFixture.CreateAsync();
        var adapter = fixture.CreateActionAdapter(new ThrowAfterCommitRunner(fixture.Runner), fixture.Operations);

        var result = await adapter.ExecuteAsync(
            fixture.ActionRequest("command.reconcile.committed", "{\"value\":2}"));

        Assert.True(result.Tag == InteractionInvocationResultTag.Committed,
            $"{result.Code}: {result.SafeMessage}");
        Assert.NotNull(result.Receipt);
        Assert.Empty(result.Receipt!.Effects);
        Assert.False(result.Receipt.EffectDetailsAvailable);
        var audit = await fixture.Operations.GetAsync(result.Receipt.OperationId);
        Assert.NotNull(audit);
        Assert.True(audit!.Success);
        Assert.Equal(ApplicationEcsExecutionIdentity.AuditTool, audit.Tool);
        Assert.Equal("interaction-step:" + result.Receipt.RequestFingerprint, audit.Subject);
        var stored = await fixture.Entities.GetComponentAsync(
            InvocationFixture.SpaceId, "subject", fixture.CounterType.QualifiedId);
        Assert.Equal(2, stored!.Revision);
        Assert.Equal("{\"value\":2}", stored.ValueJson);
    }

    [Fact]
    public async Task Post_dispatch_audit_failure_returns_stable_unknown_with_recovery_identity()
    {
        using var fixture = await InvocationFixture.CreateAsync();
        var adapter = fixture.CreateActionAdapter(fixture.Runner,
            new UnavailableReadOperationLog(fixture.Operations));

        var result = await adapter.ExecuteAsync(
            fixture.ActionRequest("command.reconcile.unknown", "{\"value\":2}"));

        Assert.True(result.Tag == InteractionInvocationResultTag.Unavailable,
            $"{result.Code}: {result.SafeMessage}");
        Assert.Equal("ACTION_OUTCOME_UNKNOWN", result.Code);
        Assert.NotNull(result.RecoveryIdentity);
        Assert.Equal(InteractionInvocationIdentity.OperationId(
            fixture.ActionRequest("command.reconcile.unknown", "{\"value\":2}").Host),
            result.RecoveryIdentity!.OperationId);
        var audit = await fixture.Operations.GetAsync(result.RecoveryIdentity.OperationId);
        Assert.NotNull(audit);
        Assert.True(audit!.Success);
        Assert.Equal(result.RecoveryIdentity.AuditSubject, audit.Subject);
        var stored = await fixture.Entities.GetComponentAsync(
            InvocationFixture.SpaceId, "subject", fixture.CounterType.QualifiedId);
        Assert.Equal(2, stored!.Revision);
        Assert.Equal("{\"value\":2}", stored.ValueJson);
    }

    internal sealed class InvocationFixture : IDisposable
    {
        public const string SpaceId = "foundation-space";
        private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("foundation-fixture");
        private const string OutputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"entityId\",\"value\"],\"properties\":{\"entityId\":{\"type\":\"string\"},\"value\":{\"type\":\"integer\"}}}";
        private const string CounterSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}";

        private readonly SqliteFixture database;
        private readonly DantesRoleplayDbContext db;
        private readonly ApplicationRevision revision;
        private readonly TrustedPrincipalContext principal;
        private readonly InteractionQueryContractReference queryContract;

        private InvocationFixture(SqliteFixture database, DantesRoleplayDbContext db,
            ApplicationRevision revision, TrustedPrincipalContext principal,
            SqliteStateSpaceRegistry stateSpaces, SqliteEntityComponentStore entities,
            OperationLog operations, RegisteredComponentTypeVersion counterType,
            CatalogRecordDefinition actionRecord, StandingGrantDefinitionTarget queryTarget,
            string activationFingerprint,
            string resolutionFingerprint, InteractionQueryContractReference queryContract,
            IApplicationActionRunner runner, IApplicationReadModelService readModels,
            IApplicationReadModelInvocationAdapter readAdapter,
            IApplicationActionInvocationAdapter actionAdapter,
            IPublicApplicationCatalogProvider catalogs, BoundedJsonSchemaValidator schemas,
            JintMechanicEngine engine, CatalogRecordDefinition serviceRecord,
            ApplicationReadOnlyServiceDefinition serviceDefinition)
        {
            this.database = database;
            this.db = db;
            this.revision = revision;
            this.principal = principal;
            StateSpaces = stateSpaces;
            Entities = entities;
            Operations = operations;
            CounterType = counterType;
            ActionRecord = actionRecord;
            QueryTarget = queryTarget;
            ActivationFingerprint = activationFingerprint;
            ResolutionFingerprint = resolutionFingerprint;
            this.queryContract = queryContract;
            Runner = runner;
            ReadModels = readModels;
            ReadAdapter = readAdapter;
            ActionAdapter = actionAdapter;
            Catalogs = catalogs;
            Schemas = schemas;
            Engine = engine;
            ServiceRecord = serviceRecord;
            ServiceDefinition = serviceDefinition;
        }

        public SqliteStateSpaceRegistry StateSpaces { get; }
        public SqliteEntityComponentStore Entities { get; }
        public OperationLog Operations { get; }
        public RegisteredComponentTypeVersion CounterType { get; }
        public CatalogRecordDefinition ActionRecord { get; }
        public StandingGrantDefinitionTarget QueryTarget { get; }
        public string ActivationFingerprint { get; }
        public string ResolutionFingerprint { get; }
        public IApplicationActionRunner Runner { get; }
        public IApplicationReadModelService ReadModels { get; }
        public IApplicationReadModelInvocationAdapter ReadAdapter { get; }
        public IApplicationActionInvocationAdapter ActionAdapter { get; }
        public InteractionQueryContractReference QueryContract => queryContract;
        public string OriginalOutputSchema => OutputSchema;
        public IPublicApplicationCatalogProvider Catalogs { get; }
        public BoundedJsonSchemaValidator Schemas { get; }
        public JintMechanicEngine Engine { get; }
        public CatalogRecordDefinition ServiceRecord { get; }
        public ApplicationReadOnlyServiceDefinition ServiceDefinition { get; }

        public static async Task<InvocationFixture> CreateAsync(string? serviceSource = null)
        {
            var database = new SqliteFixture();
            var db = database.CreateContext();
            var applications = new SqliteApplicationRegistry(db);
            var revision = applications.Register(new(App, "Foundation fixture", "Invocation acceptance.", []));
            var activationFingerprint = Hash("foundation-activation");
            var resolutionFingerprint = Hash("foundation-resolution");
            var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
            stateSpaces.Create(new(SpaceId, revision, activationFingerprint, resolutionFingerprint));
            var schemas = new BoundedJsonSchemaValidator();
            var types = new SqliteComponentTypeRegistry(db, schemas);
            var counterType = types.Define(new(App, App.Value + ".counter", CounterSchema));
            var entities = new SqliteEntityComponentStore(db, types, schemas);
            await entities.CreateEntityAsync(SpaceId, "subject", "Subject");
            await entities.AddComponentAsync(new(SpaceId, "subject", Reference(counterType), "{\"value\":1}", 0));
            var edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);
            var operations = new OperationLog(db);

            var output = schemas.Compile(OutputSchema);
            Assert.True(output.IsAccepted);
            var readMechanic = Record("mechanic", App.Value + ".mechanic.read-counter",
                JsonSerializer.Serialize(new
                {
                    id = "mechanic.read-counter",
                    requirements = "{\"roles\":{\"subject\":{\"components\":[\"counter\"]}}}",
                    source = "var c=JSON.parse(ctx.roles.subject.components.counter);return {data:{entityId:ctx.roles.subject.id,value:c.value}};"
                }));
            var actionRecord = Record("mechanic", App.Value + ".mechanic.set-counter",
                JsonSerializer.Serialize(new
                {
                    id = "mechanic.set-counter",
                    requirements = "{\"roles\":{\"subject\":{\"components\":[\"counter\"]}},\"inputSchema\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}}",
                    source = "var c=JSON.parse(ctx.roles.subject.components.counter);return {effects:[{type:'component.set',entityId:ctx.roles.subject.id,definitionId:'counter',data:JSON.stringify({value:ctx.input.value})}],data:{before:c.value,after:ctx.input.value}};"
                }));
            var queryContent = JsonSerializer.Serialize(new
            {
                id = App.Value + ".query.counter",
                category = "fixture.counter",
                name = "Counter",
                description = "Reads the committed counter.",
                matches = new[] { "read counter" },
                roles = new Dictionary<string, string> { ["subject"] = "Counter subject." },
                executor = ApplicationQueryContract.MechanicProjectionExecutor,
                projection = new
                {
                    qualifiedId = readMechanic.QualifiedId,
                    version = readMechanic.Version,
                    contentHash = readMechanic.ContentFingerprint,
                    outputSchemaHash = output.SchemaHash
                },
                outputSchema = JsonSerializer.Deserialize<JsonElement>(OutputSchema),
                exposure = "model-visible",
                status = "active"
            });
            var queryRecord = Record("query", App.Value + ".query.counter", queryContent);
            var parsedQuery = ApplicationQueryContract.Parse(queryContent, App);
            var queryContract = new InteractionQueryContractReference(parsedQuery.Executor,
                parsedQuery.ProjectionQualifiedId, parsedQuery.ProjectionVersion,
                parsedQuery.ProjectionContentHash, parsedQuery.OutputSchemaHash,
                parsedQuery.OutputSchemaJson, parsedQuery.Exposure, parsedQuery.Roles.Keys);
            var inputSchema = schemas.Compile("""{"type":"object","additionalProperties":false,"properties":{}}""");
            var serviceDefinition = new ApplicationReadOnlyServiceDefinition(inputSchema.SchemaHash,
                inputSchema.NormalizedSchema, output.SchemaHash, output.NormalizedSchema,
                [new("counter", parsedQuery.Id, queryContract,
                    new Dictionary<string, string> { ["subject"] = "subject" }, schemas, output.NormalizedSchema)], schemas);
            var serviceRecord = Record("mechanic", App.Value + ".mechanic.counter-service", JsonSerializer.Serialize(new
            {
                id = "mechanic.counter-service",
                requirements = "{\"service\":" + serviceDefinition.ToJson() + "}",
                source = serviceSource ?? "var r=ctx.services.read('counter',{});ctx.services.progress({phase:'read'});return {data:JSON.parse(r.dataJson)};"
            }));
            var manifest = CatalogNavigationManifest.Create(App, Hash("foundation-catalog"), "catalog-lexical-v1",
                [new(App.Value, "Foundation fixture", "Invocation acceptance.")],
                [
                    new(App.Value, "", "Foundation fixture", "Invocation acceptance.", CatalogDescriptionStatus.Authored),
                    new(App.Value, "mechanics", "Mechanics", "", CatalogDescriptionStatus.Missing),
                    new(App.Value, "queries", "Queries", "", CatalogDescriptionStatus.Missing)
                ], [readMechanic, actionRecord, queryRecord, serviceRecord]);
            var catalogs = new InMemoryPublicApplicationCatalogProvider(
                new Dictionary<ApplicationIdentifier, ICatalogNavigator>
                {
                    [App] = new InMemoryCatalogNavigator(manifest,
                        new CatalogCursorCodec(Enumerable.Repeat((byte)0x31, 32).ToArray()))
                });
            var active = new ActiveApplicationManifest(App, 1, revision.Revision, revision.Fingerprint,
                Hash("preview"), Hash("scan"), Hash("candidate"), Hash("dependencies"), activationFingerprint,
                "coverage-v1", true, [], [], "operation.activation", DateTime.UtcNow)
            {
                ResolutionFingerprint = resolutionFingerprint
            };
            var activation = new StaticActivation(active);
            var mapping = new ApplicationMechanicProjectionMappingResolver(catalogs, stateSpaces, types, edges);
            var projection = new ApplicationMechanicProjectionResolver(db, stateSpaces);
            var engine = new JintMechanicEngine();
            var evaluator = new ApplicationMechanicEvaluator(catalogs, projection, engine);
            var readService = new ApplicationReadModelService(catalogs, activation, stateSpaces, mapping, evaluator, schemas);
            var effects = new ApplicationEcsEffectApplier(db, entities, stateSpaces, operations, edges);
            var runner = new ApplicationActionRunner(catalogs, activation, stateSpaces, types, entities, edges,
                mapping, evaluator, effects, operations);
            var authorization = new PrivateHostInteractionAuthorizationPolicy(stateSpaces);
            return new(database, db, revision,
                PrivateOperatorPrincipal.Create("local-loopback", "foundation-invocation-acceptance"),
                stateSpaces, entities, operations, counterType, actionRecord,
                new(queryRecord.QualifiedId, "query", App, App.Value + ".query",
                    "catalog-owner.query.counter", queryRecord.Version, queryRecord.ContentFingerprint),
                activationFingerprint, resolutionFingerprint, queryContract,
                runner, readService,
                new ApplicationReadModelInvocationAdapter(authorization, stateSpaces, readService),
                new ApplicationActionInvocationAdapter(authorization, stateSpaces, runner, operations),
                catalogs, schemas, engine, serviceRecord, serviceDefinition);
        }

        public IApplicationActionInvocationAdapter CreateActionAdapter(
            IApplicationActionRunner actions,
            IOperationLog operationLog) =>
            new ApplicationActionInvocationAdapter(
                new PrivateHostInteractionAuthorizationPolicy(StateSpaces), StateSpaces, actions, operationLog);

        public IApplicationReadOnlyServiceInvocationAdapter CreateServiceAdapter(
            IStandingGrantTargetResolver targets, IStandingGrantPolicy policy) =>
            new ApplicationReadOnlyServiceInvocationAdapter(Catalogs,
                new ApplicationReadOnlyServiceDefinitionReader(Schemas),
                new StandingGrantApplicationReadModelInvocationAdapter(policy, targets, StateSpaces, ReadModels),
                Schemas, Engine, StateSpaces, targets, policy);

        public ApplicationReadModelInvocationRequest ReadRequest(
            string commandId,
            string grantReference = "interaction.private-host.read",
            int maximumOperations = 8) => new(
            Host(commandId, InteractionExecutionProfile.ReadOnly, grantReference,
                maximumOperations: maximumOperations),
            App.Value + ".query.counter", queryContract,
            new Dictionary<string, string> { ["subject"] = "subject" });

        public ApplicationReadOnlyServiceInvocationRequest ServiceRequest(string commandId,
            ApplicationServiceProgressChannel? progress = null, ExecutionLimits? limits = null) => new(
            Host(commandId, InteractionExecutionProfile.ReadOnly, "interaction.private-host.read"),
            new SystemTaskSelectedDefinition(ServiceRecord.QualifiedId, ServiceRecord.Version, ServiceRecord.ContentFingerprint),
            ServiceDefinition, new Dictionary<string, string> { ["subject"] = "subject" }, "{}",
            limits ?? ExecutionLimits.ReadModel, progress);

        public ApplicationActionInvocationRequest ActionRequest(
            string commandId,
            string input,
            InteractionExecutionProfile profile = InteractionExecutionProfile.Atomic,
            string grant = "interaction.private-host.execute",
            string stateSpaceId = SpaceId,
            string? stateRevision = null,
            string? parentCommandId = null) => new(
            Host(commandId, profile, grant, stateSpaceId, stateRevision, parentCommandId),
            ActionRecord.QualifiedId, ActionRecord.Version, ActionRecord.ContentFingerprint,
            new Dictionary<string, string> { ["subject"] = "subject" }, input);

        public async Task<(int Revision, string ValueJson, int OperationCount)> SnapshotAsync()
        {
            var component = await Entities.GetComponentAsync(SpaceId, "subject", CounterType.QualifiedId);
            return (component!.Revision, component.ValueJson, (await Operations.RecentAsync(100)).Count);
        }

        private InteractionInvocationHost Host(
            string commandId,
            InteractionExecutionProfile profile,
            string grant,
            string stateSpaceId = SpaceId,
            string? stateRevision = null,
            string? parentCommandId = null,
            int maximumOperations = 8)
        {
            var state = StateSpaces.Get(SpaceId)!;
            return new(principal, revision, stateSpaceId, grant, commandId,
                stateRevision ?? InteractionStateRevision.From(state), profile,
                new InteractionInvocationBudget(maximumOperations, DateTime.UtcNow.AddMinutes(2)), parentCommandId);
        }

        public void Dispose()
        {
            db.Dispose();
            database.Dispose();
        }

        private static CatalogRecordDefinition Record(string kind, string qualifiedId, string content) =>
            new(App.Value, kind, qualifiedId, qualifiedId, "Invocation fixture.", [], [],
                kind == "query" ? "queries" : "mechanics", "active", 1, content, Hash(content),
                "fixture", qualifiedId + ".json");

        private static EcsComponentReference Reference(RegisteredComponentTypeVersion type) =>
            new(type.QualifiedId, type.Version, type.SchemaHash);

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        private sealed class StaticActivation(ActiveApplicationManifest value) : IApplicationActivationReader
        {
            public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
                applicationId == value.ApplicationId ? value : null;
        }
    }

    // Missing dependencies prove only fail-closed behavior, never standing-grant availability.
    private class MissingStandingAuthority : IStandingGrantTargetResolver, IStandingGrantPolicy
    {
        public int PolicyCalls { get; private set; }
        public virtual Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Unavailable,
                "STANDING_AUTHORITY_UNAVAILABLE", null));

        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host,
            ApplicationCandidateSnapshot candidate, StandingGrantDefinitionReference selection,
            CancellationToken cancellationToken = default) => ResolveAsync(host, selection, cancellationToken);

        public Task<StandingGrantDecision> EvaluateAsync(InteractionInvocationHost host,
            StandingGrantRequirement requirement, CancellationToken cancellationToken = default)
        {
            PolicyCalls++;
            throw new InvalidOperationException("Missing targets must not reach policy evaluation.");
        }
    }

    private sealed class GatedMissingStandingAuthority : MissingStandingAuthority
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<StandingGrantTargetResolution> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return _result.Task;
        }

        public void Release() => _result.TrySetResult(new(StandingGrantTargetResolutionStatus.Unavailable,
            "STANDING_AUTHORITY_UNAVAILABLE", null));
    }

    private sealed class ThrowAfterCommitRunner(IApplicationActionRunner inner) : IApplicationActionRunner
    {
        public async Task<ApplicationActionExecutionResult> RunAsync(
            ApplicationActionExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.RunAsync(request, cancellationToken);
            if (result.Successful)
                throw new InvalidOperationException("Injected transport failure after the real action runner returned.");
            return result;
        }
    }

    private sealed class UnavailableReadOperationLog(IOperationLog inner) : IOperationLog
    {
        public Task<Operation?> GetAsync(string id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected audit read failure.");

        public Task<Operation> RecordAsync(string tool, string summary, bool success, string intent = "",
            string subject = "", IEnumerable<string>? proceduresCited = null, string error = "",
            bool consumesReadEvidence = false, CancellationToken cancellationToken = default,
            string mechanicId = "", int? mechanicVersion = null, long? seed = null,
            string projectionJson = "", string guardEvidenceJson = "", string id = "") =>
            inner.RecordAsync(tool, summary, success, intent, subject, proceduresCited, error,
                consumesReadEvidence, cancellationToken, mechanicId, mechanicVersion, seed,
                projectionJson, guardEvidenceJson, id);

        public Task<IReadOnlyList<Operation>> RecentAsync(int limit = 20, bool failuresOnly = false,
            string? tool = null, string? subject = null, CancellationToken cancellationToken = default) =>
            inner.RecentAsync(limit, failuresOnly, tool, subject, cancellationToken);

        public Task<IReadOnlyList<string>> RecentlyReadProceduresAsync(
            CancellationToken cancellationToken = default) =>
            inner.RecentlyReadProceduresAsync(cancellationToken);
    }
}
