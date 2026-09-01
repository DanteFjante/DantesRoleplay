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

        Assert.NotEmpty(navigator.Search(new(Application, "tie token", NamespaceId: "fixture")).Records);
        Assert.Empty(navigator.Search(new(Application, "tie token", NamespaceId: "catalog-root")).Records);
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

    [Fact]
    public void Browse_and_effective_content_keep_winners_and_additive_extension_records()
    {
        var records = new[]
        {
            Record("entity", "fixture.rules.fireball", "Fireball", "Core spell.", "tools", "active", [], []),
            Record("entity", "fixture.extension.homebrew.rules.fireball", "Fireball revised", "Replacement spell.", "tools", "active", [], []),
            Record("entity", "fixture.extension.homebrew.rules.spark", "Spark", "New spell.", "tools", "active", [], [])
        };
        var manifest = CatalogNavigationManifest.Create(Application, new string('C', 64), "catalog-lexical-v1",
            [new("fixtures", "Fixture catalog", "Extension navigation fixtures.")],
            [new("fixtures", "", "Fixture catalog", "Extension navigation fixtures.", CatalogDescriptionStatus.Authored),
             new("fixtures", "tools", "Tools", "Public records.", CatalogDescriptionStatus.Authored)], records);
        var resolution = CatalogExtensionResolutionContext.Create(Application, new string('D', 64),
            [new("homebrew", "Fixture Homebrew", "Reviewed additions.", "homebrew", ["homebrew-source"],
                ["fixture.extension.homebrew"], [], true)]);
        var navigator = new InMemoryCatalogNavigator(manifest, new CatalogCursorCodec(CursorKey), resolution);

        var browse = navigator.Browse(new(Application, "fixtures", "tools"));
        Assert.Equal(2, browse.DirectCounts["entity"]);
        Assert.Equal(["fixture.extension.homebrew.rules.fireball", "fixture.extension.homebrew.rules.spark"],
            browse.Entries.Select(value => value.Record!.QualifiedId));

        var content = navigator.EffectiveContent(new(Application, PageSize: 10));
        Assert.Equal(new string('D', 64), content.ResolutionFingerprint);
        Assert.Equal("Fixture Homebrew", Assert.Single(content.ActiveExtensions).DisplayName);
        Assert.Equal(2, content.ResolvedWinners.Count);
        Assert.Equal("fixture.extension.homebrew.rules.spark",
            Assert.Single(content.AdditiveExtensionContent).Record.QualifiedId);
        Assert.False(content.ResolvedWinners.Single(value => !value.IsAdditive).IsAdditive);
    }

    [Fact]
    public void Readable_rules_use_component_sections_extension_winners_and_audience_visibility()
    {
        var records = new[]
        {
            ReadableRule("fixture.rule.combat.attack", "Core attack", "public", "Combat", 20),
            ReadableRule("fixture.extension.homebrew.rule.combat.attack", "Homebrew attack", "public", "Combat", 20),
            ReadableRule("fixture.extension.homebrew.rule.magic.spark", "Spark", "public", "Magic", 30),
            ReadableRule("fixture.rule.guidance.secrets", "DM guidance", "dm", "Guidance", 40)
        };
        var manifest = CatalogNavigationManifest.Create(Application, new string('E', 64), "catalog-lexical-v1",
            [new("fixtures", "Fixture catalog", "Readable rule fixtures.")],
            [new("fixtures", "", "Fixture catalog", "Readable rule fixtures.", CatalogDescriptionStatus.Authored),
             new("fixtures", "tools", "Arbitrary folder", "Folders do not define rule sections.", CatalogDescriptionStatus.Authored)],
            records);
        var resolution = CatalogExtensionResolutionContext.Create(Application, new string('F', 64),
            [new("homebrew", "Fixture Homebrew", "Reviewed additions.", "homebrew", ["homebrew-source"],
                ["fixture.extension.homebrew"], [], true)]);
        var navigator = new InMemoryCatalogNavigator(manifest, new CatalogCursorCodec(CursorKey), resolution);

        var publicRules = navigator.ReadableRules(new(Application));
        var dmRules = navigator.ReadableRules(new(Application, ReadableRuleAudience.Dm));

        Assert.Equal(new string('F', 64), publicRules.ResolutionFingerprint);
        Assert.Matches("^[0-9A-F]{64}$", publicRules.RulesFingerprint);
        Assert.Equal(["combat", "magic"], publicRules.Sections.Select(value => value.Id));
        Assert.Equal("fixture.extension.homebrew.rule.combat.attack",
            Assert.Single(publicRules.Sections[0].Rules).Id);
        Assert.All(publicRules.Sections.SelectMany(value => value.Rules),
            value => Assert.Equal("homebrew", value.Source.Classification));
        Assert.Equal("rule.combat.attack", publicRules.Sections[0].Rules[0].ResolutionKey);
        Assert.Equal(["combat", "magic", "guidance"], dmRules.Sections.Select(value => value.Id));
        Assert.Equal("dm", dmRules.Sections.Single(value => value.Id == "guidance").Rules[0].Visibility);
        Assert.NotEqual(publicRules.RulesFingerprint, dmRules.RulesFingerprint);
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

    private static CatalogRecordDefinition ReadableRule(
        string id, string title, string visibility, string sectionLabel, int sectionOrder)
    {
        var sectionId = sectionLabel.ToLowerInvariant();
        var content = $$"""
            {"id":"{{id}}","name":"{{title}}","components":{"game.core.rules.readable":{
              "section":{"id":"{{sectionId}}","label":"{{sectionLabel}}","order":{{sectionOrder}}},
              "order":10,"title":"{{title}}","summary":"A readable fixture rule.",
              "blocks":[{"kind":"paragraph","heading":null,"body":"Readable fixture body.","items":[]}],
              "examples":[],"relatedRuleRefs":[],
              "citations":[{"sourceId":"fixture-source","locator":"Fixture > Rule"}],
              "mechanicIds":["fixture.mechanic.rule"],"procedureIds":[],
              "visibility":"{{visibility}}","presentationStatus":"published"}
            }
            }
            """;
        return new("fixtures", "entity", id, title, "Readable fixture.", [], [], "tools", "active", 1,
            content, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
            id.Contains(".extension.", StringComparison.Ordinal) ? "homebrew" : "core",
            "catalog/fixtures.json");
    }

    private sealed record FixtureResult(CatalogNavigationManifest Manifest, InMemoryCatalogNavigator Navigator);
}
