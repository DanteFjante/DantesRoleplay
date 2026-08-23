namespace DantesRoleplay.Categories;

/// <summary>
/// Category paths, and the one place that knows what a category means.
///
/// A category is a dot-delimited path — <c>catalog.example.gameplay.fixed-checks</c> —
/// and every filter that takes one means **this node and everything under it**. There is no
/// separate `recursive` flag and no multi-category array: three near-synonymous filters on one
/// query is exactly the kind of decision a low-context session gets wrong, and the branch reading
/// is the one it wants nearly every time.
///
/// A single segment is a valid path, so <c>system</c> is not a special "flat" case needing its own
/// rule — it is a path one level deep. There is one grammar, not two.
///
/// Matching stops at a dot boundary: <c>catalog.example.play</c> does not match
/// <c>catalog.example.player</c>. Getting that wrong is the classic prefix-matching bug, and here
/// it would silently widen a rule search.
///
/// Nothing in here touches the database. The stores hand it flat (path, count) pairs and it builds
/// whatever view is asked for, which is why there is no category table and no migration.
/// </summary>
public static class CategoryPath
{
    /// <summary>
    /// Matches the <c>Category</c> column width. Enforced here so an over-long path comes back as
    /// a named check with a fix, rather than as a database error the caller cannot act on.
    /// </summary>
    public const int MaxLength = 100;

    private const char Separator = '.';

    /// <summary>
    /// Validates the path grammar: dot-delimited segments of lowercase letters, digits and
    /// hyphens, each starting and ending with a letter or digit.
    ///
    /// <paramref name="problem"/> is written for a caller that has to fix it in one round trip, so
    /// it names the offending part and shows the shape rather than restating the rule.
    /// </summary>
    public static bool TryValidate(string? path, out string problem)
    {
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            problem = "A category is required. Use a dot-delimited path, e.g. "
                + "\"catalog.example.gameplay.fixed-checks\" — or a single segment such as "
                + "\"system\", which is a path one level deep.";
            return false;
        }

        if (!string.Equals(path, path.Trim(), StringComparison.Ordinal) || path.Any(char.IsWhiteSpace))
        {
            problem = $"'{path}' contains whitespace. A category path has no spaces — "
                + "use hyphens inside a segment, e.g. \"catalog.example.fixed-checks\".";
            return false;
        }

        if (path.Length > MaxLength)
        {
            problem = $"'{path}' is {path.Length} characters; the limit is {MaxLength}. "
                + "Shorten a segment or use a shallower path.";
            return false;
        }

        var segments = path.Split(Separator);

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                problem = $"'{path}' has an empty segment — a leading, trailing or doubled dot. "
                    + "Every segment between dots must be a word, e.g. \"catalog.example.actions\".";
                return false;
            }

            if (!IsSegment(segment))
            {
                problem = $"Segment '{segment}' in '{path}' is not valid. A segment is lowercase "
                    + "letters, digits and hyphens, starting and ending with a letter or digit, "
                    + "e.g. \"ability-checks\".";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="category"/> is <paramref name="branch"/> itself or sits under it.
    ///
    /// The dot is what makes this safe: without it, a branch would swallow every sibling that
    /// merely starts with the same letters.
    /// </summary>
    public static bool IsWithin(string? category, string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            return true;
        }

        if (string.IsNullOrEmpty(category))
        {
            return false;
        }

        var node = branch.Trim();

        return string.Equals(category, node, StringComparison.Ordinal)
            || category.StartsWith(node + Separator, StringComparison.Ordinal);
    }

    /// <summary>Ancestor paths, nearest first, excluding the path itself.</summary>
    public static IEnumerable<string> Ancestors(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        var segments = path.Trim().Split(Separator);

        for (var take = segments.Length - 1; take > 0; take--)
        {
            yield return string.Join(Separator, segments.Take(take));
        }
    }

    /// <summary>
    /// The closest ancestor of <paramref name="path"/> that something already lives under, or null
    /// when the path introduces a whole new root.
    ///
    /// This is what turns "that is a new category, here is a list of all 90 of them" into "that is
    /// new; its nearest existing branch is X, whose children are Y" — the anti-sprawl nudge stays
    /// useful as a taxonomy grows, which is exactly when a flat dump stops being readable.
    /// </summary>
    public static string? NearestKnownNode(string path, IEnumerable<string> known)
    {
        var existing = known.ToList();

        return Ancestors(path).FirstOrDefault(ancestor => existing.Any(k => IsWithin(k, ancestor)));
    }

    /// <summary>The direct child paths of a branch. Pass null or empty for the roots.</summary>
    public static IReadOnlyList<string> ChildNodes(string? branch, IEnumerable<string> known) =>
        [.. Browse(branch, known.Select(k => new CategoryCount(k, 1))).Children.Select(c => c.Path)];

    /// <summary>
    /// One level of the tree, derived from flat (path, count) pairs.
    ///
    /// Both counts are returned per child because one of them cannot answer the question a
    /// browsing session is actually asking. <see cref="CategoryNode.Direct"/> says whether opening
    /// this node returns anything; <see cref="CategoryNode.Subtree"/> says whether it is worth
    /// descending. A node with Direct 0 and Subtree 14 is a branch to walk into, not an empty one
    /// to skip.
    /// </summary>
    public static CategoryBranch Browse(string? branch, IEnumerable<CategoryCount> counts)
    {
        var all = counts.ToList();
        var node = string.IsNullOrWhiteSpace(branch) ? string.Empty : branch.Trim();
        var depth = node.Length == 0 ? 0 : node.Split(Separator).Length;

        var inBranch = node.Length == 0
            ? all
            : [.. all.Where(c => IsWithin(c.Path, node))];

        var children = inBranch
            .Where(c => !string.Equals(c.Path, node, StringComparison.Ordinal))
            .Select(c => string.Join(Separator, c.Path.Split(Separator).Take(depth + 1)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(childPath => new CategoryNode(
                childPath,
                childPath.Split(Separator)[^1],
                all.Where(c => string.Equals(c.Path, childPath, StringComparison.Ordinal)).Sum(c => c.Count),
                all.Where(c => IsWithin(c.Path, childPath)).Sum(c => c.Count)))
            .ToList();

        return new CategoryBranch(
            node,
            inBranch.Where(c => string.Equals(c.Path, node, StringComparison.Ordinal)).Sum(c => c.Count),
            inBranch.Sum(c => c.Count),
            children);
    }

    private static bool IsSegment(string segment) =>
        IsWordCharacter(segment[0])
        && IsWordCharacter(segment[^1])
        && segment.All(c => IsWordCharacter(c) || c == '-');

    private static bool IsWordCharacter(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9';
}

/// <summary>How many records carry exactly this category path. The stores' input to the tree.</summary>
public sealed record CategoryCount(string Path, int Count);

/// <param name="Path">The full path of this child, not just its last segment.</param>
/// <param name="Segment">The last segment, for display.</param>
/// <param name="Direct">Records whose category is exactly this path.</param>
/// <param name="Subtree">Records in this path or anywhere below it.</param>
public sealed record CategoryNode(string Path, string Segment, int Direct, int Subtree);

/// <param name="Path">The branch being viewed. Empty string means the roots.</param>
/// <param name="Direct">Records whose category is exactly this branch.</param>
/// <param name="Subtree">Records in this branch or anywhere below it.</param>
/// <param name="Children">Its direct children, in path order.</param>
public sealed record CategoryBranch(
    string Path,
    int Direct,
    int Subtree,
    IReadOnlyList<CategoryNode> Children);
