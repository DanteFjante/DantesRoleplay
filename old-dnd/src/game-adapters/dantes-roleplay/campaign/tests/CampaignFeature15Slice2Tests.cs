using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer.Tools;
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
        Assert.Equal($"{setup.CampaignId}.participation.{setup.ActorId}", attached.ParticipationId);
        var participation = await setup.World.GetEntityAsync(attached.ParticipationId!);
        Assert.NotNull(participation);
        Assert.Equal("{\"status\":\"active\"}", Assert.Single(participation!.Components, x => x.DefinitionId == "game.core.campaign.character-participation").Data);
        Assert.Equal("{}", Assert.Single(await setup.World.GetRelationshipsAsync(setup.CampaignId, false), x => x.Kind == "game.core.campaign.has-character-participation").Data);
        Assert.Equal("{}", Assert.Single(await setup.World.GetRelationshipsAsync(attached.ParticipationId!, false), x => x.Kind == "game.core.campaign.character-participation.for-actor").Data);
        Assert.Equal(4, (await setup.Ledger.FindAsync(rootOperationId: attached.OperationId)).Count);
        var scope = await setup.Verifier.ResolveActiveScopeAsync(setup.ActorId);
        Assert.True(scope.Valid); Assert.Equal(attached.ParticipationId, scope.ParticipationId); Assert.Equal(setup.CampaignId, scope.CampaignId);

        var replay = await setup.Attacher.AttachAsync(new("attach-character-participation", setup.CampaignId, setup.ActorId));
        Assert.False(replay.Attached); Assert.Equal("ACTOR_ALREADY_ATTACHED", Assert.Single(replay.Problems).Code);
        Assert.Equal(attached.ParticipationId, (await setup.Verifier.ResolveActiveScopeAsync(setup.ActorId)).ParticipationId);
    }

    [Fact]
    public async Task Attaches_two_group_members_to_the_same_campaign_with_distinct_scopes()
    {
        var setup = await ArrangeAsync();
        const string secondActorId = "actor.test.group.blair";
        await setup.World.CreateEntityAsync("Blair", secondActorId);

        var first = await setup.Attacher.AttachAsync(new("attach-character-participation", setup.CampaignId, setup.ActorId));
        var second = await setup.Attacher.AttachAsync(new("attach-character-participation", setup.CampaignId, secondActorId));

        Assert.True(first.Attached); Assert.True(second.Attached);
        Assert.NotEqual(first.ParticipationId, second.ParticipationId);
        Assert.Equal(2, (await setup.World.GetRelationshipsAsync(setup.CampaignId, false)).Count(x => x.Kind == "game.core.campaign.has-character-participation"));
        Assert.Equal(setup.CampaignId, (await setup.Verifier.ResolveActiveScopeAsync(secondActorId)).CampaignId);
    }

    [Fact]
    public async Task Exposes_only_the_closed_campaign_attach_operation()
    {
        var setup = await ArrangeAsync();
        var payload = $"{{\"operation\":\"attach-character-participation\",\"campaignId\":\"{setup.CampaignId}\",\"actorId\":\"{setup.ActorId}\"}}";

        var attached = await new CommitTool().CommitAsync(
            procedures: null!, world: null!, effects: null!, mechanics: null!, eventTypes: null!, subscriptions: null!, actions: null!, itineraries: null!,
            campaigns: null!, campaignBootstrapper: null!, campaignContinuity: null!, campaignSessions: null!, campaignSessionStarter: null!, quests: null!, questLifecycle: null!,
            log: new OperationLog(setup.Db), notifications: null!, kind: "campaign", payload: payload, campaignParticipation: setup.Attacher);
        var extra = await new CommitTool().CommitAsync(
            procedures: null!, world: null!, effects: null!, mechanics: null!, eventTypes: null!, subscriptions: null!, actions: null!, itineraries: null!,
            campaigns: null!, campaignBootstrapper: null!, campaignContinuity: null!, campaignSessions: null!, campaignSessionStarter: null!, quests: null!, questLifecycle: null!,
            log: new OperationLog(setup.Db), notifications: null!, kind: "campaign", payload: payload[..^1] + ",\"status\":\"active\"}", campaignParticipation: setup.Attacher);

        Assert.True(attached.Ok); Assert.False(extra.Ok);
        Assert.Equal(1, (await setup.World.GetRelationshipsAsync(setup.CampaignId, false)).Count(x => x.Kind == "game.core.campaign.has-character-participation"));
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
        var ledger = new EventLedger(db); await new EventTypeSeeder(new EventTypeStore(db)).SeedAsync();
        return new(db, world, ledger, new CampaignCharacterParticipationAttacher(db, world, new EffectApplier(db, world, null, ledger), new OperationLog(db)), new CampaignCharacterParticipationVerifier(world), campaignId, actorId);
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

    private sealed record Setup(DantesRoleplayDbContext Db, WorldStore World, EventLedger Ledger, CampaignCharacterParticipationAttacher Attacher, CampaignCharacterParticipationVerifier Verifier, string CampaignId, string ActorId);
}
