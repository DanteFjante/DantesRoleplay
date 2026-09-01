using System.Text.Json;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024WorldCampaignOwnerConvergenceTests
{
    [Fact]
    public void Canonical_shared_game_components_are_registerable_runtime_contracts()
    {
        var root = Path.Combine(RepositoryRoot(), "catalog", "components", "game");
        var validator = new BoundedJsonSchemaValidator();
        var failures = Directory.EnumerateFiles(root, "*.schema.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => (Path: path, Result: validator.Compile(File.ReadAllText(path))))
            .Where(value => !value.Result.IsAccepted)
            .Select(value => Path.GetRelativePath(root, value.Path) + ": "
                + string.Join("; ", value.Result.Diagnostics.Select(problem =>
                    $"{problem.Code} {problem.Pointer}: {problem.Message}")))
            .ToArray();

        Assert.True(failures.Length == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Runtime_bindings_use_registered_game_core_world_and_campaign_types_without_aliases()
    {
        var root = RepositoryRoot();
        var binding = File.ReadAllText(Path.Combine(root, "catalog", "applications", "dnd2024", "metadata",
            "authorized-knowledge.json"));
        var server = File.ReadAllText(Path.Combine(root, "src", "system", "web-interface", "dnd2024", "src",
            "server", "game-server-context.js"));
        var resolver = File.ReadAllText(Path.Combine(root, "src", "system", "application-execution", "persistence",
            "ApplicationMechanicProjectionResolver.cs"));
        var worldDefinition = File.ReadAllText(Path.Combine(root, "catalog", "components", "game", "core", "world",
            "root.json"));
        var campaignDefinition = File.ReadAllText(Path.Combine(root, "catalog", "components", "game", "core",
            "campaign", "root.json"));

        Assert.Contains("game.core.world.root", binding);
        Assert.Contains("game.core.world.location", binding);
        Assert.Contains("game.core.campaign.root", binding);
        Assert.Contains("game.core.world.location", server);
        Assert.Contains("game.core.campaign.root", server);
        Assert.Contains("mapping.Components.TryGetValue(localId", resolver);
        Assert.Contains("game.core.world.root", worldDefinition);
        Assert.Contains("game.core.campaign.root", campaignDefinition);
        Assert.False(File.Exists(Path.Combine(root, "catalog", "applications", "dnd2024", "components",
            "dnd2024.world.root.json")));
        Assert.False(File.Exists(Path.Combine(root, "catalog", "applications", "dnd2024", "components",
            "dnd2024.campaign.root.json")));
    }

    private static void AssertOwner(IReadOnlyList<JsonElement> owners, string capability, string local,
        string qualified, string? legacy, string disposition)
    {
        var owner = Assert.Single(owners, value => value.GetProperty("capability").GetString() == capability);
        Assert.Equal(local, owner.GetProperty("catalogLocalId").GetString());
        Assert.Equal(qualified, owner.GetProperty("canonicalQualifiedTypeId").GetString());
        Assert.Equal(legacy, owner.GetProperty("legacyPrototypeId").GetString());
        Assert.Equal(disposition, owner.GetProperty("legacyDisposition").GetString());
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
