using System.Text.Json;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.Tests;

public sealed class CatalogAntiSprawlTests
{
    [Fact]
    public void Identical_phrase_blocks_two_active_mechanics()
    {
        var analysis = Analyze(
            Mechanic("dnd2024.mechanic.location.create", "register a location"),
            Mechanic("dnd2024.mechanic.location.register", "register a location"));

        var finding = Assert.Single(analysis.Findings);
        Assert.True(finding.Blocking);
        Assert.Equal("deterministic", finding.Classification);
        Assert.Contains(finding.Reasons, value => value.StartsWith("identical-match-phrase:", StringComparison.Ordinal));
    }

    [Fact]
    public void Effect_ownership_and_equivalent_child_graphs_are_deterministic_conflicts()
    {
        const string requirements = """
            {"effectComponentIds":["game.core.world.location"],"children":{"place":{"mechanicId":"game.core.mechanic.location.place","roleBindings":{"location":"location"},"inheritInput":true}}}
            """;
        var analysis = Analyze(
            Mechanic("dnd2024.mechanic.location.create", "create a location", requirements),
            Mechanic("dnd2024.mechanic.location.register", "register a place", requirements));

        var finding = Assert.Single(analysis.Findings);
        Assert.True(finding.Blocking);
        Assert.Contains(finding.Reasons, value => value.StartsWith("overlapping-effect-ownership:", StringComparison.Ordinal));
        Assert.Contains(finding.Reasons, value => value.StartsWith("equivalent-child-graph:", StringComparison.Ordinal));
    }

    [Fact]
    public void Shared_effect_ownership_alone_is_a_nonblocking_review_signal()
    {
        const string leftRequirements = """
            {"effectComponentIds":["game.core.world.location"]}
            """;
        const string rightRequirements = """
            {"effectComponentIds":["game.core.world.location"],"inputs":{"reason":{"type":"string"}}}
            """;

        var finding = Assert.Single(Analyze(
            Mechanic("dnd2024.mechanic.location.create", "create a location", leftRequirements),
            Mechanic("dnd2024.mechanic.location.describe", "describe a location", rightRequirements)).Findings);

        Assert.False(finding.Blocking);
        Assert.Contains(finding.Reasons, value => value.StartsWith("overlapping-effect-ownership:", StringComparison.Ordinal));
        Assert.DoesNotContain(finding.Reasons, value => value.StartsWith("equivalent-child-graph:", StringComparison.Ordinal));
    }

    [Fact]
    public void Shared_child_graph_alone_is_a_nonblocking_review_signal()
    {
        const string leftRequirements = """
            {"children":{"place":{"mechanicId":"game.core.mechanic.location.place","roleBindings":{"location":"location"},"inheritInput":true}}}
            """;
        const string rightRequirements = """
            {"children":{"place":{"mechanicId":"game.core.mechanic.location.place","roleBindings":{"location":"location"},"inheritInput":true}},"inputs":{"reason":{"type":"string"}}}
            """;

        var finding = Assert.Single(Analyze(
            Mechanic("dnd2024.mechanic.location.create", "create a location", leftRequirements),
            Mechanic("dnd2024.mechanic.location.arrive", "arrive at a location", rightRequirements)).Findings);

        Assert.False(finding.Blocking);
        Assert.Contains(finding.Reasons, value => value.StartsWith("equivalent-child-graph:", StringComparison.Ordinal));
        Assert.DoesNotContain(finding.Reasons, value => value.StartsWith("overlapping-effect-ownership:", StringComparison.Ordinal));
    }

    [Fact]
    public void Exact_coexistence_review_unblocks_and_expires_when_either_fingerprint_changes()
    {
        var left = Mechanic("dnd2024.mechanic.location.create", "register a location");
        var right = Mechanic("dnd2024.mechanic.location.register", "register a location");
        var review = Review(left, right, CatalogAntiSprawlDispositions.DistinctResponsibility);

        var reviewed = CatalogAntiSprawlAnalyzer.Analyze([left, right], [review]);
        var allowed = Assert.Single(reviewed.Findings);
        Assert.False(allowed.Blocking);
        Assert.Equal("reviewed", allowed.ReviewState);

        var changed = Mechanic("dnd2024.mechanic.location.register", "register a location",
            source: "return { narration: 'changed', effects: [] };");
        var expired = CatalogAntiSprawlAnalyzer.Analyze([left, changed], [review]);
        var blocked = Assert.Single(expired.Findings);
        Assert.True(blocked.Blocking);
        Assert.Equal("stale", blocked.ReviewState);
    }

