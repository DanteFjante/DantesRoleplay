using System.Text.Json;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024WorldCampaignOwnerConvergenceTests
{
    [Fact]
    public void Historical_g7_evidence_records_the_installed_runtime_owner_decision()
    {
        var root = RepositoryRoot();
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "ruleset", "dnd2024",
            "adoption", "evidence", "complete-campaign-world-campaign-owner-convergence.json")));

        var decision = evidence.RootElement.GetProperty("decision");
        Assert.Equal("dnd2024", decision.GetProperty("applicationId").GetString());
        Assert.Equal("dnd2024.game.core", decision.GetProperty("runtimeOwnerPrefix").GetString());
        Assert.Equal("game.core", decision.GetProperty("catalogLocalPrefix").GetString());

        var owners = evidence.RootElement.GetProperty("owners").EnumerateArray().ToArray();
        Assert.Equal(
            ["campaign-root", "world-location", "world-root"],
            owners.Select(value => value.GetProperty("capability").GetString()).OrderBy(value => value));
        AssertOwner(owners, "world-root", "game.core.world.root", "dnd2024.game.core.world.root",
            "dnd2024.world.root", "migration-input-only");
        AssertOwner(owners, "world-location", "game.core.world.location", "dnd2024.game.core.world.location",
            null, "not-applicable");
        AssertOwner(owners, "campaign-root", "game.core.campaign.root", "dnd2024.game.core.campaign.root",
            "dnd2024.campaign.root", "migration-input-only");
    }

    [Fact]
    public void Runtime_bindings_use_application_qualified_world_and_campaign_types()
    {
        var root = RepositoryRoot();
        var binding = File.ReadAllText(Path.Combine(root, "catalog", "applications", "dnd2024", "metadata",
            "authorized-knowledge.json"));
        var server = File.ReadAllText(Path.Combine(root, "src", "system", "web-interface", "dnd2024", "src",
            "server", "game-server-context.js"));
        var resolver = File.ReadAllText(Path.Combine(root, "src", "system", "application-execution", "persistence",
            "ApplicationMechanicProjectionResolver.cs"));
        var prototypeWorld = File.ReadAllText(Path.Combine(root, "catalog", "applications", "dnd2024", "components",
            "dnd2024.world.root.json"));
        var prototypeCampaign = File.ReadAllText(Path.Combine(root, "catalog", "applications", "dnd2024", "components",
            "dnd2024.campaign.root.json"));

        Assert.Contains("dnd2024.game.core.world.root", binding);
        Assert.Contains("dnd2024.game.core.world.location", binding);
        Assert.Contains("dnd2024.game.core.campaign.root", binding);
        Assert.Contains("dnd2024.game.core.world.location", server);
        Assert.Contains("dnd2024.game.core.campaign.root", server);
        Assert.Contains("mapping.Components.TryGetValue(localId", resolver);
        Assert.Contains("dnd2024.world.root", prototypeWorld);
        Assert.Contains("dnd2024.campaign.root", prototypeCampaign);
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
