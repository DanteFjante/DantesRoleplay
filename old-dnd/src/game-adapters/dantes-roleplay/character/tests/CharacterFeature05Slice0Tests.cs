using DantesRoleplay.Campaign;
using DantesRoleplay.Characters;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

/// <summary>CH5 Slice 0 proves virtual new-actor composition without a partial database write.</summary>
public sealed class CharacterFeature05Slice0Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"character-feature-05-slice-0-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Staged_actor_and_participation_let_existing_child_planners_return_one_unpersisted_bundle()
    {
        var setup = await ArrangeAsync();
        var start = await setup.Composer.StartAsync(Boundary());
        Assert.True(start.Valid, start.Problems.FirstOrDefault()?.Reason);
        Assert.Null(await setup.World.GetEntityAsync(ActorId));
        Assert.NotNull(await start.World!.GetEntityAsync(ActorId));

        var participation = await setup.Participation.PlanAsync(new(CampaignId, ActorId), start.World!);
        Assert.True(participation.Valid, participation.Problems.FirstOrDefault()?.Reason);
        var attached = await setup.Composer.AppendAsync(start, participation.Effects);
        Assert.True(attached.Valid, attached.Problems.FirstOrDefault()?.Reason);
        Assert.True((await new CampaignCharacterParticipationVerifier(attached.World!).ResolveActiveScopeAsync(ActorId)).Valid);

        var profile = await new CharacterProfileRecorder(attached.World!, new CampaignCharacterParticipationVerifier(attached.World!))
            .PlanAsync(new(ActorId, """{"pronouns":"they/them"}"""));
        Assert.True(profile.Valid, profile.Problems.FirstOrDefault()?.Reason);
        var withProfile = await setup.Composer.AppendAsync(attached, profile.Effects);
        Assert.True(withProfile.Valid, withProfile.Problems.FirstOrDefault()?.Reason);

        var abilities = await new CharacterAbilityScoreRecorder(withProfile.World!, new CampaignCharacterParticipationVerifier(withProfile.World!))
            .PlanAsync(new(ActorId, """{"str":15,"dex":14,"con":13,"int":8,"wis":10,"cha":12}"""));
        Assert.True(abilities.Valid, abilities.Problems.FirstOrDefault()?.Reason);
        var complete = await setup.Composer.AppendAsync(withProfile, abilities.Effects);
        Assert.True(complete.Valid, complete.Problems.FirstOrDefault()?.Reason);
        Assert.Equal(7, complete.Effects.Count);
        Assert.Null(await setup.World.GetEntityAsync(ActorId));
        Assert.Null(await setup.World.GetEntityAsync(ParticipationId));

        var applied = await setup.Effects.ApplyAsync(complete.Effects);
        Assert.True(applied.Applied);
        Assert.True((await new CampaignCharacterParticipationVerifier(setup.World).ResolveActiveScopeAsync(ActorId)).Valid);
        Assert.Contains((await setup.World.GetEntityAsync(ActorId))!.Components, component => component.DefinitionId == "dnd2024.character.profile");
        Assert.Contains((await setup.World.GetEntityAsync(ActorId))!.Components, component => component.DefinitionId == "dnd2024.abilities");
    }

    [Fact]
    public async Task Staged_children_are_boundary_limited_and_cannot_write_the_real_world()
    {
        var setup = await ArrangeAsync();
        var start = await setup.Composer.StartAsync(Boundary());
        Assert.True(start.Valid);

        await Assert.ThrowsAsync<InvalidOperationException>(() => start.World!.CreateEntityAsync("Forbidden", "actor.forbidden"));
        var rejected = await setup.Composer.AppendAsync(start,
        [
            new Effect { Type = EffectType.EntityCreate, EntityId = "actor.forbidden", Name = "Forbidden" }
        ]);

        Assert.False(rejected.Valid);
        Assert.Equal("STAGED_ENTITY_NOT_ALLOWED", Assert.Single(rejected.Problems).Code);
        Assert.Null(await setup.World.GetEntityAsync(ActorId));
        Assert.Null(await setup.World.GetEntityAsync("actor.forbidden"));
    }

    private async Task<Setup> ArrangeAsync()
    {
        Copy(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        await world.CreateEntityAsync("Staged campaign", CampaignId);
        await world.SetComponentAsync(CampaignId, "game.core.campaign.root", "{\"status\":\"active\"}");
        var effects = new EffectApplier(db, world);
        return new(world, effects, new StagedWorldComposer(effects, world), new CampaignCharacterParticipationPlanner());
    }

    private static StagedWorldBoundary Boundary() => new(
        new(ActorId, "Alex"),
        new HashSet<string>([CampaignId, ActorId, ParticipationId], StringComparer.Ordinal));

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

    private const string CampaignId = "campaign.test.staged";
    private const string ActorId = "actor.test.staged.alex";
    private const string ParticipationId = "campaign.test.staged.participation.actor.test.staged.alex";
    private sealed record Setup(WorldStore World, EffectApplier Effects, StagedWorldComposer Composer, CampaignCharacterParticipationPlanner Participation);
}