    [Fact]
    public void Merge_and_replacement_decisions_require_the_active_overlay_to_finish_the_decision()
    {
        var left = Mechanic("dnd2024.mechanic.location.create", "register a location");
        var right = Mechanic("dnd2024.mechanic.location.register", "register a location");

        foreach (var disposition in new[] { CatalogAntiSprawlDispositions.Merge, CatalogAntiSprawlDispositions.Replacement })
        {
            var finding = Assert.Single(CatalogAntiSprawlAnalyzer.Analyze(
                [left, right], [Review(left, right, disposition)]).Findings);
            Assert.True(finding.Blocking);
            Assert.Contains("complete that decision", finding.Summary);
        }
    }

    [Fact]
    public void Draft_overlap_and_fuzzy_similarity_create_nonblocking_review_candidates()
    {
        var draft = Mechanic("dnd2024.mechanic.location.draft", "register a location",
            status: MechanicStatus.Draft);
        var active = Mechanic("dnd2024.mechanic.location.active", "register a location");
        Assert.False(Assert.Single(Analyze(draft, active).Findings).Blocking);

        var fuzzy = Analyze(
            Mechanic("dnd2024.mechanic.location.furnished", "create a furnished chamber",
                description: "Create and place a furnished location with connections and discoverable features."),
            Mechanic("dnd2024.mechanic.location.equipped", "build an equipped room",
                description: "Create and place a furnished location with connections and discoverable features."));
        var candidate = Assert.Single(fuzzy.Findings);
        Assert.False(candidate.Blocking);
        Assert.Equal("fuzzy", candidate.Classification);
        Assert.True(candidate.Similarity >= 0.55);
    }

    [Fact]
    public void Review_json_is_closed_and_content_addressed()
    {
        var left = Mechanic("dnd2024.mechanic.location.create", "register a location");
        var right = Mechanic("dnd2024.mechanic.location.register", "register a location");
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            left = new { qualifiedId = left.QualifiedId, contentFingerprint = left.ContentFingerprint },
            right = new { qualifiedId = right.QualifiedId, contentFingerprint = right.ContentFingerprint },
            disposition = CatalogAntiSprawlDispositions.IntentionalOverride,
            rationale = "The extension intentionally replaces the base selection route."
        });

        var parsed = CatalogAntiSprawlReview.Parse(json, "review.json");
        Assert.Equal(left.ContentFingerprint, parsed.Left.ContentFingerprint);
        Assert.Throws<InvalidOperationException>(() => CatalogAntiSprawlReview.Parse(
            json.TrimEnd('}') + ",\"unknown\":true}", "review.json"));
    }

    private static CatalogAntiSprawlAnalysis Analyze(params CatalogAntiSprawlMechanic[] mechanics) =>
        CatalogAntiSprawlAnalyzer.Analyze(mechanics, []);

    private static CatalogAntiSprawlReview Review(
        CatalogAntiSprawlMechanic left,
        CatalogAntiSprawlMechanic right,
        string disposition) => new(
        new(left.QualifiedId, left.ContentFingerprint),
        new(right.QualifiedId, right.ContentFingerprint),
        disposition,
        "Reviewed in this focused test.",
        "review.json");

    private static CatalogAntiSprawlMechanic Mechanic(
        string id,
        string matches,
        string requirements = "{}",
        string source = "return { narration: 'ok', effects: [] };",
        MechanicStatus status = MechanicStatus.Active,
        string description = "A focused authored mechanic for anti-sprawl tests.") =>
        CatalogAntiSprawlMechanic.Create(new MechanicFile(
            id, "game.core.world.location", id, description, matches, requirements, source, "", status), id);
}
