using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Characters;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using Json.Schema;

namespace DantesRoleplay.Tests;

public sealed class CharacterFeature01Slice2Tests : IDisposable
{
    private const string Definition = "dnd2024.character.profile";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"character-feature-01-slice-2-{Guid.NewGuid():n}");
    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true); }

    [Fact]
    public async Task Plans_one_profile_only_for_an_active_campaign_attached_actor()
    {
        var setup = await ArrangeAsync(attachActor: true);
        var plan = await setup.Recorder.PlanAsync(new(setup.ActorId, """{"pronouns":"they/them","appearance":"A weathered blue coat."}"""));

        Assert.True(plan.Valid, plan.Problems.FirstOrDefault()?.Reason);
        Assert.Equal(setup.CampaignId, plan.CampaignId); Assert.Equal(EffectType.ComponentAdd, Assert.Single(plan.Effects).Type);
        Assert.Equal(Definition, Assert.Single(plan.Effects).DefinitionId);
        Assert.True((await new EffectApplier(setup.Db, setup.World).ApplyAsync(plan.Effects, dryRun: false)).Applied);
        var actor = await setup.World.GetEntityAsync(setup.ActorId);
        using var profile = JsonDocument.Parse(Assert.Single(actor!.Components, x => x.DefinitionId == Definition).Data);
        Assert.Equal("they/them", profile.RootElement.GetProperty("pronouns").GetString());
        Assert.Equal("A weathered blue coat.", profile.RootElement.GetProperty("appearance").GetString());
        Assert.False(profile.RootElement.TryGetProperty("campaignId", out _));

        var duplicate = await setup.Recorder.PlanAsync(new(setup.ActorId, "{}"));
        Assert.False(duplicate.Valid); Assert.Equal("PROFILE_ALREADY_EXISTS", Assert.Single(duplicate.Problems).Code);
    }

    [Theory]
    [InlineData("{\"pronouns\":null}")]
    [InlineData("{\"appearance\":\" not trimmed\"}")]
    [InlineData("{\"biography\":\"\"}")]
    [InlineData("{\"secret\":\"not profile data\"}")]
    [InlineData("[]")]
    public async Task Rejects_invalid_profile_data_without_an_effect(string profile)
    {
        var setup = await ArrangeAsync(attachActor: true);
        var plan = await setup.Recorder.PlanAsync(new(setup.ActorId, profile));
        Assert.False(plan.Valid); Assert.Empty(plan.Effects);
        Assert.DoesNotContain((await setup.World.GetEntityAsync(setup.ActorId))!.Components, x => x.DefinitionId == Definition);
    }

    [Fact]
    public async Task Rejects_an_unattached_actor_and_keeps_the_executable_mechanic_internal()
    {
        var setup = await ArrangeAsync(attachActor: false);
        var plan = await setup.Recorder.PlanAsync(new(setup.ActorId, "{}"));
        Assert.False(plan.Valid); Assert.Equal("CAMPAIGN_SCOPE_REQUIRED", Assert.Single(plan.Problems).Code);
        var mechanic = await setup.Mechanics.GetAsync("mechanic.dnd2024.character-profile.record");
        Assert.NotNull(mechanic); Assert.Equal(MechanicStatus.Draft, mechanic!.Status);
    }

    [Fact]
    public async Task Profile_schema_rejects_campaign_and_mechanical_fields()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, x => x.Id == Definition).Schema);
        using var forbidden = JsonDocument.Parse("""{"campaignId":"campaign.test.group","class":"fighter"}""");
        using var valid = JsonDocument.Parse("""{"biography":"Former scout of the market watch."}""");
        Assert.False(schema.Evaluate(forbidden.RootElement).IsValid); Assert.True(schema.Evaluate(valid.RootElement).IsValid);
    }

    private async Task<Setup> ArrangeAsync(bool attachActor)
    {
        Copy(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext(); var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        const string campaignId = "campaign.test.profile"; const string actorId = "actor.test.profile.alex";
        await world.CreateEntityAsync("Profile campaign", campaignId); await world.SetComponentAsync(campaignId, "game.core.campaign.root", "{\"status\":\"active\"}"); await world.CreateEntityAsync("Alex", actorId);
        var verifier = new CampaignCharacterParticipationVerifier(world);
        if (attachActor) Assert.True((await new CampaignCharacterParticipationAttacher(db, world, new EffectApplier(db, world), new OperationLog(db)).AttachAsync(new("attach-character-participation", campaignId, actorId))).Attached);
        return new(db, world, mechanics, new CharacterProfileRecorder(world, verifier), campaignId, actorId);
    }

    private static string RepositoryCatalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "catalog", "manifest.json"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string destination) { Directory.CreateDirectory(destination); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file))); }
    private sealed record Setup(DantesRoleplayDbContext Db, WorldStore World, MechanicStore Mechanics, CharacterProfileRecorder Recorder, string CampaignId, string ActorId);
}
