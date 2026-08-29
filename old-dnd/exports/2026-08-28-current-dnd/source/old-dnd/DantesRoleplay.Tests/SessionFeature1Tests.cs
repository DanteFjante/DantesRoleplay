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
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class SessionFeature1Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"session-feature-01-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Validates_the_next_C3_session_without_creating_state_and_rejects_an_active_session()
    {
        var setup = await ArrangeAsync();
        var request = new CampaignSessionValidationRequest("validate-session", setup.CampaignId, "session.test.sealed-observatory.opening");
        var before = await setup.World.FindEntitiesAsync(withDefinitionId: "game.core.campaign.session");

        var preview = await new CommitTool().CommitAsync(
            procedures: null!, world: null!, effects: null!, mechanics: null!, eventTypes: null!, subscriptions: null!, actions: null!, itineraries: null!,
            campaigns: null!, campaignBootstrapper: null!, campaignContinuity: null!, campaignSessions: setup.Validator, campaignSessionStarter: null!,
            quests: null!, questLifecycle: null!, log: new OperationLog(setup.Db), notifications: null!, kind: "campaign", payload: JsonSerializer.Serialize(new { operation = request.Operation, campaignId = request.CampaignId, sessionId = request.SessionId }));

        Assert.True(preview.Ok, JsonSerializer.Serialize(preview));
        var result = Assert.IsType<CampaignSessionValidationResult>(preview.Data);
        Assert.True(result.Valid); Assert.Equal(1, result.Ordinal);
        Assert.Empty(before);
        Assert.Empty(await setup.World.FindEntitiesAsync(withDefinitionId: "game.core.campaign.session"));
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: preview.OperationId));

        var activeId = "session.test.sealed-observatory.active";
        await setup.World.CreateEntityAsync("Existing session", activeId);
        await setup.World.SetComponentAsync(activeId, "game.core.campaign.session", "{\"status\":\"active\",\"ordinal\":1}");
        await setup.World.RelateAsync(setup.CampaignId, activeId, "game.core.campaign.has-session");

        var rejected = await setup.Validator.ValidateAsync(request);

        Assert.False(rejected.Valid);
        Assert.Equal("ACTIVE_SESSION_EXISTS", Assert.Single(rejected.Problems).Code);
        Assert.Equal(activeId, Assert.Single(await setup.World.FindEntitiesAsync(withDefinitionId: "game.core.campaign.session")).Id);
    }

    [Fact]
    public async Task Starts_one_session_atomically_and_a_fresh_host_derives_the_active_record()
    {
        var setup = await ArrangeAsync();
        var sessionId = "session.test.sealed-observatory.opening";
        var request = new CampaignSessionValidationRequest("start-session", setup.CampaignId, sessionId);
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.Starter.StartAsync(request, cancellationToken: canceled.Token));
        }
        Assert.Null(await setup.World.GetEntityAsync(sessionId));

        var started = await new CommitTool().CommitAsync(
            procedures: null!, world: null!, effects: null!, mechanics: null!, eventTypes: null!, subscriptions: null!, actions: null!, itineraries: null!,
            campaigns: null!, campaignBootstrapper: null!, campaignContinuity: null!, campaignSessions: null!, campaignSessionStarter: setup.Starter,
            quests: null!, questLifecycle: null!, log: new OperationLog(setup.Db), notifications: null!, kind: "campaign", payload: JsonSerializer.Serialize(new { operation = request.Operation, campaignId = request.CampaignId, sessionId = request.SessionId }));

        Assert.True(started.Ok, JsonSerializer.Serialize(started));
        using (var response = JsonDocument.Parse(JsonSerializer.Serialize(started.Data)))
        {
            Assert.Equal(setup.CampaignId, response.RootElement.GetProperty("CampaignId").GetString());
            Assert.Equal(sessionId, response.RootElement.GetProperty("SessionId").GetString());
            Assert.Equal("active", response.RootElement.GetProperty("Status").GetString());
            Assert.Equal(1, response.RootElement.GetProperty("Ordinal").GetInt32());
            Assert.True(response.RootElement.GetProperty("ResumeAvailable").GetBoolean());
        }
        var session = Assert.IsType<EntitySnapshot>(await setup.World.GetEntityAsync(sessionId));
        Assert.Equal("Session 1", session.Name);
        using (var state = JsonDocument.Parse(Assert.Single(session.Components).Data))
        {
            Assert.Equal("active", state.RootElement.GetProperty("status").GetString());
            Assert.Equal(1, state.RootElement.GetProperty("ordinal").GetInt32());
        }
        var link = Assert.Single(await setup.World.GetRelationshipsAsync(setup.CampaignId, false), value => value.Kind == "game.core.campaign.has-session");
        Assert.Equal(sessionId, link.ToEntityId); Assert.Equal("{}", link.Data);
        Assert.Equal(3, (await setup.Ledger.FindAsync(rootOperationId: started.OperationId)).Count);

        var freshHost = new CampaignSessionValidator(setup.World, new CampaignResumeReader(setup.World, setup.Ledger));
        var active = await freshHost.ValidateAsync(request with { Operation = "validate-session", SessionId = "session.test.sealed-observatory.next" });
        Assert.False(active.Valid); Assert.Equal("ACTIVE_SESSION_EXISTS", Assert.Single(active.Problems).Code);
        var replay = await setup.Starter.StartAsync(request);
        Assert.False(replay.Started); Assert.Equal("SESSION_ID_TAKEN", Assert.Single(replay.Problems).Code);
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: replay.OperationId));
    }

    [Fact]
    public async Task Resumes_one_active_session_with_current_C3_context_without_structural_effects()
    {
        var setup = await ArrangeAsync();
        var reader = new CampaignSessionResumeReader(setup.World, new CampaignResumeReader(setup.World, setup.Ledger));
        var unavailable = await reader.GetAsync(setup.CampaignId);
        Assert.False(unavailable.Resumed); Assert.Equal("NO_ACTIVE_SESSION", Assert.Single(unavailable.Problems).Code);
        var started = await setup.Starter.StartAsync(new("start-session", setup.CampaignId, "session.test.sealed-observatory.opening"));
        Assert.True(started.Started);
        var events = await setup.Ledger.FindAsync(limit: 100);

        var resumed = await new QueryTool().QueryAsync(
            procedures: null!, world: setup.World, graphs: null!, journeys: null!, itineraries: null!, campaignResumes: new CampaignResumeReader(setup.World, setup.Ledger), questSummaries: null!,
            mechanics: null!, eventTypes: null!, subscriptions: null!, events: setup.Ledger, log: new OperationLog(setup.Db), notifications: null!, kind: "campaign-resume", id: setup.CampaignId,
            includeSession: true, campaignSessionResumes: reader);

        Assert.True(resumed.Ok, JsonSerializer.Serialize(resumed));
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(resumed.Data));
        Assert.Equal("session.test.sealed-observatory.opening", payload.RootElement.GetProperty("Session").GetProperty("SessionId").GetString());
        Assert.Equal("active", payload.RootElement.GetProperty("Session").GetProperty("Status").GetString());
        Assert.Equal(setup.CampaignId, payload.RootElement.GetProperty("Campaign").GetProperty("CampaignId").GetString());
        Assert.Equal("The Ledger Signal", payload.RootElement.GetProperty("Campaign").GetProperty("CurrentChapter").GetProperty("Title").GetString());
        Assert.Equal(events.Count, (await setup.Ledger.FindAsync(limit: 100)).Count);
        var fresh = await new CampaignSessionResumeReader(setup.World, new CampaignResumeReader(setup.World, setup.Ledger)).GetAsync(setup.CampaignId);
        Assert.True(fresh.Resumed); Assert.Equal("session.test.sealed-observatory.opening", fresh.Session!.SessionId);
    }

    [Fact]
    public async Task Validates_a_C3_only_factual_session_closure_without_writing_a_recap_or_lifecycle_change()
    {
        var setup = await ArrangeAsync();
        var sessionId = "session.test.sealed-observatory.opening";
        Assert.True((await setup.Starter.StartAsync(new("start-session", setup.CampaignId, sessionId))).Started);
        var advance = await setup.Continuity.AdvanceAsync(
            setup.CampaignId,
            $"{setup.CampaignId}.chapter.opening",
            "active",
            "The party confirmed the toll ledger's observatory signal.",
            new("chapter.second", "The Archive Bargain", "Who can safely receive the verified signal?"));
        Assert.True(advance.Succeeded);
        var validator = new CampaignSessionEndValidator(setup.World, new CampaignSessionResumeReader(setup.World, new CampaignResumeReader(setup.World, setup.Ledger)));
        var request = new CampaignSessionEndRequest("validate-session-end", sessionId, "active");
        var eventsBefore = await setup.Ledger.FindAsync(limit: 100);
        var source = await new CampaignSessionResumeReader(setup.World, new CampaignResumeReader(setup.World, setup.Ledger)).GetAsync(setup.CampaignId);
        Assert.True(source.Resumed, JsonSerializer.Serialize(source));
        Assert.NotNull(source.Campaign!.CurrentChapter); Assert.NotNull(source.Campaign.CurrentArc);
        Assert.Equal("active", source.Campaign.CurrentChapter.Status); Assert.Equal("active", source.Campaign.CurrentArc.Status);
        var c3Milestone = Assert.Single(source.Campaign.RecentMilestones);
        Assert.NotEqual(default, c3Milestone.Timestamp); Assert.True(c3Milestone.Sequence >= 0);

        var preview = await new CommitTool().CommitAsync(
            procedures: null!, world: null!, effects: null!, mechanics: null!, eventTypes: null!, subscriptions: null!, actions: null!, itineraries: null!,
            campaigns: null!, campaignBootstrapper: null!, campaignContinuity: null!, campaignSessions: null!, campaignSessionStarter: null!,
            quests: null!, questLifecycle: null!, log: new OperationLog(setup.Db), notifications: null!, kind: "campaign",
            payload: JsonSerializer.Serialize(new { operation = request.Operation, sessionId = request.SessionId, expectedStatus = request.ExpectedStatus }),
            campaignSessionEndValidator: validator);

        Assert.True(preview.Ok, JsonSerializer.Serialize(preview));
        using (var output = JsonDocument.Parse(JsonSerializer.Serialize(preview.Data)))
        {
            Assert.Equal(sessionId, output.RootElement.GetProperty("SessionId").GetString());
            Assert.Equal(setup.CampaignId, output.RootElement.GetProperty("CampaignId").GetString());
            Assert.True(output.RootElement.GetProperty("PreviewAvailable").GetBoolean());
            Assert.Equal(["arc", "chapter", "milestones"], output.RootElement.GetProperty("RecapSectionKeys").EnumerateArray().Select(value => value.GetString()!).ToArray());
            Assert.False(output.RootElement.TryGetProperty("Recap", out _));
        }
        var resolved = await validator.ValidateAsync(request);
        Assert.True(resolved.Valid);
        Assert.Equal("session.s0.c3-only.v1", resolved.Recap!.ProtocolVersion);
        Assert.Equal("The Archive Bargain", resolved.Recap.Chapter.Title);
        Assert.Equal("The Observatory's Claim", resolved.Recap.Arc.Title);
        var milestone = Assert.Single(resolved.Recap.Milestones);
        Assert.Equal("The Ledger Signal", milestone.Title);
        Assert.DoesNotContain("EventId", JsonSerializer.Serialize(resolved.Recap), StringComparison.Ordinal);

        var session = Assert.IsType<EntitySnapshot>(await setup.World.GetEntityAsync(sessionId));
        using var lifecycle = JsonDocument.Parse(Assert.Single(session.Components).Data);
        Assert.Equal("active", lifecycle.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain(session.Components, component => component.DefinitionId == "game.core.campaign.session-recap");
        Assert.Equal(eventsBefore.Count, (await setup.Ledger.FindAsync(limit: 100)).Count);
        var stale = await validator.ValidateAsync(request with { ExpectedStatus = "ended" });
        Assert.False(stale.Valid); Assert.Equal("INVALID_SESSION_END_REQUEST", Assert.Single(stale.Problems).Code);
        var fresh = new CampaignSessionEndValidator(setup.World, new CampaignSessionResumeReader(setup.World, new CampaignResumeReader(setup.World, setup.Ledger)));
        Assert.True((await fresh.ValidateAsync(request)).Valid);
    }

    [Fact]
    public async Task Ends_one_session_atomically_and_reads_its_immutable_factual_recap()
    {
        var setup = await ArrangeAsync();
        var sessionId = "session.test.sealed-observatory.opening";
        Assert.True((await setup.Starter.StartAsync(new("start-session", setup.CampaignId, sessionId))).Started);
        Assert.True((await setup.Continuity.AdvanceAsync(
            setup.CampaignId, $"{setup.CampaignId}.chapter.opening", "active",
            "The party confirmed the toll ledger's observatory signal.",
            new("chapter.second", "The Archive Bargain", "Who can safely receive the verified signal?"))).Succeeded);
        var request = new CampaignSessionEndRequest("end-session", sessionId, "active");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.Ender.EndAsync(request, cancellationToken: canceled.Token));
        }
        Assert.False((await setup.Recaps.GetAsync(sessionId)).Found);

        var ended = await new CommitTool().CommitAsync(
            procedures: null!, world: null!, effects: null!, mechanics: null!, eventTypes: null!, subscriptions: null!, actions: null!, itineraries: null!,
            campaigns: null!, campaignBootstrapper: null!, campaignContinuity: null!, campaignSessions: null!, campaignSessionStarter: null!,
            quests: null!, questLifecycle: null!, log: new OperationLog(setup.Db), notifications: null!, kind: "campaign",
            payload: JsonSerializer.Serialize(new { operation = request.Operation, sessionId = request.SessionId, expectedStatus = request.ExpectedStatus }),
            campaignSessionEnder: setup.Ender);

        Assert.True(ended.Ok, JsonSerializer.Serialize(ended));
        using (var output = JsonDocument.Parse(JsonSerializer.Serialize(ended.Data)))
        {
            Assert.Equal("active", output.RootElement.GetProperty("PreviousStatus").GetString());
            Assert.Equal("ended", output.RootElement.GetProperty("CurrentStatus").GetString());
            Assert.True(output.RootElement.GetProperty("RecapPresent").GetBoolean());
            Assert.Equal(["arc", "chapter", "milestones"], output.RootElement.GetProperty("RecapSectionKeys").EnumerateArray().Select(value => value.GetString()!).ToArray());
            Assert.False(output.RootElement.TryGetProperty("Recap", out _));
        }
        Assert.Equal(2, (await setup.Ledger.FindAsync(rootOperationId: ended.OperationId)).Count);
        var session = Assert.IsType<EntitySnapshot>(await setup.World.GetEntityAsync(sessionId));
        var lifecycleData = Assert.Single(session.Components, component => component.DefinitionId == "game.core.campaign.session").Data;
        using (var lifecycle = JsonDocument.Parse(lifecycleData)) Assert.Equal("ended", lifecycle.RootElement.GetProperty("status").GetString());
        Assert.Single(session.Components, component => component.DefinitionId == "game.core.campaign.session-recap");

        var historical = await new QueryTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, journeys: null!, itineraries: null!, campaignResumes: null!, questSummaries: null!, mechanics: null!, eventTypes: null!, subscriptions: null!, events: setup.Ledger, log: new OperationLog(setup.Db), notifications: null!,
            kind: "session-recap", id: sessionId, campaignSessionRecaps: setup.Recaps);
        Assert.True(historical.Ok, JsonSerializer.Serialize(historical));
        using (var output = JsonDocument.Parse(JsonSerializer.Serialize(historical.Data)))
        {
            Assert.Equal(setup.CampaignId, output.RootElement.GetProperty("CampaignId").GetString());
            var recap = output.RootElement.GetProperty("Recap");
            Assert.Equal("session.s0.c3-only.v1", recap.GetProperty("ProtocolVersion").GetString());
            Assert.Equal("The Archive Bargain", recap.GetProperty("Chapter").GetProperty("Title").GetString());
            Assert.False(JsonSerializer.Serialize(historical.Data).Contains("EventId", StringComparison.Ordinal));
        }
        var filteredHistorical = await new QueryTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, journeys: null!, itineraries: null!, campaignResumes: null!, questSummaries: null!, mechanics: null!, eventTypes: null!, subscriptions: null!, events: setup.Ledger, log: new OperationLog(setup.Db), notifications: null!,
            kind: "session-recap", id: sessionId, limit: 1, campaignSessionRecaps: setup.Recaps);
        Assert.False(filteredHistorical.Ok); Assert.Equal("INVALID_SESSION_RECAP_QUERY", filteredHistorical.Error!.Code);
        var active = await new CampaignSessionResumeReader(setup.World, new CampaignResumeReader(setup.World, setup.Ledger)).GetAsync(setup.CampaignId);
        Assert.False(active.Resumed); Assert.Equal("NO_ACTIVE_SESSION", Assert.Single(active.Problems).Code);
        var replay = await setup.Ender.EndAsync(request);
        Assert.False(replay.Ended); Assert.Equal("STALE_SESSION_STATUS", Assert.Single(replay.Problems).Code);
        Assert.Empty(await setup.Ledger.FindAsync(rootOperationId: replay.OperationId));
        Assert.True((await new CampaignSessionRecapReader(setup.World).GetAsync(sessionId)).Found);
    }

    private async Task<Setup> ArrangeAsync()
    {
        Copy(Catalog(), _catalogCopy);
        var db = _fixture.CreateContext(); var world = new WorldStore(db);
        var import = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(import.Aborted);
        var ledger = new EventLedger(db); await new EventTypeSeeder(new EventTypeStore(db)).SeedAsync();
        var campaignId = "campaign.test.sealed-observatory";
        var blueprint = new CampaignBlueprint(campaignId, "The Sealed Observatory", "A strange signal from the sealed observatory threatens the old market records.", ["Reach the market archive.", "Choose whom to trust with the signal."], ["Curious local mystery."], "dnd2024", "world.feature-01.fixture", "location.feature-01.gate", [new("location.feature-01.gate", "start", "party"), new("actor.feature-03.mara-vell", "npc", "party"), new("actor.feature-03.oren-dale", "npc", "gm"), new("faction.feature-03.fixture", "faction-stake", "party"), new("fact.feature-04.toll-ledger", "knowledge", "party"), new("rumour.feature-04.observatory-signal", "knowledge", "party")], new("chapter.opening", "What does the old toll ledger reveal?"), new("arc.observatory", "Can the observatory's history be kept from becoming leverage?"));
        var campaignValidator = new CampaignBlueprintValidator(world);
        var bootstrapper = new CampaignBootstrapper(db, campaignValidator, new EffectApplier(db, world, null, ledger), new OperationLog(db));
        var review = await campaignValidator.ValidateAsync(blueprint); Assert.True((await bootstrapper.CreateAsync(blueprint, review.ReviewFingerprint!)).Created);
        var continuity = new CampaignContinuityRunner(db, world, new EffectApplier(db, world, null, ledger), new OperationLog(db));
        Assert.True((await continuity.InitializeAsync(new CampaignContinuitySeed(campaignId, new("chapter.opening", "The Ledger Signal", "What does the old toll ledger reveal about the observatory signal?"), new("arc.observatory", "The Observatory's Claim", "Can the group keep the observatory's history from becoming another source of leverage?")))).Succeeded);
        var sessionValidator = new CampaignSessionValidator(world, new CampaignResumeReader(world, ledger));
        var sessionResumes = new CampaignSessionResumeReader(world, new CampaignResumeReader(world, ledger));
        var endValidator = new CampaignSessionEndValidator(world, sessionResumes);
        return new(db, world, ledger, campaignId, sessionValidator, new CampaignSessionStarter(db, sessionValidator, new EffectApplier(db, world, null, ledger), new OperationLog(db)), continuity, new CampaignSessionEnder(db, endValidator, new EffectApplier(db, world, null, ledger), new OperationLog(db)), new CampaignSessionRecapReader(world));
    }

    private static string Catalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file))); }
    private sealed record Setup(DantesRoleplayDbContext Db, WorldStore World, EventLedger Ledger, string CampaignId, CampaignSessionValidator Validator, CampaignSessionStarter Starter, CampaignContinuityRunner Continuity, CampaignSessionEnder Ender, CampaignSessionRecapReader Recaps);
}
