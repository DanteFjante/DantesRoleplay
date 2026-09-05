using System.Net;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class WorldChronologyWebEndpointTests
{
    [Fact]
    public void Separate_chronology_binding_is_closed_and_valid()
    {
        var path = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024", "metadata",
            "world-chronology.json");

        Assert.True(WorldChronologyBindingDocument.TryParse(
            File.ReadAllText(path), "dnd2024", out var binding));
        Assert.Equal("game.core.world.chronology", binding.ComponentTypeId);
        Assert.Equal(["exact", "approximate", "era"], binding.Precisions);
    }

    [Fact]
    public async Task Chronology_vocabulary_resolves_only_from_its_activated_document()
    {
        var selection = new WorldChronologyApplicationSelection("dnd2024");
        var applicationId = ApplicationIdentifier.Parse("dnd2024");
        var path = Path.Combine(RepositoryRoot(), selection.BindingDocumentPath.Replace('/', Path.DirectorySeparatorChar));
        var document = new ActivatedApplicationTextDocument(
            applicationId, 16, new('A', 64), "dnd2024-core", selection.BindingDocumentPath,
            new('B', 64), File.ReadAllText(path), ["dnd2024-core"]);
        var resolver = new ActivatedWorldChronologyBindingResolver(selection, new Documents(document));
        var unavailable = new ActivatedWorldChronologyBindingResolver(selection, new Documents(null));

        var resolved = await resolver.ResolveAsync(Binding());

        Assert.NotNull(resolved);
        Assert.Equal("game.core.world.chronology", resolved.ComponentTypeId);
        Assert.Null(await unavailable.ResolveAsync(Binding()));
    }

    [Fact]
    public async Task Player_projection_orders_public_and_party_records_without_identifiers_or_subjects()
    {
        var fixture = Fixture();

        var response = await ReadAsync(fixture, ActorSeat(), "player");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("ready", response.Body.GetProperty("status").GetString());
        Assert.Equal("player", response.Body.GetProperty("perspective").GetString());
        var entries = response.Body.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(["Party charter", "Public dedication"],
            entries.Select(value => value.GetProperty("title").GetString()!).ToArray());
        Assert.Equal(["chronology-1", "chronology-2"],
            entries.Select(value => value.GetProperty("id").GetString()!).ToArray());
        Assert.All(entries, value =>
        {
            Assert.False(value.TryGetProperty("subjects", out _));
            Assert.False(value.TryGetProperty("visibility", out _));
        });
        var serialized = response.Body.GetRawText();
        Assert.DoesNotContain("chronology.a", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("chronology.b", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("GM canary", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Archived canary", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Game_master_projection_includes_gm_records_and_same_world_subjects()
    {
        var fixture = Fixture();

        var response = await ReadAsync(fixture, GameMasterSeat(), "dm");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var entries = response.Body.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(["Party charter", "Public dedication", "GM canary"],
            entries.Select(value => value.GetProperty("title").GetString()!).ToArray());
        var publicEntry = entries.Single(value =>
            value.GetProperty("title").GetString() == "Public dedication");
        var subject = Assert.Single(publicEntry.GetProperty("subjects").EnumerateArray());
        Assert.Equal("location.fixture", subject.GetProperty("id").GetString());
        Assert.Equal("Fixture Gate", subject.GetProperty("name").GetString());
        Assert.DoesNotContain("Archived canary", response.Body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Game_master_may_read_a_selected_campaign_while_actor_remains_bound()
    {
        var fixture = Fixture();

        var gameMaster = await ReadAsync(fixture, GameMasterSeat("campaign.bound"), "dm");
        var actor = await ReadAsync(fixture, ActorSeat("campaign.bound"), "player");

        Assert.Equal(StatusCodes.Status200OK, gameMaster.StatusCode);
        Assert.Equal("ready", gameMaster.Body.GetProperty("status").GetString());
        Assert.Equal(StatusCodes.Status403Forbidden, actor.StatusCode);
        Assert.Equal("CHRONOLOGY_UNAVAILABLE", actor.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Actor_cannot_request_dm_projection_and_no_ecs_state_is_read()
    {
        var fixture = Fixture();

        var response = await ReadAsync(fixture, ActorSeat(), "dm");

        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
        Assert.Equal(0, fixture.Entities.ListCalls);
        Assert.Equal(0, fixture.Edges.ReadCalls);
        Assert.Equal("CHRONOLOGY_UNAVAILABLE", response.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Cross_world_subject_fails_closed_without_partial_entries()
    {
        var fixture = Fixture(subjectContainerId: "world.other");

        var response = await ReadAsync(fixture, ActorSeat(), "player");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("CHRONOLOGY_UNAVAILABLE", response.Body.GetProperty("error").GetString());
        Assert.False(response.Body.TryGetProperty("entries", out _));
        Assert.DoesNotContain("Public dedication", response.Body.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(497, 200)]
    [InlineData(498, 503)]
    public async Task Expanded_chronology_preserves_order_and_enforces_its_bound(int additional, int expectedStatus)
    {
        var fixture = Fixture();
        for (var index = 0; index < additional; index++)
        {
            var id = $"chronology.extra.{index:D4}";
            fixture.Entities.AddEntity(id, $"Historical entry {index}");
            fixture.Entities.AddComponent(id, fixture.Chronology.ComponentTypeId,
                Chronology("active", $"Historical entry {index}", -additional + index, "public"));
            fixture.Edges.Add(Relationship(id, "world.fixture", fixture.Chronology.InWorldRelationshipKind));
        }
        var response = await ReadAsync(fixture, GameMasterSeat(), "dm");
        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus == 200)
        {
            var entries = response.Body.GetProperty("entries").EnumerateArray().ToArray();
            Assert.Equal(500, entries.Length);
            Assert.Equal("Historical entry 0", entries[0].GetProperty("title").GetString());
            Assert.Equal("GM canary", entries[^1].GetProperty("title").GetString());
            Assert.DoesNotContain("Archived canary", response.Body.GetRawText(), StringComparison.Ordinal);
        }
        else Assert.False(response.Body.TryGetProperty("entries", out _));
    }

    private static async Task<(int StatusCode, JsonElement Body)> ReadAsync(
        ChronologyFixture fixture,
        LocalKnowledgeSeatSnapshot seat,
        string perspective)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Response.Body = new MemoryStream();
        context.RequestServices = JsonResultServices();
        var result = await WorldChronologyWebEndpoint.ReadAsync(
            "dnd2024", "campaign.fixture", perspective, context,
            new Seats(seat), new Audience(seat.Role), new Bindings(fixture.Binding),
            new ChronologyBindings(fixture.Chronology), new Participation(), fixture.Entities,
            fixture.Edges, CancellationToken.None);
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }

    private static ChronologyFixture Fixture(string subjectContainerId = "world.fixture")
    {
        var binding = Binding();
        var entities = new Entities();
        entities.AddEntity("world.fixture", "Fixture World");
        entities.AddEntity("world.other", "Other World");
        entities.AddEntity("location.fixture", "Fixture Gate");
        entities.AddEntity("chronology.a", "Party charter");
        entities.AddEntity("chronology.b", "Public dedication");
        entities.AddEntity("chronology.c", "GM canary");
        entities.AddEntity("chronology.d", "Archived canary");
        entities.AddEntity("chronology.foreign", "Foreign world event");
        entities.AddComponent("world.fixture", binding.WorldRootComponentTypeId,
            new { status = binding.ActiveWorldStatus });
        entities.AddComponent("world.fixture", binding.WorldClockComponentTypeId,
            new { calendarId = "fixture-calendar", currentMinute = 100, revision = 1 });
        var chronologyBinding = ChronologyBinding();
        entities.AddComponent("chronology.a", chronologyBinding.ComponentTypeId,
            Chronology("active", "Party charter", 10, "party"));
        entities.AddComponent("chronology.b", chronologyBinding.ComponentTypeId,
            Chronology("active", "Public dedication", 10, "public"));
        entities.AddComponent("chronology.c", chronologyBinding.ComponentTypeId,
            Chronology("active", "GM canary", 20, "gm"));
        entities.AddComponent("chronology.d", chronologyBinding.ComponentTypeId,
            Chronology("archived", "Archived canary", 30, "public"));
        entities.AddComponent("chronology.foreign", chronologyBinding.ComponentTypeId, new
        {
            status = "active",
            title = "Foreign world event",
            summary = "This belongs to another world and calendar.",
            calendarId = "foreign-calendar",
            occurredAtMinute = 40,
            precision = "exact",
            dateLabel = "Foreign date",
            visibility = "public"
        });

        var edges = new Edges([
            Relationship("campaign.fixture", "world.fixture", binding.CampaignWorldRelationshipKind),
            Relationship("chronology.a", "world.fixture", chronologyBinding.InWorldRelationshipKind),
            Relationship("chronology.b", "world.fixture", chronologyBinding.InWorldRelationshipKind),
            Relationship("chronology.c", "world.fixture", chronologyBinding.InWorldRelationshipKind),
            Relationship("chronology.foreign", "world.other", chronologyBinding.InWorldRelationshipKind),
            Relationship("chronology.b", "location.fixture", chronologyBinding.AboutRelationshipKind),
        ], [Containment("location.fixture", subjectContainerId)]);
        return new(binding, chronologyBinding, entities, edges);
    }

    private static object Chronology(string status, string title, long minute, string visibility) => new
    {
        status,
        title,
        summary = $"{title} summary.",
        calendarId = "fixture-calendar",
        occurredAtMinute = minute,
        precision = "exact",
        dateLabel = $"Minute {minute}",
        visibility
    };

    private static EcsRelationshipView Relationship(string from, string to, string kind) => new(
        "state-space.fixture", from, to, kind, "{}", 1, DateTime.UnixEpoch, DateTime.UnixEpoch);

    private static EcsContainmentView Containment(string child, string parent) => new(
        "state-space.fixture", child, parent, "location", 1, DateTime.UnixEpoch, DateTime.UnixEpoch);

    private static KnowledgeApplicationBinding Binding()
    {
        var path = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024", "metadata",
            "authorized-knowledge.json");
        Assert.True(KnowledgeApplicationBindingDocument.TryParse(
            File.ReadAllText(path), "dnd2024", out var document));
        return document.Bind("dnd2024", "state-space.fixture", "campaign.fixture", "binding.fixture");
    }

    private static WorldChronologyBinding ChronologyBinding()
    {
        var path = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024", "metadata",
            "world-chronology.json");
        Assert.True(WorldChronologyBindingDocument.TryParse(
            File.ReadAllText(path), "dnd2024", out var binding));
        return binding;
    }

    private static LocalKnowledgeSeatSnapshot ActorSeat(string campaignId = "campaign.fixture") => new(
        true, "principal.fixture", "dnd2024", campaignId, "actor.fixture",
        SourceIds: ["dnd2024-core"]);

    private static LocalKnowledgeSeatSnapshot GameMasterSeat(string campaignId = "campaign.fixture") => new(
        true, "principal.fixture", "dnd2024", campaignId, null,
        KnowledgeAudienceRole.GameMaster, ["dnd2024-core"]);

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
            directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static IServiceProvider JsonResultServices() => new ServiceCollection()
        .AddLogging()
        .AddOptions()
        .Configure<JsonOptions>(_ => { })
        .BuildServiceProvider();

    private sealed record ChronologyFixture(
        KnowledgeApplicationBinding Binding,
        WorldChronologyBinding Chronology,
        Entities Entities,
        Edges Edges);

    private sealed class Seats(LocalKnowledgeSeatSnapshot value) : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => value;
    }

    private sealed class Audience(KnowledgeAudienceRole role) : IAuthorizedKnowledgeAudiencePolicy
    {
        public Task<KnowledgeAudienceResolution> ResolveAsync(
            string campaignId,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new KnowledgeAudienceResolution(new("principal.fixture", campaignId, role,
                    role == KnowledgeAudienceRole.Actor ? "actor.fixture" : null, "policy.fixture")));
    }

    private sealed class Bindings(KnowledgeApplicationBinding value) : IKnowledgeApplicationBindingResolver
    {
        public Task<KnowledgeApplicationBinding?> ResolveAsync(
            string campaignId,
            CancellationToken cancellationToken = default) => Task.FromResult<KnowledgeApplicationBinding?>(value);
    }

    private sealed class ChronologyBindings(WorldChronologyBinding value) : IWorldChronologyBindingResolver
    {
        public Task<WorldChronologyBinding?> ResolveAsync(
            KnowledgeApplicationBinding binding,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<WorldChronologyBinding?>(value);
    }

    private sealed class Documents(ActivatedApplicationTextDocument? value)
        : IActivatedApplicationDocumentReader
    {
        public ActivatedApplicationTextDocument? ReadText(
            ApplicationIdentifier applicationId,
            string relativePath) => value;
    }

    private sealed class Participation : IKnowledgeActorParticipationVerifier
    {
        public Task<KnowledgeParticipationResolution> ResolveAsync(
            KnowledgeApplicationBinding binding,
            string actorId,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new KnowledgeParticipationResolution(true, "participation.fixture"));
    }

    private sealed class Entities : IEntityComponentStore
    {
        private static readonly EcsComponentReference Reference = new("fixture", 1, new string('0', 64));
        private readonly Dictionary<string, EcsEntityView> _entities = new(StringComparer.Ordinal);
        private readonly Dictionary<(string EntityId, string TypeId), EcsComponentView> _components = new();
        public int ListCalls { get; private set; }

        public void AddEntity(string id, string name) => _entities[id] = new(
            "state-space.fixture", id, name, 1, DateTime.UnixEpoch, null);

        public void AddComponent(string entityId, string typeId, object value) =>
            _components[(entityId, typeId)] = new("state-space.fixture", entityId,
                Reference with { QualifiedTypeId = typeId }, JsonSerializer.Serialize(value), 1,
                DateTime.UnixEpoch, DateTime.UnixEpoch);

        public Task<EcsEntityView?> GetEntityAsync(string stateSpaceId, string entityId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_entities.GetValueOrDefault(entityId));

        public Task<EcsEntityDiscoveryPage> ListEntitiesAsync(string stateSpaceId, string? afterEntityId,
            int limit, CancellationToken cancellationToken = default)
        {
            ListCalls++;
            var values = _entities.Values.OrderBy(value => value.EntityId, StringComparer.Ordinal)
                .Where(value => afterEntityId is null || StringComparer.Ordinal.Compare(value.EntityId, afterEntityId) > 0)
                .Take(limit).ToArray();
            var hasMore = values.Length > 0 && _entities.Keys.Any(id =>
                StringComparer.Ordinal.Compare(id, values[^1].EntityId) > 0);
            return Task.FromResult(new EcsEntityDiscoveryPage(values, hasMore ? values[^1].EntityId : null));
        }

        public Task<EcsComponentView?> GetComponentAsync(string stateSpaceId, string entityId,
            string qualifiedTypeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_components.GetValueOrDefault((entityId, qualifiedTypeId)));

        public Task<EcsEntityView> CreateEntityAsync(string stateSpaceId, string entityId, string name,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteEntityAsync(string stateSpaceId, string entityId, int expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public Task<bool> RemoveComponentAsync(string stateSpaceId, string entityId,
            EcsComponentReference type, int expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Edges(
        IReadOnlyList<EcsRelationshipView> relationships,
        IReadOnlyList<EcsContainmentView> containments) : IStateSpaceEdgeStore
    {
        public int ReadCalls { get; private set; }
        private readonly List<EcsRelationshipView> _relationships = relationships.ToList();
        public void Add(EcsRelationshipView value) => _relationships.Add(value);

        public Task<IReadOnlyList<EcsContainmentView>> ListContainmentsAsync(
            string stateSpaceId, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(containments);
        }

        public Task<IReadOnlyList<EcsRelationshipView>> ListRelationshipsAsync(
            string stateSpaceId, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult<IReadOnlyList<EcsRelationshipView>>(_relationships);
        }

        public Task<EcsContainmentView?> GetContainmentAsync(string stateSpaceId, string containedEntityId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsContainmentDiscoveryPage> ListContainmentsAsync(string stateSpaceId,
            string containerEntityId, string? afterContainedEntityId, int limit,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsContainmentView> MoveContainmentAsync(string stateSpaceId, string containedEntityId,
            string containerEntityId, string slot, int expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveContainmentAsync(string stateSpaceId, string containedEntityId,
            int expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsRelationshipView?> GetRelationshipAsync(string stateSpaceId, string fromEntityId,
            string toEntityId, string qualifiedKind, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsRelationshipView> SetRelationshipAsync(string stateSpaceId, string fromEntityId,
            string toEntityId, string qualifiedKind, string dataJson, int expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveRelationshipAsync(string stateSpaceId, string fromEntityId,
            string toEntityId, string qualifiedKind, int expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
