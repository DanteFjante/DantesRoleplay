using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class CampaignFeature10CompositionAdapterTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"campaign-feature-10-r5-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Adapter_returns_C2_equivalent_canonical_campaign_effects_against_W17_virtual_world_without_writing()
    {
        var setup = await ArrangeAsync();
        var before = await StateAsync(setup.Db);
        var world = await setup.WorldPlanner.ComposeAsync(WorldBlueprint(), "world.c10.lantern-compact");
        Assert.True(world.Valid, world.Problems.FirstOrDefault()?.Reason);

        var result = await setup.Adapter.ComposeAsync(CampaignBlueprint(), world);

        Assert.True(result.Valid, result.Problems.FirstOrDefault()?.Reason);
        Assert.Equal("world.c10.lantern-compact.world", result.Blueprint!.ExistingWorldId);
        Assert.Equal((1, 1, 1, 10), (result.Counts!.Entities, result.Counts.RootComponents, result.Counts.InWorldRelationships, result.Counts.ReferenceRelationships));
        Assert.Equal(13, result.Effects.Count);
        Assert.Equal(new[] { EffectType.EntityCreate, EffectType.ComponentAdd, EffectType.RelationshipCreate }, result.Effects.Take(3).Select(effect => effect.Type));
        Assert.Equal("game.core.campaign.in-world", result.Effects[2].Kind);
        Assert.Equal(world.WorldRootId, result.Effects[2].ToEntityId);
        Assert.Equal(10, result.Effects.Skip(3).Count(effect => effect.Kind == "game.core.campaign.references"));
        Assert.Equal("world.c10.lantern-compact.location.gate", result.Effects[3].ToEntityId);
        Assert.Equal("world.c10.lantern-compact.knowledge.secret", result.Effects[^1].ToEntityId);
        Assert.Equal(before, await StateAsync(setup.Db));
    }

    [Fact]
    public async Task Adapter_rejects_missing_or_mismatched_staged_world_evidence_without_effects()
    {
        var setup = await ArrangeAsync();
        var before = await StateAsync(setup.Db);
        var missing = await setup.Adapter.ComposeAsync(CampaignBlueprint(), new("invalid", null, [], null, [], [], null, []));
        Assert.False(missing.Valid); Assert.Equal("INVALID_STAGED_WORLD", Assert.Single(missing.Problems).Code); Assert.Empty(missing.Effects);

        var world = await setup.WorldPlanner.ComposeAsync(WorldBlueprint(), "world.c10.lantern-compact");
        var mismatched = await setup.Adapter.ComposeAsync(CampaignBlueprint(), world with { WorldRootId = "world.c10.other.world" });
        Assert.False(mismatched.Valid); Assert.Equal("INVALID_STAGED_WORLD", Assert.Single(mismatched.Problems).Code); Assert.Empty(mismatched.Effects);
        Assert.Equal(before, await StateAsync(setup.Db));
    }

    [Fact]
    public async Task Adapter_reuses_C1_campaign_collision_validation_without_writing()
    {
        var setup = await ArrangeAsync();
        await setup.World.CreateEntityAsync("Existing campaign", "campaign.lantern-compact");
        var before = await StateAsync(setup.Db);
        var world = await setup.WorldPlanner.ComposeAsync(WorldBlueprint(), "world.c10.lantern-compact");

        var result = await setup.Adapter.ComposeAsync(CampaignBlueprint(), world);

        Assert.False(result.Valid);
        Assert.Contains(result.Problems, problem => problem.Code == "CAMPAIGN_ID_TAKEN");
        Assert.Empty(result.Effects);
        Assert.Equal(before, await StateAsync(setup.Db));
    }

    private async Task<Setup> ArrangeAsync()
    {
        Copy(Catalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var effects = new EffectApplier(db, world);
        return new(db, world, new SmallWorldCompositionPlanner(new StagedWorldComposer(effects, world)), new CampaignCompositionAdapter());
    }

    private static NewWorldCampaignBlueprint CampaignBlueprint() => new("campaign.lantern-compact", "The Lantern Compact", "A campaign about the sealed observatory.", ["Reach the archive", "Choose whom to trust"], ["Curious local mystery"], "dnd2024", new("chapter.opening", "What does the archive reveal?"), new("arc.observatory", "Can the observatory's history stay protected?"));
    private static SmallWorldBlueprint WorldBlueprint() => new(
        new("Lantern Compact", "A compact setting built for one campaign."), new("Old Ward", "The region around the sealed observatory."), new("North Gate", "The party arrives through the old northern gate."), new("Archive Market", "A market surrounding a disputed archive."), new("Sealed Observatory", "An observatory with a disputed signal."),
        new("The Lantern Compact", "A faction protecting the old records.", ["Protect the archive"], ["Negotiate quietly"], ["A sealed ledger"], "Keep the records from public misuse."), new("Mara Vell", "Mara wants the archive opened safely."), new("Oren Dale", "Oren wants the observatory secret preserved."),
        new("Old Toll Ledger", "The market archive holds the old toll ledger.", "Catalogued archive entry.", "state", "open"), new("Observatory Signal", "A light answers from the observatory after midnight.", "Market gossip.", "event", "discreet"), new("Oren's Correspondence", "Oren's family hid records implicating the old council.", "Private ledger annotation.", "relationship", "secret"),
        new("Ledger Seal", "A seal matches the market archive door.", "Inspection of the toll ledger.", "identity", "confidential"), new("Lantern Soot", "Fresh soot marks the observatory shutter.", "Soot beneath the shutter.", "state", "confidential"), new("Unsent Letter", "A letter asks Oren to keep a family promise.", "A folded letter.", "relationship", "secret"));
    private static async Task<string> StateAsync(DantesRoleplayDbContext db) => string.Join("|", await db.Entities.CountAsync(), await db.Components.CountAsync(), await db.Relationships.CountAsync(), await db.Events.CountAsync(), await db.Operations.CountAsync());
    private static string Catalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file))); }
    private sealed record Setup(DantesRoleplayDbContext Db, WorldStore World, SmallWorldCompositionPlanner WorldPlanner, CampaignCompositionAdapter Adapter);
}
