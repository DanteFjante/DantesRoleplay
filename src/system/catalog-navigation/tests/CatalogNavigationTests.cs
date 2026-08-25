using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Applications;

namespace DantesRoleplay.CatalogNavigation.Tests;

public sealed class CatalogNavigationTests
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("fixture");
    private static readonly byte[] CursorKey = Encoding.UTF8.GetBytes("fixture-catalog-cursor-key-has-at-least-256-bits");

    [Fact]
    public void Manifest_requires_described_roots_ancestors_exact_ownership_and_content_hashes()
    {
        var fixture = Fixture();
        Assert.Throws<ArgumentException>(() => CatalogNavigationManifest.Create(Application, fixture.Manifest.Fingerprint, "catalog-lexical-v1",
            fixture.Manifest.Collections, fixture.Manifest.Nodes.Where(node => node.Path != "").ToArray(), fixture.Manifest.Records));
        Assert.Throws<ArgumentException>(() => CatalogNavigationManifest.Create(Application, fixture.Manifest.Fingerprint, "catalog-lexical-v1",
            fixture.Manifest.Collections, fixture.Manifest.Nodes.Where(node => node.Path != "tools").ToArray(), fixture.Manifest.Records));
        Assert.Throws<ArgumentException>(() => CatalogNavigationManifest.Create(Application, fixture.Manifest.Fingerprint, "catalog-lexical-v1",
            fixture.Manifest.Collections, fixture.Manifest.Nodes.Select(node => node with
            {
                DescriptionStatus = CatalogDescriptionStatus.Authored,
                Description = ""
            }).ToArray(), fixture.Manifest.Records));
        Assert.Throws<ArgumentException>(() => CatalogNavigationManifest.Create(Application, fixture.Manifest.Fingerprint, "catalog-lexical-v1",
            fixture.Manifest.Collections, fixture.Manifest.Nodes, [fixture.Manifest.Records[0] with { QualifiedId = "other.record" }]));
        Assert.Throws<ArgumentException>(() => CatalogNavigationManifest.Create(Application, fixture.Manifest.Fingerprint, "catalog-lexical-v1",
            fixture.Manifest.Collections, fixture.Manifest.Nodes, [fixture.Manifest.Records[0] with { ContentFingerprint = new string('A', 64) }]));
        Assert.Throws<ArgumentException>(() => CatalogNavigationManifest.Create(Application, fixture.Manifest.Fingerprint, "catalog-lexical-v1",
            fixture.Manifest.Collections, [.. fixture.Manifest.Nodes, fixture.Manifest.Nodes[0]], fixture.Manifest.Records));
        Assert.Throws<ArgumentException>(() => CatalogNavigationManifest.Create(Application, fixture.Manifest.Fingerprint, "catalog-lexical-v1",
            fixture.Manifest.Collections, fixture.Manifest.Nodes, [.. fixture.Manifest.Records, fixture.Manifest.Records[0]]));
    }

    [Fact]
    public void Browse_returns_authored_metadata_counts_and_stable_combined_pages()
    {
        var fixture = Fixture();
        var first = fixture.Navigator.Browse(new(Application, "fixtures", PageSize: 2));
        var second = fixture.Navigator.Browse(new(Application, "fixtures", PageSize: 2, Cursor: first.NextCursor));
        var third = fixture.Navigator.Browse(new(Application, "fixtures", PageSize: 2, Cursor: second.NextCursor));

        Assert.Equal("Fixture catalog", first.Node.Title);
        Assert.Equal(CatalogDescriptionStatus.Authored, first.Node.DescriptionStatus);
        Assert.Equal([""], first.Breadcrumbs.Select(node => node.Path));
        Assert.Equal(2, first.Entries.Count);
        Assert.All(first.Entries, entry => Assert.Equal(CatalogBrowseEntryKind.Node, entry.Kind));
        Assert.Equal(["empty", "tools"], first.Entries.Select(entry => entry.Node!.Path));
        Assert.Equal(3, first.DirectCounts["document"]);
        Assert.Equal(4, first.SubtreeCounts["procedure"]);
        Assert.NotNull(first.NextCursor);
        Assert.NotNull(second.NextCursor);
        Assert.Null(third.NextCursor);

        var keys = first.Entries.Concat(second.Entries).Concat(third.Entries).Select(entry => entry.StableKey).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
        Assert.Equal(CatalogDescriptionStatus.Missing, first.Entries.Single(entry => entry.Node?.Path == "tools").Node!.DescriptionStatus);

        var tools = fixture.Navigator.Browse(new(Application, "fixtures", "tools"));
        Assert.Equal(["", "tools"], tools.Breadcrumbs.Select(node => node.Path));
        Assert.Equal(3, tools.DirectCounts["procedure"]);
        Assert.Equal(4, tools.SubtreeCounts["procedure"]);
        Assert.Equal(CatalogDescriptionStatus.Authored, tools.Entries.Single(entry => entry.Kind == CatalogBrowseEntryKind.Node).Node!.DescriptionStatus);
    }

    [Fact]
    public void Search_uses_each_stable_lexical_rank_with_unicode_and_filters()
    {
        var navigator = Fixture().Navigator;

        Assert.Equal(0, Assert.Single(navigator.Search(new(Application, "fixture.attack")).Records).Rank);
        Assert.Equal(1, Assert.Single(navigator.Search(new(Application, "strike")).Records).Rank);
        Assert.Equal(2, Assert.Single(navigator.Search(new(Application, "name match")).Records).Rank);
        Assert.Equal(3, Assert.Single(navigator.Search(new(Application, "pref")).Records).Rank);
        Assert.Equal(4, Assert.Single(navigator.Search(new(Application, "wild clue")).Records).Rank);
        Assert.Equal("fixture.cafe", Assert.Single(navigator.Search(new(Application, "Cafe\u0301")).Records).Record.QualifiedId);
        Assert.Empty(navigator.Search(new(Application, "wild clue", Statuses: ["active"])).Records);

        var filtered = navigator.Search(new(Application, "tie token", "fixtures", "tools", ["procedure"], ["active"]));
        Assert.Equal(["fixture.tie-a", "fixture.tie-b"], filtered.Records.Select(hit => hit.Record.QualifiedId));
        Assert.All(filtered.Records, hit => Assert.Equal(4, hit.Rank));
    }

    [Fact]
    public void Inspect_and_cursors_are_exact_scoped_and_cross_application_safe()
    {
        var fixture = Fixture();
        var record = fixture.Navigator.Inspect(new(Application, "fixtures", "fixture.attack"));
        Assert.Equal("fixture.attack", record.Summary.QualifiedId);
        Assert.Equal(1, record.Summary.Version);
        Assert.Equal("core", record.Summary.SourceId);
        Assert.Equal("{\"id\":\"fixture.attack\"}", record.ContentJson);

        Assert.Throws<KeyNotFoundException>(() => fixture.Navigator.Inspect(new(Application, "fixtures", "fixture.unknown")));
        Assert.Throws<ArgumentException>(() => fixture.Navigator.Browse(new(ApplicationIdentifier.Parse("other"), "fixtures")));

        var first = fixture.Navigator.Search(new(Application, "tie token", PageSize: 1));
        Assert.NotNull(first.NextCursor);
        Assert.Throws<InvalidOperationException>(() => fixture.Navigator.Search(new(Application, "tie token", PageSize: 2, Cursor: first.NextCursor)));
        var tampered = first.NextCursor![..^1] + (first.NextCursor[^1] == 'A' ? "B" : "A");
        Assert.Throws<ArgumentException>(() => fixture.Navigator.Search(new(Application, "tie token", PageSize: 1, Cursor: tampered)));

        var otherManifest = Fixture(fingerprint: new string('B', 64));
        Assert.Throws<InvalidOperationException>(() => otherManifest.Navigator.Search(new(Application, "tie token", PageSize: 1, Cursor: first.NextCursor)));
    }

    private static FixtureResult Fixture(string fingerprint = "")
    {
        var records = new[]
        {
            Record("document", "fixture.name", "Name Match", "A root document.", "", "active", [], []),
            Record("document", "fixture.prefix", "Prefix candidate", "A root document.", "", "active", [], []),
            Record("document", "fixture.wild", "Plain document", "A wild hidden clue.", "", "archived", [], []),
            Record("procedure", "fixture.attack", "Attack", "Apply a declared action.", "tools", "active", ["strike"], ["attack target"]),
            Record("procedure", "fixture.cafe", "Café", "A Unicode fixture.", "tools/advanced", "active", [], []),
            Record("procedure", "fixture.tie-a", "Tie A", "A tie token fixture.", "tools", "active", [], []),
            Record("procedure", "fixture.tie-b", "Tie B", "A tie token fixture.", "tools", "active", [], [])
        };
        var manifest = CatalogNavigationManifest.Create(Application,
            fingerprint.Length == 0 ? new string('A', 64) : fingerprint,
            "catalog-lexical-v1",
            [new("fixtures", "Fixture catalog", "Generic navigation fixtures.")],
            [
                new("fixtures", "", "Fixture catalog", "Generic navigation fixtures.", CatalogDescriptionStatus.Authored),
                new("fixtures", "empty", "Empty", "An empty node.", CatalogDescriptionStatus.Authored),
                new("fixtures", "tools", "Tools", "", CatalogDescriptionStatus.Missing),
                new("fixtures", "tools/advanced", "Advanced", "Advanced generic records.", CatalogDescriptionStatus.Authored)
            ],
            records);
        return new(manifest, new InMemoryCatalogNavigator(manifest, new CatalogCursorCodec(CursorKey)));
    }

    private static CatalogRecordDefinition Record(string kind, string id, string name, string description, string path, string status, IReadOnlyList<string> aliases, IReadOnlyList<string> phrases)
    {
        var content = $$"""{"id":"{{id}}"}""";
        return new("fixtures", kind, id, name, description, aliases, phrases, path, status, 1, content,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))), "core", "catalog/fixtures.json");
    }

    private sealed record FixtureResult(CatalogNavigationManifest Manifest, InMemoryCatalogNavigator Navigator);
}
