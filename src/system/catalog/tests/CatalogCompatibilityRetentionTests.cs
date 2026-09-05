using System.Text.Json;
using DantesRoleplay.DataAccess.Catalog;

namespace DantesRoleplay.Tests;

public sealed class CatalogCompatibilityRetentionTests
{
    private static readonly RetainedCatalogIdentity Retained = new("mechanic", "sample.legacy.run", "mechanics/sample/legacy/run.md");
    private static string Policy() => JsonSerializer.Serialize(new {
        schemaVersion = 1, classification = "migration-only", owner = "Fixture owner", reason = "Retained live references",
        retirementCondition = "Zero references and backup readback", namespacePolicy = "No new identities", evidence = "Fixture evidence",
        namespaces = new[] { "sample.legacy" }, recordKinds = new[] { "mechanic" },
        records = new[] { new { kind = Retained.Kind, id = Retained.Id, path = Retained.Path } }
    });

    [Fact]
    public void Exact_retained_inventory_allows_unrelated_current_content()
    {
        Assert.Empty(CatalogCompatibilityRetention.Validate(Policy(), [Retained, new("mechanic", "sample.current.run", "current.md"),
            new("event-type", "sample.legacy.recorded", "events.json")]));
    }

    [Fact]
    public void New_missing_moved_and_malformed_retention_fail_closed()
    {
        Assert.NotEmpty(CatalogCompatibilityRetention.Validate(Policy(), []));
        Assert.NotEmpty(CatalogCompatibilityRetention.Validate(Policy(), [Retained, new("mechanic", "sample.legacy.new", "new.md")]));
        Assert.NotEmpty(CatalogCompatibilityRetention.Validate(Policy(), [Retained with { Path = "moved.md" }]));
        Assert.NotEmpty(CatalogCompatibilityRetention.Validate("{}", [Retained]));
        Assert.NotEmpty(CatalogCompatibilityRetention.Validate(Policy().Replace("sample.legacy\"", "other.legacy\""), [Retained]));
    }
}
