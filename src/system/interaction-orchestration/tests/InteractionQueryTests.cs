using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Ecs;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Interactions.Tests;

public sealed class InteractionQueryTests
{
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("sample-app");
    private const string Schema = "{\"type\":\"object\",\"properties\":{\"entityId\":{\"type\":\"string\"},\"score\":{\"type\":\"integer\"}}}";
    private static readonly string SchemaHash = Hash("schema");
    private static readonly string ProjectionHash = Hash("projection");

    [Fact]
    public void Query_contract_is_strict_application_owned_and_has_one_complete_exposure_boundary()
    {
        var parsed = ApplicationQueryContract.Parse(QueryJson("model-visible"), App);

        Assert.Equal("sample-app.query.find-target", parsed.Id);
        Assert.Equal(ApplicationQueryExposure.ModelVisible, parsed.Exposure);
        Assert.Equal(["subject"], parsed.Roles.Keys);
        Assert.Throws<ArgumentException>(() => ApplicationQueryContract.Parse(
            QueryJson("model-visible").Replace("\"status\":\"active\"",
                "\"unknown\":true,\"status\":\"active\""), App));
        Assert.Throws<ArgumentException>(() => ApplicationQueryContract.Parse(
            QueryJson("field-redaction"), App));
        Assert.Throws<ArgumentException>(() => ApplicationQueryContract.Parse(
            QueryJson("binding-only").Replace("sample-app.query.find-target", "other-app.query.find-target"), App));
    }

    [Fact]
    public void Verifier_accepts_exact_query_then_string_role_binding_and_rejects_stale_projection()
    {
        var applications = new InMemoryApplicationRegistry();
        var revision = applications.Register(new(App, "Sample", "Query fixture.", []));
        var activationFingerprint = Hash("activation");
        var queryContent = QueryJson("binding-only");
        var query = Record("query", "sample-app.query.find-target", "queries/world", queryContent);
        var actionContent = JsonSerializer.Serialize(new
        {
            id = "mechanic.use-target",
            requirements = "{\"roles\":{\"target\":{\"components\":[]}}}",
            source = "return { effects: [] };"
        });
        var action = Record("mechanic", "sample-app.mechanic.use-target", "mechanics/world", actionContent);
        var manifest = CatalogNavigationManifest.Create(App, Hash("catalog"), "catalog-lexical-v1",
            [new(App.Value, "Sample", "Query fixture.")],
            [
                new(App.Value, "", "Sample", "Query fixture.", CatalogDescriptionStatus.Authored),
                new(App.Value, "queries", "Queries", "", CatalogDescriptionStatus.Missing),
                new(App.Value, "queries/world", "World", "", CatalogDescriptionStatus.Missing),
                new(App.Value, "mechanics", "Mechanics", "", CatalogDescriptionStatus.Missing),
                new(App.Value, "mechanics/world", "World", "", CatalogDescriptionStatus.Missing)
            ], [query, action]);
        var snapshot = new ActiveCatalogFeatureSnapshot(manifest,
            manifest.Records.Select(value => new ActiveCatalogFeatureDocument(value, SourceTrust.Trusted)).ToArray());
        var activation = new ActiveApplicationManifest(App, 1, revision.Revision, revision.Fingerprint,
            Hash("preview"), Hash("scan"), Hash("candidate"), Hash("dependencies"), activationFingerprint,
            "coverage-v1", true, [], [], "operation.activation", DateTime.UtcNow);
        var planRequest = new InteractionAuthorizationRequest(Principal(), App, "space.1",
            InteractionCapability.Plan, "plan.query");
        var host = new InteractionHostContext(planRequest.Principal, revision, "space.1", "session.1",
            "state.1", activationFingerprint, InteractionRoleProfile.Inner, new(2, 65_536, 65_536),
            InteractionAuthorizationDecision.Allow(planRequest, "plan.evidence"));
        var envelope = AuthorizedInteractionEnvelope.Create(InteractionIntent.Parse(JsonSerializer.Serialize(new
        {
            idempotencyKey = "plan.query", intentText = "Find and use a target.", maximumPlanSteps = 2
        })), host);
        var projection = Projection();
        var verifier = new InteractionProposalVerifier(applications, new Activation(activation),
            new Snapshots(snapshot), new Projections(projection));
        var draft = new InteractionPlannerProposalCommand([
            new("query.1", InteractionPlanStepKind.Query, query.QualifiedId, 1, query.ContentFingerprint,
                [], new Dictionary<string, string> { ["subject"] = "orban" }, "{}"),
            new("action.1", InteractionPlanStepKind.Action, action.QualifiedId, 1, action.ContentFingerprint,
                ["query.1"], new Dictionary<string, string>(), "{}",
                [new("query.1", "/entityId", toRole: "target")])
        ]);
        var inspected = manifest.Records.Select(record =>
        {
            var reference = InteractionFeatureReference.Create(App, InteractionRetrievalLane.TrustedFeature,
                manifest.Fingerprint, record);
            return new InteractionInspectedFeature(
                InteractionFeatureHit.Create(reference, record, null, null, true), record.ContentJson);
        }).ToArray();

        var accepted = verifier.Verify(new(envelope, inspected, draft));
        Assert.Equal(InteractionResolutionStatus.Resolved, accepted.Status);
        Assert.Equal(ApplicationQueryExposure.BindingOnly,
            accepted.Proposal!.Steps[0].QueryContract!.Exposure);
        Assert.Equal("target", Assert.Single(accepted.Proposal.Steps[1].ResultBindings).ToRole);

        var staleProjection = projection with { ContentHash = Hash("changed") };
        var stale = new InteractionProposalVerifier(applications, new Activation(activation),
            new Snapshots(snapshot), new Projections(staleProjection)).Verify(new(envelope, inspected, draft));
        Assert.Equal(InteractionResolutionStatus.Stale, stale.Status);
        Assert.Equal("QUERY_PROJECTION_STALE", stale.Code);
    }

