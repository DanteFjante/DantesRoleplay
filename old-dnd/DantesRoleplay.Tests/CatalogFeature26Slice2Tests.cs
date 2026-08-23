using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Characters;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.World;
using Json.Schema;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature26Slice2Tests : IDisposable
{
    private const string CampaignId = "campaign.test.selected-species";
    private const string ActorId = "actor.test.selected-species.alex";
    private const string ParticipationId = "campaign.test.selected-species.participation.actor.test.selected-species.alex";
    private const string Selection = "dnd2024.selected-species";
    private const string Human = "content.dnd2024.species.human.v1";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-26-slice-2-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Human_selection_is_one_add_only_staged_reference()
    {
        var setup = await ArrangeAsync();
        var staged = await StageAsync(setup);

        var result = await ResolveAsync(staged.World!, Human);

        Assert.True(result.Valid, result.Problems.FirstOrDefault()?.Reason);
        Assert.Equal(CampaignId, result.CampaignId);
        var effect = Assert.Single(result.Effects);
        Assert.Equal(EffectType.ComponentAdd, effect.Type);
        Assert.Equal(ActorId, effect.EntityId);
        Assert.Equal(Selection, effect.DefinitionId);
        Assert.Equal("{\"speciesDefinitionId\":\"content.dnd2024.species.human.v1\"}", effect.Data);
        Assert.Null(await setup.World.GetEntityAsync(ActorId));

        var completed = await setup.Composer.AppendAsync(staged, result.Effects);
        Assert.True(completed.Valid, completed.Problems.FirstOrDefault()?.Reason);
        Assert.NotNull(await completed.World!.GetEntityAsync(ActorId));
        var selected = Assert.Single((await completed.World.GetEntityAsync(ActorId))!.Components, component => component.DefinitionId == Selection);
        Assert.Equal(effect.Data, selected.Data);
    }

    [Fact]
    public async Task Every_active_catalog_species_definition_can_be_bound_without_a_trait_effect()
    {
        var setup = await ArrangeAsync();
        var staged = await StageAsync(setup);
        var catalog = await CatalogReader.ReadAsync(RepositoryCatalog());
        var speciesIds = catalog.Entities.Where(entity => entity.Id.StartsWith("content.dnd2024.species.", StringComparison.Ordinal)).Select(entity => entity.Id).Order().ToArray();

        Assert.Equal(9, speciesIds.Length);
        foreach (var speciesId in speciesIds)
        {
            var result = await ResolveAsync(staged.World!, speciesId);
            Assert.True(result.Valid, $"{speciesId}: {result.Problems.FirstOrDefault()?.Reason}");
            var effect = Assert.Single(result.Effects);
            Assert.Equal(Selection, effect.DefinitionId);
            Assert.DoesNotContain("trait", effect.Data, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("speed", effect.Data, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Resolver_requires_scope_and_rejects_existing_or_corrupt_selection()
    {
        var setup = await ArrangeAsync();
        var noScope = await setup.Composer.StartAsync(Boundary());
        var missingScope = await ResolveAsync(noScope.World!, Human);
        Assert.False(missingScope.Valid);
        Assert.Equal("CAMPAIGN_SCOPE_REQUIRED", Assert.Single(missingScope.Problems).Code);

        var staged = await StageAsync(setup);
        var validExisting = await setup.Composer.AppendAsync(staged,
        [
            new Effect { Type = EffectType.ComponentAdd, EntityId = ActorId, DefinitionId = Selection, Data = """{"speciesDefinitionId":"content.dnd2024.species.human.v1"}""" }
        ]);
        var existing = await ResolveAsync(validExisting.World!, Human);
        Assert.False(existing.Valid);
        Assert.Equal("SPECIES_ALREADY_SELECTED", Assert.Single(existing.Problems).Code);
        Assert.Empty(existing.Effects);

        var corruptStart = await StageAsync(setup);
        var corruptExisting = await setup.Composer.AppendAsync(corruptStart,
        [
            new Effect { Type = EffectType.ComponentAdd, EntityId = ActorId, DefinitionId = Selection, Data = """{"speciesDefinitionId":"invalid"}""" }
        ]);
        var corrupt = await ResolveAsync(corruptExisting.World!, Human);
        Assert.False(corrupt.Valid);
        Assert.Equal("INVALID_EXISTING_SPECIES_SELECTION", Assert.Single(corrupt.Problems).Code);
        Assert.Empty(corrupt.Effects);
    }

    [Fact]
    public async Task Resolver_rejects_invalid_definition_state_without_a_fragment()
    {
        var setup = await ArrangeAsync();
        var staged = await StageAsync(setup);
        var missing = await ResolveAsync(staged.World!, "content.dnd2024.species.unknown.v1");
        Assert.False(missing.Valid);
        Assert.Equal("SPECIES_DEFINITION_NOT_FOUND", Assert.Single(missing.Problems).Code);

        const string invalid = "content.dnd2024.species.invalid.v1";
        await setup.World.CreateEntityAsync("Invalid species", invalid);
        await setup.World.SetComponentAsync(invalid, "dnd2024.character.content-definition", """{"kind":"species","contentKey":"invalid","contentVersion":1,"status":"active","sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Character Origins > Character Species > Human, PDF page 86"}}""");
        await setup.World.SetComponentAsync(invalid, "dnd2024.species-profile", """{"contentKey":"invalid"}""");
        var corrupt = await ResolveAsync(staged.World!, invalid);
        Assert.False(corrupt.Valid);
        Assert.Equal("INVALID_SPECIES_DEFINITION", Assert.Single(corrupt.Problems).Code);
        Assert.Empty(corrupt.Effects);
        Assert.Null(await setup.World.GetEntityAsync(ActorId));
    }

    [Fact]
    public async Task Selection_schema_and_internal_catalog_contract_are_closed()
    {
        var catalog = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(catalog.Components, component => component.Id == Selection).Schema);
        using var valid = JsonDocument.Parse("""{"speciesDefinitionId":"content.dnd2024.species.human.v1"}""");
        using var extra = JsonDocument.Parse("""{"speciesDefinitionId":"content.dnd2024.species.human.v1","sourceRef":{}}""");
        var procedure = Assert.Single(catalog.Procedures, procedure => procedure.Id == "procedure.mechanic.dnd2024.species-selection");
        var mechanic = Assert.Single(catalog.Mechanics, mechanic => mechanic.Id == "mechanic.dnd2024.species-selection.resolve");

        Assert.True(schema.Evaluate(valid.RootElement).IsValid);
        Assert.False(schema.Evaluate(extra.RootElement).IsValid);
        Assert.Equal(DantesRoleplay.Procedures.ProcedureStatus.Active, procedure.Status);
        Assert.Equal(DantesRoleplay.Mechanics.MechanicStatus.Draft, mechanic.Status);
    }

    private async Task<Setup> ArrangeAsync()
    {
        Copy(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        await world.CreateEntityAsync("Selected species campaign", CampaignId);
        await world.SetComponentAsync(CampaignId, "game.core.campaign.root", "{\"status\":\"active\"}");
        return new(world, new StagedWorldComposer(new EffectApplier(db, world), world), new CampaignCharacterParticipationPlanner());
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

    private static Task<CharacterSpeciesSelectionPlan> ResolveAsync(IWorldStore world, string speciesId) =>
        new CharacterSpeciesSelectionResolver(world, new CampaignCharacterParticipationVerifier(world)).PlanAsync(new(ActorId, speciesId));

    private static StagedWorldBoundary Boundary() => new(new(ActorId, "Alex"), new HashSet<string>([CampaignId, ActorId, ParticipationId], StringComparer.Ordinal));
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "catalog", "manifest.json"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string destination) { Directory.CreateDirectory(destination); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
    private sealed record Setup(WorldStore World, StagedWorldComposer Composer, CampaignCharacterParticipationPlanner Participation);
}
