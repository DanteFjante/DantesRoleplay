using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CampaignFeature15Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"campaign-feature-15-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Resolves_exactly_one_active_campaign_scope_without_writing()
    {
        var setup = await ArrangeAsync("active");
        var before = await setup.World.FindEntitiesAsync();

        var scope = await setup.Verifier.ResolveActiveScopeAsync(setup.ActorId);

        Assert.True(scope.Valid); Assert.Equal(setup.CampaignId, scope.CampaignId); Assert.Equal(setup.ParticipationId, scope.ParticipationId);
        Assert.Equal(before.Select(x => x.Id).Order(), (await setup.World.FindEntitiesAsync()).Select(x => x.Id).Order());
        Assert.Equal("{}", Assert.Single(await setup.World.GetRelationshipsAsync(setup.CampaignId, false), x => x.Kind == "game.core.campaign.has-character-participation").Data);
    }

    [Theory]
    [InlineData("withdrawn", "PARTICIPATION_NOT_ACTIVE")]
    [InlineData("malformed", "PARTICIPATION_NOT_ACTIVE")]
    public async Task Rejects_non_active_or_malformed_participation_without_a_scope(string state, string code)
    {
        var setup = await ArrangeAsync(state);

        var scope = await setup.Verifier.ResolveActiveScopeAsync(setup.ActorId);

        Assert.False(scope.Valid); Assert.Null(scope.CampaignId); Assert.Equal(code, Assert.Single(scope.Problems).Code);
    }

    [Fact]
    public async Task Rejects_duplicate_participations_and_inactive_campaigns()
    {
        var setup = await ArrangeAsync("active");
        var second = "campaign.test.group.participation.second";
        await setup.World.CreateEntityAsync("Second participation", second);
        await setup.World.SetComponentAsync(second, "game.core.campaign.character-participation", "{\"status\":\"active\"}");
        await setup.World.RelateAsync(setup.CampaignId, second, "game.core.campaign.has-character-participation");
        await setup.World.RelateAsync(second, setup.ActorId, "game.core.campaign.character-participation.for-actor");

        var duplicate = await setup.Verifier.ResolveActiveScopeAsync(setup.ActorId);

        Assert.False(duplicate.Valid); Assert.Equal("PARTICIPATION_GRAPH_INVALID", Assert.Single(duplicate.Problems).Code);
        await setup.World.UnrelateAsync(second, setup.ActorId, "game.core.campaign.character-participation.for-actor");
        await setup.World.SetComponentAsync(setup.CampaignId, "game.core.campaign.root", "{\"status\":\"closed\"}");
        var inactive = await setup.Verifier.ResolveActiveScopeAsync(setup.ActorId);
        Assert.False(inactive.Valid); Assert.Equal("CAMPAIGN_NOT_ACTIVE", Assert.Single(inactive.Problems).Code);
    }

    private async Task<Setup> ArrangeAsync(string participationState)
    {
        Copy(Catalog(), _catalogCopy);
        var db = _fixture.CreateContext(); var world = new WorldStore(db);
        var import = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(import.Aborted);
        const string campaignId = "campaign.test.group"; const string actorId = "actor.test.group.alex"; const string participationId = "campaign.test.group.participation.actor-test-group-alex";
        await world.CreateEntityAsync("Group campaign", campaignId);
        await world.SetComponentAsync(campaignId, "game.core.campaign.root", "{\"status\":\"active\"}");
        await world.CreateEntityAsync("Alex", actorId);
        await world.CreateEntityAsync("Alex participation", participationId);
        await world.SetComponentAsync(participationId, "game.core.campaign.character-participation", participationState == "malformed" ? "{\"status\":\"active\",\"extra\":true}" : $"{{\"status\":\"{participationState}\"}}");
        await world.RelateAsync(campaignId, participationId, "game.core.campaign.has-character-participation");
        await world.RelateAsync(participationId, actorId, "game.core.campaign.character-participation.for-actor");
        return new(db, world, new CampaignCharacterParticipationVerifier(world), campaignId, actorId, participationId);
    }

    private static string Catalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file))); }
    private sealed record Setup(DantesRoleplayDbContext Db, WorldStore World, CampaignCharacterParticipationVerifier Verifier, string CampaignId, string ActorId, string ParticipationId);
}
