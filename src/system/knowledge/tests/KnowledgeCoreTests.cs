using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Operations;
using DantesRoleplay.Retrieval;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Knowledge.Tests;

public sealed class KnowledgeCoreTests
{
    [Fact]
    public void Answer_request_has_no_identity_or_scope_override_fields()
    {
        var names = typeof(AuthorizedKnowledgeRequest).GetProperties()
            .Select(value => value.Name).OrderBy(value => value, StringComparer.Ordinal).ToArray();

        Assert.Equal([
            "AsOfMinute", "CampaignId", "CandidateLimit", "Kinds", "Question", "SubjectIds"
        ], names);
    }

    [Fact]
    public async Task Denied_audience_is_resolved_before_binding_or_game_state()
    {
        var policy = new Policy(KnowledgeAudienceResolution.Denied());
        var untouched = new UntouchedDependencies();
        var resolver = new AuthorizedKnowledgeCandidateResolver(policy, untouched, untouched,
            untouched, untouched, untouched);

        var result = await resolver.ResolveAsync(new("campaign-fixture", "archive ledger"));

        Assert.False(result.Granted);
        Assert.Equal(1, policy.Calls);
        Assert.Equal(0, untouched.Calls);
    }

    [Fact]
    public async Task Wrong_campaign_grant_is_denied_before_binding_or_game_state()
    {
        var policy = new Policy(new(new("principal", "another-campaign",
            KnowledgeAudienceRole.Actor, "actor", "policy.1")));
        var untouched = new UntouchedDependencies();
        var resolver = new AuthorizedKnowledgeCandidateResolver(policy, untouched, untouched,
            untouched, untouched, untouched);

        var result = await resolver.ResolveAsync(new("campaign-fixture", "archive ledger"));

        Assert.False(result.Granted);
        Assert.Equal(1, policy.Calls);
        Assert.Equal(0, untouched.Calls);
    }

