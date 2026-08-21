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
        return new(db, world, ledger, campaignId, sessionValidator, new CampaignSessionStarter(db, sessionValidator, new EffectApplier(db, world, null, ledger), new OperationLog(db)));
    }

    private static string Catalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file))); }
    private sealed record Setup(DantesRoleplayDbContext Db, WorldStore World, EventLedger Ledger, string CampaignId, CampaignSessionValidator Validator, CampaignSessionStarter Starter);
}
