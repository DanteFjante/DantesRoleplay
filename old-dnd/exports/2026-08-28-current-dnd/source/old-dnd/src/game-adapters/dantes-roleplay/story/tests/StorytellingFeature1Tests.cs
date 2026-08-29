using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.Tests;

public sealed class StorytellingFeature1Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Fresh_seeded_host_retrieves_the_canonical_trusted_host_storytelling_contract()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);
        await new ProcedureSeeder(store).SeedAsync();

        var procedure = await store.GetAsync("procedure.play.storytelling");

        Assert.NotNull(procedure);
        var text = $"{procedure.Description} {procedure.Instructions} {procedure.Constraints}";
        Assert.Contains("query(kind: \"campaign-resume\", id: \"campaign....\")", text, StringComparison.Ordinal);
        Assert.Contains("game.core.campaign.chapter", text, StringComparison.Ordinal);
        Assert.Contains("game.core.world.motive", text, StringComparison.Ordinal);
        Assert.Contains("game.core.world.clue", text, StringComparison.Ordinal);
        Assert.Contains("visibility labels are descriptive metadata", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never commits", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`chapter` component", text, StringComparison.Ordinal);
        Assert.DoesNotContain("`motive` component", text, StringComparison.Ordinal);
        Assert.DoesNotContain("`clue` data", text, StringComparison.Ordinal);
    }
}