    [Fact]
    public async Task Activated_binding_resolves_only_the_exact_active_campaign_space()
    {
        using var fixture = new KnowledgeFixture();
        await fixture.AddCoreAsync();
        var document = new ActivatedApplicationTextDocument(
            ApplicationIdentifier.Parse(fixture.ApplicationId), 3, new('A', 64), "fixture-source",
            new KnowledgeApplicationSelection(fixture.ApplicationId).BindingDocumentPath,
            new('B', 64), fixture.BindingDocument());
        var resolver = new ActivatedKnowledgeApplicationBindingResolver(
            new(fixture.ApplicationId), new BindingDocumentReader(document),
            fixture.StateSpaces, fixture.Entities);

        var resolved = await resolver.ResolveAsync(fixture.Campaign);
        var missing = await resolver.ResolveAsync("campaign-other");

        Assert.NotNull(resolved);
        Assert.Equal(fixture.ApplicationId, resolved.ApplicationId);
        Assert.Equal(fixture.Campaign, resolved.CampaignEntityId);
        Assert.Equal(fixture.Binding.CampaignRootComponentTypeId, resolved.CampaignRootComponentTypeId);
        Assert.Equal(fixture.Binding.ParticipationComponentTypeId, resolved.ParticipationComponentTypeId);
        Assert.Equal(64, resolved.BindingRevision.Length);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Participation_requires_one_exact_active_campaign_actor_path()
    {
        using var fixture = new KnowledgeFixture();
        await fixture.AddCoreAsync();
        await fixture.AddParticipationAsync();
        var verifier = new ApplicationKnowledgeActorParticipationVerifier(
            fixture.Entities, fixture.Edges);

        var active = await verifier.ResolveAsync(fixture.Binding, fixture.Actor);
        var missing = await verifier.ResolveAsync(fixture.Binding, "actor-missing");

        Assert.True(active.Active);
        Assert.Equal(64, active.Revision.Length);
        Assert.False(missing.Active);
        Assert.True(missing.ActorMissing);
        await fixture.AddEntityAsync("actor-other", "Other actor");
        await fixture.RelateAsync(fixture.Participation, "actor-other",
            fixture.Binding.ParticipationActorRelationshipKind, "{}");
        var wrongActor = await verifier.ResolveAsync(fixture.Binding, "actor-other");
        var ambiguousActor = await verifier.ResolveAsync(fixture.Binding, fixture.Actor);
        Assert.False(wrongActor.Active);
        Assert.False(ambiguousActor.Active);

        using var withdrawn = new KnowledgeFixture();
        await withdrawn.AddCoreAsync();
        await withdrawn.AddParticipationAsync("withdrawn");
        var withdrawnResult = await new ApplicationKnowledgeActorParticipationVerifier(
            withdrawn.Entities, withdrawn.Edges).ResolveAsync(withdrawn.Binding, withdrawn.Actor);
        Assert.False(withdrawnResult.Active);
    }

    [Fact]
    public void Lexical_allowlist_is_applied_before_ranking_and_limit()
    {
        var retriever = new DeterministicKnowledgeLexicalRetriever();
        var hidden = Document("hidden", "ledger ledger ledger");
        var allowed = Document("allowed", "ledger");

        var result = retriever.Search([hidden, allowed], new(
            "ledger", null, null, 10, 1,
            new HashSet<string>(["allowed"], StringComparer.Ordinal)));

        Assert.Equal("allowed", Assert.Single(result).Document.KnowledgeId);
    }

    [Fact]
    public async Task Application_projection_and_effective_states_preserve_explicit_precedence()
    {
        using var fixture = new KnowledgeFixture();
        await fixture.AddCoreAsync();
        await fixture.AddKnowledgeAsync("known", "The archive ledger is genuine.");
        await fixture.AddKnowledgeAsync("hidden", "The sealed ledger names the spy.");
        await fixture.AddKnowledgeAsync("familiar", "The brass seal belongs to the archive.");
        await fixture.AddKnowledgeAsync("baseline", "The market ledger records old tolls.");
        await fixture.RelateAsync(fixture.Actor, "known", fixture.Binding.ExplicitStateRelationshipKind,
            "{\"stance\":\"suspected\"}");
        await fixture.RelateAsync(fixture.World, "hidden", fixture.Binding.BaselineRelationshipKind,
            "{\"inheritance\":\"current\"}");
        await fixture.RelateAsync(fixture.Actor, "hidden", fixture.Binding.ExplicitStateRelationshipKind,
            "{\"stance\":\"unknown\"}");
        await fixture.RelateAsync(fixture.Actor, "familiar", fixture.Binding.ExplicitStateRelationshipKind,
            "{\"stance\":\"familiar\"}");
        await fixture.RelateAsync(fixture.World, "baseline", fixture.Binding.BaselineRelationshipKind,
            "{\"inheritance\":\"current\"}");

        var scope = Assert.IsType<KnowledgeCampaignScope>(
            await fixture.Source.ReadCampaignScopeAsync(fixture.Binding));
        var projection = Assert.IsType<KnowledgeCampaignProjection>(
            await fixture.Source.ReadWorldAsync(fixture.Binding, scope));
        var states = await fixture.States.ResolveAllAsync(fixture.Binding, fixture.Actor,
            fixture.World, projection.Documents.Select(value => value.KnowledgeId).ToArray());

        Assert.Equal(4, projection.Documents.Count);
        Assert.Equal("suspected", states["known"].State);
        Assert.Equal("unknown", states["hidden"].State);
        Assert.Equal("familiar", states["familiar"].State);
        Assert.Equal("known", states["baseline"].State);
        Assert.Equal("world-baseline", states["baseline"].SourceKind);
    }

    [Fact]
    public async Task Candidate_resolver_excludes_unknown_and_returns_familiar_recognition_only()
    {
        using var fixture = new KnowledgeFixture();
        await fixture.AddCoreAsync();
        await fixture.AddKnowledgeAsync("known", "The archive ledger records old tolls.");
        await fixture.AddKnowledgeAsync("hidden", "The archive ledger names the spy.");
        await fixture.AddKnowledgeAsync("familiar", "The brass seal marks the royal archive.");
        await fixture.RelateAsync(fixture.Actor, "known", fixture.Binding.ExplicitStateRelationshipKind,
            "{\"stance\":\"known\"}");
        await fixture.RelateAsync(fixture.Actor, "hidden", fixture.Binding.ExplicitStateRelationshipKind,
            "{\"stance\":\"unknown\"}");
        await fixture.RelateAsync(fixture.Actor, "familiar", fixture.Binding.ExplicitStateRelationshipKind,
            "{\"stance\":\"familiar\"}");
        var resolver = fixture.CandidateResolver();

        var ledger = await resolver.ResolveAsync(new(fixture.Campaign, "archive ledger"));
        var seal = await resolver.ResolveAsync(new(fixture.Campaign, "brass seal"));

        Assert.Equal("known", Assert.Single(ledger.Candidates).KnowledgeId);
        Assert.DoesNotContain("hidden", ledger.Candidates.Select(value => value.KnowledgeId));
        Assert.Empty(seal.Candidates);
        Assert.True(seal.FamiliarMatch);
    }

    [Fact]
    public async Task Active_faction_and_containing_region_baselines_apply_without_becoming_authority()
    {
        using var fixture = new KnowledgeFixture();
        await fixture.AddCoreAsync();
        await fixture.AddKnowledgeAsync("faction-record", "The guild archive keeps a ledger.");
        await fixture.AddKnowledgeAsync("region-record", "The market archive keeps a map.");
        await fixture.AddFactionAsync("guild");
        await fixture.AddRegionAsync("market");
        await fixture.RelateAsync("guild", "faction-record", fixture.Binding.BaselineRelationshipKind,
            "{\"inheritance\":\"current\"}");
        await fixture.RelateAsync("market", "region-record", fixture.Binding.BaselineRelationshipKind,
            "{\"inheritance\":\"current\"}");

        var states = await fixture.States.ResolveAllAsync(fixture.Binding, fixture.Actor,
            fixture.World, ["faction-record", "region-record"]);

        Assert.Equal("known", states["faction-record"].State);
        Assert.Equal("scope-baseline", states["faction-record"].SourceKind);
        Assert.Equal("known", states["region-record"].State);
        Assert.Equal("scope-baseline", states["region-record"].SourceKind);
    }

    [Fact]
    public async Task Malformed_baseline_scope_invalidates_the_record_instead_of_falling_back()
    {
        using var fixture = new KnowledgeFixture();
        await fixture.AddCoreAsync();
        await fixture.AddKnowledgeAsync("record", "The archive keeps a ledger.");
        await fixture.AddEntityAsync("not-a-scope", "Not a scope");
        await fixture.RelateAsync(fixture.World, "record", fixture.Binding.BaselineRelationshipKind,
            "{\"inheritance\":\"current\"}");
        await fixture.RelateAsync("not-a-scope", "record", fixture.Binding.BaselineRelationshipKind,
            "{\"inheritance\":\"current\"}");

        var states = await fixture.States.ResolveAllAsync(fixture.Binding, fixture.Actor,
            fixture.World, ["record"]);

        Assert.Empty(states);
    }

    [Fact]
    public async Task Safe_answer_strips_ids_and_rejects_mixed_perspectives()
    {
        var safe = Set([new("fact.internal", "Archive ledger", "suspected", "statement", "r1")]);
        var coordinator = new AuthorizedKnowledgeCoordinator(new Sets(safe, safe), new Completion("""
            {"selectedIds":["fact.internal"],"statements":[{"text":"The archive holds an old ledger.","citations":["fact.internal"]}],"unresolved":[],"unknown":false}
            """));

        var result = await coordinator.AnswerAsync(new("campaign-fixture", "archive ledger"));

        Assert.True(result.Answered);
        Assert.Equal("suspected", Assert.Single(result.Statements).Stance);
        Assert.DoesNotContain("fact.internal", System.Text.Json.JsonSerializer.Serialize(result),
            StringComparison.Ordinal);

        var mixed = Set([
            new("fact.internal", "Archive ledger", "known", "statement", "r1"),
            new("rumour.internal", "Haunted wharf", "believed", "rumour", "r2")
        ]);
        var rejected = new AuthorizedKnowledgeCoordinator(new Sets(mixed), new Completion("""
            {"selectedIds":["fact.internal","rumour.internal"],"statements":[{"text":"The ledger says the wharf is haunted.","citations":["fact.internal","rumour.internal"]}],"unresolved":[],"unknown":false}
            """));

        Assert.Equal("unknown", (await rejected.AnswerAsync(
            new("campaign-fixture", "wharf"))).Status);
    }

    [Fact]
    public async Task Repeated_candidate_change_fails_stale_after_one_retry()
    {
        var first = Set([new("fact.internal", "Archive ledger", "known", "statement", "r1")]);
        var changed = first with
        {
            Candidates = [new("fact.internal", "Archive ledger", "known", "statement", "r2")]
        };
        var completion = new Completion("""
            {"selectedIds":["fact.internal"],"statements":[{"text":"The archive holds a ledger.","citations":["fact.internal"]}],"unresolved":[],"unknown":false}
            """);
        var coordinator = new AuthorizedKnowledgeCoordinator(
            new Sets(first, changed, first, changed), completion);

        var result = await coordinator.AnswerAsync(new("campaign-fixture", "archive ledger"));

        Assert.Equal("KNOWLEDGE_INPUT_STALE", result.ErrorCode);
        Assert.Equal(2, completion.Calls);
    }

    [Fact]
    public void Opt_in_registration_resolves_only_with_explicit_host_owners()
    {
        using var fixture = new KnowledgeFixture();
        var services = new ServiceCollection();
        services.AddSingleton<IStateSpaceRegistry>(fixture.StateSpaces);
        services.AddSingleton<IEntityComponentStore>(fixture.Entities);
        services.AddSingleton<IStateSpaceEdgeStore>(fixture.Edges);
        services.AddSingleton<IAuthorizedKnowledgeAudiencePolicy>(new Policy(
            new(new("principal", fixture.Campaign, KnowledgeAudienceRole.Actor,
                fixture.Actor, "policy.1"))));
        services.AddSingleton<IKnowledgeApplicationBindingResolver>(new Binding(fixture.Binding));
        services.AddSingleton<IKnowledgeActorParticipationVerifier>(new Participation());
        services.AddSingleton<IApplicationEcsEffectApplier>(new RecordingEffects());
        services.AddSingleton<ILocalStructuredCompletionProvider>(new Completion("{}"));
        services.AddAuthorizedKnowledgeCore();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAuthorizedKnowledgeCoordinator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAuthorizedKnowledgeNotebookReader>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IReviewedKnowledgeStateSynchronizer>());
    }

