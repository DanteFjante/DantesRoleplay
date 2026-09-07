using System.Text.Json;
using DantesRoleplay.DataAccess.Catalog;

namespace DantesRoleplay.Tests;

public sealed class CatalogCompatibilityRetentionTests
{
    private static readonly RetainedCatalogIdentity Retained = new("mechanic", "sample.legacy.run", "mechanics/sample/legacy/run.md");
    private static string Policy() => JsonSerializer.Serialize(new {
        schemaVersion = 2, classification = "migration-only", owner = "Fixture owner", reason = "Retained live references",
        retirementCondition = "Zero references and backup readback", namespacePolicy = "No new identities", evidence = "Fixture evidence",
        review = new {
            reviewedAt = "2026-09-07", disposition = "retain-all",
            liveExport = new { recordCount = 1, operationCount = 0, retainedRecordCount = 1,
                databaseSha256 = new string('A', 64) },
            recovery = new { databaseSha256 = new string('B', 64), blobCount = 0,
                blobBytes = 0, blobHashDifferences = 0 },
            groups = new[] { new { name = "fixture", reason = "Fixture retention",
                recordIds = new[] { Retained.Id } } }
        },
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
        Assert.NotEmpty(CatalogCompatibilityRetention.Validate(Policy().Replace("retain-all", "retire"), [Retained]));
        Assert.NotEmpty(CatalogCompatibilityRetention.Validate(Policy().Replace(
            $"\"recordIds\":[\"{Retained.Id}\"]", "\"recordIds\":[\"sample.legacy.other\"]"), [Retained]));
    }
}
