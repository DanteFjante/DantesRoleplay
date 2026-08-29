using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Characters;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature28Slice3Tests : IDisposable
{
    private const string CampaignId = "campaign.test.origin-languages";
    private const string ActorId = "actor.test.origin-languages.alex";
    private const string ParticipationId = "campaign.test.origin-languages.participation.actor.test.origin-languages.alex";
    private const string Languages = "dnd2024.language-proficiencies";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-28-slice-3-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Ratified_pair_becomes_one_canonical_source_cited_staged_fragment()
    {
        var setup = await ArrangeAsync();
        var staged = await StageAsync(setup);

        var result = await ResolveAsync(staged.World!, """{"languages":["giant","dwarvish"]}""");

        Assert.True(result.Valid, result.Problems.FirstOrDefault()?.Reason);
        Assert.Equal(CampaignId, result.CampaignId);
        var effect = Assert.Single(result.Effects);
        Assert.Equal(EffectType.ComponentAdd, effect.Type);
        Assert.Equal(ActorId, effect.EntityId);
        Assert.Equal(Languages, effect.DefinitionId);
        Assert.Null(await setup.World.GetEntityAsync(ActorId));
        using var data = JsonDocument.Parse(effect.Data);
        Assert.Equal(new[] { "common", "dwarvish", "giant" }, data.RootElement.GetProperty("languages").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal("source.dnd2024.srd-5.2.1", data.RootElement.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal("Character Creation > Step 2: Character Origin > Choose Languages", data.RootElement.GetProperty("sourceRef").GetProperty("locator").GetString());

        var completed = await setup.Composer.AppendAsync(staged, result.Effects);
        Assert.True(completed.Valid, completed.Problems.FirstOrDefault()?.Reason);
        Assert.NotNull(await completed.World!.GetEntityAsync(ActorId));
    }

    [Fact]
    public async Task Every_other_standard_language_is_a_legal_origin_choice()
    {
        var setup = await ArrangeAsync();
        var staged = await StageAsync(setup);

        foreach (var language in new[] { "common-sign-language", "draconic", "elvish", "giant", "gnomish", "goblin", "halfling", "orc" })
        {
            var result = await ResolveAsync(staged.World!, JsonSerializer.Serialize(new { languages = new[] { "dwarvish", language } }));
            Assert.True(result.Valid, $"{language}: {result.Problems.FirstOrDefault()?.Reason}");
            Assert.Single(result.Effects);
        }
    }

    [Fact]
    public async Task Invalid_selections_have_no_fragment_or_base_world_write()
    {
        var setup = await ArrangeAsync();
        var staged = await StageAsync(setup);

        foreach (var (selection, code) in new[]
        {
            ("not-json", "INVALID_ORIGIN_LANGUAGE_SELECTION"),
            ("{}", "INVALID_ORIGIN_LANGUAGE_SELECTION"),
            ("{\"languages\":[\"dwarvish\"]}", "INVALID_ORIGIN_LANGUAGE_SELECTION"),
            ("{\"languages\":[\"dwarvish\",\"dwarvish\"]}", "INVALID_ORIGIN_LANGUAGE_SELECTION"),
            ("{\"languages\":[\"common\",\"giant\"]}", "ORIGIN_LANGUAGE_NOT_STANDARD"),
            ("{\"languages\":[\"abyssal\",\"giant\"]}", "ORIGIN_LANGUAGE_NOT_STANDARD"),
            ("{\"languages\":[\"Dwarvish\",\"giant\"]}", "ORIGIN_LANGUAGE_NOT_STANDARD"),
            ("{\"languages\":[\"dwarvish\",\"giant\"],\"sourceRef\":{}}", "INVALID_ORIGIN_LANGUAGE_SELECTION")
        })
        {
            var result = await ResolveAsync(staged.World!, selection);
            Assert.False(result.Valid);
            Assert.Equal(code, Assert.Single(result.Problems).Code);
            Assert.Empty(result.Effects);
            Assert.Null(await setup.World.GetEntityAsync(ActorId));
        }
    }

    [Fact]
    public async Task Resolver_requires_scope_and_rejects_existing_or_corrupt_language_state()
    {
        var setup = await ArrangeAsync();
        var noScope = await setup.Composer.StartAsync(Boundary());
        var missingScope = await ResolveAsync(noScope.World!, """{"languages":["dwarvish","giant"]}""");
        Assert.False(missingScope.Valid);
        Assert.Equal("CAMPAIGN_SCOPE_REQUIRED", Assert.Single(missingScope.Problems).Code);

        var staged = await StageAsync(setup);
        var validExisting = await setup.Composer.AppendAsync(staged,
        [
            new Effect { Type = EffectType.ComponentAdd, EntityId = ActorId, DefinitionId = Languages, Data = """{"languages":["common"],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Character Creation > Step 2: Character Origin > Choose Languages"}}""" }
        ]);
        var existing = await ResolveAsync(validExisting.World!, """{"languages":["dwarvish","giant"]}""");
        Assert.False(existing.Valid);
        Assert.Equal("LANGUAGE_PROFICIENCIES_ALREADY_EXIST", Assert.Single(existing.Problems).Code);
        Assert.Empty(existing.Effects);

        var corruptStart = await StageAsync(setup);
        var corruptExisting = await setup.Composer.AppendAsync(corruptStart,
        [
            new Effect { Type = EffectType.ComponentAdd, EntityId = ActorId, DefinitionId = Languages, Data = """{"languages":["dwarvish"]}""" }
        ]);
        var corrupt = await ResolveAsync(corruptExisting.World!, """{"languages":["dwarvish","giant"]}""");
        Assert.False(corrupt.Valid);
        Assert.Equal("INVALID_EXISTING_LANGUAGE_STATE", Assert.Single(corrupt.Problems).Code);
        Assert.Empty(corrupt.Effects);
    }

    [Fact]
    public async Task Catalog_contract_is_internal_and_source_bound()
    {
        var catalog = await CatalogReader.ReadAsync(RepositoryCatalog());
        var procedure = Assert.Single(catalog.Procedures, procedure => procedure.Id == "procedure.mechanic.dnd2024.origin-languages");
        var mechanic = Assert.Single(catalog.Mechanics, mechanic => mechanic.Id == "mechanic.dnd2024.origin-languages.resolve");

        Assert.Equal(DantesRoleplay.Procedures.ProcedureStatus.Active, procedure.Status);
        Assert.Equal(DantesRoleplay.Mechanics.MechanicStatus.Draft, mechanic.Status);
        Assert.Contains("Common", procedure.Instructions, StringComparison.Ordinal);
        Assert.Contains("dwarvish", procedure.Instructions, StringComparison.Ordinal);
    }

    private async Task<Setup> ArrangeAsync()
    {
        Copy(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        await world.CreateEntityAsync("Origin language campaign", CampaignId);
        await world.SetComponentAsync(CampaignId, "game.core.campaign.root", "{\"status\":\"active\"}");
        var effects = new EffectApplier(db, world);
        return new(world, new StagedWorldComposer(effects, world), new CampaignCharacterParticipationPlanner());
    }

    private static async Task<StagedWorldPlan> StageAsync(Setup setup)
    {
        var start = await setup.Composer.StartAsync(Boundary());
        Assert.True(start.Valid, start.Problems.FirstOrDefault()?.Reason);
        var participation = await setup.Participation.PlanAsync(new(CampaignId, ActorId), start.World!);
        Assert.True(participation.Valid, participation.Problems.FirstOrDefault()?.Reason);
        var staged = await setup.Composer.AppendAsync(start, participation.Effects);
        Assert.True(staged.Valid, staged.Problems.FirstOrDefault()?.Reason);
        return staged;
    }

    private static Task<CharacterOriginLanguagePlan> ResolveAsync(IWorldStore world, string selection) =>
        new CharacterOriginLanguageResolver(world, new CampaignCharacterParticipationVerifier(world))
            .PlanAsync(new(ActorId, selection));

    private static StagedWorldBoundary Boundary() => new(new(ActorId, "Alex"), new HashSet<string>([CampaignId, ActorId, ParticipationId], StringComparer.Ordinal));
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "catalog", "manifest.json"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string destination) { Directory.CreateDirectory(destination); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
    private sealed record Setup(WorldStore World, StagedWorldComposer Composer, CampaignCharacterParticipationPlanner Participation);
}
