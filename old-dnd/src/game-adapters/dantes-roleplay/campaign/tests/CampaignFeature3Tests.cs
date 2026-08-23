using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.Tests;

public sealed class CampaignFeature3Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new(); private readonly string _copy = Path.Combine(Path.GetTempPath(), $"campaign-feature-03-{Guid.NewGuid():n}");
    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, true); }

    [Fact]
    public async Task C3_initializes_advances_closes_concludes_and_resumes_without_a_quest()
    {
        var (_, world, continuity, reader, ledger) = await ImportCampaignAsync(); var seed = Seed();
        var initialized = await continuity.InitializeAsync(seed);
        Assert.True(initialized.Succeeded); Assert.Equal(7, initialized.StructuralEventCount); Assert.Equal(7, (await ledger.FindAsync(rootOperationId: initialized.OperationId)).Count);
        var first = await reader.GetAsync(seed.CampaignId); Assert.NotNull(first); Assert.Equal(seed.Chapter.PartyQuestion, first!.CurrentChapter!.PartyQuestion); Assert.Equal(seed.Arc.PartyStake, first.CurrentArc!.PartyStake); Assert.Equal(6, first.References.Count);
        var advanced = await continuity.AdvanceAsync(seed.CampaignId, initialized.ChapterId!, "active", "The market archive identifies the signal's source.", new("chapter.market-archive", "The Market Archive", "Who benefits if the ledger stays buried?"));
        Assert.True(advanced.Succeeded); Assert.Equal(5, advanced.StructuralEventCount);
        var closed = await continuity.CloseAsync(seed.CampaignId, advanced.ChapterId!, "active", "The group secures the ledger's public copy."); Assert.True(closed.Succeeded); Assert.Equal(1, closed.StructuralEventCount);
        var concluded = await continuity.ConcludeArcAsync(seed.CampaignId, initialized.ArcId!, "active", "resolved", "The observatory's history becomes a shared record, not private leverage."); Assert.True(concluded.Succeeded); Assert.Equal(1, concluded.StructuralEventCount);
        var resumed = await reader.GetAsync(seed.CampaignId); Assert.NotNull(resumed); Assert.Null(resumed!.CurrentChapter); Assert.Null(resumed.CurrentArc); Assert.Equal(2, resumed.RecentMilestones.Count); Assert.Equal("The Market Archive", resumed.RecentMilestones[0].Title);
    }

    [Fact]
    public async Task C3_rejects_replay_and_stale_lifecycle_without_new_events()
    {
        var (_, _, continuity, _, ledger) = await ImportCampaignAsync(); var seed = Seed(); var initialized = await continuity.InitializeAsync(seed);
        var replay = await continuity.InitializeAsync(seed); Assert.False(replay.Succeeded); Assert.Empty(await ledger.FindAsync(rootOperationId: replay.OperationId));
        var closed = await continuity.CloseAsync(seed.CampaignId, initialized.ChapterId!, "active", "A factual ending."); Assert.True(closed.Succeeded);
        var stale = await continuity.CloseAsync(seed.CampaignId, initialized.ChapterId!, "active", "A second ending."); Assert.False(stale.Succeeded); Assert.Empty(await ledger.FindAsync(rootOperationId: stale.OperationId));
    }

    private async Task<(DantesRoleplayDbContext Db, WorldStore World, CampaignContinuityRunner Continuity, CampaignResumeReader Reader, EventLedger Ledger)> ImportCampaignAsync()
    {
        Copy(Catalog(), _copy); var db = _fixture.CreateContext(); var world = new WorldStore(db); Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted); await new EventTypeSeeder(new EventTypeStore(db)).SeedAsync();
        var ledger = new EventLedger(db); var validator = new CampaignBlueprintValidator(world); var bootstrap = new CampaignBootstrapper(db, validator, new EffectApplier(db, world, null, ledger), new OperationLog(db)); var blueprint = Blueprint(); var review = await validator.ValidateAsync(blueprint); Assert.True((await bootstrap.CreateAsync(blueprint, review.ReviewFingerprint!)).Created);
        return (db, world, new CampaignContinuityRunner(db, world, new EffectApplier(db, world, null, ledger), new OperationLog(db)), new CampaignResumeReader(world, ledger), ledger);
    }
    private static CampaignContinuitySeed Seed() => new("campaign.test.sealed-observatory", new("chapter.opening", "The Ledger Signal", "What does the old toll ledger reveal about the observatory signal?", "The answer is not precommitted by this test brief."), new("arc.observatory", "The Observatory's Claim", "Can the group keep the observatory's history from becoming another source of leverage?", "No arc outcome is fixed."));
    private static CampaignBlueprint Blueprint() => new("campaign.test.sealed-observatory", "The Sealed Observatory", "A strange signal from the sealed observatory threatens the old market records.", ["Reach the market archive.", "Choose whom to trust with the signal."], ["Curious local mystery."], "dnd2024", "world.feature-01.fixture", "location.feature-01.gate", [new("location.feature-01.gate", "start", "party"), new("actor.feature-03.mara-vell", "npc", "party"), new("actor.feature-03.oren-dale", "npc", "gm"), new("faction.feature-03.fixture", "faction-stake", "party"), new("fact.feature-04.toll-ledger", "knowledge", "party"), new("rumour.feature-04.observatory-signal", "knowledge", "party")], new("chapter.opening", "What does the old toll ledger reveal?"), new("arc.observatory", "Can the observatory's history be kept from becoming leverage?"));
    private static string Catalog() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "DantesRoleplay.slnx"))) return Path.Combine(d.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, d))); foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(target, Path.GetRelativePath(source, f))); }
}