    [Fact]
    public async Task Projection_executor_and_binder_are_deterministic_and_never_coerce_values()
    {
        var projection = Projection();
        var source = new ProjectionSourceRevision("orban",
            new("sample-app.stats", 1, Hash("component-schema")), 3);
        var executor = new ProjectionInteractionQueryExecutor(new Materializer(
            new(projection.Reference, "{\"entityId\":\"driver\",\"score\":16}", [source])));
        var contract = new InteractionQueryContractReference(ApplicationQueryContract.ProjectionExecutor,
            projection.QualifiedId, projection.Version, projection.ContentHash, projection.OutputSchemaHash,
            projection.OutputSchemaJson, ApplicationQueryExposure.BindingOnly, ["subject"]);

        var first = await executor.ExecuteAsync(new("space.1", App, contract,
            new Dictionary<string, string> { ["subject"] = "orban" }));
        var second = await executor.ExecuteAsync(new("space.1", App, contract,
            new Dictionary<string, string> { ["subject"] = "orban" }));
        Assert.Equal(first, second);

        var action = new InteractionPlanStep("action.1", InteractionPlanStepKind.Action,
            new(InteractionFeatureScope.Application, App, "sample-app.mechanic.use-target",
                "mechanic.use-target", 1, Hash("action")), ["query.1"],
            new Dictionary<string, string>(), "{\"payload\":{}}", "state.1",
            [
                new("query.1", "/entityId", toRole: "target"),
                new("query.1", "/score", toInputPointer: "/payload/score")
            ]);
        var bound = InteractionResultBinder.Bind(action,
            new Dictionary<string, InteractionQueryExecutionResult> { ["query.1"] = first });
        Assert.Equal("driver", bound.RoleBindings["target"]);
        Assert.Equal("{\"payload\":{\"score\":16}}", bound.InputJson);

        var nonString = first with { OutputJson = "{\"entityId\":7,\"score\":16}" };
        Assert.Equal("RESULT_BINDING_ROLE_INVALID", Assert.Throws<InteractionContractException>(() =>
            InteractionResultBinder.Bind(action,
                new Dictionary<string, InteractionQueryExecutionResult> { ["query.1"] = nonString })).Code);
    }

