using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class CampaignFeature15Slice3Tests : IDisposable
{
    private const string Participation = "game.core.campaign.character-participation";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"campaign-feature-15-slice-3-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Returns_one_withdrawal_fragment_without_writing_and_the_containing_root_can_roll_it_back()
    {
        var setup = await ArrangeAsync();
        var attached = await setup.Attacher.AttachAsync(new("attach-character-participation", setup.CampaignId, setup.ActorId));
        Assert.True(attached.Attached, attached.Problems.FirstOrDefault()?.Reason);
        var participationId = Assert.IsType<string>(attached.ParticipationId);
        var before = await StateAsync(setup.World, participationId);

        var plan = await setup.Withdrawal.PlanWithdrawalAsync(new(setup.ActorId));

        Assert.True(plan.Valid, plan.Problems.FirstOrDefault()?.Reason);
        Assert.Equal(setup.CampaignId, plan.CampaignId);
        Assert.Equal(setup.ActorId, plan.ActorId);
        Assert.Equal(participationId, plan.ParticipationId);
        var effect = Assert.Single(plan.Effects);
        Assert.Equal(EffectType.ComponentSet, effect.Type);
        Assert.Equal(participationId, effect.EntityId);
        Assert.Equal(Participation, effect.DefinitionId);
        Assert.Equal("{\"status\":\"withdrawn\"}", effect.Data);
        Assert.Equal(before, await StateAsync(setup.World, participationId));

        await using (var transaction = await setup.Db.Database.BeginTransactionAsync())
        {
            var applied = await setup.Effects.ApplyAsync(plan.Effects, rootOperationId: "campaign-feature-15-slice-3-root");
            Assert.True(applied.Applied, string.Join(" ", applied.Problems.Select(problem => problem.Problem)));
            Assert.Equal("{\"status\":\"withdrawn\"}", await StateAsync(setup.World, participationId));
            await transaction.RollbackAsync();
            setup.Db.ChangeTracker.Clear();
        }

        using var fresh = _fixture.CreateContext();
        var freshWorld = new WorldStore(fresh);
        Assert.Equal(before, await StateAsync(freshWorld, participationId));
        Assert.True((await new CampaignCharacterParticipationVerifier(freshWorld).ResolveActiveScopeAsync(setup.ActorId)).Valid);
    }

    [Fact]
    public async Task Rejects_absent_or_non_active_scope_without_returning_effects()
    {
        var setup = await ArrangeAsync();
        var absent = await setup.Withdrawal.PlanWithdrawalAsync(new("actor.test.group.absent"));
        Assert.False(absent.Valid);
        Assert.Equal("ACTOR_NOT_FOUND", Assert.Single(absent.Problems).Code);
        Assert.Empty(absent.Effects);

        var attached = await setup.Attacher.AttachAsync(new("attach-character-participation", setup.CampaignId, setup.ActorId));
        Assert.True(attached.Attached);
        await setup.World.SetComponentAsync(attached.ParticipationId!, Participation, "{\"status\":\"withdrawn\"}");

        var withdrawn = await setup.Withdrawal.PlanWithdrawalAsync(new(setup.ActorId));
        Assert.False(withdrawn.Valid);
        Assert.Equal("PARTICIPATION_NOT_ACTIVE", Assert.Single(withdrawn.Problems).Code);
        Assert.Empty(withdrawn.Effects);
        Assert.Equal("{\"status\":\"withdrawn\"}", await StateAsync(setup.World, attached.ParticipationId!));
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
        var ledger = new EventLedger(db);
        await new EventTypeSeeder(new EventTypeStore(db)).SeedAsync();
        var verifier = new CampaignCharacterParticipationVerifier(world);
        return new(db, world,
            new CampaignCharacterParticipationAttacher(db, world, new EffectApplier(db, world, null, ledger), new OperationLog(db)),
            new CampaignCharacterParticipationWithdrawalPlanner(verifier), new EffectApplier(db, world), campaignId, actorId);
    }

    private static async Task<string?> StateAsync(IWorldStore world, string participationId) =>
        (await world.GetEntityAsync(participationId))?.Components.Single(component => component.DefinitionId == Participation).Data;

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

    private sealed record Setup(
        DantesRoleplayDbContext Db,
        WorldStore World,
        CampaignCharacterParticipationAttacher Attacher,
        CampaignCharacterParticipationWithdrawalPlanner Withdrawal,
        EffectApplier Effects,
        string CampaignId,
        string ActorId);
}
