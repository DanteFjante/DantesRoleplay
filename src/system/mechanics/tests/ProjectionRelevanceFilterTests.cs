using DantesRoleplay.DataAccess;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.Tests;

/// <summary>
/// A role that declares `contentsRelevantToRoles` says it only needs to see where the named roles
/// sit inside its containment tree. That keeps a containment test working on a world far larger
/// than the node limit, without widening the limit for everyone.
/// </summary>
public sealed class ProjectionRelevanceFilterTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    /// <summary>A world of `siblings` regions, each holding one settlement; the target is buried last.</summary>
    private static async Task<WorldStore> LargeWorldAsync(DantesRoleplayDbContext db, int siblings)
    {
        var world = new WorldStore(db);
        await world.DefineComponentAsync("world", "World", "World root.");
        await world.DefineComponentAsync("place", "Place", "A location.");
        await world.CreateEntityAsync("Caldris", "world.caldris");
        await world.SetComponentAsync("world.caldris", "world", "{}");

        for (var index = 0; index < siblings; index++)
        {
            var region = $"region.{index:D3}";
            await world.CreateEntityAsync($"Region {index:D3}", region);
            await world.SetComponentAsync(region, "place", "{}");
            await world.MoveAsync(region, "world.caldris", "region");

            var town = $"town.{index:D3}";
            await world.CreateEntityAsync($"Town {index:D3}", town);
            await world.SetComponentAsync(town, "place", "{}");
            await world.MoveAsync(town, region, "settlement");
        }
        return world;
    }

    private const string Filtered = """
        {"roles":{
          "world":{"components":["world"],"includeContents":true,"contentsDepth":4,"contentsRelevantToRoles":["location"]},
          "location":{"components":["place"]}}}
        """;

    private const string Unfiltered = """
        {"roles":{
          "world":{"components":["world"],"includeContents":true,"contentsDepth":4},
          "location":{"components":["place"]}}}
        """;

    [Fact]
    public async Task An_oversized_world_still_projects_the_path_to_the_declared_role()
    {
        await using var db = _fixture.CreateContext();
        await LargeWorldAsync(db, 80); // 160 nodes: well past MaxContainedNodes of 100.
        var resolver = new ProjectionResolver(db);
        var roles = new Dictionary<string, string>
        {
            ["world"] = "world.caldris",
            ["location"] = "town.079"
        };

        var unfiltered = await resolver.ResolveAsync(MechanicRequirements.Parse(Unfiltered), roles);
        Assert.False(unfiltered.Ok);
        Assert.Contains(unfiltered.Problems, value => value.StartsWith("CONTAINMENT_PROJECTION_LIMIT", StringComparison.Ordinal));
        Assert.Contains(unfiltered.Problems, value => value.Contains("contentsRelevantToRoles", StringComparison.Ordinal));

        var filtered = await resolver.ResolveAsync(MechanicRequirements.Parse(Filtered), roles);
        Assert.True(filtered.Ok, string.Join("; ", filtered.Problems));

        // Exactly one surviving path: the region that holds the target, holding the target.
        var contents = filtered.Projection!.Roles["world"].Contains!;
        Assert.Single(contents);
        Assert.Equal("region.079", contents[0].Id);
        Assert.Equal("region", contents[0].Slot);
        Assert.Single(contents[0].Contains!);
        Assert.Equal("town.079", contents[0].Contains![0].Id);
        Assert.Equal("settlement", contents[0].Contains![0].Slot);
    }

    [Fact]
    public async Task The_filtered_projection_answers_a_containment_test_exactly_as_before()
    {
        await using var db = _fixture.CreateContext();
        await LargeWorldAsync(db, 80);
        var resolver = new ProjectionResolver(db);

        var filtered = await resolver.ResolveAsync(MechanicRequirements.Parse(Filtered),
            new Dictionary<string, string> { ["world"] = "world.caldris", ["location"] = "town.079" });
        Assert.True(filtered.Ok, string.Join("; ", filtered.Problems));

        // This is the unmodified shape real campaign mechanics use to test world membership.
        var sandbox = await new JintMechanicEngine().RunAsync("""
            function contains(nodes,id){return (nodes||[]).some(function(x){return x.id===id||contains(x.contains,id);});}
            return { data: JSON.stringify({
              found: contains(ctx.roles.world.contains, ctx.roles.location.id),
              absent: contains(ctx.roles.world.contains, 'town.001')
            }) };
            """, filtered.Projection!, ExecutionLimits.Default);

        Assert.True(sandbox.Ok, sandbox.Error);
        using var data = System.Text.Json.JsonDocument.Parse(sandbox.Output.Data);
        Assert.True(data.RootElement.GetProperty("found").GetBoolean());

        // The honest cost of the filter: siblings the mechanic did not declare are not visible.
        // A mechanic that needs to enumerate the tree must not declare the filter.
        Assert.False(data.RootElement.GetProperty("absent").GetBoolean());
    }

    [Fact]
    public async Task A_small_world_projects_identically_with_and_without_the_filter_for_the_target()
    {
        await using var db = _fixture.CreateContext();
        await LargeWorldAsync(db, 3);
        var resolver = new ProjectionResolver(db);
        var roles = new Dictionary<string, string>
        {
            ["world"] = "world.caldris",
            ["location"] = "town.001"
        };

        var unfiltered = await resolver.ResolveAsync(MechanicRequirements.Parse(Unfiltered), roles);
        var filtered = await resolver.ResolveAsync(MechanicRequirements.Parse(Filtered), roles);
        Assert.True(unfiltered.Ok, string.Join("; ", unfiltered.Problems));
        Assert.True(filtered.Ok, string.Join("; ", filtered.Problems));

        // Unfiltered sees all three regions; filtered sees one, and it is the same node.
        Assert.Equal(3, unfiltered.Projection!.Roles["world"].Contains!.Count);
        var kept = Assert.Single(filtered.Projection!.Roles["world"].Contains!);
        var same = unfiltered.Projection.Roles["world"].Contains!.Single(value => value.Id == "region.001");
        Assert.Equal(same.Id, kept.Id);
        Assert.Equal(same.Slot, kept.Slot);
        Assert.Equal(same.Contains!.Single().Id, kept.Contains!.Single().Id);
    }

    [Fact]
    public async Task The_filter_must_name_a_declared_role_other_than_itself()
    {
        await using var db = _fixture.CreateContext();
        await LargeWorldAsync(db, 2);
        var resolver = new ProjectionResolver(db);
        var roles = new Dictionary<string, string> { ["world"] = "world.caldris", ["location"] = "town.001" };

        var undeclared = await resolver.ResolveAsync(MechanicRequirements.Parse("""
            {"roles":{"world":{"components":["world"],"includeContents":true,"contentsRelevantToRoles":["ghost"]},
                      "location":{"components":["place"]}}}
            """), roles);
        Assert.False(undeclared.Ok);
        Assert.Contains(undeclared.Problems, value => value.Contains("undeclared role 'ghost'", StringComparison.Ordinal));

        var itself = await resolver.ResolveAsync(MechanicRequirements.Parse("""
            {"roles":{"world":{"components":["world"],"includeContents":true,"contentsRelevantToRoles":["world"]},
                      "location":{"components":["place"]}}}
            """), roles);
        Assert.False(itself.Ok);
        Assert.Contains(itself.Problems, value => value.Contains("cannot declare itself", StringComparison.Ordinal));

        var withoutContents = await resolver.ResolveAsync(MechanicRequirements.Parse("""
            {"roles":{"world":{"components":["world"],"contentsRelevantToRoles":["location"]},
                      "location":{"components":["place"]}}}
            """), roles);
        Assert.False(withoutContents.Ok);
        Assert.Contains(withoutContents.Problems, value => value.Contains("without includeContents", StringComparison.Ordinal));
    }
}
