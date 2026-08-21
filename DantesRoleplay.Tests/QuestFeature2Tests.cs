using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.Quest;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class QuestFeature2Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"quest-q2-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Offers_then_accepts_the_q1_fixture_atomically_with_one_initial_objective()
    {
        var setup = await ArrangeAsync();
        var request = Request(setup.Continuity);
        Assert.True((await setup.Creator.CreateAsync(request)).Created);

        var offered = await setup.Lifecycle.TransitionAsync(new("offer", request.QuestId, "draft", "The host has presented the investigation."), "Offer the investigation.");
        var accepted = await setup.Lifecycle.TransitionAsync(new("accept", request.QuestId, "offered", "The party accepts the investigation."), "Accept the investigation.");

        Assert.True(offered.Succeeded);
        Assert.Equal(1, offered.StructuralEventCount);
        Assert.Empty(offered.ChangedObjectiveIds);
        Assert.True(accepted.Succeeded);
        Assert.Equal(2, accepted.StructuralEventCount);
        Assert.Equal(["quest.test.missing-margin.objective.trace-the-margin"], accepted.ChangedObjectiveIds);
        Assert.Equal("active", Status(await setup.World.GetEntityAsync(request.QuestId), "game.core.quest.root"));
        Assert.Equal("active", Status(await setup.World.GetEntityAsync("quest.test.missing-margin.objective.trace-the-margin"), "game.core.quest.objective"));
        Assert.Equal("dormant", Status(await setup.World.GetEntityAsync("quest.test.missing-margin.objective.test-the-witnesses"), "game.core.quest.objective"));
        Assert.Equal("dormant", Status(await setup.World.GetEntityAsync("quest.test.missing-margin.objective.read-the-seal"), "game.core.quest.objective"));
        Assert.Single(await setup.Ledger.FindAsync(rootOperationId: offered.OperationId));
        Assert.Equal(2, (await setup.Ledger.FindAsync(rootOperationId: accepted.OperationId)).Count);
        var audit = Assert.Single(await new OperationLog(setup.Db).RecentAsync(subject: request.QuestId), operation => operation.Id == accepted.OperationId);
        Assert.Equal("procedure.quest.modify", audit.ProceduresCited);
        Assert.Contains("The party accepts the investigation.", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_stale_or_invalid_requests_without_structural_state()
    {
        var setup = await ArrangeAsync();
        var request = Request(setup.Continuity);
        Assert.True((await setup.Creator.CreateAsync(request)).Created);
        var before = Status(await setup.World.GetEntityAsync(request.QuestId), "game.core.quest.root");

        var stale = await setup.Lifecycle.TransitionAsync(new("accept", request.QuestId, "offered", "The party accepts."));
        var invalidReason = await setup.Lifecycle.TransitionAsync(new("offer", request.QuestId, "draft", " reason "));
        var offer = await setup.Lifecycle.TransitionAsync(new("offer", request.QuestId, "draft", "The host has presented the investigation."));
        var replay = await setup.Lifecycle.TransitionAsync(new("offer", request.QuestId, "draft", "The host repeats the offer."));

        Assert.False(stale.Succeeded);
        Assert.Equal("STALE_QUEST_STATUS", Assert.Single(stale.Problems).Code);
        Assert.False(invalidReason.Succeeded);
        Assert.Equal("INVALID_LIFECYCLE_REQUEST", Assert.Single(invalidReason.Problems).Code);
        Assert.True(offer.Succeeded);
        Assert.False(replay.Succeeded);
        Assert.Equal("STALE_QUEST_STATUS", Assert.Single(replay.Problems).Code);
        Assert.Equal("offered", Status(await setup.World.GetEntityAsync(request.QuestId), "game.core.quest.root"));
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: stale.OperationId));
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: invalidReason.OperationId));
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: replay.OperationId));
        Assert.NotEqual(before, Status(await setup.World.GetEntityAsync(request.QuestId), "game.core.quest.root"));
    }

    [Fact]
    public async Task Completing_an_owned_objective_activates_all_newly_eligible_dependants_without_reconciling_the_quest()
    {
        var setup = await ArrangeAsync();
        var request = Request(setup.Continuity);
        Assert.True((await setup.Creator.CreateAsync(request)).Created);
        await ActivateAsync(setup, request.QuestId);
        const string trace = "quest.test.missing-margin.objective.trace-the-margin";
        const string witnesses = "quest.test.missing-margin.objective.test-the-witnesses";
        const string seal = "quest.test.missing-margin.objective.read-the-seal";

        var completed = await setup.Lifecycle.TransitionObjectiveAsync(new("set-objective", request.QuestId, "active", trace, "active", "completed", "The records establish the missing margin."), "Complete the trace objective.");
        var optional = await setup.Lifecycle.TransitionObjectiveAsync(new("set-objective", request.QuestId, "active", seal, "active", "completed", "The seal has been read."), "Complete the optional objective.");

        Assert.True(completed.Succeeded);
        Assert.Equal(3, completed.StructuralEventCount);
        Assert.Equal([trace, witnesses, seal], completed.ChangedObjectiveIds);
        Assert.Equal("completed", Status(await setup.World.GetEntityAsync(trace), "game.core.quest.objective"));
        Assert.Equal("active", Status(await setup.World.GetEntityAsync(witnesses), "game.core.quest.objective"));
        Assert.Equal("completed", Status(await setup.World.GetEntityAsync(seal), "game.core.quest.objective"));
        Assert.True(optional.Succeeded);
        Assert.Equal(1, optional.StructuralEventCount);
        Assert.Equal([seal], optional.ChangedObjectiveIds);
        Assert.Equal("active", Status(await setup.World.GetEntityAsync(request.QuestId), "game.core.quest.root"));
        Assert.Equal("procedure.quest.modify", (await new OperationLog(setup.Db).RecentAsync(subject: request.QuestId)).Single(operation => operation.Id == completed.OperationId).ProceduresCited);
    }

    [Fact]
    public async Task Blocks_and_unblocks_only_an_owned_active_or_blocked_objective_and_rejects_bad_objective_requests()
    {
        var setup = await ArrangeAsync();
        var request = Request(setup.Continuity);
        Assert.True((await setup.Creator.CreateAsync(request)).Created);
        await ActivateAsync(setup, request.QuestId);
        const string trace = "quest.test.missing-margin.objective.trace-the-margin";

        var blocked = await setup.Lifecycle.TransitionObjectiveAsync(new("set-objective", request.QuestId, "active", trace, "active", "blocked", "The archive is inaccessible."));
        var stale = await setup.Lifecycle.TransitionObjectiveAsync(new("set-objective", request.QuestId, "active", trace, "active", "failed", "A stale retry."));
        var foreign = await setup.Lifecycle.TransitionObjectiveAsync(new("set-objective", request.QuestId, "active", "quest.test.missing-margin.objective.unknown", "active", "failed", "A foreign objective."));
        var unblocked = await setup.Lifecycle.TransitionObjectiveAsync(new("unblock-objective", request.QuestId, "active", trace, "blocked", null, "The archive has reopened."));
        var illegal = await setup.Lifecycle.TransitionObjectiveAsync(new("set-objective", request.QuestId, "active", trace, "active", "active", "An invalid target."));

        Assert.True(blocked.Succeeded);
        Assert.Equal(1, blocked.StructuralEventCount);
        Assert.Equal([trace], blocked.ChangedObjectiveIds);
        Assert.Equal("STALE_OBJECTIVE_STATUS", Assert.Single(stale.Problems).Code);
        Assert.Equal("OBJECTIVE_NOT_IN_QUEST", Assert.Single(foreign.Problems).Code);
        Assert.True(unblocked.Succeeded);
        Assert.Equal(1, unblocked.StructuralEventCount);
        Assert.Equal("active", Status(await setup.World.GetEntityAsync(trace), "game.core.quest.objective"));
        Assert.Equal("ILLEGAL_OBJECTIVE_TARGET", Assert.Single(illegal.Problems).Code);
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: stale.OperationId));
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: foreign.OperationId));
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: illegal.OperationId));
    }

    [Fact]
    public async Task Reconcile_explicitly_fails_required_failure_completes_all_required_and_otherwise_changes_nothing()
    {
        var failed = await ArrangeAsync();
        var failedRequest = Request(failed.Continuity);
        Assert.True((await failed.Creator.CreateAsync(failedRequest)).Created);
        await ActivateAsync(failed, failedRequest.QuestId);
        var noChange = await failed.Lifecycle.TransitionAsync(new("reconcile", failedRequest.QuestId, "active", "The required objectives remain unresolved."));
        var failTrace = await failed.Lifecycle.TransitionObjectiveAsync(new("set-objective", failedRequest.QuestId, "active", "quest.test.missing-margin.objective.trace-the-margin", "active", "failed", "The records were destroyed."));
        var reconciledFailure = await failed.Lifecycle.TransitionAsync(new("reconcile", failedRequest.QuestId, "active", "A required objective has failed."));

        Assert.Equal("NO_RECONCILIATION_CHANGE", Assert.Single(noChange.Problems).Code);
        Assert.True(failTrace.Succeeded);
        Assert.True(reconciledFailure.Succeeded);
        Assert.Equal(1, reconciledFailure.StructuralEventCount);
        Assert.Equal("failed", Status(await failed.World.GetEntityAsync(failedRequest.QuestId), "game.core.quest.root"));
        Assert.Empty(await failed.Ledger.FindAsync(rootOperationId: noChange.OperationId));

        Assert.True((await failed.Lifecycle.TransitionAsync(new("reopen-quest", failedRequest.QuestId, "failed", "New evidence restores the investigation."))).Succeeded);
        Assert.True((await failed.Lifecycle.TransitionObjectiveAsync(new("reopen-objective", failedRequest.QuestId, "active", "quest.test.missing-margin.objective.trace-the-margin", "failed", null, "The destroyed records have been recovered."))).Succeeded);
        Assert.True((await failed.Lifecycle.TransitionObjectiveAsync(new("set-objective", failedRequest.QuestId, "active", "quest.test.missing-margin.objective.trace-the-margin", "active", "completed", "The margin is established."))).Succeeded);
        Assert.True((await failed.Lifecycle.TransitionObjectiveAsync(new("set-objective", failedRequest.QuestId, "active", "quest.test.missing-margin.objective.test-the-witnesses", "active", "completed", "The witness accounts are settled."))).Succeeded);
        var reconciledCompletion = await failed.Lifecycle.TransitionAsync(new("reconcile", failedRequest.QuestId, "active", "Every required objective is complete."));

        Assert.True(reconciledCompletion.Succeeded);
        Assert.Equal(1, reconciledCompletion.StructuralEventCount);
        Assert.Equal("completed", Status(await failed.World.GetEntityAsync(failedRequest.QuestId), "game.core.quest.root"));
        Assert.Equal("active", Status(await failed.World.GetEntityAsync("quest.test.missing-margin.objective.read-the-seal"), "game.core.quest.objective"));
    }

    [Fact]
    public async Task Root_terminal_corrections_and_objective_reopening_are_closed_and_guard_completed_dependants()
    {
        var setup = await ArrangeAsync();
        var request = Request(setup.Continuity);
        Assert.True((await setup.Creator.CreateAsync(request)).Created);
        await ActivateAsync(setup, request.QuestId);
        var failed = await setup.Lifecycle.TransitionAsync(new("fail", request.QuestId, "active", "The investigation can no longer continue."));
        var reopenedQuest = await setup.Lifecycle.TransitionAsync(new("reopen-quest", request.QuestId, "failed", "New evidence restores the investigation."));

        Assert.True(failed.Succeeded);
        Assert.Equal(1, failed.StructuralEventCount);
        Assert.True(reopenedQuest.Succeeded);
        Assert.Equal("active", Status(await setup.World.GetEntityAsync(request.QuestId), "game.core.quest.root"));

        Assert.True((await setup.Lifecycle.TransitionObjectiveAsync(new("set-objective", request.QuestId, "active", "quest.test.missing-margin.objective.trace-the-margin", "active", "completed", "The margin is established."))).Succeeded);
        Assert.True((await setup.Lifecycle.TransitionObjectiveAsync(new("set-objective", request.QuestId, "active", "quest.test.missing-margin.objective.read-the-seal", "active", "completed", "The seal is read."))).Succeeded);
        var reopenOptional = await setup.Lifecycle.TransitionObjectiveAsync(new("reopen-objective", request.QuestId, "active", "quest.test.missing-margin.objective.read-the-seal", "completed", null, "The seal interpretation must be revisited."));
        Assert.True((await setup.Lifecycle.TransitionObjectiveAsync(new("set-objective", request.QuestId, "active", "quest.test.missing-margin.objective.test-the-witnesses", "active", "completed", "The witnesses are settled."))).Succeeded);
        var denied = await setup.Lifecycle.TransitionObjectiveAsync(new("reopen-objective", request.QuestId, "active", "quest.test.missing-margin.objective.trace-the-margin", "completed", null, "The foundational finding is challenged."));

        Assert.True(reopenOptional.Succeeded);
        Assert.Equal(1, reopenOptional.StructuralEventCount);
        Assert.Equal("active", Status(await setup.World.GetEntityAsync("quest.test.missing-margin.objective.read-the-seal"), "game.core.quest.objective"));
        Assert.Equal("OBJECTIVE_HAS_COMPLETED_DEPENDANT", Assert.Single(denied.Problems).Code);
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: denied.OperationId));
    }

    [Fact]
    public async Task Archive_is_available_only_for_an_unaccepted_offer()
    {
        var setup = await ArrangeAsync();
        var request = Request(setup.Continuity);
        Assert.True((await setup.Creator.CreateAsync(request)).Created);
        Assert.True((await setup.Lifecycle.TransitionAsync(new("offer", request.QuestId, "draft", "The host has presented the investigation."))).Succeeded);

        var archived = await setup.Lifecycle.TransitionAsync(new("archive", request.QuestId, "offered", "The offer is withdrawn."));
        var replay = await setup.Lifecycle.TransitionAsync(new("archive", request.QuestId, "offered", "A stale retry."));

        Assert.True(archived.Succeeded);
        Assert.Equal(1, archived.StructuralEventCount);
        Assert.Equal("archived", Status(await setup.World.GetEntityAsync(request.QuestId), "game.core.quest.root"));
        Assert.Equal("STALE_QUEST_STATUS", Assert.Single(replay.Problems).Code);
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: replay.OperationId));
    }

    [Fact]
    public async Task Quest_summary_is_a_bounded_verified_active_quest_projection()
    {
        var setup = await ArrangeAsync();
        var request = Request(setup.Continuity);
        Assert.True((await setup.Creator.CreateAsync(request)).Created);
        await ActivateAsync(setup, request.QuestId);
        Assert.True((await setup.Lifecycle.TransitionObjectiveAsync(new("set-objective", request.QuestId, "active", "quest.test.missing-margin.objective.trace-the-margin", "active", "completed", "The records establish the margin."))).Succeeded);

        var reader = new QuestSummaryReader(setup.World, setup.Ledger);
        var summary = await reader.GetAsync(request.QuestId);

        Assert.NotNull(summary);
        Assert.Equal("active", summary.Status);
        Assert.Equal([1, 2, 3], summary.Objectives.Select(x => x.DisplayOrder));
        Assert.Equal("completed", summary.Objectives[0].Status);
        Assert.Equal(["fact.feature-04.toll-ledger"], summary.Objectives[0].Evidence.Select(x => x.TargetId));
        Assert.Equal(["actor.feature-03.mara-vell"], summary.Objectives[1].Evidence.Select(x => x.TargetId));
        Assert.Equal(6, summary.RecentTransitions.Count);
        Assert.All(summary.RecentTransitions, transition => Assert.True(transition.RecordKind is "quest" or "objective"));
        Assert.Equal(summary.RecentTransitions.Select(x => x.EventId).Distinct(), summary.RecentTransitions.Select(x => x.EventId));
        Assert.Equal("Trusted-host view only. Descriptive visibility is editorial metadata, not authorization.", summary.TrustBoundary);

        var envelope = await new QueryTool().QueryAsync(
            procedures: null!, world: setup.World, graphs: null!, journeys: null!, itineraries: null!, campaignResumes: null!, questSummaries: reader,
            mechanics: null!, eventTypes: null!, subscriptions: null!, events: setup.Ledger, log: new OperationLog(setup.Db), notifications: null!, kind: "quest-summary", id: request.QuestId);
        Assert.True(envelope.Ok, JsonSerializer.Serialize(envelope));
        Assert.IsType<QuestSummary>(envelope.Data);
        Assert.Equal("query", (await new OperationLog(setup.Db).RecentAsync(subject: request.QuestId)).First().Tool);

        var rejected = await new QueryTool().QueryAsync(
            procedures: null!, world: setup.World, graphs: null!, journeys: null!, itineraries: null!, campaignResumes: null!, questSummaries: reader,
            mechanics: null!, eventTypes: null!, subscriptions: null!, events: setup.Ledger, log: new OperationLog(setup.Db), notifications: null!, kind: "quest-summary", id: request.QuestId, limit: 1);
        Assert.False(rejected.Ok);
        Assert.Equal("INVALID_QUEST_SUMMARY_QUERY", rejected.Error?.Code);
        Assert.Equal("query(kind: \"quest-summary\", id: \"quest....\")", rejected.Error?.Fix);
    }

    [Fact]
    public async Task Public_surface_accepts_only_closed_lifecycle_payloads()
    {
        var setup = await ArrangeAsync();
        var request = Request(setup.Continuity);
        Assert.True((await setup.Creator.CreateAsync(request)).Created);

        await ActivateAsync(setup, request.QuestId);
        var accepted = await CommitAsync(setup, JsonSerializer.Serialize(new
        {
            operation = "set-objective",
            questId = request.QuestId,
            expectedQuestStatus = "active",
            objectiveId = "quest.test.missing-margin.objective.trace-the-margin",
            expectedObjectiveStatus = "active",
            targetStatus = "blocked",
            reason = "The archive access is blocked."
        }));
        var rejected = await CommitAsync(setup, JsonSerializer.Serialize(new
        {
            operation = "set-objective",
            questId = request.QuestId,
            expectedQuestStatus = "active",
            objectiveId = "quest.test.missing-margin.objective.trace-the-margin",
            expectedObjectiveStatus = "blocked",
            targetStatus = "active",
            reason = "The host attempts an unsupported target.",
            extra = true
        }));
        var terminal = await CommitAsync(setup, JsonSerializer.Serialize(new
        {
            operation = "fail",
            questId = request.QuestId,
            expectedQuestStatus = "active",
            reason = "The investigation is abandoned."
        }));

        Assert.True(accepted.Ok);
        Assert.False(rejected.Ok);
        Assert.True(terminal.Ok);
        Assert.Equal("INVALID_PAYLOAD", rejected.Error?.Code);
        Assert.StartsWith("commit(kind: \"quest\", payload: ", rejected.Error?.Fix, StringComparison.Ordinal);
        Assert.Equal("failed", Status(await setup.World.GetEntityAsync(request.QuestId), "game.core.quest.root"));
    }

    private static async Task ActivateAsync(Setup setup, string questId)
    {
        Assert.True((await setup.Lifecycle.TransitionAsync(new("offer", questId, "draft", "The host has presented the investigation."))).Succeeded);
        Assert.True((await setup.Lifecycle.TransitionAsync(new("accept", questId, "offered", "The party accepts the investigation."))).Succeeded);
    }

    private static async Task<ToolEnvelope> CommitAsync(Setup setup, string payload) => await new CommitTool().CommitAsync(
        procedures: null!, world: null!, effects: null!, mechanics: null!, eventTypes: null!, subscriptions: null!, actions: null!, itineraries: null!,
        campaigns: null!, campaignBootstrapper: null!, campaignContinuity: null!, campaignSessions: null!, campaignSessionStarter: null!, quests: setup.Creator, questLifecycle: setup.Lifecycle,
        log: new OperationLog(setup.Db), notifications: null!, kind: "quest", payload: payload);

    private async Task<Setup> ArrangeAsync()
    {
        CopyCatalog(CatalogRoot(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var importer = new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world);
        Assert.False((await importer.ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var ledger = new EventLedger(db);
        await new EventTypeSeeder(new EventTypeStore(db)).SeedAsync();
        var blueprint = Blueprint();
        var bootstrapper = new CampaignBootstrapper(db, new CampaignBlueprintValidator(world), new EffectApplier(db, world, null, ledger), new OperationLog(db));
        var review = await new CampaignBlueprintValidator(world).ValidateAsync(blueprint);
        Assert.True((await bootstrapper.CreateAsync(blueprint, review.ReviewFingerprint!)).Created);
        var continuity = await new CampaignContinuityRunner(db, world, new EffectApplier(db, world, null, ledger), new OperationLog(db)).InitializeAsync(
            new CampaignContinuitySeed(blueprint.CampaignId, new CampaignChapterSeed("chapter.opening", "The Ledger Signal", "What does the ledger reveal?"), new CampaignArcSeed("arc.observatory", "The Observatory's Claim", "Can history avoid becoming leverage?")));
        Assert.True(continuity.Succeeded);
        return new(db, world, ledger, continuity, new QuestCreator(db, world, new EffectApplier(db, world, null, ledger), new OperationLog(db)), new QuestLifecycleRunner(db, world, new EffectApplier(db, world, null, ledger), new OperationLog(db)));
    }

    private static string Status(EntitySnapshot? entity, string definitionId)
    {
        var component = Assert.Single(entity!.Components, component => component.DefinitionId == definitionId);
        using var document = JsonDocument.Parse(component.Data);
        return document.RootElement.GetProperty("status").GetString()!;
    }

    private static QuestCreateRequest Request(CampaignContinuityResult continuity) => new(
        "quest.test.missing-margin", "The Missing Margin", "Find why the observatory signal matters.", "An open investigation.", "party",
        "campaign.test.sealed-observatory", continuity.ArcId!, [continuity.ChapterId!],
        [
            new QuestObjectiveInput("objective.trace-the-margin", "Trace the Missing Margin", "Compare records.", true, "party", 1, [], [new QuestReference("fact.feature-04.toll-ledger", "knowledge", "party")]),
            new QuestObjectiveInput("objective.test-the-witnesses", "Test the Witnesses", "Compare accounts.", true, "party", 2, ["objective.trace-the-margin"], [new QuestReference("actor.feature-03.mara-vell", "actor", "party")]),
            new QuestObjectiveInput("objective.read-the-seal", "Read the Seal", "Inspect the optional physical lead.", false, "gm", 3, ["objective.trace-the-margin"], [new QuestReference("clue.feature-04.ledger-seal", "knowledge", "gm")])
        ]);

    private static CampaignBlueprint Blueprint() => new(
        "campaign.test.sealed-observatory", "The Sealed Observatory", "A signal threatens old market records.", ["Reach the archive."], ["Curious mystery."], "dnd2024", "world.feature-01.fixture", "location.feature-01.gate",
        [new CampaignReference("location.feature-01.gate", "start", "party"), new CampaignReference("actor.feature-03.mara-vell", "npc", "party"), new CampaignReference("actor.feature-03.oren-dale", "npc", "gm"), new CampaignReference("faction.feature-03.fixture", "faction-stake", "party"), new CampaignReference("fact.feature-04.toll-ledger", "knowledge", "party"), new CampaignReference("rumour.feature-04.observatory-signal", "knowledge", "party")],
        new CampaignChapter("chapter.opening", "What does the ledger reveal?"), new CampaignArc("arc.observatory", "Can history avoid leverage?"));

    private static string CatalogRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }

    private static void CopyCatalog(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    private sealed record Setup(DantesRoleplayDbContext Db, IWorldStore World, EventLedger Ledger, CampaignContinuityResult Continuity, QuestCreator Creator, QuestLifecycleRunner Lifecycle);
}
