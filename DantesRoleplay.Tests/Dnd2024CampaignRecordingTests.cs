using DantesRoleplay.Mechanics;
using DantesRoleplay.Effects;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024CampaignRecordingTests
{
    private const string Campaign = "campaign.fixture";
    private const string World = "world.fixture";
    private const string Location = "location.fixture";
    private const string Session = "session.fixture.1";
    private const string Arc = "campaign.fixture.arc.road";
    private const string Visit = "campaign.fixture.visit.location.fixture";

    [Fact]
    public async Task Chapter_advance_closes_the_exact_active_chapter_and_opens_its_successor()
    {
        const string chapter = "campaign.fixture.chapter.opening";
        const string next = "campaign.fixture.chapter.crossing";
        var campaign = CampaignRole(
            Edge(Campaign, Arc, "game.core.campaign.has-arc"),
            Edge(Campaign, chapter, "game.core.campaign.has-chapter"));
        var arc = Role(Arc, "The Old Road", new()
        {
            ["game.core.campaign.arc"] =
                "{\"status\":\"active\",\"title\":\"The Old Road\",\"partyStake\":\"Reach the coast.\"}"
        }, Edge(Campaign, Arc, "game.core.campaign.has-arc"));
        var active = Role(chapter, "Opening", new()
        {
            ["game.core.campaign.chapter"] =
                "{\"status\":\"active\",\"title\":\"Opening\",\"partyQuestion\":\"What lit the beacon?\",\"gmContext\":\"The lens remembers.\"}"
        }, Edge(Campaign, chapter, "game.core.campaign.has-chapter"),
            Edge(chapter, Arc, "game.core.campaign.chapter.in-arc"));

        var run = await RunAsync("dnd2024.mechanic.campaign.chapter.advance.js",
            "{\"nextChapterId\":\"campaign.fixture.chapter.crossing\",\"nextTitle\":\"The Crossing\",\"nextPartyQuestion\":\"Can the party cross the reef?\",\"nextGmContext\":\"The tide is changing.\",\"closingSummary\":\"The signal was traced.\"}",
            new() { ["campaign"] = campaign, ["arc"] = arc, ["chapter"] = active });

        Assert.True(run.Ok, run.Error);
        Assert.Equal(5, run.Output.Effects.Count);
        Assert.Contains(run.Output.Effects, effect => effect.Type == "component.set" &&
            effect.EntityId == chapter && effect.Data.Contains("\"status\":\"closed\"", StringComparison.Ordinal));
        Assert.Contains(run.Output.Effects, effect => effect.Type == "entity.create" && effect.EntityId == next);
        Assert.Contains(run.Output.Effects, effect => effect.Type == "relationship.create" &&
            effect.EntityId == next && effect.ToEntityId == Arc &&
            effect.Kind == "game.core.campaign.chapter.in-arc");
    }

    [Fact]
    public async Task Chapter_advance_rejects_a_chapter_outside_the_selected_arc()
    {
        const string chapter = "campaign.fixture.chapter.opening";
        var run = await RunAsync("dnd2024.mechanic.campaign.chapter.advance.js",
            "{\"nextChapterId\":\"campaign.fixture.chapter.crossing\",\"nextTitle\":\"The Crossing\",\"nextPartyQuestion\":\"Can the party cross?\",\"nextGmContext\":\"The tide changes.\",\"closingSummary\":\"The signal was traced.\"}",
            new()
            {
                ["campaign"] = CampaignRole(
                    Edge(Campaign, Arc, "game.core.campaign.has-arc"),
                    Edge(Campaign, chapter, "game.core.campaign.has-chapter")),
                ["arc"] = Role(Arc, "Arc", new()
                {
                    ["game.core.campaign.arc"] =
                        "{\"status\":\"active\",\"title\":\"Arc\",\"partyStake\":\"Travel.\"}"
                }, Edge(Campaign, Arc, "game.core.campaign.has-arc")),
                ["chapter"] = Role(chapter, "Opening", new()
                {
                    ["game.core.campaign.chapter"] =
                        "{\"status\":\"active\",\"title\":\"Opening\",\"partyQuestion\":\"Why?\"}"
                })
            });

        Assert.False(run.Ok);
        Assert.Empty(run.Output.Effects);
    }

    [Fact]
    public async Task Current_scene_move_requires_the_campaign_world_and_adds_destination_relevance()
    {
        var campaign = Role(Campaign, "Campaign", new()
        {
            ["game.core.campaign.root"] = "{}",
            ["game.core.campaign.current-scene"] = "{\"location\":{\"entityId\":\"location.old\"}}"
        }, Edge(Campaign, World, "game.core.campaign.in-world"));
        var location = LocationRole();
        var world = new EntityProjection(World, "World", new Dictionary<string, string>
        {
            ["game.core.world.root"] = "{}"
        }, Contains: [new ContainedProjection(Location, "Brackenford", "location", location.Components)]);

        var run = await RunAsync("dnd2024.mechanic.campaign.current-scene.set.js", "{\"mode\":\"move\"}",
            new() { ["campaign"] = campaign, ["world"] = world, ["location"] = location });

        Assert.True(run.Ok, run.Error);
        Assert.Collection(run.Output.Effects,
            effect =>
            {
                Assert.Equal("relationship.create", effect.Type);
                Assert.Equal("game.core.campaign.references", effect.Kind);
                Assert.Equal(Location, effect.ToEntityId);
            },
            effect =>
            {
                Assert.Equal("component.set", effect.Type);
                Assert.Equal("game.core.campaign.current-scene", effect.DefinitionId);
            });
    }

    [Fact]
    public async Task Current_scene_move_rejects_a_destination_outside_the_campaign_world()
    {
        var campaign = Role(Campaign, "Campaign", new()
        {
            ["game.core.campaign.root"] = "{}",
            ["game.core.campaign.current-scene"] = "{\"location\":{\"entityId\":\"location.old\"}}"
        }, Edge(Campaign, World, "game.core.campaign.in-world"));
        var world = new EntityProjection(World, "World", new Dictionary<string, string>
        {
            ["game.core.world.root"] = "{}"
        });

        var run = await RunAsync("dnd2024.mechanic.campaign.current-scene.set.js", "{\"mode\":\"move\"}",
            new() { ["campaign"] = campaign, ["world"] = world, ["location"] = LocationRole() });

        Assert.False(run.Ok);
        Assert.Empty(run.Output.Effects);
    }

    [Fact]
    public async Task Companion_creation_atomically_establishes_presence_and_campaign_participation()
    {
        var campaign = Role(Campaign, "Campaign", new()
        {
            ["game.core.campaign.root"] = "{}",
            ["game.core.campaign.current-scene"] = "{\"location\":{\"entityId\":\"location.fixture\"}}"
        }, Edge(Campaign, World, "game.core.campaign.in-world"));
        var location = LocationRole();
        var world = new EntityProjection(World, "World", new Dictionary<string, string>
        {
            ["game.core.world.root"] = "{}"
        }, Contains:
        [
            new ContainedProjection(Location, "Brackenford", "location", location.Components)
        ]);

        var run = await RunAsync("dnd2024.mechanic.campaign.companion.create-and-attach.js",
            "{\"actorId\":\"actor.fixture.mira\",\"name\":\"Mira\",\"motiveSummary\":\"Mira keeps the harbor safe.\"}",
            new() { ["campaign"] = campaign, ["world"] = world, ["location"] = location });

        Assert.True(run.Ok, run.Error);
        Assert.Equal(9, run.Output.Effects.Count);
        Assert.Contains(run.Output.Effects, effect => effect.Type == "containment.move" &&
            effect.EntityId == "actor.fixture.mira" && effect.ToEntityId == Location && effect.Slot == "presence");
        Assert.Contains(run.Output.Effects, effect => effect.Type == "component.add" &&
            effect.DefinitionId == "game.core.campaign.character-participation");
        Assert.Contains(run.Output.Effects, effect => effect.Type == "relationship.create" &&
            effect.Kind == "game.core.campaign.character-participation.for-actor" &&
            effect.ToEntityId == "actor.fixture.mira");
        Assert.Contains("\"characterSheetStatus\":\"not-created\"", run.Output.Data);
    }

    [Fact]
    public async Task Companion_creation_rejects_a_location_other_than_the_current_scene()
    {
        var campaign = Role(Campaign, "Campaign", new()
        {
            ["game.core.campaign.root"] = "{}",
            ["game.core.campaign.current-scene"] = "{\"location\":{\"entityId\":\"location.other\"}}"
        }, Edge(Campaign, World, "game.core.campaign.in-world"));
        var location = LocationRole();
        var world = new EntityProjection(World, "World", new Dictionary<string, string>
        {
            ["game.core.world.root"] = "{}"
        }, Contains:
        [
            new ContainedProjection(Location, "Brackenford", "location", location.Components)
        ]);

        var run = await RunAsync("dnd2024.mechanic.campaign.companion.create-and-attach.js",
            "{\"actorId\":\"actor.fixture.mira\",\"name\":\"Mira\",\"motiveSummary\":\"Mira keeps the harbor safe.\"}",
            new() { ["campaign"] = campaign, ["world"] = world, ["location"] = location });

        Assert.False(run.Ok);
        Assert.Empty(run.Output.Effects);
    }

    [Fact]
    public async Task Ended_session_and_terminal_arc_create_only_explicit_reference_edges()
    {
        var campaign = CampaignRole(
            Edge(Campaign, Session, "game.core.campaign.has-session"),
            Edge(Campaign, Arc, "game.core.campaign.has-arc"),
            Edge(Campaign, Location, "game.core.campaign.references"));
        var target = Role(Location, "Brackenford", []);
        var session = Role(Session, "First session", new()
        {
            ["game.core.campaign.session"] = "{\"status\":\"ended\",\"ordinal\":1}",
            ["game.core.campaign.session-recap"] =
                "{\"protocolVersion\":\"session.s0.c3-only.v1\",\"chapter\":{},\"arc\":{},\"milestones\":[]}"
        }, Edge(Campaign, Session, "game.core.campaign.has-session"));
        var arc = Role(Arc, "The Old Road", new()
        {
            ["game.core.campaign.arc"] =
                "{\"status\":\"resolved\",\"title\":\"The Old Road\",\"partyStake\":\"Travel.\",\"closingSummary\":\"Open.\"}"
        }, Edge(Campaign, Arc, "game.core.campaign.has-arc"));

        var sessionRun = await RunAsync(
            "dnd2024.mechanic.campaign.session.reference-world-entity.js", "{}",
            new() { ["campaign"] = campaign, ["session"] = session, ["target"] = target });
        var arcRun = await RunAsync(
            "dnd2024.mechanic.campaign.arc.reference-world-entity.js", "{}",
            new() { ["campaign"] = campaign, ["arc"] = arc, ["target"] = target });

        Assert.True(sessionRun.Ok, sessionRun.Error);
        Assert.True(arcRun.Ok, arcRun.Error);
        Assert.Collection(sessionRun.Output.Effects, effect => Reference(effect, Session));
        Assert.Collection(arcRun.Output.Effects, effect => Reference(effect, Arc));
    }

    [Fact]
    public async Task Reference_actions_reject_active_records_and_targets_outside_campaign_relevance()
    {
        var campaign = CampaignRole(Edge(Campaign, Session, "game.core.campaign.has-session"));
        var session = Role(Session, "Session", new()
        {
            ["game.core.campaign.session"] = "{\"status\":\"active\",\"ordinal\":1}",
            ["game.core.campaign.session-recap"] =
                "{\"protocolVersion\":\"session.s0.c3-only.v1\"}"
        }, Edge(Campaign, Session, "game.core.campaign.has-session"));

        var run = await RunAsync(
            "dnd2024.mechanic.campaign.session.reference-world-entity.js", "{}",
            new() { ["campaign"] = campaign, ["session"] = session, ["target"] = Role(Location, "Place", []) });

        Assert.False(run.Ok);
        Assert.Empty(run.Output.Effects);
    }

    [Fact]
    public async Task Visit_capture_derives_identity_and_authoritative_minute_then_updates_monotonically()
    {
        var campaign = CampaignRole(
            Edge(Campaign, World, "game.core.campaign.in-world"),
            Edge(Campaign, Location, "game.core.campaign.references"));
        var roles = new Dictionary<string, EntityProjection>
        {
            ["campaign"] = campaign,
            ["world"] = WorldRole(120),
            ["location"] = LocationRole()
        };
        var input = "{\"status\":\"current\",\"summary\":\"Frontier village.\",\"memory\":\"The party earned its trust.\",\"gmContext\":null}";

        var created = await RunAsync("dnd2024.mechanic.campaign.location-visit.record.js", input, roles);

        Assert.True(created.Ok, created.Error);
        Assert.Equal(4, created.Output.Effects.Count);
        Assert.Equal(("entity.create", Visit), (created.Output.Effects[0].Type, created.Output.Effects[0].EntityId));
        Assert.Contains("\"firstVisitedMinute\":120", created.Output.Effects[1].Data);
        Assert.Contains("\"visitCount\":1", created.Output.Effects[1].Data);

        roles["campaign"] = CampaignRole(
            Edge(Campaign, World, "game.core.campaign.in-world"),
            Edge(Campaign, Location, "game.core.campaign.references"),
            Edge(Campaign, Visit, "game.core.campaign.has-location-visit"));
        roles["world"] = WorldRole(360);
        roles["visit"] = Role(Visit, "Brackenford visit", new()
        {
            ["game.core.campaign.location-visit"] =
                "{\"firstVisitedMinute\":120,\"lastVisitedMinute\":120,\"visitCount\":1,\"status\":\"current\",\"summary\":\"Frontier village.\",\"memory\":\"Trust.\"}"
        }, Edge(Visit, Location, "game.core.campaign.location-visit.at-location"));
        var updated = await RunAsync("dnd2024.mechanic.campaign.location-visit.record.js",
            "{\"status\":\"departed\",\"summary\":\"Frontier village.\",\"memory\":\"The road is open.\",\"gmContext\":\"The waystone wakes.\"}", roles);

        Assert.True(updated.Ok, updated.Error);
        var effect = Assert.Single(updated.Output.Effects);
        Assert.Equal("component.set", effect.Type);
        Assert.Contains("\"firstVisitedMinute\":120", effect.Data);
        Assert.Contains("\"lastVisitedMinute\":360", effect.Data);
        Assert.Contains("\"visitCount\":2", effect.Data);
        Assert.Contains("\"status\":\"departed\"", effect.Data);
    }

    [Fact]
    public async Task Visit_update_rejects_backward_time_and_a_missing_existing_role()
    {
        var campaign = CampaignRole(
            Edge(Campaign, World, "game.core.campaign.in-world"),
            Edge(Campaign, Location, "game.core.campaign.references"),
            Edge(Campaign, Visit, "game.core.campaign.has-location-visit"));
        var input = "{\"status\":\"current\",\"summary\":\"Place.\",\"memory\":\"Memory.\",\"gmContext\":null}";
        var missing = await RunAsync("dnd2024.mechanic.campaign.location-visit.record.js", input,
            new() { ["campaign"] = campaign, ["world"] = WorldRole(100), ["location"] = LocationRole() });
        var backward = await RunAsync("dnd2024.mechanic.campaign.location-visit.record.js", input,
            new()
            {
                ["campaign"] = campaign,
                ["world"] = WorldRole(100),
                ["location"] = LocationRole(),
                ["visit"] = Role(Visit, "Visit", new()
                {
                    ["game.core.campaign.location-visit"] =
                        "{\"firstVisitedMinute\":120,\"lastVisitedMinute\":120,\"visitCount\":1,\"status\":\"current\",\"summary\":\"Place.\",\"memory\":\"Memory.\"}"
                }, Edge(Visit, Location, "game.core.campaign.location-visit.at-location"))
            });

        Assert.False(missing.Ok);
        Assert.False(backward.Ok);
        Assert.Empty(missing.Output.Effects);
        Assert.Empty(backward.Output.Effects);
    }

    private static EntityProjection CampaignRole(params RelationshipProjection[] relationships) =>
        Role(Campaign, "Campaign", new()
        {
            ["game.core.campaign.root"] =
                "{\"status\":\"active\",\"summary\":\"Fixture.\",\"visibility\":\"party\"}"
        }, relationships);

    private static EntityProjection WorldRole(int minute) => Role(World, "World", new()
    {
        ["game.core.world.root"] =
            "{\"status\":\"active\",\"summary\":\"Fixture.\",\"visibility\":\"party\"}",
        ["game.core.world.clock"] =
            $"{{\"calendarId\":\"fixture\",\"currentMinute\":{minute},\"revision\":1}}"
    });

    private static EntityProjection LocationRole() => Role(Location, "Brackenford", new()
    {
        ["game.core.world.location"] =
            "{\"kind\":\"settlement\",\"status\":\"active\",\"summary\":\"Fixture.\",\"visibility\":\"party\"}"
    });

    private static EntityProjection Role(string id, string name, Dictionary<string, string> components,
        params RelationshipProjection[] relationships) =>
        new(id, name, components, Relationships: relationships);

    private static RelationshipProjection Edge(string from, string to, string kind) =>
        new(from, to, kind, "{}");

    private static void Reference(Effect effect, string recordId)
    {
        Assert.Equal("relationship.create", effect.Type);
        Assert.Equal(recordId, effect.EntityId);
        Assert.Equal(Location, effect.ToEntityId);
        Assert.Equal("game.core.campaign.record.references-world-entity", effect.Kind);
        Assert.Equal("{}", effect.Data);
    }

    private static async Task<MechanicRunResult> RunAsync(
        string file,
        string input,
        Dictionary<string, EntityProjection> roles)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(), "catalog", "applications", "dnd2024", "mechanics", "campaign", file));
        return await new JintMechanicEngine().RunAsync(source,
            new MechanicProjection { Input = input, Roles = roles }, ExecutionLimits.Default);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