    [Fact]
    public async Task Coordinator_binds_query_into_action_and_equal_replay_does_no_second_read_or_action()
    {
        var applications = new InMemoryApplicationRegistry();
        var revision = applications.Register(new(App, "Sample", "Query fixture.", []));
        var activationFingerprint = Hash("coordinator-activation");
        var state = new StateSpaceView("space.1", revision, activationFingerprint, 1,
            DateTime.UtcNow, DateTime.UtcNow);
        var record = Record("query", "sample-app.query.find-target", "queries/world", QueryJson("model-visible"));
        var actionContent = JsonSerializer.Serialize(new
        {
            id = "mechanic.use-target",
            requirements = "{\"roles\":{\"target\":{\"components\":[]}}}",
            source = "return { effects: [] };"
        });
        var actionRecord = Record("mechanic", "sample-app.mechanic.use-target", "mechanics/world", actionContent);
        var manifest = CatalogNavigationManifest.Create(App, Hash("coordinator-catalog"), "catalog-lexical-v1",
            [new(App.Value, "Sample", "Query fixture.")],
            [new(App.Value, "", "Sample", "Query fixture.", CatalogDescriptionStatus.Authored),
             new(App.Value, "queries", "Queries", "", CatalogDescriptionStatus.Missing),
             new(App.Value, "queries/world", "World", "", CatalogDescriptionStatus.Missing),
             new(App.Value, "mechanics", "Mechanics", "", CatalogDescriptionStatus.Missing),
             new(App.Value, "mechanics/world", "World", "", CatalogDescriptionStatus.Missing)], [record, actionRecord]);
        var snapshot = new ActiveCatalogFeatureSnapshot(manifest,
            [new(record, SourceTrust.Trusted), new(actionRecord, SourceTrust.Trusted)]);
        var activation = new ActiveApplicationManifest(App, 1, revision.Revision, revision.Fingerprint,
            Hash("preview"), Hash("scan"), Hash("candidate"), Hash("dependencies"), activationFingerprint,
            "coverage-v1", true, [], [], "operation.activation", DateTime.UtcNow);
        var planRequest = new InteractionAuthorizationRequest(Principal(), App, state.StateSpaceId,
            InteractionCapability.Plan, "plan.coordinator");
        var host = new InteractionHostContext(planRequest.Principal, revision, state.StateSpaceId, "session.1",
            InteractionStateRevision.From(state), activationFingerprint, InteractionRoleProfile.Inner,
            new(2, 65_536, 65_536), InteractionAuthorizationDecision.Allow(planRequest, "plan.evidence"));
        var envelope = AuthorizedInteractionEnvelope.Create(InteractionIntent.Parse(JsonSerializer.Serialize(new
        {
            idempotencyKey = "plan.coordinator", intentText = "Read and use target facts.", maximumPlanSteps = 2
        })), host);
        var queryContract = new InteractionQueryContractReference("projection",
            "sample-app.projection.find-target", 1, ProjectionHash, SchemaHash, Schema,
            ApplicationQueryExposure.ModelVisible, ["subject"]);
        var contract = new InteractionContractReference(InteractionFeatureScope.Application, App,
            record.QualifiedId, "sample-app.query.find-target", 1, record.ContentFingerprint);
        var actionContract = new InteractionContractReference(InteractionFeatureScope.Application, App,
            actionRecord.QualifiedId, "mechanic.use-target", 1, actionRecord.ContentFingerprint);
        InteractionProposal Proposal(AuthorizedInteractionEnvelope value) => InteractionProposal.Create(value,
            [
                new("query.1", InteractionPlanStepKind.Query, contract, [],
                    new Dictionary<string, string> { ["subject"] = "orban" }, "{}",
                    value.Host.StateRevision, queryContract: queryContract),
                new("action.1", InteractionPlanStepKind.Action, actionContract, ["query.1"],
                    new Dictionary<string, string>(), "{\"payload\":{}}", value.Host.StateRevision,
                    [new("query.1", "/entityId", toRole: "target"),
                     new("query.1", "/score", toInputPointer: "/payload/score")])
            ]);
        var proposal = Proposal(envelope);
        var authority = new InteractionResolutionExecutionAuthority(
            "interaction-receipt." + new string('a', 32), planRequest.Principal.PrincipalId, App,
            revision.Revision, revision.Fingerprint, state.StateSpaceId, "session.1",
            InteractionStateRevision.From(state), activationFingerprint, InteractionRoleProfile.Inner.StableKey,
            null, null, "plan.evidence", "plan.coordinator", envelope.Fingerprint, "resolved", proposal.Fingerprint);
        var receipts = new QueryReceipts();
        var executor = new QueryExecutor();
        var actions = new CapturingActions();
        var coordinator = new InteractionExecutionCoordinator(new Allow(), new Authority(authority), receipts,
            applications, new Activation(activation), new Spaces(state), new Snapshots(snapshot),
            new StaticVerifier(Proposal), actions, queryExecutors: new QueryRegistry(executor));
        var command = new InteractionPlannerProposalCommand([
            new("query.1", InteractionPlanStepKind.Query, record.QualifiedId, 1,
                record.ContentFingerprint, [], new Dictionary<string, string> { ["subject"] = "orban" }, "{}"),
            new("action.1", InteractionPlanStepKind.Action, actionRecord.QualifiedId, 1,
                actionRecord.ContentFingerprint, ["query.1"], new Dictionary<string, string>(),
                "{\"payload\":{}}",
                [new("query.1", "/entityId", toRole: "target"),
                 new("query.1", "/score", toInputPointer: "/payload/score")])
        ]);
        var request = new InteractionExecutionRequest(authority.ResolutionReceiptId, proposal.Fingerprint,
            "execute.query", command);
        var authorization = new InteractionAuthorizationRequest(planRequest.Principal, App, state.StateSpaceId,
            InteractionCapability.Execute, "execute.query");

        var first = await coordinator.ExecuteAsync(request, authorization);
        var replay = await coordinator.ExecuteAsync(request, authorization);

        Assert.True(first.Successful);
        Assert.Equal(1, executor.Calls);
        Assert.Equal(1, actions.Calls);
        Assert.Equal("driver", actions.Last!.RoleEntityIds["target"]);
        Assert.Equal("{\"payload\":{\"score\":16}}", actions.Last.InputJson);
        Assert.Equal("driver", Assert.Single(first.QueryResults!).Output!.Value.GetProperty("entityId").GetString());
        Assert.Equal(InteractionReceiptWriteDisposition.Replay, replay.Receipt!.Disposition);
        Assert.Equal(1, executor.Calls);
        Assert.Equal(1, actions.Calls);
        Assert.Equal("driver", Assert.Single(replay.QueryResults!).Output!.Value.GetProperty("entityId").GetString());
    }

