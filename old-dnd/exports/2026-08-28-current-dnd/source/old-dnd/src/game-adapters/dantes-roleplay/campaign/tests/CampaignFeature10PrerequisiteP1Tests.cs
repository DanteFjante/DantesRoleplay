using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.Quest;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

/// <summary>
/// P1 proves the existing-world campaign path survives a complete stored-state session without
/// introducing the new-world C10 creation path or relying on raw component inspection.
/// </summary>
public sealed class CampaignFeature10PrerequisiteP1Tests : IDisposable
{
    private const string CampaignId = "campaign.test.p1.sealed-observatory";
    private const string QuestId = "quest.test.p1.missing-margin";
    private const string SessionId = "session.test.p1.sealed-observatory.opening";
    private const string WorldId = "world.feature-01.fixture";
    private const string GateId = "location.feature-01.gate";
    private const string MarketId = "location.feature-01.market";
    private const string TravellerId = "traveller.feature-02.fixture";
    private const string ClueId = "clue.feature-04.ledger-seal";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"campaign-feature-10-p1-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Existing_world_campaign_session_reopens_from_stored_state_without_a_transcript()
    {
        Copy(Catalog(), _catalogCopy);
        var expected = await PlayAndCloseAsync();

        await using var freshDb = _fixture.CreateContext();
        var freshWorld = new WorldStore(freshDb);
        var freshLedger = new EventLedger(freshDb);
        var campaign = await new CampaignResumeReader(freshWorld, freshLedger, new QuestSummaryReader(freshWorld, freshLedger)).GetAsync(CampaignId);
        var quest = await new QuestSummaryReader(freshWorld, freshLedger).GetAsync(QuestId);
        var session = await new CampaignSessionResumeReader(freshWorld, new CampaignResumeReader(freshWorld, freshLedger)).GetAsync(CampaignId);
        var recap = await new CampaignSessionRecapReader(freshWorld).GetAsync(SessionId);
        var knowledge = await new GraphProjectionReader(freshWorld).ReadAsync(new(
            WorldId,
            ["game.core.world.fact", "game.core.world.rumour", "game.core.world.clue"],
            0,
            ["game.core.world.knowledge.in-world", "game.core.world.knowledge.about", "game.core.world.clue.supports"],
            2,
            100,
            150));
        var returnedJourney = await new JourneyPlanReader(freshWorld).ReadAsync(new(WorldId, TravellerId, GateId));

        Assert.NotNull(campaign);
        Assert.Equal(expected.NextChapterId, campaign!.CurrentChapter!.Id);
        Assert.Equal("The Ledger Signal", campaign.CurrentChapter.Title);
        Assert.Equal(expected.ArcId, campaign.CurrentArc!.Id);
        var resumedQuest = Assert.Single(campaign.Quests);
        Assert.Equal(QuestId, resumedQuest.QuestId);
        Assert.Equal("active", resumedQuest.Status);
        Assert.Contains(expected.NextChapterId, resumedQuest.ChapterIds);
        Assert.Equal("completed", Assert.Single(resumedQuest.Objectives, objective => objective.Id == $"{QuestId}.objective.trace").Status);

        Assert.NotNull(quest);
        Assert.Equal("active", quest!.Status);
        Assert.Equal("completed", Assert.Single(quest.Objectives, objective => objective.Id == $"{QuestId}.objective.trace").Status);
        Assert.False(session.Resumed);
        Assert.Equal("NO_ACTIVE_SESSION", Assert.Single(session.Problems).Code);
        Assert.True(recap.Found);
        Assert.Equal(expected.NextChapterId, recap.Recap!.Chapter.Id);
        Assert.Equal("The Ledger Signal", recap.Recap.Chapter.Title);
        Assert.Empty(recap.Recap.Milestones);
        Assert.True(knowledge.Ok, knowledge.ErrorMessage);
        Assert.Null(knowledge.Projection!.Truncated);
        Assert.Contains(knowledge.Projection.Nodes, node => node.Id == "fact.feature-04.toll-ledger");
        Assert.Contains(knowledge.Projection.Nodes, node => node.Id == "rumour.feature-04.observatory-signal");
        Assert.Contains(knowledge.Projection.Edges, edge => edge.FromEntityId == "fact.feature-04.toll-ledger" && edge.ToEntityId == MarketId && edge.Kind == "game.core.world.knowledge.about");
        Assert.Contains(knowledge.Projection.Edges, edge => edge.FromEntityId == "rumour.feature-04.observatory-signal" && edge.ToEntityId == "location.feature-01.observatory" && edge.Kind == "game.core.world.knowledge.about");
        var clue = Assert.Single(knowledge.Projection.Nodes, node => node.Id == ClueId);
        using (var clueData = JsonDocument.Parse(Assert.Single(clue.Components, component => component.DefinitionId == "game.core.world.clue").Data))
            Assert.Equal("revealed", clueData.RootElement.GetProperty("status").GetString());
        Assert.True(returnedJourney.Ok, returnedJourney.ErrorMessage);
        Assert.Equal(MarketId, returnedJourney.Projection!.OriginId);

        var freshMechanics = new MechanicStore(freshDb);
        var freshProjection = new ProjectionResolver(freshDb);
        var freshEngine = new JintMechanicEngine();
        var freshActions = new ActionRunner(freshDb, freshMechanics, freshProjection, freshEngine, new EffectApplier(freshDb, freshWorld, null, freshLedger), new OperationLog(freshDb),
            new MechanicComposer(freshMechanics, freshProjection, freshEngine));
        var continued = await freshActions.RunAsync(new ActionRequest
        {
            Intent = "move to a connected location",
            RoleEntityIds = new Dictionary<string, string> { ["traveller"] = TravellerId, ["origin"] = MarketId, ["destination"] = GateId },
            Input = "{}",
            Seed = 904
        });
        Assert.True(continued.Ok, continued.Error?.Why);
        Assert.Equal(1, continued.AppliedCount);
    }

