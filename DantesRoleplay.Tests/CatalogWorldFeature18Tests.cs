using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class CatalogWorldFeature18Tests
{
    private const string Anchor = "game.core.world.map.anchor";

    [Fact]
    public async Task Catalog_publishes_one_unchanged_anchor_schema_for_every_supported_plane()
    {
        var contents = await CatalogReader.ReadAsync(Catalog());
        var anchor = Assert.Single(contents.Components, component => component.Id == Anchor);
        var spatial = Assert.Single(contents.Procedures,
            procedure => procedure.Id == "procedure.game.core.world.spatial");
        var read = Assert.Single(contents.Procedures,
            procedure => procedure.Id == "procedure.game.core.world.read");

        Assert.Contains("active `game.core.world.root`", spatial.Instructions, StringComparison.Ordinal);
        Assert.Contains("kind is `region` or `settlement`", spatial.Instructions, StringComparison.Ordinal);
        Assert.Contains("unique within that plane", spatial.Instructions, StringComparison.Ordinal);
        Assert.Contains("selected active map plane", read.Instructions, StringComparison.Ordinal);
        Assert.Contains("Equal coordinates on another plane are unrelated", read.Instructions,
            StringComparison.Ordinal);

        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(anchor.Schema);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
        Assert.Equal(SchemaValueStatus.Valid,
            validator.Validate(compilation.ProfileId, compilation.NormalizedSchema,
                """{"x":500,"y":500}""").Status);
        Assert.Equal(SchemaValueStatus.Invalid,
            validator.Validate(compilation.ProfileId, compilation.NormalizedSchema,
                """{"x":500,"y":500,"planeId":"world.fixture"}""").Status);
    }

    [Theory]
    [InlineData("root", "active", "region", "active", "region", true)]
    [InlineData("region", "active", "region", "active", "region", true)]
    [InlineData("region", "active", "settlement", "active", "location", true)]
    [InlineData("settlement", "active", "site", "active", "location", true)]
    [InlineData("settlement", "active", "interior", "active", "location", true)]
    [InlineData("root", "active", "settlement", "active", "location", false)]
    [InlineData("settlement", "active", "region", "active", "region", false)]
    [InlineData("site", "active", "interior", "active", "location", false)]
    [InlineData("interior", "active", "site", "active", "location", false)]
    [InlineData("region", "draft", "settlement", "active", "location", false)]
    [InlineData("region", "active", "settlement", "archived", "location", false)]
    [InlineData("region", "active", "settlement", "active", "region", false)]
    public void Plane_scope_accepts_only_active_topology_valid_direct_children(
        string planeKind, string planeStatus, string childKind, string childStatus,
        string slot, bool expected) =>
        Assert.Equal(expected, ValidPlacement(planeKind, planeStatus, childKind, childStatus, slot));

    [Fact]
    public void Coordinate_uniqueness_is_scoped_per_plane()
    {
        Assert.True(UniquePerPlane([
            new("world.fixture", 500, 500),
            new("region.fixture", 500, 500),
            new("city.fixture", 500, 500),
        ]));
        Assert.False(UniquePerPlane([
            new("region.fixture", 250, 750),
            new("region.fixture", 250, 750),
        ]));
    }

    private static bool ValidPlacement(string planeKind, string planeStatus,
        string childKind, string childStatus, string slot)
    {
        if (planeStatus != "active" || childStatus != "active" ||
            planeKind is not ("root" or "region" or "settlement") ||
            childKind is not ("region" or "settlement" or "site" or "interior")) return false;
        return planeKind switch
        {
            "root" => childKind == "region" && slot == "region",
            "region" => childKind == "region" ? slot == "region" : slot == "location",
            "settlement" => childKind != "region" && slot == "location",
            _ => false
        };
    }

    private static bool UniquePerPlane(IEnumerable<Placement> placements) => placements
        .GroupBy(placement => placement.PlaneId, StringComparer.Ordinal)
        .All(group => group.Select(placement => (placement.X, placement.Y)).Distinct().Count() == group.Count());

    private static string Catalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }

    private sealed record Placement(string PlaneId, int X, int Y);
}