    private static RegisteredProjectionDefinition Projection() => new(App,
        "sample-app.projection.find-target", 1, "system-json-schema-draft-2020-12-v1", Schema,
        SchemaHash, ProjectionHash,
        [new("value", "subject", new("sample-app.stats", 1, Hash("component-schema")))], [],
        [new("value", "/entityId", "/entityId"), new("value", "/score", "/score")], DateTime.UtcNow);

    private static CatalogRecordDefinition Record(string kind, string id, string path, string content) =>
        new(App.Value, kind, id, id, id, [], [], path, "active", 1, content, Hash(content),
            "source", path + "/contract.json");

    private static string QueryJson(string exposure) => JsonSerializer.Serialize(new
    {
        id = "sample-app.query.find-target",
        category = "world.target",
        name = "Find target",
        description = "Find one safe target projection.",
        matches = new[] { "find a target" },
        roles = new Dictionary<string, string> { ["subject"] = "The subject used to resolve the target." },
        executor = "projection",
        projection = new
        {
            qualifiedId = "sample-app.projection.find-target",
            version = 1,
            contentHash = ProjectionHash,
            outputSchemaHash = SchemaHash
        },
        outputSchema = JsonSerializer.Deserialize<JsonElement>(Schema),
        exposure,
        status = "active"
    });

