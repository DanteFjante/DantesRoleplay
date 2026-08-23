using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.Quest;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class QuestFeature1Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"quest-q1-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Creates_the_ratified_three_objective_quest_atomically_with_entity_names_as_titles()
    {
        var setup = await ArrangeAsync();

        var result = await setup.Creator.CreateAsync(Request(setup.Continuity));

        Assert.True(result.Created);
        Assert.Equal(19, result.StructuralEventCount);
        var quest = Assert.IsType<EntitySnapshot>(await setup.World.GetEntityAsync("quest.test.missing-margin"));
        Assert.Equal("The Missing Margin", quest.Name);
        var root = Assert.Single(quest.Components, component => component.DefinitionId == "game.core.quest.root");
        using (var rootData = JsonDocument.Parse(root.Data))
        {
            Assert.False(rootData.RootElement.TryGetProperty("title", out _));
            Assert.Equal("draft", rootData.RootElement.GetProperty("status").GetString());
        }

        var objectiveLinks = (await setup.World.GetRelationshipsAsync(quest.Id, false))
            .Where(link => link.Kind == "game.core.quest.has-objective").ToArray();
        Assert.Equal(3, objectiveLinks.Length);
        foreach (var objectiveLink in objectiveLinks)
        {
            var objective = Assert.IsType<EntitySnapshot>(await setup.World.GetEntityAsync(objectiveLink.ToEntityId));
            var objectiveData = Assert.Single(objective.Components, component => component.DefinitionId == "game.core.quest.objective");
            using var document = JsonDocument.Parse(objectiveData.Data);
            Assert.False(document.RootElement.TryGetProperty("title", out _));
            Assert.Equal("dormant", document.RootElement.GetProperty("status").GetString());
        }

        Assert.Equal(19, (await setup.Ledger.FindAsync(rootOperationId: result.OperationId)).Count);
        var replay = await setup.Creator.CreateAsync(Request(setup.Continuity));
        Assert.False(replay.Created);
        Assert.Equal("QUEST_ID_TAKEN", Assert.Single(replay.Problems).Code);
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: replay.OperationId));
    }

    [Fact]
    public async Task Rejects_party_reference_to_unrevealed_gm_clue_without_structural_state()
    {
        var setup = await ArrangeAsync();
        var request = Request(setup.Continuity) with
        {
            Objectives =
            [
                new QuestObjectiveInput(
                    "objective.trace-the-margin", "Trace the Missing Margin", "Compare records.", true, "party", 1, [],
                    [new QuestReference("clue.feature-04.ledger-seal", "knowledge", "party")]),
                Request(setup.Continuity).Objectives[1],
                Request(setup.Continuity).Objectives[2]
            ]
        };

        var result = await setup.Creator.CreateAsync(request);

        Assert.False(result.Created);
        Assert.Equal("REFERENCE_NOT_VISIBLE", Assert.Single(result.Problems).Code);
        Assert.Null(await setup.World.GetEntityAsync(request.QuestId));
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: result.OperationId));
    }

    [Fact]
    public async Task Creates_with_one_closed_and_one_active_chapter_in_the_same_arc()
    {
        var setup = await ArrangeAsync();
        var continuity = new CampaignContinuityRunner(
            setup.Db, setup.World, new EffectApplier(setup.Db, setup.World, null, setup.Ledger), new OperationLog(setup.Db));
        var next = await continuity.AdvanceAsync(
            "campaign.test.sealed-observatory",
            setup.Continuity.ChapterId!,
            "active",
            "The signal led the party to the market archive.",
            new CampaignNextChapter("chapter.archive", "The Archive Question", "Who altered the ledger?"));
        Assert.True(next.Succeeded);

        var request = Request(next) with { ChapterIds = [setup.Continuity.ChapterId!, next.ChapterId!] };
        var result = await setup.Creator.CreateAsync(request);

        Assert.True(result.Created);
        Assert.Equal(20, result.StructuralEventCount);
        Assert.Equal(2, (await setup.World.GetRelationshipsAsync(request.QuestId, false))
            .Count(link => link.Kind == "game.core.quest.in-chapter"));
    }

    [Fact]
    public async Task Rejects_chapter_that_is_not_in_the_selected_arc_without_structural_state()
    {
        var setup = await ArrangeAsync();
        var request = Request(setup.Continuity) with { ChapterIds = ["chapter.not-a-campaign-chapter"] };

        var result = await setup.Creator.CreateAsync(request);

        Assert.False(result.Created);
        Assert.Equal("INVALID_CHAPTER", Assert.Single(result.Problems).Code);
        Assert.Null(await setup.World.GetEntityAsync(request.QuestId));
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: result.OperationId));
    }

    [Fact]
    public async Task Public_surface_rejects_lifecycle_payload_with_a_callable_create_recovery()
    {
        var setup = await ArrangeAsync();
        var payload = JsonSerializer.Serialize(new
        {
            operation = "offer",
            questId = "quest.test.missing-margin",
            expectedStatus = "draft"
        });

        var result = await new CommitTool().CommitAsync(
            procedures: null!,
            world: null!,
            effects: null!,
            mechanics: null!,
            eventTypes: null!,
            subscriptions: null!,
            actions: null!,
            itineraries: null!,
            campaigns: null!,
            campaignBootstrapper: null!,
            campaignContinuity: null!,
            campaignSessions: null!,
            campaignSessionStarter: null!,
            quests: setup.Creator,
            questLifecycle: null!,
            log: new OperationLog(setup.Db),
            notifications: null!,
            kind: "quest",
            payload: payload);

        Assert.False(result.Ok);
        Assert.Equal("INVALID_PAYLOAD", result.Error?.Code);
        Assert.StartsWith("commit(kind: \"quest\", payload: ", result.Error?.Fix, StringComparison.Ordinal);
        Assert.Null(await setup.World.GetEntityAsync("quest.test.missing-margin"));
    }

    private async Task<Setup> ArrangeAsync()
    {
        CopyCatalog(CatalogRoot(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var importer = new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world);
        Assert.False((await importer.ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);

        var ledger = new EventLedger(db);
        await new EventTypeSeeder(new EventTypeStore(db)).SeedAsync();
        var validator = new CampaignBlueprintValidator(world);
        var blueprint = Blueprint();
        var bootstrapper = new CampaignBootstrapper(db, validator, new EffectApplier(db, world, null, ledger), new OperationLog(db));
        var review = await validator.ValidateAsync(blueprint);
        Assert.True((await bootstrapper.CreateAsync(blueprint, review.ReviewFingerprint!)).Created);

        var continuityRunner = new CampaignContinuityRunner(
            db, world, new EffectApplier(db, world, null, ledger), new OperationLog(db));
        var continuity = await continuityRunner.InitializeAsync(new CampaignContinuitySeed(
            blueprint.CampaignId,
            new CampaignChapterSeed("chapter.opening", "The Ledger Signal", "What does the ledger reveal?"),
            new CampaignArcSeed("arc.observatory", "The Observatory's Claim", "Can history avoid becoming leverage?")));
        Assert.True(continuity.Succeeded);

        return new Setup(db, world, ledger, continuity, new QuestCreator(
            db, world, new EffectApplier(db, world, null, ledger), new OperationLog(db)));
    }

    private static QuestCreateRequest Request(CampaignContinuityResult continuity) => new(
        "quest.test.missing-margin",
        "The Missing Margin",
        "Find why the observatory signal matters.",
        "An open investigation.",
        "party",
        "campaign.test.sealed-observatory",
        continuity.ArcId!,
        [continuity.ChapterId!],
        [
            new QuestObjectiveInput(
                "objective.trace-the-margin", "Trace the Missing Margin", "Compare records.", true, "party", 1, [],
                [new QuestReference("fact.feature-04.toll-ledger", "knowledge", "party")]),
            new QuestObjectiveInput(
                "objective.test-the-witnesses", "Test the Witnesses", "Compare accounts.", true, "party", 2,
                ["objective.trace-the-margin"],
                [new QuestReference("actor.feature-03.mara-vell", "actor", "party")]),
            new QuestObjectiveInput(
                "objective.read-the-seal", "Read the Seal", "Inspect the optional physical lead.", false, "gm", 3,
                ["objective.trace-the-margin"],
                [new QuestReference("clue.feature-04.ledger-seal", "knowledge", "gm")])
        ]);

    private static CampaignBlueprint Blueprint() => new(
        "campaign.test.sealed-observatory",
        "The Sealed Observatory",
        "A signal threatens old market records.",
        ["Reach the archive."],
        ["Curious mystery."],
        "dnd2024",
        "world.feature-01.fixture",
        "location.feature-01.gate",
        [
            new CampaignReference("location.feature-01.gate", "start", "party"),
            new CampaignReference("actor.feature-03.mara-vell", "npc", "party"),
            new CampaignReference("actor.feature-03.oren-dale", "npc", "gm"),
            new CampaignReference("faction.feature-03.fixture", "faction-stake", "party"),
            new CampaignReference("fact.feature-04.toll-ledger", "knowledge", "party"),
            new CampaignReference("rumour.feature-04.observatory-signal", "knowledge", "party")
        ],
        new CampaignChapter("chapter.opening", "What does the ledger reveal?"),
        new CampaignArc("arc.observatory", "Can history avoid leverage?"));

    private static string CatalogRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return Path.Combine(directory.FullName, "catalog");
        }

        throw new DirectoryNotFoundException();
    }

    private static void CopyCatalog(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    private sealed record Setup(
        DantesRoleplayDbContext Db,
        IWorldStore World,
        EventLedger Ledger,
        CampaignContinuityResult Continuity,
        QuestCreator Creator);
}
