using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.Tests;

public sealed class CampaignFeature2Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _copy = Path.Combine(Path.GetTempPath(), $"campaign-feature-02-{Guid.NewGuid():n}");
    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, true); }

    [Fact]
    public async Task Reviewed_blueprint_creates_only_the_closed_campaign_graph_in_one_root_operation()
    {
        var (db, world, runner, ledger) = await ImportAsync();
        var blueprint = Blueprint(); var review = await new CampaignBlueprintValidator(world).ValidateAsync(blueprint);

        var created = await runner.CreateAsync(blueprint, review.ReviewFingerprint!, "Create the reviewed test campaign.", ["procedure.campaign.create"]);

        Assert.True(created.Created, JsonSerializer.Serialize(created)); Assert.Equal(6, created.ReferenceCount); Assert.Equal(9, created.StructuralEventCount);
        var campaign = await world.GetEntityAsync(blueprint.CampaignId); Assert.NotNull(campaign);
        var root = Assert.Single(campaign!.Components, x => x.DefinitionId == "game.core.campaign.root");
        using var data = JsonDocument.Parse(root.Data);
        Assert.Equal("active", data.RootElement.GetProperty("status").GetString()); Assert.Equal(review.ReviewFingerprint, data.RootElement.GetProperty("reviewFingerprint").GetString());
        Assert.False(data.RootElement.TryGetProperty("existingWorldId", out _)); Assert.False(data.RootElement.TryGetProperty("initialChapter", out _)); Assert.False(data.RootElement.TryGetProperty("futureQuestShapedProblem", out _));
        var links = (await world.GetRelationshipsAsync(blueprint.CampaignId)).Where(x => x.FromEntityId == blueprint.CampaignId).ToList();
        Assert.Collection(links, first => { Assert.Equal((blueprint.CampaignId, blueprint.ExistingWorldId, "game.core.campaign.in-world", "{}"), (first.FromEntityId, first.ToEntityId, first.Kind, first.Data)); }, rest => Assert.Equal("game.core.campaign.references", rest.Kind), rest => Assert.Equal("game.core.campaign.references", rest.Kind), rest => Assert.Equal("game.core.campaign.references", rest.Kind), rest => Assert.Equal("game.core.campaign.references", rest.Kind), rest => Assert.Equal("game.core.campaign.references", rest.Kind), rest => Assert.Equal("game.core.campaign.references", rest.Kind));
        Assert.Equal(9, (await ledger.FindAsync(rootOperationId: created.OperationId)).Count);
        var operation = (await new OperationLog(db).RecentAsync(subject: blueprint.CampaignId)).Single(x => x.Success);
        Assert.Equal(created.OperationId, operation.Id); Assert.Equal("Create the reviewed test campaign.", operation.Intent); Assert.Equal("procedure.campaign.create", operation.ProceduresCited);
    }

    [Fact]
    public async Task Stale_or_replayed_create_leaves_no_second_campaign_state()
    {
        var (_, world, runner, ledger) = await ImportAsync(); var blueprint = Blueprint(); var review = await new CampaignBlueprintValidator(world).ValidateAsync(blueprint);
        var stale = await runner.CreateAsync(blueprint, new string('0', 64));
        Assert.False(stale.Created); Assert.Null(await world.GetEntityAsync(blueprint.CampaignId)); Assert.Empty(await ledger.FindAsync(rootOperationId: stale.OperationId));
        var created = await runner.CreateAsync(blueprint, review.ReviewFingerprint!); var replay = await runner.CreateAsync(blueprint, review.ReviewFingerprint!);
        Assert.True(created.Created, JsonSerializer.Serialize(created)); Assert.False(replay.Created); Assert.Single(await world.FindEntitiesAsync(nameQuery: "The Sealed Observatory"), x => x.Id == blueprint.CampaignId);
        Assert.Equal(9, (await ledger.FindAsync(rootOperationId: created.OperationId)).Count); Assert.Empty(await ledger.FindAsync(rootOperationId: replay.OperationId));
    }

    private async Task<(DantesRoleplayDbContext Db, WorldStore World, CampaignBootstrapper Runner, EventLedger Ledger)> ImportAsync()
    {
        Copy(Catalog(), _copy); var db = _fixture.CreateContext(); var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        await new EventTypeSeeder(new EventTypeStore(db)).SeedAsync();
        var ledger = new EventLedger(db); return (db, world, new CampaignBootstrapper(db, new CampaignBlueprintValidator(world), new EffectApplier(db, world, null, ledger), new OperationLog(db)), ledger);
    }

    private static CampaignBlueprint Blueprint() => new("campaign.test.sealed-observatory", "The Sealed Observatory", "A strange signal from the sealed observatory threatens the old market records.", ["Reach the market archive.", "Choose whom to trust with the signal."], ["Curious local mystery."], "dnd2024", "world.feature-01.fixture", "location.feature-01.gate", [new("location.feature-01.gate", "start", "party"), new("actor.feature-03.mara-vell", "npc", "party"), new("actor.feature-03.oren-dale", "npc", "gm"), new("faction.feature-03.fixture", "faction-stake", "party"), new("fact.feature-04.toll-ledger", "knowledge", "party"), new("rumour.feature-04.observatory-signal", "knowledge", "party")], new("chapter.opening", "What does the old toll ledger reveal?"), new("arc.observatory", "Can the observatory's history be kept from becoming leverage?"), new("gm", "A future investigation may involve Oren's family history."));
    private static string Catalog() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "DantesRoleplay.slnx"))) return Path.Combine(d.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, d))); foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(target, Path.GetRelativePath(source, f))); }
}
