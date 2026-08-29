using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.Quest;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CampaignFeature4Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _copy = Path.Combine(Path.GetTempPath(), $"campaign-feature-04-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_copy)) Directory.Delete(_copy, recursive: true);
    }

    [Fact]
    public async Task First_attachment_is_two_atomic_links_and_resume_reconstructs_without_quest_mutation()
    {
        var setup = await ArrangeAsync();
        var before = await QuestComponentBytesAsync(setup.World, setup.QuestId);

        var attached = await setup.Runner.AttachAsync(Request(setup, setup.FirstChapterId));

        Assert.True(attached.Attached);
        Assert.Equal(2, attached.StructuralEventCount);
        Assert.Equal(2, (await setup.Ledger.FindAsync(rootOperationId: attached.OperationId)).Count);
        Assert.Equal(before, await QuestComponentBytesAsync(setup.World, setup.QuestId));
        var arcLink = Assert.Single(await ContextLinksAsync(setup.World, setup.ArcId, "game.core.campaign.arc.features-quest"));
        var chapterLink = Assert.Single(await ContextLinksAsync(setup.World, setup.FirstChapterId, "game.core.campaign.chapter.features-quest"));
        Assert.Equal(setup.QuestId, arcLink.ToEntityId);
        Assert.Equal("{}", arcLink.Data);
        Assert.Equal(setup.QuestId, chapterLink.ToEntityId);
        Assert.Equal("{}", chapterLink.Data);

        var resumed = await new CampaignResumeReader(setup.World, setup.Ledger, new QuestSummaryReader(setup.World, setup.Ledger)).GetAsync(CampaignId);
        var quest = Assert.Single(resumed!.Quests);
        Assert.Equal(setup.QuestId, quest.QuestId);
        Assert.Equal(setup.ArcId, quest.ArcId);
        Assert.Equal([setup.FirstChapterId], quest.ChapterIds);
        Assert.Equal([1, 2, 3], quest.Objectives.Select(objective => objective.DisplayOrder));

        var replay = await setup.Runner.AttachAsync(Request(setup, setup.FirstChapterId));
        Assert.False(replay.Attached);
        Assert.Equal("QUEST_CONTEXT_REPLAY", Assert.Single(replay.Problems).Code);
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: replay.OperationId));
        Assert.Equal(before, await QuestComponentBytesAsync(setup.World, setup.QuestId));
    }

    [Fact]
    public async Task Same_arc_quest_can_add_its_second_owned_chapter_with_one_link()
    {
        var setup = await ArrangeAsync(withSecondChapter: true);
        var before = await QuestComponentBytesAsync(setup.World, setup.QuestId);

        var first = await setup.Runner.AttachAsync(Request(setup, setup.FirstChapterId));
        var second = await setup.Runner.AttachAsync(Request(setup, setup.ActiveChapterId));

        Assert.True(first.Attached);
        Assert.Equal(2, first.StructuralEventCount);
        Assert.True(second.Attached);
        Assert.Equal(1, second.StructuralEventCount);
        Assert.Single(await ContextLinksAsync(setup.World, setup.ArcId, "game.core.campaign.arc.features-quest"));
        Assert.Single(await ContextLinksAsync(setup.World, setup.FirstChapterId, "game.core.campaign.chapter.features-quest"));
        Assert.Single(await ContextLinksAsync(setup.World, setup.ActiveChapterId, "game.core.campaign.chapter.features-quest"));
        Assert.Equal(before, await QuestComponentBytesAsync(setup.World, setup.QuestId));
        var resume = await setup.Reader.GetAsync(CampaignId);
        Assert.Equal(new[] { setup.ActiveChapterId, setup.FirstChapterId }.Order(StringComparer.Ordinal), Assert.Single(resume!.Quests).ChapterIds);
    }

    [Fact]
    public async Task Stale_and_terminal_context_requests_reject_without_success_events()
    {
        var stale = await ArrangeAsync();
        var staleResult = await stale.Runner.AttachAsync(Request(stale, stale.FirstChapterId) with { ExpectedQuestStatus = "offered" });
        Assert.Equal("INVALID_QUEST_CONTEXT", Assert.Single(staleResult.Problems).Code);
        Assert.Empty(await stale.Ledger.FindAsync(rootOperationId: staleResult.OperationId));
        var wrongCampaign = await stale.Runner.AttachAsync(Request(stale, stale.FirstChapterId) with { CampaignId = "campaign.test.other" });
        Assert.Equal("INVALID_CAMPAIGN", Assert.Single(wrongCampaign.Problems).Code);
        Assert.Empty(await stale.Ledger.FindAsync(rootOperationId: wrongCampaign.OperationId));
        var wrongChapter = await stale.Runner.AttachAsync(Request(stale, stale.FirstChapterId) with { ChapterId = "campaign.test.other.chapter" });
        Assert.Equal("QUEST_CONTEXT_SCOPE_MISMATCH", Assert.Single(wrongChapter.Problems).Code);
        Assert.Empty(await stale.Ledger.FindAsync(rootOperationId: wrongChapter.OperationId));

        Assert.True((await stale.Lifecycle.TransitionAsync(new("fail", stale.QuestId, "active", "The investigation can no longer continue."))).Succeeded);
        var terminal = await stale.Runner.AttachAsync(Request(stale, stale.FirstChapterId));
        Assert.Equal("QUEST_CONTEXT_UNAVAILABLE", Assert.Single(terminal.Problems).Code);
        Assert.Empty(await stale.Ledger.FindAsync(rootOperationId: terminal.OperationId));

    }

    [Fact]
    public async Task Reversed_context_link_rejects_without_creating_the_correct_link()
    {
        var reversed = await ArrangeAsync("quest.test.reversed-context");
        var seeded = await new EffectApplier(reversed.Db, reversed.World, null, reversed.Ledger).ApplyAsync(
            [new Effect { Type = EffectType.RelationshipCreate, EntityId = reversed.QuestId, ToEntityId = reversed.ArcId, Kind = "game.core.campaign.arc.features-quest", Data = "{}" }]);
        Assert.True(seeded.Applied);
        var rejected = await reversed.Runner.AttachAsync(Request(reversed, reversed.FirstChapterId));
        Assert.Equal("QUEST_CONTEXT_GRAPH_INVALID", Assert.Single(rejected.Problems).Code);
        Assert.Empty(await reversed.Ledger.FindAsync(rootOperationId: rejected.OperationId));
        Assert.Empty(await ContextLinksAsync(reversed.World, reversed.ArcId, "game.core.campaign.arc.features-quest"));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_links_and_structural_events()
    {
        var setup = await ArrangeAsync();
        var arcEventsBefore = (await setup.Ledger.FindAsync(entityId: setup.ArcId, limit: 100)).Count;
        var chapterEventsBefore = (await setup.Ledger.FindAsync(entityId: setup.FirstChapterId, limit: 100)).Count;
        var runner = new CampaignQuestContextRunner(setup.Db, setup.World, new QuestSummaryReader(setup.World, setup.Ledger),
            new EffectApplier(setup.Db, setup.World, null, setup.Ledger), new ThrowingOperationLog());

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.AttachAsync(Request(setup, setup.FirstChapterId)));

        Assert.Empty(await ContextLinksAsync(setup.World, setup.ArcId, "game.core.campaign.arc.features-quest"));
        Assert.Empty(await ContextLinksAsync(setup.World, setup.FirstChapterId, "game.core.campaign.chapter.features-quest"));
        Assert.Equal(arcEventsBefore, (await setup.Ledger.FindAsync(entityId: setup.ArcId, limit: 100)).Count);
        Assert.Equal(chapterEventsBefore, (await setup.Ledger.FindAsync(entityId: setup.FirstChapterId, limit: 100)).Count);
    }

    [Fact]
    public async Task Campaign_and_quest_lifecycle_owners_remain_isolated_after_attachment()
    {
        var setup = await ArrangeAsync();
        Assert.True((await setup.Runner.AttachAsync(Request(setup, setup.FirstChapterId))).Attached);
        var campaignBeforeQuestChange = await EntityComponentBytesAsync(setup.World, [CampaignId, setup.ArcId, setup.FirstChapterId]);

        var objective = await setup.Lifecycle.TransitionObjectiveAsync(new("set-objective", setup.QuestId, "active",
            $"{setup.QuestId}.objective.trace", "active", "completed", "The records establish the missing margin."));

        Assert.True(objective.Succeeded);
        Assert.Equal(campaignBeforeQuestChange, await EntityComponentBytesAsync(setup.World, [CampaignId, setup.ArcId, setup.FirstChapterId]));
        var resumed = await setup.Reader.GetAsync(CampaignId);
        Assert.Equal("completed", Assert.Single(resumed!.Quests).Objectives[0].Status);
        var questBeforeCampaignChange = await QuestComponentBytesAsync(setup.World, setup.QuestId);

        var closed = await setup.Continuity.CloseAsync(CampaignId, setup.FirstChapterId, "active", "The party secures the ledger's public copy.");

        Assert.True(closed.Succeeded);
        Assert.Equal(questBeforeCampaignChange, await QuestComponentBytesAsync(setup.World, setup.QuestId));
    }

    [Fact]
    public async Task Resume_caps_quest_and_objective_context_in_canonical_order()
    {
        var setup = await ArrangeAsync();
        var ids = new[] { "quest.test.bound-d", "quest.test.bound-a", "quest.test.bound-c", "quest.test.bound-b" };
        var summaries = new Dictionary<string, QuestSummary>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var effects = new List<Effect>
            {
                new() { Type = EffectType.EntityCreate, EntityId = id, Name = id },
                Link(id, CampaignId, "game.core.quest.in-campaign"),
                Link(id, setup.ArcId, "game.core.quest.in-arc"),
                Link(id, setup.FirstChapterId, "game.core.quest.in-chapter"),
                Link(setup.ArcId, id, "game.core.campaign.arc.features-quest"),
                Link(setup.FirstChapterId, id, "game.core.campaign.chapter.features-quest")
            };
            Assert.True((await new EffectApplier(setup.Db, setup.World, null, setup.Ledger).ApplyAsync(effects)).Applied);
            summaries[id] = Summary(id, objectiveCount: 4);
        }
        summaries["quest.test.unrelated"] = Summary("quest.test.unrelated", objectiveCount: 4);

        var resume = await new CampaignResumeReader(setup.World, setup.Ledger, new FixedQuestSummaryReader(summaries)).GetAsync(CampaignId);

        Assert.Equal(["quest.test.bound-a", "quest.test.bound-b", "quest.test.bound-c"], resume!.Quests.Select(quest => quest.QuestId));
        Assert.All(resume.Quests, quest => Assert.Equal(3, quest.Objectives.Count));
        Assert.DoesNotContain(resume.Quests, quest => quest.QuestId == "quest.test.unrelated");
        Assert.DoesNotContain(resume.Quests.SelectMany(quest => quest.Objectives), objective => objective.Id.EndsWith("objective.4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Public_campaign_operation_rejects_extra_fields_and_accepts_the_exact_shape()
    {
        var setup = await ArrangeAsync();
        var invalid = await CommitAsync(setup, JsonSerializer.Serialize(new
        {
            operation = "attach-quest-context", campaignId = CampaignId, arcId = setup.ArcId,
            chapterId = setup.FirstChapterId, questId = setup.QuestId, expectedQuestStatus = "active", effects = Array.Empty<object>()
        }));
        Assert.False(invalid.Ok);
        Assert.Equal("INVALID_PAYLOAD", invalid.Error?.Code);
        Assert.Empty(await ContextLinksAsync(setup.World, setup.ArcId, "game.core.campaign.arc.features-quest"));

        var accepted = await CommitAsync(setup, JsonSerializer.Serialize(new
        {
            operation = "attach-quest-context", campaignId = CampaignId, arcId = setup.ArcId,
            chapterId = setup.FirstChapterId, questId = setup.QuestId, expectedQuestStatus = "active"
        }));
        Assert.True(accepted.Ok, JsonSerializer.Serialize(accepted));
        Assert.Equal(2, Assert.IsType<CampaignQuestContextResult>(accepted.Data).StructuralEventCount);
    }

    private async Task<Setup> ArrangeAsync(string questId = "quest.test.campaign-context", bool withSecondChapter = false)
    {
        Copy(Catalog(), _copy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        await new EventTypeSeeder(new EventTypeStore(db)).SeedAsync();
        var ledger = new EventLedger(db);
        var effects = new EffectApplier(db, world, null, ledger);
        var log = new OperationLog(db);
        var blueprint = Blueprint();
        var validator = new CampaignBlueprintValidator(world);
        var review = await validator.ValidateAsync(blueprint);
        Assert.True((await new CampaignBootstrapper(db, validator, effects, log).CreateAsync(blueprint, review.ReviewFingerprint!)).Created);
        var continuity = new CampaignContinuityRunner(db, world, effects, log);
        var initial = await continuity.InitializeAsync(new(CampaignId,
            new("chapter.opening", "The Ledger Signal", "What does the old toll ledger reveal?"),
            new("arc.observatory", "The Observatory's Claim", "Can history avoid becoming leverage?")));
        Assert.True(initial.Succeeded);
        var activeChapterId = initial.ChapterId!;
        if (withSecondChapter)
        {
            var advanced = await continuity.AdvanceAsync(CampaignId, initial.ChapterId!, "active", "The first lead points to the archive.",
                new("chapter.archive", "The Market Archive", "Who benefits if the ledger stays buried?"));
            Assert.True(advanced.Succeeded);
            activeChapterId = advanced.ChapterId!;
        }
        var creator = new QuestCreator(db, world, effects, log);
        var chapterIds = withSecondChapter ? new[] { initial.ChapterId!, activeChapterId } : new[] { initial.ChapterId! };
        Assert.True((await creator.CreateAsync(QuestRequest(questId, initial.ArcId!, chapterIds))).Created);
        var lifecycle = new QuestLifecycleRunner(db, world, effects, log);
        Assert.True((await lifecycle.TransitionAsync(new("offer", questId, "draft", "The host presents the investigation."))).Succeeded);
        Assert.True((await lifecycle.TransitionAsync(new("accept", questId, "offered", "The party accepts the investigation."))).Succeeded);
        var summary = new QuestSummaryReader(world, ledger);
        return new(db, world, ledger, continuity, creator, lifecycle,
            new CampaignQuestContextRunner(db, world, summary, effects, log), new CampaignResumeReader(world, ledger, summary),
            questId, initial.ArcId!, initial.ChapterId!, activeChapterId);
    }

    private static CampaignQuestContextRequest Request(Setup setup, string chapterId) =>
        new(CampaignId, setup.ArcId, chapterId, setup.QuestId, "active");

    private static QuestCreateRequest QuestRequest(string questId, string arcId, IReadOnlyList<string> chapterIds) => new(
        questId, "The Missing Margin", "Find why the observatory signal matters.", "An open investigation.", "party",
        CampaignId, arcId, chapterIds,
        [
            new("objective.trace", "Trace the Margin", "Compare the surviving records.", true, "party", 1, [], [new("fact.feature-04.toll-ledger", "knowledge", "party")]),
            new("objective.witnesses", "Test the Witnesses", "Compare the witness accounts.", true, "party", 2, ["objective.trace"], [new("actor.feature-03.mara-vell", "actor", "party")]),
            new("objective.seal", "Read the Seal", "Inspect the physical seal.", false, "gm", 3, ["objective.trace"], [new("clue.feature-04.ledger-seal", "knowledge", "gm")])
        ]);

    private static CampaignBlueprint Blueprint() => new(
        CampaignId, "The Sealed Observatory", "A signal threatens old market records.", ["Reach the archive."], ["Curious mystery."],
        "dnd2024", "world.feature-01.fixture", "location.feature-01.gate",
        [new("location.feature-01.gate", "start", "party"), new("actor.feature-03.mara-vell", "npc", "party"), new("actor.feature-03.oren-dale", "npc", "gm"), new("faction.feature-03.fixture", "faction-stake", "party"), new("fact.feature-04.toll-ledger", "knowledge", "party"), new("rumour.feature-04.observatory-signal", "knowledge", "party")],
        new("chapter.opening", "What does the ledger reveal?"), new("arc.observatory", "Can history avoid leverage?"));

    private static async Task<IReadOnlyList<RelationshipView>> ContextLinksAsync(IWorldStore world, string fromId, string kind) =>
        (await world.GetRelationshipsAsync(fromId, false)).Where(link => link.Kind == kind).ToList();

    private static async Task<IReadOnlyDictionary<string, string>> QuestComponentBytesAsync(IWorldStore world, string questId)
    {
        var ids = new[] { questId, $"{questId}.objective.trace", $"{questId}.objective.witnesses", $"{questId}.objective.seal" };
        return await EntityComponentBytesAsync(world, ids);
    }

    private static async Task<IReadOnlyDictionary<string, string>> EntityComponentBytesAsync(IWorldStore world, IReadOnlyList<string> ids)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var entity = (await world.GetEntityAsync(id))!;
            foreach (var component in entity.Components) result[$"{id}\u001f{component.DefinitionId}"] = component.Data;
        }
        return result;
    }

    private static Effect Link(string from, string to, string kind) =>
        new() { Type = EffectType.RelationshipCreate, EntityId = from, ToEntityId = to, Kind = kind, Data = "{}" };

    private static QuestSummary Summary(string id, int objectiveCount) => new(id, id, "active", $"Summary for {id}.", "gm",
        Enumerable.Range(1, objectiveCount).Select(order => new QuestObjectiveSummary($"{id}.objective.{order}", $"Objective {order}", "active", $"Act {order}.", order < 3, "gm", order, [])).ToList(),
        [], "Trusted-host view only.");

    private static async Task<ToolEnvelope> CommitAsync(Setup setup, string payload) => await new CommitTool().CommitAsync(
        procedures: null!, world: setup.World, effects: null!, mechanics: null!, eventTypes: null!, subscriptions: null!, actions: null!, itineraries: null!,
        campaigns: null!, campaignBootstrapper: null!, campaignContinuity: setup.Continuity, campaignSessions: null!, campaignSessionStarter: null!,
        quests: setup.Creator, questLifecycle: setup.Lifecycle, log: new OperationLog(setup.Db), notifications: null!, kind: "campaign", payload: payload,
        campaignQuestContexts: setup.Runner);

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

    private const string CampaignId = "campaign.test.sealed-observatory";
    private sealed record Setup(DantesRoleplayDbContext Db, WorldStore World, EventLedger Ledger, CampaignContinuityRunner Continuity, QuestCreator Creator,
        QuestLifecycleRunner Lifecycle, CampaignQuestContextRunner Runner, CampaignResumeReader Reader, string QuestId, string ArcId, string FirstChapterId, string ActiveChapterId);

    private sealed class FixedQuestSummaryReader(IReadOnlyDictionary<string, QuestSummary> summaries) : IQuestSummaryReader
    {
        public Task<QuestSummary?> GetAsync(string questId, CancellationToken cancellationToken = default) =>
            Task.FromResult(summaries.TryGetValue(questId, out var summary) ? summary : null);
    }

    private sealed class ThrowingOperationLog : IOperationLog
    {
        public Task<Operation> RecordAsync(string tool, string summary, bool success, string intent = "", string subject = "", IEnumerable<string>? proceduresCited = null,
            string error = "", bool consumesReadEvidence = false, CancellationToken cancellationToken = default, string mechanicId = "", int? mechanicVersion = null,
            long? seed = null, string projectionJson = "", string guardEvidenceJson = "", string id = "") => throw new InvalidOperationException("Injected audit failure.");
        public Task<IReadOnlyList<Operation>> RecentAsync(int limit = 20, bool failuresOnly = false, string? tool = null, string? subject = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Operation>>([]);
        public Task<IReadOnlyList<string>> RecentlyReadProceduresAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
