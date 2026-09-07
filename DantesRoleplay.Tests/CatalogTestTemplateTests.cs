using DantesRoleplay.DataAccess;

namespace DantesRoleplay.Tests;

public sealed class CatalogTestTemplateTests
{
    [Fact]
    public async Task Clones_are_private_writable_databases()
    {
        using var first = await CatalogTestTemplate.CloneImportedAsync();
        using var second = await CatalogTestTemplate.CloneImportedAsync();
        await using var firstDb = first.CreateContext();
        await using var secondDb = second.CreateContext();
        var firstWorld = new WorldStore(firstDb);
        var secondWorld = new WorldStore(secondDb);

        const string cloneOnly = "location.feature-01.clone-only";
        await firstWorld.CreateEntityAsync("Clone-only entity", cloneOnly);

        Assert.NotNull(await firstWorld.GetEntityAsync(cloneOnly));
        Assert.Null(await secondWorld.GetEntityAsync(cloneOnly));
    }
}
