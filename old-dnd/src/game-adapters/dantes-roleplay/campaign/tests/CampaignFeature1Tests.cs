using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.Tests;

public sealed class CampaignFeature1Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new(); private readonly string _copy = Path.Combine(Path.GetTempPath(), $"campaign-feature-01-{Guid.NewGuid():n}");
    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, true); }

    [Fact]
    public async Task Ratified_fixture_blueprint_validates_deterministically_without_writing()
    {
        var world = await ImportAsync(); var validator = new CampaignBlueprintValidator(world); var before = await world.FindEntitiesAsync(limit: 1000);
        var first = await validator.ValidateAsync(Blueprint()); var second = await validator.ValidateAsync(Blueprint());
        Assert.True(first.Valid); Assert.True(second.Valid); Assert.Equal(first.ReviewFingerprint, second.ReviewFingerprint); Assert.Equal(first.ResolvedReferences.Select(x => (x.EntityId, x.Role, x.Audience, x.ComponentId)), second.ResolvedReferences.Select(x => (x.EntityId, x.Role, x.Audience, x.ComponentId))); Assert.Equal(new CampaignCreationCounts(1, 1, 1, 6), first.CreationCounts); Assert.Matches("^[0-9a-f]{64}$", first.ReviewFingerprint!); Assert.Equal(before.Select(x => (x.Id, x.Name, string.Join(",", x.ComponentIds))), (await world.FindEntitiesAsync(limit: 1000)).Select(x => (x.Id, x.Name, string.Join(",", x.ComponentIds))));
    }

    [Fact]
    public async Task Invalid_or_hidden_campaign_references_never_produce_a_fingerprint()
    {
        var world = await ImportAsync(); var validator = new CampaignBlueprintValidator(world);
        var duplicate = Blueprint() with { References = [.. Blueprint().References, new("actor.feature-03.mara-vell", "knowledge", "party")] };
        var result = await validator.ValidateAsync(duplicate); Assert.False(result.Valid); Assert.Null(result.ReviewFingerprint); Assert.Contains(result.Problems, x => x.Code is "INVALID_REFERENCES" or "INVALID_REFERENCE");
    }

    private async Task<WorldStore> ImportAsync() { Copy(Catalog(), _copy); var db = _fixture.CreateContext(); var world = new WorldStore(db); Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_copy, new CatalogImportOptions())).Aborted); return world; }
    private static CampaignBlueprint Blueprint() => new("campaign.test.sealed-observatory", "The Sealed Observatory", "A strange signal from the sealed observatory threatens the old market records.", ["Reach the market archive.", "Choose whom to trust with the signal."], ["Curious local mystery."], "dnd2024", "world.feature-01.fixture", "location.feature-01.gate", [new("location.feature-01.gate", "start", "party"), new("actor.feature-03.mara-vell", "npc", "party"), new("actor.feature-03.oren-dale", "npc", "gm"), new("faction.feature-03.fixture", "faction-stake", "party"), new("fact.feature-04.toll-ledger", "knowledge", "party"), new("rumour.feature-04.observatory-signal", "knowledge", "party")], new("chapter.opening", "What does the old toll ledger reveal?"), new("arc.observatory", "Can the observatory's history be kept from becoming leverage?"), new("gm", "A future investigation may involve Oren's family history."));
    private static string Catalog() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "DantesRoleplay.slnx"))) return Path.Combine(d.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, d))); foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(target, Path.GetRelativePath(source, f))); }
}
