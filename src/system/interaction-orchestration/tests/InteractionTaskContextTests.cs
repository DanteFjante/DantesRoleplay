using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Capabilities;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Play;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Tests;

public sealed class InteractionTaskContextTests
{
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("sample-app");
    private static readonly string CatalogFingerprint = Hash("catalog");

    [Fact]
    public async Task Pack_is_authorized_first_canonically_rehydrated_and_provenance_complete()
    {
        var mechanic = Mechanic("sample-app.mechanic.act", "Act", "Apply the requested action.");
        var query = Query("sample-app.query.resume", "Resume", "Read the current situation.");
        var snapshot = Snapshot([mechanic, query]);
        var policy = new RecordingAuthorization();
        var materializer = new InteractionTaskContextMaterializer(policy,
            new RecordingRetriever(policy, snapshot, [mechanic, query]),
            new FixedSnapshots(snapshot), new ReadModels(), new Knowledge(), new Play(), new Receipts());
        var (envelope, request) = Envelope();

        var pack = await materializer.MaterializeAsync(envelope, request);

        Assert.Equal(InteractionTaskContextProfiles.Version2, pack.Profile);
        Assert.True(Encoding.UTF8.GetByteCount(pack.Json) <= InteractionTaskContextMaterializer.MaximumPackBytes);
        Assert.Equal(Hash(pack.Json), pack.Fingerprint);
        Assert.Equal(2, policy.Calls);
        using var document = JsonDocument.Parse(pack.Json);
        var root = document.RootElement;
        Assert.Equal(4, root.GetProperty("scope").GetArrayLength());
        Assert.Equal(2, root.GetProperty("capabilities").GetArrayLength());
        Assert.Single(root.GetProperty("readViews").EnumerateArray());
        Assert.Single(root.GetProperty("knowledge").EnumerateArray());
        Assert.Single(root.GetProperty("facts").EnumerateArray());
        Assert.Single(root.GetProperty("continuity").EnumerateArray());
        Assert.Single(root.GetProperty("recentReceipts").EnumerateArray());
        Assert.Equal(InteractionTaskContextMaterializer.MaximumPackBytes,
            root.GetProperty("budgets").GetProperty("maximumBytes").GetInt32());
        Assert.Equal(InteractionTaskContextMaterializer.MaximumPackItems,
            root.GetProperty("budgets").GetProperty("maximumItems").GetInt32());
        Assert.Empty(root.GetProperty("omissions").EnumerateArray());
        foreach (var item in root.GetProperty("scope").EnumerateArray()
                     .Concat(root.GetProperty("capabilities").EnumerateArray())
                     .Concat(root.GetProperty("readViews").EnumerateArray())
                     .Concat(root.GetProperty("knowledge").EnumerateArray())
                     .Concat(root.GetProperty("facts").EnumerateArray())
                     .Concat(root.GetProperty("continuity").EnumerateArray())
                     .Concat(root.GetProperty("recentReceipts").EnumerateArray()))
        {
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("Reference").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("Revision").GetString()));
            Assert.Matches("^[0-9A-F]{64}$", item.GetProperty("Fingerprint").GetString()!);
        }
        Assert.Contains(pack.SourceReferences, value => value.StartsWith("read-view:sample-app.query.resume#", StringComparison.Ordinal));
        Assert.Contains(pack.SourceReferences, value => value.StartsWith("knowledge:knowledge.1#", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Denial_stops_before_retrieval_or_state_access()
    {
        var policy = new RecordingAuthorization(allowed: false);
        var snapshot = Snapshot([Mechanic("sample-app.mechanic.act", "Act", "Apply an action.")]);
        var retriever = new RecordingRetriever(policy, snapshot, []);
        var materializer = new InteractionTaskContextMaterializer(policy, retriever,
            new FixedSnapshots(snapshot), new ReadModels());
        var (envelope, request) = Envelope();

        var exception = await Assert.ThrowsAsync<InteractionTaskContextException>(() =>
            materializer.MaterializeAsync(envelope, request));

        Assert.Equal("TASK_CONTEXT_NOT_AUTHORIZED", exception.Code);
        Assert.Equal(0, retriever.Calls);
    }

    [Fact]
    public async Task Field_based_capability_uses_only_exact_registry_discovery_after_authorization()
    {
        var query = FieldQuery("sample-app.query.member");
        var snapshot = Snapshot([query]);
        var policy = new RecordingAuthorization();
        var registry = new RecordingProjectionRegistry(Discovery());
        var materializer = new InteractionTaskContextMaterializer(policy,
            new RecordingRetriever(policy, snapshot, [query]), new FixedSnapshots(snapshot),
            new ReadModels(), projections: registry);
        var (envelope, request) = Envelope();

        var pack = await materializer.MaterializeAsync(envelope, request);

        Assert.Equal(1, registry.Calls);
        Assert.Equal(Discovery().Object, registry.Reference);
        using var document = JsonDocument.Parse(pack.Json);
        var capability = Assert.Single(document.RootElement.GetProperty("capabilities").EnumerateArray());
        var value = capability.GetProperty("Value");
        Assert.Equal("sample-app.identity", value.GetProperty("ObjectDiscovery")
            .GetProperty("Sources")[0].GetProperty("QualifiedComponentId").GetString());
        var readView = Assert.Single(document.RootElement.GetProperty("readViews").EnumerateArray());
        Assert.Equal("available", readView.GetProperty("Value").GetProperty("objectReadEvidence")
            .GetProperty("Availability").GetString());
        Assert.DoesNotContain(document.RootElement.GetProperty("limitations").EnumerateArray(),
            item => item.GetString()?.StartsWith("TASK_CONTEXT_OBJECT_DISCOVERY_", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(document.RootElement.GetProperty("limitations").EnumerateArray(),
            item => item.GetString()?.StartsWith("TASK_CONTEXT_OBJECT_READ_EVIDENCE_", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Field_based_read_evidence_is_host_opted_and_missing_evidence_is_explicit()
    {
        var query = FieldQuery("sample-app.query.member");
        var snapshot = Snapshot([query]);
        var policy = new RecordingAuthorization();
        var reads = new ReadModels(includeEvidence: false);
        var materializer = new InteractionTaskContextMaterializer(policy,
            new RecordingRetriever(policy, snapshot, [query]), new FixedSnapshots(snapshot), reads,
            projections: new RecordingProjectionRegistry(Discovery()));
        var (envelope, request) = Envelope();

        var pack = await materializer.MaterializeAsync(envelope, request);

        Assert.True(reads.LastRequest!.IncludeObjectReadEvidence);
        using var document = JsonDocument.Parse(pack.Json);
        Assert.Contains(document.RootElement.GetProperty("limitations").EnumerateArray(),
            value => value.GetString() == "TASK_CONTEXT_OBJECT_READ_EVIDENCE_UNAVAILABLE");
        Assert.False(Assert.Single(document.RootElement.GetProperty("readViews").EnumerateArray())
            .GetProperty("Value").TryGetProperty("objectReadEvidence", out _));

        var budgetPolicy = new RecordingAuthorization();
        var budgeted = await new InteractionTaskContextMaterializer(budgetPolicy,
            new RecordingRetriever(budgetPolicy, snapshot, [query]), new FixedSnapshots(snapshot),
            new ReadModels(budgetExceeded: true),
            projections: new RecordingProjectionRegistry(Discovery())).MaterializeAsync(envelope, request);
        using var budgetDocument = JsonDocument.Parse(budgeted.Json);
        Assert.Contains(budgetDocument.RootElement.GetProperty("limitations").EnumerateArray(),
            value => value.GetString() == "TASK_CONTEXT_OBJECT_READ_EVIDENCE_BUDGET_EXCEEDED");
    }

    [Fact]
    public async Task Missing_or_oversized_field_discovery_is_explicit_and_never_breaks_the_pack_bound()
    {
        var query = FieldQuery("sample-app.query.member");
        var snapshot = Snapshot([query]);
        var (envelope, request) = Envelope();

        var missingPolicy = new RecordingAuthorization();
        var missing = await new InteractionTaskContextMaterializer(missingPolicy,
            new RecordingRetriever(missingPolicy, snapshot, [query]), new FixedSnapshots(snapshot),
            new ReadModels(), projections: new RecordingProjectionRegistry(null))
            .MaterializeAsync(envelope, request);
        using (var document = JsonDocument.Parse(missing.Json))
            Assert.Contains(document.RootElement.GetProperty("limitations").EnumerateArray(),
                item => item.GetString() == "TASK_CONTEXT_OBJECT_DISCOVERY_UNAVAILABLE");

        var largePolicy = new RecordingAuthorization();
        var oversized = Discovery("{\"type\":\"object\",\"description\":\"" + new string('x', 20_000) + "\"}");
        var bounded = await new InteractionTaskContextMaterializer(largePolicy,
            new RecordingRetriever(largePolicy, snapshot, [query]), new FixedSnapshots(snapshot),
            new ReadModels(), projections: new RecordingProjectionRegistry(oversized))
            .MaterializeAsync(envelope, request);
        Assert.True(Encoding.UTF8.GetByteCount(bounded.Json) <= InteractionTaskContextMaterializer.MaximumPackBytes);
        using var boundedDocument = JsonDocument.Parse(bounded.Json);
        Assert.Contains(boundedDocument.RootElement.GetProperty("limitations").EnumerateArray(),
            item => item.GetString() == "TASK_CONTEXT_OBJECT_DISCOVERY_BUDGET_EXCEEDED");
        Assert.False(Assert.Single(boundedDocument.RootElement.GetProperty("capabilities").EnumerateArray())
            .GetProperty("Value").TryGetProperty("ObjectDiscovery", out _));
    }

    [Fact]
    public async Task Authorization_denial_prevents_field_discovery_lookup()
    {
        var query = FieldQuery("sample-app.query.member");
        var snapshot = Snapshot([query]);
        var policy = new RecordingAuthorization(allowed: false);
        var registry = new RecordingProjectionRegistry(Discovery());
        var materializer = new InteractionTaskContextMaterializer(policy,
            new RecordingRetriever(policy, snapshot, [query]), new FixedSnapshots(snapshot),
            new ReadModels(), projections: registry);
        var (envelope, request) = Envelope();

        await Assert.ThrowsAsync<InteractionTaskContextException>(() =>
            materializer.MaterializeAsync(envelope, request));

        Assert.Equal(0, registry.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Navigation_materialization_fingerprint_is_distinct_from_activation_authority(bool staleActivation)
    {
        var mechanic = Mechanic("sample-app.mechanic.act", "Act", "Apply an action.");
        var derived = Snapshot([mechanic], Hash("navigation-materializer-version"));
        var snapshot = new ActiveCatalogFeatureSnapshot(derived.Manifest, derived.Documents)
        {
            EffectiveSetFingerprint = staleActivation ? Hash("stale-activation") : CatalogFingerprint,
            Resolution = CatalogExtensionResolutionContext.Create(App, CatalogFingerprint, [])
        };
        var policy = new RecordingAuthorization();
        var materializer = new InteractionTaskContextMaterializer(policy,
            new RecordingRetriever(policy, snapshot, [mechanic]), new FixedSnapshots(snapshot), new ReadModels());
        var (envelope, request) = Envelope(includeFactReference: false);
        if (staleActivation)
        {
            var error = await Assert.ThrowsAsync<InteractionTaskContextException>(() => materializer.MaterializeAsync(envelope, request));
            Assert.Equal("TASK_CONTEXT_CATALOG_STALE", error.Code);
        }
        else
            Assert.Matches("^[0-9A-F]{64}$", (await materializer.MaterializeAsync(envelope, request)).Fingerprint);
    }

    [Fact]
    public async Task Catalog_change_during_assembly_fails_closed()
    {
        var mechanic = Mechanic("sample-app.mechanic.act", "Act", "Apply an action.");
        var first = Snapshot([mechanic]);
        var second = Snapshot([mechanic], Hash("changed-catalog"));
        var policy = new RecordingAuthorization();
        var materializer = new InteractionTaskContextMaterializer(policy,
            new RecordingRetriever(policy, first, [mechanic]),
            new ChangingSnapshots(first, second), new ReadModels());
        var (envelope, request) = Envelope();

        var exception = await Assert.ThrowsAsync<InteractionTaskContextException>(() =>
            materializer.MaterializeAsync(envelope, request));

        Assert.Equal("TASK_CONTEXT_CATALOG_STALE", exception.Code);
    }

    [Fact]
    public async Task Oversized_optional_candidates_are_trimmed_to_the_closed_byte_budget()
    {
        var records = Enumerable.Range(1, 12).Select(index => Mechanic(
            $"sample-app.mechanic.large-{index}", $"Large {index}", new string((char)('a' + index), 4_000)))
            .ToArray();
        var snapshot = Snapshot(records);
        var policy = new RecordingAuthorization();
        var materializer = new InteractionTaskContextMaterializer(policy,
            new RecordingRetriever(policy, snapshot, records), new FixedSnapshots(snapshot),
            new ReadModels());
        var (envelope, request) = Envelope();

        var pack = await materializer.MaterializeAsync(envelope, request);

        Assert.True(Encoding.UTF8.GetByteCount(pack.Json) <= InteractionTaskContextMaterializer.MaximumPackBytes);
        using var document = JsonDocument.Parse(pack.Json);
        var capabilities = document.RootElement.GetProperty("capabilities").GetArrayLength();
        Assert.InRange(capabilities, 1, 11);
        Assert.Contains(document.RootElement.GetProperty("limitations").EnumerateArray(),
            value => value.GetString() == "TASK_CONTEXT_TRUNCATED");
        Assert.Contains(document.RootElement.GetProperty("omissions").EnumerateArray(),
            value => value.GetProperty("reason").GetString() == "byte-budget"
                     && value.GetProperty("removedItems").GetInt32() > 0);
    }

    [Fact]
    public async Task Item_budget_omissions_name_the_trimmed_section_and_count()
    {
        var mechanic = Mechanic("sample-app.mechanic.act", "Act", "Apply an action.");
        var snapshot = Snapshot([mechanic]);
        var policy = new RecordingAuthorization();
        var materializer = new InteractionTaskContextMaterializer(policy,
            new RecordingRetriever(policy, snapshot, [mechanic]), new FixedSnapshots(snapshot),
            new ReadModels(), play: new Play(20));
        var (envelope, request) = Envelope(includeFactReference: false);

        var pack = await materializer.MaterializeAsync(envelope, request);

        using var document = JsonDocument.Parse(pack.Json);
        Assert.Equal(16, document.RootElement.GetProperty("facts").GetArrayLength());
        var omission = Assert.Single(document.RootElement.GetProperty("omissions").EnumerateArray());
        Assert.Equal("facts", omission.GetProperty("section").GetString());
        Assert.Equal("item-budget", omission.GetProperty("reason").GetString());
        Assert.Equal(4, omission.GetProperty("removedItems").GetInt32());
    }

    private static (AuthorizedInteractionEnvelope Envelope, InteractionAuthorizationRequest Request) Envelope(
        bool includeFactReference = true)
    {
        var applications = new InMemoryApplicationRegistry();
        var revision = applications.Register(new(App, "Sample", "Task-context fixture.", []));
        var principal = TrustedPrincipalContext.VerifiedPrincipal(Principal, "fixture");
        var request = new InteractionAuthorizationRequest(principal, App, "state.1",
            InteractionCapability.Plan, "context.fixture");
        var initial = InteractionAuthorizationDecision.Allow(request, "authorization.initial");
        var host = new InteractionHostContext(principal, revision, "state.1", "session.1",
            "state-revision.1", CatalogFingerprint, InteractionRoleProfile.Inner,
            new(4, 65_536, 65_536), initial, resolutionFingerprint: CatalogFingerprint);
        var references = includeFactReference ? "[\"fact.1\"]" : "[]";
        var intent = InteractionIntent.Parse("""
            {"idempotencyKey":"context.1","intentText":"resume the campaign and act","maximumPlanSteps":2,"roleHints":{"campaign":"campaign.1","subject":"entity.1"},"conversationFactReferences":FACT_REFERENCES}
            """.Replace("FACT_REFERENCES", references, StringComparison.Ordinal));
        return (AuthorizedInteractionEnvelope.Create(intent, host), request);
    }

    private static ActiveCatalogFeatureSnapshot Snapshot(
        IReadOnlyList<CatalogRecordDefinition> records,
        string? fingerprint = null)
    {
        var manifest = CatalogNavigationManifest.Create(App, fingerprint ?? CatalogFingerprint,
            "catalog-lexical-v1", [new("sample", "Sample", "Task-context records.")],
            [new("sample", "", "Sample", "Task-context records.", CatalogDescriptionStatus.Authored)], records);
        return new(manifest, records.Select(record => new ActiveCatalogFeatureDocument(
            record, SourceTrust.Trusted)).ToArray());
    }

    private static CatalogRecordDefinition Mechanic(string id, string name, string description)
    {
        var content = JsonSerializer.Serialize(new
        {
            id,
            category = "fixture",
            name,
            description,
            matches = "act",
            requirements = "{\"roles\":{\"subject\":{\"components\":[]}}}",
            source = "return { effects: [] };",
            scope = "fixture",
            status = "active"
        });
        return Record("mechanic", id, name, description, content);
    }

    private static CatalogRecordDefinition Query(string id, string name, string description)
    {
        var content = JsonSerializer.Serialize(new
        {
            id,
            category = "campaign.resume",
            name,
            description,
            matches = new[] { "resume campaign" },
            roles = new Dictionary<string, string> { ["subject"] = "The current actor." },
            executor = ApplicationQueryContract.MechanicProjectionExecutor,
            projection = new
            {
                qualifiedId = "sample-app.mechanic.resume.project",
                version = 1,
                contentHash = Hash("projection"),
                outputSchemaHash = Hash("schema")
            },
            outputSchema = new { type = "object" },
            exposure = "model-visible",
            status = "active"
        });
        return Record(ApplicationQueryContract.CatalogKind, id, name, description, content);
    }

    private static CatalogRecordDefinition FieldQuery(string id)
    {
        var content = JsonSerializer.Serialize(new
        {
            id,
            category = "world.member",
            name = "Member",
            description = "Reads one member.",
            matches = new[] { "read member" },
            roles = new Dictionary<string, string> { ["subject"] = "The current actor." },
            executor = ApplicationQueryContract.ObjectProjectionExecutor,
            profile = RegisteredApplicationObjectContract.FieldBasedContractProfileId,
            @object = new
            {
                qualifiedId = "sample-app.object.member",
                version = 1,
                contentFingerprint = Hash("object")
            },
            collection = "members",
            exposure = "model-visible",
            status = "active"
        });
        return Record(ApplicationQueryContract.CatalogKind, id, "Member", "Reads one member.", content);
    }

    private static ApplicationObjectDiscovery Discovery(string? schema = null)
    {
        schema ??= "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}";
        var component = new EcsComponentReference("sample-app.identity", 1,
            CapabilityContractBuilder.SchemaHash(schema));
        var field = new ApplicationObjectFieldProvenance("/name", ["identity"], "subject",
            component, "/name", true);
        return new(new("sample-app.object.member", 1, Hash("object")),
            RegisteredApplicationObjectContract.FieldBasedContractProfileId,
            [new("source-1", ["identity"], "subject", true, component,
                CapabilityContractBuilder.JsonSchemaProfile, schema)], [field], []);
    }

    private static ApplicationObjectReadEvidence ReadEvidence()
    {
        var discovery = Discovery();
        var source = Assert.Single(discovery.Sources);
        return new(discovery.Object, discovery.ProfileId, "available",
            [new(source.SourceId, source.InputPath, source.EntityRole, source.Required, source.Component,
                "available", [new(source.Component, source.SchemaProfileId, source.SchemaJson)])],
            [new("/name", source.SourceId, "/name", ["value"])]);
    }

    private static CatalogRecordDefinition Record(
        string kind, string id, string name, string description, string content) =>
        new("sample", kind, id, name, description, [], [], "", "active", 1, content,
            Hash(content), "fixture-source", $"{kind}/{id}.json");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class RecordingAuthorization(bool allowed = true) : IInteractionAuthorizationPolicy
    {
        public int Calls { get; private set; }
        public InteractionAuthorizationDecision Evaluate(InteractionAuthorizationRequest request)
        {
            Calls++;
            return allowed
                ? InteractionAuthorizationDecision.Allow(request, "authorization.current")
                : InteractionAuthorizationDecision.Deny(request, "DENIED", "authorization.denied");
        }
    }

    private sealed class RecordingRetriever(
        RecordingAuthorization authorization,
        ActiveCatalogFeatureSnapshot snapshot,
        IReadOnlyList<CatalogRecordDefinition> records) : IInteractionFeatureRetriever
    {
        public int Calls { get; private set; }
        public Task<InteractionFeatureSearchResult> SearchAsync(
            InteractionFeatureRetrievalScope scope,
            InteractionFeatureSearchInput input,
            CancellationToken cancellationToken = default)
        {
            Assert.True(authorization.Calls > 0);
            Calls++;
            var hits = records.Select((record, rank) => InteractionFeatureHit.Create(
                InteractionFeatureReference.Create(App, InteractionRetrievalLane.TrustedFeature,
                    snapshot.Manifest.Fingerprint, record), record, rank + 1, null, false)).ToArray();
            return Task.FromResult(InteractionFeatureSearchResult.Create(
                InteractionRetrievalMode.Lexical, hits));
        }

        public Task<InteractionFeatureRebuildResult> RebuildAsync(
            InteractionFeatureRetrievalScope scope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedSnapshots(ActiveCatalogFeatureSnapshot snapshot) : IActiveCatalogFeatureSnapshotProvider
    {
        public bool TryGetSnapshot(ApplicationIdentifier applicationId, out ActiveCatalogFeatureSnapshot value)
        {
            value = snapshot;
            return applicationId == App;
        }
    }

    private sealed class ChangingSnapshots(
        ActiveCatalogFeatureSnapshot first,
        ActiveCatalogFeatureSnapshot second) : IActiveCatalogFeatureSnapshotProvider
    {
        private int calls;
        public bool TryGetSnapshot(ApplicationIdentifier applicationId, out ActiveCatalogFeatureSnapshot value)
        {
            value = Interlocked.Increment(ref calls) == 1 ? first : second;
            return applicationId == App;
        }
    }

    private sealed class ReadModels(bool includeEvidence = true, bool budgetExceeded = false) : IApplicationReadModelService
    {
        public ApplicationReadModelRequest? LastRequest { get; private set; }
        public Task<ApplicationReadModelResult> ReadAsync(
            ApplicationReadModelRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var result = new ApplicationReadModelResult(
                App.Value, request.StateSpaceId, request.QualifiedQueryId, CatalogFingerprint,
                CatalogFingerprint, Hash("schema"), Hash("result"), Hash("source-revision"),
                "{\"summary\":\"Current situation\"}");
            if (includeEvidence && request.IncludeObjectReadEvidence)
                result = result with { ObjectReadEvidence = budgetExceeded
                    ? ReadEvidence() with { Availability = "budget-exceeded", Sources = [], Fields = [] }
                    : ReadEvidence() };
            return Task.FromResult(result);
        }
    }

    private sealed class Knowledge : IAuthorizedKnowledgeCandidateResolver
    {
        public Task<AuthorizedKnowledgeCandidateSet> ResolveAsync(
            AuthorizedKnowledgeRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new AuthorizedKnowledgeCandidateSet(
            true, true, "policy.1", Hash("scope"),
            [new("knowledge.1", "The gate is open.", "known", "fact", Hash("knowledge"))],
            false));
    }

    private sealed class Play(int factCount = 1) : IApplicationPlayRecordStore
    {
        public PlayConversationDocument? GetSession(PlayConversationIdentity identity) => new(
            "conversation.1", identity.PrincipalId, identity.ApplicationId, identity.StateSpaceId,
            identity.SessionContextId, "active", 3, 1, [],
            new("situation.1", 2, PlaySituationKinds.Conversation, PlaySituationStatuses.Active,
                "At the open gate.", [], null, DateTime.UnixEpoch, DateTime.UnixEpoch, null),
            Enumerable.Range(1, factCount).Select(index => new PlayTruthDocument(
                "fact." + index, index, "Established fact " + index + ".", ["entity.gate"],
                "message." + index, "situation.1", DateTime.UnixEpoch)).ToArray(),
            DateTime.UnixEpoch, DateTime.UnixEpoch);
        public PlayConversationDocument ResumeOrCreate(PlayConversationIdentity identity) => throw new NotSupportedException();
        public PlayConversationDocument? Get(string principalId, string applicationId, string conversationId) => throw new NotSupportedException();
        public PlayConversationDocument AppendMessage(string conversationId, PlayMessageAppend message, string status) => throw new NotSupportedException();
        public PlayConversationDocument AppendNarrative(string conversationId, PlayNarrativeAppend narrative, string status) => throw new NotSupportedException();
        public PlayConversationDocument SetStatus(string conversationId, string status) => throw new NotSupportedException();
        public PlayMessagePage GetMessages(string principalId, string applicationId, string conversationId, int? beforeOrdinal, int limit) => throw new NotSupportedException();
    }

    private sealed class Receipts : IInteractionRecentReceiptReader
    {
        public Task<IReadOnlyList<InteractionReceiptContext>> ReadRecentAsync(
            InteractionAuthorizationRequest authorizationRequest,
            string sessionContextId,
            int limit,
            CancellationToken cancellationToken = default)
        {
            var receipt = new InteractionReceiptProjection("interaction-receipt." + new string('b', 32),
                "resolution", Principal, App, "state.1", "intent.1", Hash("receipt"),
                "resolved", "RESOLVED", Hash("proposal"), "Resolved.", [], DateTime.UnixEpoch);
            return Task.FromResult<IReadOnlyList<InteractionReceiptContext>>([
                new($"receipt:{receipt.Id}#{receipt.RequestFingerprint}", sessionContextId, 1,
                    Envelope().Envelope.Host.ApplicationRevision.Fingerprint, "state-revision.1",
                    CatalogFingerprint, "authorization.current", receipt)
            ]);
        }
    }

    private sealed class RecordingProjectionRegistry(ApplicationObjectDiscovery? discovery)
        : IProjectionDefinitionRegistry
    {
        public int Calls { get; private set; }
        public ProjectionReference? Reference { get; private set; }
        public ApplicationObjectDiscovery? Discover(ProjectionReference reference)
        {
            Calls++;
            Reference = reference;
            return discovery is not null && discovery.Object == reference ? discovery : null;
        }
        public RegisteredProjectionDefinition Define(ProjectionDefinitionRequest definition) =>
            throw new NotSupportedException();
        public RegisteredProjectionDefinition? Get(string qualifiedId, int version) => null;
        public ProjectionImpactGraph GetImpactGraph(ApplicationIdentifier owner) =>
            new(new Dictionary<string, IReadOnlyList<string>>(),
                new Dictionary<string, IReadOnlyList<string>>());
    }
}