    [Fact]
    public async Task Notebook_lists_only_effective_actor_knowledge_and_never_returns_ids()
    {
        using var fixture = new KnowledgeFixture();
        await fixture.AddCoreAsync();
        await fixture.AddKnowledgeAsync("known", "The archive ledger records old tolls.");
        await fixture.AddKnowledgeAsync("hidden", "The archive ledger names the spy.");
        await fixture.AddKnowledgeAsync("familiar", "The brass seal marks the royal archive.");
        await fixture.RelateAsync(fixture.Actor, "known", fixture.Binding.ExplicitStateRelationshipKind,
            "{\"stance\":\"known\"}");
        await fixture.RelateAsync(fixture.Actor, "hidden", fixture.Binding.ExplicitStateRelationshipKind,
            "{\"stance\":\"unknown\"}");
        await fixture.RelateAsync(fixture.Actor, "familiar", fixture.Binding.ExplicitStateRelationshipKind,
            "{\"stance\":\"familiar\"}");
        var reader = new AuthorizedKnowledgeNotebookReader(
            new Policy(new(new("principal", fixture.Campaign, KnowledgeAudienceRole.Actor,
                fixture.Actor, "policy.1"))),
            new Binding(fixture.Binding), new Participation(), fixture.Source, fixture.States,
            new DeterministicKnowledgeLexicalRetriever());

        var result = await reader.ReadAsync(new(fixture.Campaign));

        Assert.Equal("ready", result.Status);
        Assert.Equal(2, result.Entries.Count);
        Assert.Contains(result.Entries, value => value.Text.Contains("old tolls", StringComparison.Ordinal));
        Assert.Contains(result.Entries, value => value.Stance == "familiar" &&
            value.Text == "You recognize this as a familiar topic, but do not remember details.");
        Assert.Empty(result.Locations);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain("hidden", json, StringComparison.Ordinal);
        Assert.DoesNotContain("names the spy", json, StringComparison.Ordinal);
        Assert.DoesNotContain("KnowledgeId", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notebook_accepts_complete_world_sized_pages_and_rejects_oversized_requests()
    {
        using var fixture = new KnowledgeFixture();
        await fixture.AddCoreAsync();
        var reader = new AuthorizedKnowledgeNotebookReader(
            new Policy(new(new("principal", fixture.Campaign, KnowledgeAudienceRole.Actor,
                fixture.Actor, "policy.1"))),
            new Binding(fixture.Binding), new Participation(), fixture.Source, fixture.States,
            new DeterministicKnowledgeLexicalRetriever());

        var completePage = await reader.ReadAsync(new(fixture.Campaign, Limit: 200));
        var oversizedPage = await reader.ReadAsync(new(fixture.Campaign, Limit: 201));

        Assert.NotEqual("invalid", completePage.Status);
        Assert.Equal("invalid", oversizedPage.Status);
        Assert.Equal("INVALID_KNOWLEDGE_REQUEST", oversizedPage.ErrorCode);
    }

    [Fact]
    public async Task Notebook_groups_only_known_active_location_subjects_without_ids()
    {
        using var fixture = new KnowledgeFixture();
        await fixture.AddCoreAsync();
        await fixture.AddActiveLocationAsync("market", "Market");
        await fixture.AddKnowledgeAsync("known", "The market archive records old tolls.", "market");
        await fixture.AddKnowledgeAsync("familiar", "The market seal is familiar.", "market");
        await fixture.RelateAsync(fixture.Actor, "known", fixture.Binding.ExplicitStateRelationshipKind,
            "{\"stance\":\"known\"}");
        await fixture.RelateAsync(fixture.Actor, "familiar", fixture.Binding.ExplicitStateRelationshipKind,
            "{\"stance\":\"familiar\"}");
        var reader = new AuthorizedKnowledgeNotebookReader(
            new Policy(new(new("principal", fixture.Campaign, KnowledgeAudienceRole.Actor,
                fixture.Actor, "policy.1"))),
            new Binding(fixture.Binding), new Participation(), fixture.Source, fixture.States,
            new DeterministicKnowledgeLexicalRetriever());

        var result = await reader.ReadAsync(new(fixture.Campaign));

        var location = Assert.Single(result.Locations);
        Assert.Equal("Market", location.Name);
        var entry = Assert.Single(location.Entries);
        Assert.Contains("old tolls", entry.Text, StringComparison.Ordinal);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Locations);
        Assert.DoesNotContain("SubjectId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("market seal", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reviewed_sync_dry_runs_then_atomically_applies_and_replays_exact_manifest()
    {
        using var fixture = new KnowledgeFixture();
        await fixture.AddCoreAsync();
        await fixture.AddParticipationAsync();
        await fixture.AddKnowledgeAsync("known", "The archive ledger records old tolls.");
        var policy = new Policy(new(new("principal", fixture.Campaign, KnowledgeAudienceRole.Actor,
            fixture.Actor, "policy.1")));
        var applier = new ApplicationEcsEffectApplier(fixture.Db, fixture.Entities,
            fixture.StateSpaces, new OperationLog(fixture.Db), fixture.Edges);
        var synchronization = new ReviewedKnowledgeStateSynchronizer(policy,
            new Binding(fixture.Binding),
            new ApplicationKnowledgeActorParticipationVerifier(fixture.Entities, fixture.Edges),
            fixture.Source, fixture.Edges, applier);
        var request = new ReviewedKnowledgeStateSyncRequest("reviewed.fixture.1", fixture.Campaign,
            [new("known", "known")]);

        var preview = await synchronization.SynchronizeAsync(request, dryRun: true);
        var before = await fixture.Edges.ListRelationshipsAsync(fixture.Campaign);
        var applied = await synchronization.SynchronizeAsync(request, dryRun: false);
        var after = await fixture.Edges.ListRelationshipsAsync(fixture.Campaign);
        var replay = await synchronization.SynchronizeAsync(request, dryRun: false);

        Assert.True(preview.Accepted);
        Assert.True(preview.DryRun);
        Assert.DoesNotContain(before, value => value.QualifiedKind == fixture.Binding.ExplicitStateRelationshipKind);
        Assert.True(applied.Accepted);
        Assert.Equal(1, applied.ChangedCount);
        var state = Assert.Single(after, value =>
            value.QualifiedKind == fixture.Binding.ExplicitStateRelationshipKind);
        Assert.Equal(fixture.Actor, state.FromEntityId);
        Assert.Equal("known", state.ToEntityId);
        Assert.Equal("{\"stance\":\"known\"}", state.DataJson);
        Assert.True(replay.Accepted);
        Assert.True(replay.Replayed);
        Assert.Equal(applied.OperationId, replay.OperationId);
    }

    private static CanonicalKnowledgeDocument Document(string id, string text) => new(
        id, "world", "statement", "active", false, "subject", "Subject", false, null, null,
        text, text, "statement", "revision");

    private static AuthorizedKnowledgeCandidateSet Set(
        IReadOnlyList<AuthorizedKnowledgeCandidate> candidates) =>
        new(true, true, "policy.1", "scope.1", candidates, false);

    private sealed class Policy(KnowledgeAudienceResolution resolution) : IAuthorizedKnowledgeAudiencePolicy
    {
        public int Calls { get; private set; }
        public Task<KnowledgeAudienceResolution> ResolveAsync(string campaignId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(resolution);
        }
    }

    private sealed class Binding(KnowledgeApplicationBinding value) : IKnowledgeApplicationBindingResolver
    {
        public Task<KnowledgeApplicationBinding?> ResolveAsync(string campaignId,
            CancellationToken cancellationToken = default) => Task.FromResult<KnowledgeApplicationBinding?>(value);
    }

    private sealed class Participation : IKnowledgeActorParticipationVerifier
    {
        public Task<KnowledgeParticipationResolution> ResolveAsync(KnowledgeApplicationBinding binding,
            string actorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new KnowledgeParticipationResolution(true, "participation.1"));
    }

    private sealed class RecordingEffects : IApplicationEcsEffectApplier
    {
        public Task<ApplicationEcsEffectResult> ApplyAsync(ApplicationEcsEffectBatch batch,
            bool dryRun = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationEcsEffectResult(!dryRun, dryRun, new string('a', 32), [], []));
    }

    private sealed class UntouchedDependencies :
        IKnowledgeApplicationBindingResolver,
        IKnowledgeActorParticipationVerifier,
        IKnowledgeCanonicalSource,
        IKnowledgeEffectiveStateResolver,
        IKnowledgeLexicalRetriever
    {
        public int Calls { get; private set; }
        private T Touch<T>() { Calls++; throw new InvalidOperationException("Must not be touched."); }
        public Task<KnowledgeApplicationBinding?> ResolveAsync(string campaignId,
            CancellationToken cancellationToken = default) => Touch<Task<KnowledgeApplicationBinding?>>();
        public Task<KnowledgeParticipationResolution> ResolveAsync(KnowledgeApplicationBinding binding,
            string actorId, CancellationToken cancellationToken = default) => Touch<Task<KnowledgeParticipationResolution>>();
        public Task<KnowledgeCampaignScope?> ReadCampaignScopeAsync(KnowledgeApplicationBinding binding,
            CancellationToken cancellationToken = default) => Touch<Task<KnowledgeCampaignScope?>>();
        public Task<KnowledgeCampaignProjection?> ReadWorldAsync(KnowledgeApplicationBinding binding,
            KnowledgeCampaignScope scope, CancellationToken cancellationToken = default) =>
            Touch<Task<KnowledgeCampaignProjection?>>();
        public Task<CanonicalKnowledgeDocument?> ReadDocumentAsync(KnowledgeApplicationBinding binding,
            string worldId, string knowledgeId, CancellationToken cancellationToken = default) =>
            Touch<Task<CanonicalKnowledgeDocument?>>();
        public Task<IReadOnlyDictionary<string, EffectiveKnowledgeState>> ResolveAllAsync(
            KnowledgeApplicationBinding binding, string actorId, string worldId,
            IReadOnlyList<string> knowledgeIds, CancellationToken cancellationToken = default) =>
            Touch<Task<IReadOnlyDictionary<string, EffectiveKnowledgeState>>>();
        public IReadOnlyList<KnowledgeLexicalHit> Search(IReadOnlyList<CanonicalKnowledgeDocument> documents,
            KnowledgeLexicalRequest request) => Touch<IReadOnlyList<KnowledgeLexicalHit>>();
    }

    private sealed class Sets(params AuthorizedKnowledgeCandidateSet[] values) : IAuthorizedKnowledgeCandidateResolver
    {
        private int _index;
        public Task<AuthorizedKnowledgeCandidateSet> ResolveAsync(AuthorizedKnowledgeRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(values[Math.Min(_index++, values.Length - 1)]);
    }

    private sealed class Completion(string json) : ILocalStructuredCompletionProvider
    {
        public int Calls { get; private set; }
        public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalModelStatus(true, new("test", "model", "v1")));
        public Task<StructuredCompletionResult> CompleteAsync(StructuredCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new StructuredCompletionResult(new("test", "model", "v1"), json, 1));
        }
    }

    private sealed class BindingDocumentReader(ActivatedApplicationTextDocument document)
        : IActivatedApplicationDocumentReader
    {
        public ActivatedApplicationTextDocument? ReadText(
            ApplicationIdentifier applicationId,
            string relativePath) =>
            applicationId == document.ApplicationId && relativePath == document.RelativePath
                ? document : null;
    }

    private sealed class KnowledgeFixture : IDisposable
    {
        private const string Application = "fixture-knowledge";
        private static readonly string Manifest = new('A', 64);
        private readonly SqliteFixture _database = new();
        private readonly DantesRoleplayDbContext _db;
        private readonly Dictionary<string, EcsComponentReference> _types = [];

        public string Campaign => "campaign-fixture";
        public string World => "world-fixture";
        public string Actor => "actor-fixture";
        public string Participation => "participation-fixture";
        public string ApplicationId => Application;
        public IStateSpaceRegistry StateSpaces { get; }
        public IEntityComponentStore Entities { get; }
        public IStateSpaceEdgeStore Edges { get; }
        public KnowledgeApplicationBinding Binding { get; }
        public IKnowledgeCanonicalSource Source { get; }
        public IKnowledgeEffectiveStateResolver States { get; }
        public DantesRoleplayDbContext Db => _db;

        public KnowledgeFixture()
        {
            _db = _database.CreateContext();
            var applications = new SqliteApplicationRegistry(_db);
            var application = ApplicationIdentifier.Parse(Application);
            var revision = applications.Register(new(application, Application, "", []));
            StateSpaces = new SqliteStateSpaceRegistry(_db, applications);
            StateSpaces.Create(new(Campaign, revision, Manifest));
            var schemas = new BoundedJsonSchemaValidator();
            var registry = new SqliteComponentTypeRegistry(_db, schemas);
            Entities = new SqliteEntityComponentStore(_db, registry, schemas);
            Edges = new SqliteStateSpaceEdgeStore(_db, StateSpaces);
            Binding = CreateBinding();
            foreach (var typeId in ComponentTypeIds(Binding))
            {
                var type = registry.Define(new(application, typeId, "true"));
                _types.Add(typeId, new(type.QualifiedId, type.Version, type.SchemaHash));
            }
            Source = new ApplicationKnowledgeCanonicalSource(StateSpaces, Entities, Edges);
            States = new ApplicationKnowledgeEffectiveStateResolver(Entities, Edges);
        }

        public async Task AddCoreAsync()
        {
            await EntityAsync(Campaign, "Campaign");
            await EntityAsync(World, "World");
            await EntityAsync(Actor, "Actor");
            await ComponentAsync(Campaign, Binding.CampaignRootComponentTypeId, "{\"state\":\"ready\"}");
            await ComponentAsync(World, Binding.WorldRootComponentTypeId, "{\"state\":\"ready\"}");
            await ComponentAsync(World, Binding.WorldClockComponentTypeId, "{\"minute\":10}");
            await RelateAsync(Campaign, World, Binding.CampaignWorldRelationshipKind, "{}");
        }

        public async Task AddKnowledgeAsync(string id, string text, string? subjectId = null)
        {
            var subject = subjectId ?? $"subject-{id}";
            if (subjectId is null) await EntityAsync(subject, $"Subject {id}");
            await EntityAsync(id, $"Record {id}");
            await ComponentAsync(id, Binding.KnowledgeKinds[0].ComponentTypeId,
                $"{{\"state\":\"active\",\"text\":{System.Text.Json.JsonSerializer.Serialize(text)}}}");
            await ComponentAsync(id, Binding.ClassificationComponentTypeId, "{\"level\":\"reviewed\"}");
            await RelateAsync(id, World, Binding.KnowledgeWorldRelationshipKind, "{}");
            await RelateAsync(id, subject, Binding.KnowledgeAboutRelationshipKind, "{}");
        }

        public async Task AddActiveLocationAsync(string id, string name)
        {
            await EntityAsync(id, name);
            await ComponentAsync(id, Binding.LocationComponentTypeId,
                "{\"state\":\"ready\",\"kind\":\"region\"}");
        }

        public Task AddEntityAsync(string id, string name) => EntityAsync(id, name);

        public async Task AddParticipationAsync(string status = "ready")
        {
            await EntityAsync(Participation, "Fixture participation");
            await ComponentAsync(Participation, Binding.ParticipationComponentTypeId,
                System.Text.Json.JsonSerializer.Serialize(new { state = status }));
            await RelateAsync(Campaign, Participation,
                Binding.CampaignParticipationRelationshipKind, "{}");
            await RelateAsync(Participation, Actor,
                Binding.ParticipationActorRelationshipKind, "{}");
        }

        public string BindingDocument()
        {
            var value = Binding;
            var kinds = value.KnowledgeKinds.Select(kind => (object)new Dictionary<string, object?>
            {
                ["componentTypeId"] = kind.ComponentTypeId,
                ["kind"] = kind.Kind,
                ["presentationKind"] = kind.PresentationKind,
                ["archivedStatuses"] = kind.ArchivedStatuses
            }).ToArray();
            var binding = new Dictionary<string, object?>
            {
                ["campaignRootComponentTypeId"] = value.CampaignRootComponentTypeId,
                ["campaignStatusProperty"] = value.CampaignStatusProperty,
                ["activeCampaignStatus"] = value.ActiveCampaignStatus,
                ["campaignWorldRelationshipKind"] = value.CampaignWorldRelationshipKind,
                ["participationComponentTypeId"] = value.ParticipationComponentTypeId,
                ["participationStatusProperty"] = value.ParticipationStatusProperty,
                ["activeParticipationStatus"] = value.ActiveParticipationStatus,
                ["campaignParticipationRelationshipKind"] = value.CampaignParticipationRelationshipKind,
                ["participationActorRelationshipKind"] = value.ParticipationActorRelationshipKind,
                ["worldRootComponentTypeId"] = value.WorldRootComponentTypeId,
                ["worldStatusProperty"] = value.WorldStatusProperty,
                ["activeWorldStatus"] = value.ActiveWorldStatus,
                ["worldClockComponentTypeId"] = value.WorldClockComponentTypeId,
                ["currentMinuteProperty"] = value.CurrentMinuteProperty,
                ["knowledgeKinds"] = kinds,
                ["primaryStatusProperty"] = value.PrimaryStatusProperty,
                ["primarySummaryProperty"] = value.PrimarySummaryProperty,
                ["classificationComponentTypeId"] = value.ClassificationComponentTypeId,
                ["classificationSensitivityProperty"] = value.ClassificationSensitivityProperty,
                ["validityComponentTypeId"] = value.ValidityComponentTypeId,
                ["validFromProperty"] = value.ValidFromProperty,
                ["validUntilProperty"] = value.ValidUntilProperty,
                ["knowledgeWorldRelationshipKind"] = value.KnowledgeWorldRelationshipKind,
                ["knowledgeAboutRelationshipKind"] = value.KnowledgeAboutRelationshipKind,
                ["explicitStateRelationshipKind"] = value.ExplicitStateRelationshipKind,
                ["baselineRelationshipKind"] = value.BaselineRelationshipKind,
                ["stateProperty"] = value.StateProperty,
                ["baselineInheritanceProperty"] = value.BaselineInheritanceProperty,
                ["baselineInheritanceValue"] = value.BaselineInheritanceValue,
                ["contentStates"] = value.ContentStates,
                ["familiarState"] = value.FamiliarState,
                ["unknownState"] = value.UnknownState,
                ["baselineState"] = value.BaselineState,
                ["factionComponentTypeId"] = value.FactionComponentTypeId,
                ["factionStatusProperty"] = value.FactionStatusProperty,
                ["activeFactionStatus"] = value.ActiveFactionStatus,
                ["factionWorldRelationshipKind"] = value.FactionWorldRelationshipKind,
                ["factionMemberRelationshipKind"] = value.FactionMemberRelationshipKind,
                ["locationComponentTypeId"] = value.LocationComponentTypeId,
                ["locationStatusProperty"] = value.LocationStatusProperty,
                ["activeLocationStatus"] = value.ActiveLocationStatus,
                ["locationKindProperty"] = value.LocationKindProperty,
                ["regionLocationKind"] = value.RegionLocationKind
            };
            return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["format"] = "system.knowledge.binding.v1",
                ["applicationId"] = Application,
                ["binding"] = binding
            });
        }

        public async Task AddFactionAsync(string id)
        {
            await EntityAsync(id, "Guild");
            await ComponentAsync(id, Binding.FactionComponentTypeId, "{\"state\":\"ready\"}");
            await RelateAsync(id, World, Binding.FactionWorldRelationshipKind, "{}");
            await RelateAsync(id, Actor, Binding.FactionMemberRelationshipKind, "{}");
        }

        public async Task AddRegionAsync(string id)
        {
            await EntityAsync(id, "Market");
            await ComponentAsync(id, Binding.LocationComponentTypeId,
                "{\"state\":\"ready\",\"kind\":\"region\"}");
            _ = await Edges.MoveContainmentAsync(Campaign, id, World, "region", 0);
            _ = await Edges.MoveContainmentAsync(Campaign, Actor, id, "presence", 0);
        }

        public async Task RelateAsync(string from, string to, string kind, string data) =>
            _ = await Edges.SetRelationshipAsync(Campaign, from, to, kind, data, 0);

        public IAuthorizedKnowledgeCandidateResolver CandidateResolver() =>
            new AuthorizedKnowledgeCandidateResolver(
                new Policy(new(new("principal", Campaign, KnowledgeAudienceRole.Actor,
                    Actor, "policy.1"))),
                new Binding(Binding), new Participation(), Source, States,
                new DeterministicKnowledgeLexicalRetriever());

        private async Task EntityAsync(string id, string name) =>
            _ = await Entities.CreateEntityAsync(Campaign, id, name);

        private async Task ComponentAsync(string entityId, string typeId, string json) =>
            _ = await Entities.AddComponentAsync(new(Campaign, entityId, _types[typeId], json, 0));

        public void Dispose()
        {
            _db.Dispose();
            _database.Dispose();
        }

        private static IReadOnlyList<string> ComponentTypeIds(KnowledgeApplicationBinding value) =>
            new[]
            {
                value.CampaignRootComponentTypeId, value.ParticipationComponentTypeId,
                value.WorldRootComponentTypeId,
                value.WorldClockComponentTypeId, value.ClassificationComponentTypeId,
                value.ValidityComponentTypeId, value.FactionComponentTypeId,
                value.LocationComponentTypeId
            }.Concat(value.KnowledgeKinds.Select(kind => kind.ComponentTypeId))
            .Distinct(StringComparer.Ordinal).ToArray();

        private KnowledgeApplicationBinding CreateBinding() => new()
        {
            ApplicationId = Application,
            StateSpaceId = Campaign,
            CampaignEntityId = Campaign,
            BindingRevision = "binding-revision",
            CampaignRootComponentTypeId = $"{Application}.campaign-root",
            CampaignStatusProperty = "state",
            ActiveCampaignStatus = "ready",
            CampaignWorldRelationshipKind = $"{Application}.campaign-world",
            ParticipationComponentTypeId = $"{Application}.campaign-participation",
            ParticipationStatusProperty = "state",
            ActiveParticipationStatus = "ready",
            CampaignParticipationRelationshipKind = $"{Application}.campaign-participation-link",
            ParticipationActorRelationshipKind = $"{Application}.participation-actor-link",
            WorldRootComponentTypeId = $"{Application}.world-root",
            WorldStatusProperty = "state",
            ActiveWorldStatus = "ready",
            WorldClockComponentTypeId = $"{Application}.world-clock",
            CurrentMinuteProperty = "minute",
            KnowledgeKinds = [new($"{Application}.knowledge-record", "statement", "statement", ["archived"])],
            PrimaryStatusProperty = "state",
            PrimarySummaryProperty = "text",
            ClassificationComponentTypeId = $"{Application}.knowledge-classification",
            ClassificationSensitivityProperty = "level",
            ValidityComponentTypeId = $"{Application}.knowledge-validity",
            ValidFromProperty = "from",
            ValidUntilProperty = "until",
            KnowledgeWorldRelationshipKind = $"{Application}.knowledge-world",
            KnowledgeAboutRelationshipKind = $"{Application}.knowledge-about",
            ExplicitStateRelationshipKind = $"{Application}.knowledge-state",
            BaselineRelationshipKind = $"{Application}.knowledge-baseline",
            StateProperty = "stance",
            BaselineInheritanceProperty = "inheritance",
            BaselineInheritanceValue = "current",
            ContentStates = ["known", "suspected", "believed", "doubted", "disbelieved"],
            FamiliarState = "familiar",
            UnknownState = "unknown",
            BaselineState = "known",
            FactionComponentTypeId = $"{Application}.faction",
            FactionStatusProperty = "state",
            ActiveFactionStatus = "ready",
            FactionWorldRelationshipKind = $"{Application}.faction-world",
            FactionMemberRelationshipKind = $"{Application}.faction-member",
            LocationComponentTypeId = $"{Application}.location",
            LocationStatusProperty = "state",
            ActiveLocationStatus = "ready",
            LocationKindProperty = "kind",
            RegionLocationKind = "region"
        };
    }
}