    private static TrustedPrincipalContext Principal() =>
        PrivateOperatorPrincipal.Create("local-loopback", "interaction-query-fixture");
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class Activation(ActiveApplicationManifest value) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            applicationId == value.ApplicationId ? value : null;
    }

    private sealed class Snapshots(ActiveCatalogFeatureSnapshot value) : IActiveCatalogFeatureSnapshotProvider
    {
        public bool TryGetSnapshot(ApplicationIdentifier applicationId, out ActiveCatalogFeatureSnapshot snapshot)
        { snapshot = value; return applicationId == App; }
    }

    private sealed class Projections(RegisteredProjectionDefinition value) : IProjectionDefinitionRegistry
    {
        public RegisteredProjectionDefinition Define(ProjectionDefinitionRequest definition) => throw new NotSupportedException();
        public RegisteredProjectionDefinition? Get(string qualifiedId, int version) =>
            qualifiedId == value.QualifiedId && version == value.Version ? value : null;
        public ProjectionImpactGraph GetImpactGraph(ApplicationIdentifier owner) => new(
            new Dictionary<string, IReadOnlyList<string>>(), new Dictionary<string, IReadOnlyList<string>>());
    }

    private sealed class Materializer(ProjectionMaterializationResult value) : IProjectionMaterializer
    {
        public Task<ProjectionMaterializationResult> MaterializeAsync(ProjectionMaterializationRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(value);
    }

    private sealed class Allow : IInteractionAuthorizationPolicy
    {
        public InteractionAuthorizationDecision Evaluate(InteractionAuthorizationRequest request) =>
            InteractionAuthorizationDecision.Allow(request, "query.evidence");
    }

    private sealed class Authority(InteractionResolutionExecutionAuthority value) : IInteractionExecutionAuthorityStore
    {
        public Task<InteractionResolutionExecutionAuthority?> GetAsync(InteractionAuthorizationRequest request,
            string resolutionReceiptId, CancellationToken cancellationToken = default) =>
            Task.FromResult<InteractionResolutionExecutionAuthority?>(resolutionReceiptId == value.ResolutionReceiptId ? value : null);
    }

    private sealed class Spaces(StateSpaceView value) : IStateSpaceRegistry
    {
        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceView? Get(string stateSpaceId) => stateSpaceId == value.StateSpaceId ? value : null;
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId, string? afterStateSpaceId, int limit) => new([value], null);
    }

    private sealed class StaticVerifier(Func<AuthorizedInteractionEnvelope, InteractionProposal> proposal)
        : IInteractionProposalVerifier
    {
        public InteractionResolutionResult Verify(InteractionProposalVerificationRequest request) =>
            InteractionResolutionResult.Resolved(proposal(request.Envelope));
    }

    private sealed class QueryExecutor : IInteractionQueryExecutor
    {
        public int Calls { get; private set; }
        public string Kind => "projection";
        public Task<InteractionQueryExecutionResult> ExecuteAsync(InteractionQueryExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new InteractionQueryExecutionResult(
                "{\"entityId\":\"driver\",\"score\":16}", SchemaHash, Hash("result"), Hash("revisions")));
        }
    }

    private sealed class QueryRegistry(IInteractionQueryExecutor executor) : IInteractionQueryExecutorRegistry
    {
        public bool TryGet(string kind, out IInteractionQueryExecutor result)
        { result = executor; return kind == executor.Kind; }
    }

    private sealed class CapturingActions : IApplicationActionRunner
    {
        public int Calls { get; private set; }
        public ApplicationActionExecutionRequest? Last { get; private set; }
        public Task<ApplicationActionExecutionResult> RunAsync(ApplicationActionExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Last = request;
            return Task.FromResult(new ApplicationActionExecutionResult(
                ApplicationActionExecutionDisposition.Succeeded, request.ExecutionIdentity.OperationId,
                request.QualifiedMechanicId, request.ContentFingerprint, request.Seed,
                "Used the exact query result.", 1, []));
        }
    }

    private sealed class QueryReceipts : IInteractionReceiptStore
    {
        private InteractionReceiptWriteResult? _stored;
        public Task<InteractionReceiptWriteResult> AppendResolutionAsync(InteractionResolutionReceiptDraft draft,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InteractionReceiptWriteResult> AppendExecutionAsync(InteractionExecutionReceiptDraft draft,
            CancellationToken cancellationToken = default)
        {
            var queries = draft.QueryResults.Select(value => new InteractionQueryResultProjection(
                value.ProposalStepId, value.QualifiedId, value.OutputSchemaHash, value.ResultFingerprint,
                value.SourceRevisionFingerprint, value.OutputJson is null ? null
                    : JsonSerializer.Deserialize<JsonElement>(value.OutputJson))).ToArray();
            var receipt = new InteractionReceiptProjection("interaction-receipt." + new string('b', 32),
                "execution", draft.Consent.PrincipalReference, draft.Consent.ApplicationId,
                draft.Consent.StateSpaceId, draft.Consent.IdempotencyKey, draft.ExecutionRequestFingerprint,
                "succeeded", "INTERACTION_EXECUTION_SUCCEEDED", draft.Consent.ProposalFingerprint,
                draft.SafeSummary, draft.Evidence, DateTime.UtcNow, draft.Consent.ResolutionReceiptId,
                draft.Steps.Select(value => new InteractionExecutionStepReceiptProjection(value.Ordinal,
                    value.ProposalStepId, "succeeded", value.OperationId)).ToArray(), QueryResults: queries);
            _stored = InteractionReceiptWriteResult.Appended(receipt);
            return Task.FromResult(_stored);
        }
        public Task<InteractionReceiptWriteResult?> FindExecutionAsync(InteractionExecutionConsentReference consent,
            string executionRequestFingerprint, CancellationToken cancellationToken = default) =>
            Task.FromResult(_stored is null ? null : InteractionReceiptWriteResult.Replay(_stored.Receipt!));
        public Task<InteractionReceiptProjection?> GetAsync(InteractionAuthorizationRequest authorizationRequest,
            string receiptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
