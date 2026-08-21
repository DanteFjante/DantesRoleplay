using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CampaignFeature15Slice2Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"campaign-feature-15-slice-2-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Attaches_one_existing_actor_atomically_and_exposes_the_canonical_active_scope()
    {
        var setup = await ArrangeAsync();
        var attached = await setup.Attacher.AttachAsync(new("attach-character-participation", setup.CampaignId, setup.ActorId));

        Assert.True(attached.Attached, attached.Problems.FirstOrDefault()?.Reason);
        Assert.NotNull(attached.ParticipationId);
        var participation = await setup.World.GetEntityAsync(attached.ParticipationId!);
        Assert.NotNull(participation);
        Assert.Equal("{\"status\":\"active\"}", Assert.Single(participation!.Components, x => x.DefinitionId == "game.core.campaign.character-participation").Data);
        Assert.Equal("{}", Assert.Single(await setup.World.GetRelationshipsAsync(setup.CampaignId, false), x => x.Kind == "game.core.campaign.has-character-participation").Data);
        Assert.Equal("{}", Assert.Single(await setup.World.GetRelationshipsAsync(attached.ParticipationId!, false), x => x.Kind == "game.core.campaign.character-participation.for-actor").Data);
        var scope = await setup.Verifier.ResolveActiveScopeAsync(setup.ActorId);
        Assert.True(scope.Valid); Assert.Equal(attached.ParticipationId, scope.ParticipationId); Assert.Equal(setup.CampaignId, scope.CampaignId);

        var replay = await setup.Attacher.AttachAsync(new("attach-character-participation", setup.CampaignId, setup.ActorId));
        Assert.False(replay.Attached); Assert.Equal("ACTOR_ALREADY_ATTACHED", Assert.Single(replay.Problems).Code);
        Assert.Equal(attached.ParticipationId, (await setup.Verifier.ResolveActiveScopeAsync(setup.ActorId)).ParticipationId);
    }

    [Fact]
    public async Task Rejects_invalid_scope_without_creating_any_participation()
    {
        var setup = await ArrangeAsync();
        var absentActor = await setup.Attacher.AttachAsync(new("attach-character-participation", setup.CampaignId, "actor.test.group.absent"));
        Assert.False(absentActor.Attached); Assert.Equal("ACTOR_NOT_FOUND", Assert.Single(absentActor.Problems).Code);
        Assert.DoesNotContain(await setup.World.FindEntitiesAsync(), x => x.Id.Contains(".participation.", StringComparison.Ordinal));

        await setup.World.SetComponentAsync(setup.CampaignId, "game.core.campaign.root", "{\"status\":\"closed\"}");
        var inactive = await setup.Attacher.AttachAsync(new("attach-character-participation", setup.CampaignId, setup.ActorId));
        Assert.False(inactive.Attached); Assert.Equal("CAMPAIGN_NOT_ACTIVE", Assert.Single(inactive.Problems).Code);
        Assert.DoesNotContain(await setup.World.FindEntitiesAsync(), x => x.Id.Contains(".participation.", StringComparison.Ordinal));
    }

    private async Task<Setup> ArrangeAsync()
    {
        Copy(Catalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        const string campaignId = "campaign.test.group";
        const string actorId = "actor.test.group.alex";
        await world.CreateEntityAsync("Group campaign", campaignId);
        await world.SetComponentAsync(campaignId, "game.core.campaign.root", "{\"status\":\"active\"}");
        await world.CreateEntityAsync("Alex", actorId);
        return new(world, new CampaignCharacterParticipationAttacher(db, world, new EffectApplier(db, world), new OperationLog(db)), new CampaignCharacterParticipationVerifier(world), campaignId, actorId);
    }

    private static string Catalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    private sealed record Setup(WorldStore World, CampaignCharacterParticipationAttacher Attacher, CampaignCharacterParticipationVerifier Verifier, string CampaignId, string ActorId);
}
