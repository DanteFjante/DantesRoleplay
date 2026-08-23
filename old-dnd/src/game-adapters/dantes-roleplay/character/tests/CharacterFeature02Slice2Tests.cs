using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.Campaign;
using DantesRoleplay.Characters;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CharacterFeature02Slice2Tests : IDisposable
{
    private const string Policy = "content.dnd2024.ability-assignment.standard-array.v1";
    private const string Abilities = "dnd2024.abilities";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"character-feature-02-slice-2-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Validated_standard_array_records_one_ability_component_then_existing_level_one()
    {
        var setup = await ArrangeAsync(attachActor: true);
        var validation = await setup.Validator.ValidateAsync(new(Policy, """{"str":15,"dex":14,"con":13,"int":8,"wis":10,"cha":12}"""));
        Assert.True(validation.Valid, validation.Problems.FirstOrDefault()?.Reason);

        var record = await setup.Recorder.PlanAsync(new(setup.ActorId, validation.CanonicalScoresJson!));
        Assert.True(record.Valid, record.Problems.FirstOrDefault()?.Reason);
        Assert.Equal(setup.CampaignId, record.CampaignId);
        Assert.Equal(EffectType.ComponentAdd, Assert.Single(record.Effects).Type);
        Assert.True((await new EffectApplier(setup.Db, setup.World).ApplyAsync(record.Effects)).Applied);

        var level = await setup.Runner.RunAsync(new ActionRequest
        {
            Intent = "record character level",
            Input = """{"level":1}""",
            Seed = 2,
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = setup.ActorId }
        });
        Assert.True(level.Ok, level.Error?.Why);

        var actor = await setup.World.GetEntityAsync(setup.ActorId);
        using var abilities = JsonDocument.Parse(Assert.Single(actor!.Components, x => x.DefinitionId == Abilities).Data);
        Assert.Equal(15, abilities.RootElement.GetProperty("str").GetInt32());
        Assert.False(abilities.RootElement.TryGetProperty("modifier", out _));
        using var totalLevel = JsonDocument.Parse(Assert.Single(actor.Components, x => x.DefinitionId == "dnd2024.character-level").Data);
        Assert.Equal(1, totalLevel.RootElement.GetProperty("level").GetInt32());
        Assert.False(totalLevel.RootElement.TryGetProperty("proficiencyBonus", out _));

        var duplicate = await setup.Recorder.PlanAsync(new(setup.ActorId, validation.CanonicalScoresJson!));
        Assert.False(duplicate.Valid); Assert.Equal("ABILITIES_ALREADY_EXIST", Assert.Single(duplicate.Problems).Code);
    }

    [Theory]
    [InlineData("""{"str":15,"dex":14,"con":13,"int":8,"wis":10,"cha":12,"policyId":"x"}""")]
    [InlineData("""{"str":15,"dex":14,"con":13,"int":8,"wis":10,"cha":12,"modifier":2}""")]
    [InlineData("""{"str":15,"dex":14,"con":13,"int":8,"wis":10,"cha":12.5}""")]
    public async Task Recorder_rejects_any_noncanonical_score_payload_without_effect(string scores)
    {
        var setup = await ArrangeAsync(attachActor: true);
        var record = await setup.Recorder.PlanAsync(new(setup.ActorId, scores));

        Assert.False(record.Valid); Assert.Empty(record.Effects);
        Assert.Equal("INVALID_SCORES", Assert.Single(record.Problems).Code);
        Assert.DoesNotContain((await setup.World.GetEntityAsync(setup.ActorId))!.Components, x => x.DefinitionId == Abilities);
    }

    [Fact]
    public async Task Recorder_requires_active_campaign_scope_and_the_catalog_declaration_is_draft()
    {
        var setup = await ArrangeAsync(attachActor: false);
        var record = await setup.Recorder.PlanAsync(new(setup.ActorId, """{"str":15,"dex":14,"con":13,"int":8,"wis":10,"cha":12}"""));

        Assert.False(record.Valid); Assert.Empty(record.Effects);
        Assert.Equal("CAMPAIGN_SCOPE_REQUIRED", Assert.Single(record.Problems).Code);
        var mechanic = await setup.Mechanics.GetAsync("mechanic.dnd2024.abilities.record");
        Assert.NotNull(mechanic); Assert.Equal(MechanicStatus.Draft, mechanic!.Status);
    }

    private async Task<Setup> ArrangeAsync(bool attachActor)
    {
        Copy(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var procedures = new ProcedureStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, procedures, world).ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        const string campaignId = "campaign.test.abilities";
        const string actorId = "actor.test.abilities.alex";
        await world.CreateEntityAsync("Ability campaign", campaignId);
        await world.SetComponentAsync(campaignId, "game.core.campaign.root", "{" + "\"status\":\"active\"}");
        await world.CreateEntityAsync("Alex", actorId);
        var verifier = new CampaignCharacterParticipationVerifier(world);
        if (attachActor)
            Assert.True((await new CampaignCharacterParticipationAttacher(db, world, new EffectApplier(db, world), new OperationLog(db))
                .AttachAsync(new("attach-character-participation", campaignId, actorId))).Attached);
        var runner = new ActionRunner(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
        return new(db, world, mechanics, new CharacterAbilityAssignmentValidator(world), new CharacterAbilityScoreRecorder(world, verifier), runner, campaignId, actorId);
    }

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "catalog", "manifest.json"))) return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    private sealed record Setup(
        DantesRoleplayDbContext Db,
        WorldStore World,
        MechanicStore Mechanics,
        CharacterAbilityAssignmentValidator Validator,
        CharacterAbilityScoreRecorder Recorder,
        ActionRunner Runner,
        string CampaignId,
        string ActorId);
}
