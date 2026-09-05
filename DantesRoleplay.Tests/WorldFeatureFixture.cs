using System.Text.Json.Nodes;

namespace DantesRoleplay.Tests;

internal static class WorldFeatureFixture
{
    // Retained test graph from ad4b5f3c^ before a live export replaced world/relationships.json.
    // Never restore demonstration relationships into the authored or running-world catalog.
    internal static void RestoreRelationships(string sourceCatalog, string copiedCatalog)
    {
        if (Path.GetFullPath(sourceCatalog) == Path.GetFullPath(copiedCatalog))
            throw new InvalidOperationException("Fixture relationships require an isolated catalog copy.");
        var fixturePath = Path.Combine(Path.GetDirectoryName(sourceCatalog)!,
            "DantesRoleplay.Tests", "Fixtures", "world-feature-relationships.json");
        var targetPath = Path.Combine(copiedCatalog, "world", "relationships.json");
        var target = JsonNode.Parse(File.ReadAllText(targetPath))!;
        var relationships = target["relationships"]!.AsArray();
        var fixture = JsonNode.Parse(File.ReadAllText(fixturePath))!["relationships"]!.AsArray();
        foreach (var link in fixture)
        {
            if (!link!["from"]!.GetValue<string>().Contains(".feature-", StringComparison.Ordinal)
                || !link["to"]!.GetValue<string>().Contains(".feature-", StringComparison.Ordinal))
                throw new InvalidOperationException("A test-only graph cannot contain live campaign links.");
            var existing = relationships.SingleOrDefault(candidate =>
                candidate!["from"]!.ToJsonString() == link["from"]!.ToJsonString()
                && candidate["to"]!.ToJsonString() == link["to"]!.ToJsonString()
                && candidate["kind"]!.ToJsonString() == link["kind"]!.ToJsonString());
            if (existing is null) relationships.Add(link.DeepClone());
            else if (!JsonNode.DeepEquals(existing, link))
                throw new InvalidOperationException("Conflicting test fixture relationship.");
        }
        File.WriteAllText(targetPath, target.ToJsonString());
    }
}
