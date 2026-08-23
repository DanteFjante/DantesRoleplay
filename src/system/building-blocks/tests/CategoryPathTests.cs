using DantesRoleplay.Categories;

namespace DantesRoleplay.Tests;

/// <summary>
/// The category grammar and the branch reading of a category filter.
///
/// The test that matters most is <see cref="Branch_does_not_match_a_sibling_sharing_its_prefix"/>.
/// Prefix matching without a dot boundary is the classic version of this bug, and here it would
/// not throw — it would silently widen a rule search, which surfaces later as the wrong rule
/// answering an action.
/// </summary>
public sealed class CategoryPathTests
{
    [Theory]
    [InlineData("system")]
    [InlineData("ruleset.dnd2024")]
    [InlineData("ruleset.dnd2024.gameplay.ability-checks.fixed-dc")]
    [InlineData("a")]
    [InlineData("dnd2024")]
    public void Valid_paths_are_accepted(string path)
    {
        Assert.True(CategoryPath.TryValidate(path, out var problem), problem);
        Assert.Empty(problem);
    }

    /// <summary>
    /// A single segment is a valid path, not a grandfathered special case. The plan proposed
    /// validating a category as "either a legacy flat value or a path"; one rule that happens to
    /// cover both is less to explain and less to get wrong.
    /// </summary>
    [Fact]
    public void A_single_segment_is_an_ordinary_path()
    {
        Assert.True(CategoryPath.TryValidate("system", out _));
        Assert.True(CategoryPath.IsWithin("system", "system"));
        Assert.Empty(CategoryPath.Ancestors("system"));
    }

    [Theory]
    [InlineData("", "required")]
    [InlineData("   ", "required")]
    [InlineData("Ruleset.Dnd2024", "not valid")]
    [InlineData("ruleset..dnd2024", "empty segment")]
    [InlineData(".ruleset", "empty segment")]
    [InlineData("ruleset.", "empty segment")]
    [InlineData("ruleset dnd", "whitespace")]
    [InlineData("ruleset.-dnd", "not valid")]
    [InlineData("ruleset.dnd-", "not valid")]
    [InlineData("ruleset.dnd_2024", "not valid")]
    public void Malformed_paths_are_rejected_with_a_reason(string path, string expected)
    {
        Assert.False(CategoryPath.TryValidate(path, out var problem));
        Assert.Contains(expected, problem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The column is 100 wide, so the check has to be here rather than at the database.</summary>
    [Fact]
    public void A_path_longer_than_the_column_is_rejected_before_it_reaches_the_database()
    {
        var tooLong = string.Join('.', Enumerable.Repeat("segment", 20));

        Assert.True(tooLong.Length > CategoryPath.MaxLength);
        Assert.False(CategoryPath.TryValidate(tooLong, out var problem));
        Assert.Contains(CategoryPath.MaxLength.ToString(), problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_branch_matches_itself_and_its_descendants()
    {
        Assert.True(CategoryPath.IsWithin("ruleset.dnd2024", "ruleset.dnd2024"));
        Assert.True(CategoryPath.IsWithin("ruleset.dnd2024.play", "ruleset.dnd2024"));
        Assert.True(CategoryPath.IsWithin("ruleset.dnd2024.gameplay.checks", "ruleset"));
        Assert.False(CategoryPath.IsWithin("ruleset", "ruleset.dnd2024"));
    }

    /// <summary>The whole reason the separator is checked rather than a bare prefix.</summary>
    [Fact]
    public void Branch_does_not_match_a_sibling_sharing_its_prefix()
    {
        Assert.False(CategoryPath.IsWithin("ruleset.dnd2024.player", "ruleset.dnd2024.play"));
        Assert.False(CategoryPath.IsWithin("systems", "system"));
    }

    /// <summary>An omitted filter is not a filter, which is what makes exploring free.</summary>
    [Fact]
    public void An_empty_branch_matches_everything()
    {
        Assert.True(CategoryPath.IsWithin("anything.at.all", null));
        Assert.True(CategoryPath.IsWithin("anything.at.all", ""));
    }

    [Fact]
    public void Ancestors_are_returned_nearest_first()
    {
        Assert.Equal(
            ["ruleset.dnd2024.gameplay", "ruleset.dnd2024", "ruleset"],
            CategoryPath.Ancestors("ruleset.dnd2024.gameplay.checks"));
    }

    [Fact]
    public void The_nearest_known_node_is_the_deepest_branch_something_already_lives_under()
    {
        string[] known = ["ruleset.dnd2024.play", "ruleset.dnd2024.governance", "system"];

        Assert.Equal(
            "ruleset.dnd2024",
            CategoryPath.NearestKnownNode("ruleset.dnd2024.combat.attack", known));

        Assert.Null(CategoryPath.NearestKnownNode("homebrew.house-rules", known));
    }

    [Fact]
    public void Browsing_the_roots_rolls_every_descendant_into_its_root()
    {
        var branch = CategoryPath.Browse(null, Counts(
            ("system", 5),
            ("ruleset.dnd2024.play", 2),
            ("ruleset.dnd2024.combat.attack", 3)));

        Assert.Equal(string.Empty, branch.Path);
        Assert.Equal(0, branch.Direct);
        Assert.Equal(10, branch.Subtree);

        Assert.Equal(["ruleset", "system"], branch.Children.Select(c => c.Path));
        Assert.Equal(0, branch.Children[0].Direct);
        Assert.Equal(5, branch.Children[0].Subtree);
        Assert.Equal(5, branch.Children[1].Direct);
    }

    /// <summary>
    /// Direct 0 with a non-zero Subtree is the case a single count cannot express, and it is the
    /// common one: a branch node holds no records of its own because placeholder leaves are not
    /// created. A session shown only "0" would skip a subtree with fourteen rules in it.
    /// </summary>
    [Fact]
    public void A_branch_with_no_records_of_its_own_still_reports_its_subtree()
    {
        var branch = CategoryPath.Browse("ruleset.dnd2024", Counts(
            ("ruleset.dnd2024.play", 2),
            ("ruleset.dnd2024.combat.attack", 3),
            ("ruleset.dnd2024.combat.damage", 4),
            ("system", 5)));

        Assert.Equal(0, branch.Direct);
        Assert.Equal(9, branch.Subtree);

        var combat = branch.Children.Single(c => c.Segment == "combat");

        Assert.Equal("ruleset.dnd2024.combat", combat.Path);
        Assert.Equal(0, combat.Direct);
        Assert.Equal(7, combat.Subtree);
    }

    [Fact]
    public void Browsing_a_leaf_returns_its_own_count_and_no_children()
    {
        var branch = CategoryPath.Browse("system", Counts(("system", 5), ("ruleset.dnd2024", 1)));

        Assert.Equal(5, branch.Direct);
        Assert.Equal(5, branch.Subtree);
        Assert.Empty(branch.Children);
    }

    [Fact]
    public void Child_nodes_are_the_next_segment_down_only()
    {
        string[] known =
        [
            "ruleset.dnd2024.play",
            "ruleset.dnd2024.combat.attack",
            "ruleset.dnd2024.combat.damage"
        ];

        Assert.Equal(["ruleset"], CategoryPath.ChildNodes(null, known));

        Assert.Equal(
            ["ruleset.dnd2024.combat", "ruleset.dnd2024.play"],
            CategoryPath.ChildNodes("ruleset.dnd2024", known));
    }

    private static IEnumerable<CategoryCount> Counts(params (string Path, int Count)[] pairs) =>
        pairs.Select(p => new CategoryCount(p.Path, p.Count));
}
