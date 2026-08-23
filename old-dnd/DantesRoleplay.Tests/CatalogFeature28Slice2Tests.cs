using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Characters;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.World;
using Json.Schema;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature28Slice2Tests : IDisposable
{
    private const string CampaignId = "campaign.test.background-increases";
    private const string ActorId = "actor.test.background-increases.alex";
    private const string ParticipationId = "campaign.test.background-increases.participation.actor.test.background-increases.alex";
    private const string Soldier = "content.dnd2024.background.soldier.v1";
    private const string Options = "dnd2024.background.ability-increase-options";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-28-slice-2-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Soldier_profile_resolves_the_ratified_choice_as_one_staged_ability_merge()
    {
        var setup = await ArrangeAsync();
        var staged = await StageAsync(setup, """{"str":15,"dex":14,"con":13,"int":8,"wis":10,"cha":12}""");

        var result = await new BackgroundAbilityScoreIncreaseResolver(staged.World, new CampaignCharacterParticipationVerifier(staged.World))
            .PlanAsync(new(ActorId, Soldier, """{"str":2,"con":1}"""));

        Assert.True(result.Valid, result.Problems.FirstOrDefault()?.Reason);
        Assert.Equal(CampaignId, result.CampaignId);
        var effect = Assert.Single(result.Effects);
        Assert.Equal(EffectType.ComponentMerge, effect.Type);
        Assert.Equal(ActorId, effect.EntityId);
        Assert.Equal("dnd2024.abilities", effect.DefinitionId);
        using var delta = JsonDocument.Parse(effect.Data);
        Assert.Equal(17, delta.RootElement.GetProperty("str").GetInt32());
        Assert.Equal(14, delta.RootElement.GetProperty("con").GetInt32());
        Assert.False(delta.RootElement.TryGetProperty("dex", out _));

        var completed = await setup.Composer.AppendAsync(staged.Plan, result.Effects);
        Assert.True(completed.Valid, completed.Problems.FirstOrDefault()?.Reason);
        Assert.Null(await setup.World.GetEntityAsync(ActorId));
        using var merged = JsonDocument.Parse(Assert.Single((await completed.World!.GetEntityAsync(ActorId))!.Components, component => component.DefinitionId == "dnd2024.abilities").Data);
        Assert.Equal(17, merged.RootElement.GetProperty("str").GetInt32());
        Assert.Equal(14, merged.RootElement.GetProperty("con").GetInt32());
        Assert.Equal(14, merged.RootElement.GetProperty("dex").GetInt32());

        var alternative = await new BackgroundAbilityScoreIncreaseResolver(staged.World, new CampaignCharacterParticipationVerifier(staged.World))
            .PlanAsync(new(ActorId, Soldier, """{"str":1,"con":1,"wis":1}"""));
        Assert.True(alternative.Valid, alternative.Problems.FirstOrDefault()?.Reason);
    }

    [Theory]
    [InlineData("""{"str":2,"dex":1}""", "INVALID_ABILITY_INCREASE_SELECTION")]
    [InlineData("""{"str":2,"con":2}""", "ABILITY_INCREASE_PATTERN_NOT_ALLOWED")]
    [InlineData("""{"str":2,"con":1,"wis":1}""", "ABILITY_INCREASE_PATTERN_NOT_ALLOWED")]
    public async Task Resolver_rejects_non_source_choices_without_a_fragment(string selection, string code)
    {
        var setup = await ArrangeAsync();
        var staged = await StageAsync(setup, """{"str":15,"dex":14,"con":13,"int":8,"wis":10,"cha":12}""");

        var result = await new BackgroundAbilityScoreIncreaseResolver(staged.World, new CampaignCharacterParticipationVerifier(staged.World))
            .PlanAsync(new(ActorId, Soldier, selection));

        Assert.False(result.Valid);
        Assert.Equal(code, Assert.Single(result.Problems).Code);
        Assert.Empty(result.Effects);
        Assert.Null(await setup.World.GetEntityAsync(ActorId));
    }

    [Fact]
    public async Task Resolver_requires_scope_and_rejects_an_over_cap_result()
    {
        var setup = await ArrangeAsync();
        var start = await setup.Composer.StartAsync(Boundary());
        var baseOnly = await setup.Composer.AppendAsync(start,
        [
            new Effect { Type = EffectType.ComponentAdd, EntityId = ActorId, DefinitionId = "dnd2024.abilities", Data = """{"str":15,"dex":14,"con":13,"int":8,"wis":10,"cha":12}""" }
        ]);
        var noScope = await new BackgroundAbilityScoreIncreaseResolver(baseOnly.World!, new CampaignCharacterParticipationVerifier(baseOnly.World!))
            .PlanAsync(new(ActorId, Soldier, """{"str":2,"con":1}"""));
        Assert.False(noScope.Valid); Assert.Equal("CAMPAIGN_SCOPE_REQUIRED", Assert.Single(noScope.Problems).Code);

        var staged = await StageAsync(setup, """{"str":19,"dex":14,"con":13,"int":8,"wis":10,"cha":12}""");
        var capped = await new BackgroundAbilityScoreIncreaseResolver(staged.World, new CampaignCharacterParticipationVerifier(staged.World))
            .PlanAsync(new(ActorId, Soldier, """{"str":2,"con":1}"""));
        Assert.False(capped.Valid); Assert.Equal("ABILITY_SCORE_CAP_EXCEEDED", Assert.Single(capped.Problems).Code);
        Assert.Empty(capped.Effects);
    }

    [Fact]
    public async Task Profile_schema_is_closed_and_soldier_is_source_cited()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, component => component.Id == Options).Schema);
        var soldier = Assert.Single(contents.Entities, entity => entity.Id == Soldier);
        using var profile = JsonDocument.Parse(Assert.Single(soldier.Components, component => component.DefinitionId == Options).Data);
        using var extra = JsonDocument.Parse("""{"contentKey":"soldier","contentVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Character Origins > Character Backgrounds > Soldier, PDF page 83"},"eligibleAbilities":["str","con","wis"],"allowedPatterns":["plus-2-plus-1","plus-1-each"],"selected":{"str":2,"con":1}}""");

        Assert.True(schema.Evaluate(profile.RootElement).IsValid);
        Assert.False(schema.Evaluate(extra.RootElement).IsValid);
        Assert.Equal(new[] { "str", "con", "wis" }, profile.RootElement.GetProperty("eligibleAbilities").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    private async Task<Setup> ArrangeAsync()
    {
        Copy(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        await world.CreateEntityAsync("Background increase campaign", CampaignId);
        await world.SetComponentAsync(CampaignId, "game.core.campaign.root", "{\"status\":\"active\"}");
        var effects = new EffectApplier(db, world);
        return new(world, new StagedWorldComposer(effects, world), new CampaignCharacterParticipationPlanner());
    }

    private static async Task<Staged> StageAsync(Setup setup, string scores)
    {
        var start = await setup.Composer.StartAsync(Boundary());
        Assert.True(start.Valid, start.Problems.FirstOrDefault()?.Reason);
        var participation = await setup.Participation.PlanAsync(new(CampaignId, ActorId), start.World!);
        Assert.True(participation.Valid, participation.Problems.FirstOrDefault()?.Reason);
        var attached = await setup.Composer.AppendAsync(start, participation.Effects);
        Assert.True(attached.Valid, attached.Problems.FirstOrDefault()?.Reason);
        var abilities = await new CharacterAbilityScoreRecorder(attached.World!, new CampaignCharacterParticipationVerifier(attached.World!))
            .PlanAsync(new(ActorId, scores));
        Assert.True(abilities.Valid, abilities.Problems.FirstOrDefault()?.Reason);
        var plan = await setup.Composer.AppendAsync(attached, abilities.Effects);
        Assert.True(plan.Valid, plan.Problems.FirstOrDefault()?.Reason);
        return new(plan, plan.World!);
    }

    private static StagedWorldBoundary Boundary() => new(new(ActorId, "Alex"), new HashSet<string>([CampaignId, ActorId, ParticipationId], StringComparer.Ordinal));
    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "catalog", "manifest.json"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string destination) { Directory.CreateDirectory(destination); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
    private sealed record Setup(WorldStore World, StagedWorldComposer Composer, CampaignCharacterParticipationPlanner Participation);
    private sealed record Staged(StagedWorldPlan Plan, IWorldStore World);
}