    private async Task<PlayedState> PlayAndCloseAsync()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db))
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        await new EventTypeSeeder(new EventTypeStore(db)).SeedAsync();
        var ledger = new EventLedger(db);
        var effects = new EffectApplier(db, world, null, ledger);
        var log = new OperationLog(db);

        var blueprint = Blueprint();
        var validator = new CampaignBlueprintValidator(world);
        var review = await validator.ValidateAsync(blueprint);
        Assert.True(review.Valid, review.Problems.FirstOrDefault()?.Reason);
        Assert.True((await new CampaignBootstrapper(db, validator, effects, log).CreateAsync(blueprint, review.ReviewFingerprint!)).Created);

        var continuity = new CampaignContinuityRunner(db, world, effects, log);
        var initial = await continuity.InitializeAsync(new(
            CampaignId,
            new("chapter.opening", "The Ledger Signal", "What does the old toll ledger reveal about the observatory signal?"),
            new("arc.observatory", "The Observatory's Claim", "Can the group keep the observatory's history from becoming leverage?")));
        Assert.True(initial.Succeeded, initial.Problems.FirstOrDefault()?.Reason);

        var creator = new QuestCreator(db, world, effects, log);
        Assert.True((await creator.CreateAsync(QuestRequest(initial))).Created);
        var lifecycle = new QuestLifecycleRunner(db, world, effects, log);
        Assert.True((await lifecycle.TransitionAsync(new("offer", QuestId, "draft", "The host presents the investigation."))).Succeeded);
        Assert.True((await lifecycle.TransitionAsync(new("accept", QuestId, "offered", "The party accepts the investigation."))).Succeeded);

        var questSummary = new QuestSummaryReader(world, ledger);
        var contexts = new CampaignQuestContextRunner(db, world, questSummary, effects, log);
        Assert.True((await contexts.AttachAsync(new(CampaignId, initial.ArcId!, initial.ChapterId!, QuestId, "active"))).Attached);

        var campaignResume = new CampaignResumeReader(world, ledger, questSummary);
        var sessionValidator = new CampaignSessionValidator(world, campaignResume);
        var starter = new CampaignSessionStarter(db, sessionValidator, effects, log);
        Assert.True((await starter.StartAsync(new("start-session", CampaignId, SessionId))).Started);

        var actions = new ActionRunner(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), effects, log,
            new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
        var planned = await new JourneyPlanReader(world).ReadAsync(new(WorldId, TravellerId, MarketId));
        Assert.True(planned.Ok, planned.ErrorMessage);
        Assert.Equal(GateId, planned.Projection!.OriginId);
        var rejected = await actions.RunAsync(new ActionRequest
        {
            Intent = "move to a connected location",
            RoleEntityIds = new Dictionary<string, string> { ["traveller"] = TravellerId, ["origin"] = GateId, ["destination"] = "location.feature-01.observatory" },
            Input = "{}",
            Seed = 902
        });
        Assert.False(rejected.Ok);
        Assert.Equal(0, rejected.AppliedCount);
        var afterRejected = await new JourneyPlanReader(world).ReadAsync(new(WorldId, TravellerId, MarketId));
        Assert.True(afterRejected.Ok, afterRejected.ErrorMessage);
        Assert.Equal(GateId, afterRejected.Projection!.OriginId);
        var moved = await actions.RunAsync(new ActionRequest
        {
            Intent = "move to a connected location",
            RoleEntityIds = new Dictionary<string, string> { ["traveller"] = TravellerId, ["origin"] = GateId, ["destination"] = MarketId },
            Input = "{}",
            Seed = 903
        });
        Assert.True(moved.Ok, moved.Error?.Why);
        Assert.Equal(1, moved.AppliedCount);
        var revealed = await actions.RunAsync(new ActionRequest
        {
            Intent = "reveal a clue",
            RoleEntityIds = new Dictionary<string, string> { ["clue"] = ClueId, ["world"] = WorldId },
            Input = "{}",
            Seed = 404
        });
        Assert.True(revealed.Ok, revealed.Error?.Why);
        Assert.Equal(1, revealed.AppliedCount);

        var objective = await lifecycle.TransitionObjectiveAsync(new(
            "set-objective", QuestId, "active", $"{QuestId}.objective.trace", "active", "completed",
            "The party compares the public ledger with the signal."));
        Assert.True(objective.Succeeded, objective.Problems.FirstOrDefault()?.Reason);

        var sessionResume = new CampaignSessionResumeReader(world, new CampaignResumeReader(world, ledger, questSummary));
        var ender = new CampaignSessionEnder(db, new CampaignSessionEndValidator(world, sessionResume), effects, log);
        var ended = await ender.EndAsync(new("end-session", SessionId, "active"));
        Assert.True(ended.Ended, ended.Problems.FirstOrDefault()?.Reason);
        var eventCount = (await ledger.FindAsync(limit: 500)).Count;
        var replay = await ender.EndAsync(new("end-session", SessionId, "active"));
        Assert.False(replay.Ended);
        Assert.Equal("STALE_SESSION_STATUS", Assert.Single(replay.Problems).Code);
        Assert.Equal(eventCount, (await ledger.FindAsync(limit: 500)).Count);

        return new(initial.ChapterId!, initial.ArcId!);
    }

    private static CampaignBlueprint Blueprint() => new(
        CampaignId,
        "The Sealed Observatory",
        "A strange signal from the sealed observatory threatens the old market records.",
        ["Reach the market archive.", "Choose whom to trust with the signal."],
        ["Curious local mystery."],
        "dnd2024",
        WorldId,
        GateId,
        [
            new(GateId, "start", "party"),
            new("actor.feature-03.mara-vell", "npc", "party"),
            new("actor.feature-03.oren-dale", "npc", "gm"),
            new("faction.feature-03.fixture", "faction-stake", "party"),
            new("fact.feature-04.toll-ledger", "knowledge", "party"),
            new("rumour.feature-04.observatory-signal", "knowledge", "party")
        ],
        new("chapter.opening", "What does the old toll ledger reveal?"),
        new("arc.observatory", "Can the observatory's history be kept from becoming leverage?"));

    private static QuestCreateRequest QuestRequest(CampaignContinuityResult continuity) => new(
        QuestId,
        "The Missing Margin",
        "Find why the observatory signal matters.",
        "An open investigation.",
        "party",
        CampaignId,
        continuity.ArcId!,
        [continuity.ChapterId!],
        [
            new("objective.trace", "Trace the Margin", "Compare the surviving records.", true, "party", 1, [], [new("fact.feature-04.toll-ledger", "knowledge", "party")]),
            new("objective.witnesses", "Test the Witnesses", "Compare the witness accounts.", true, "party", 2, ["objective.trace"], [new("actor.feature-03.mara-vell", "actor", "party")]),
            new("objective.seal", "Read the Seal", "Inspect the physical seal.", false, "gm", 3, ["objective.trace"], [new(ClueId, "knowledge", "gm")])
        ]);

    private static string Catalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    private sealed record PlayedState(string NextChapterId, string ArcId);
}
