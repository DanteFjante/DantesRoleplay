using System.Text.RegularExpressions;

namespace DantesRoleplay.Tests;

/// <summary>
/// Tests that guard architectural invariants rather than behaviour.
///
/// Both describe failures that are invisible until they are expensive: game logic leaking into
/// the kernel (§3.11), and the operating manual drifting away from what the system can actually
/// do (§7.9). The second one has now failed for real — a cold-model test found orient claiming
/// capabilities that did not exist — so the drift check runs in BOTH directions.
///
/// All of these read source files, so they need no reference to the host project.
/// </summary>
public sealed class GuardTests
{
    /// <summary>
    /// Words that describe a GAME. If one appears in kernel source, a game concept has leaked
    /// into C# and belongs in JavaScript or in a component definition instead.
    ///
    /// Kept deliberately unambiguous. Words like "level", "stat" and "skill" are omitted not
    /// because they would be acceptable but because they collide with ordinary programming
    /// vocabulary, and a guard that cries wolf gets deleted. Add to this list whenever a real
    /// leak gets through.
    /// </summary>
    private static readonly string[] ForbiddenInKernel =
    [
        "attack", "damage", "initiative", "spell", "hitpoints", "mana", "weapon",
        "armour", "armor", "monster", "dice", "combat", "quest", "dungeon", "loot"
    ];

    private static readonly string[] KernelProjects =
    [
        "DantesRoleplay",
        "DantesRoleplay.DataAccess",

        // The sandbox counts too, and it is the most tempting place to break the rule: the moment
        // someone adds a rolling helper "because every game needs one", the kernel has a dice
        // convention baked in and every game built on it inherits that convention forever. The
        // engine offers a seeded random source and nothing above it.
        "DantesRoleplay.RuleAccess"
    ];

    [Fact]
    public void The_kernel_contains_no_game_vocabulary()
    {
        var root = RepositoryRoot();
        var offences = new List<string>();

        foreach (var project in KernelProjects)
        {
            var directory = Path.Combine(root, project);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in EnumerateSource(directory))
            {
                var code = StripCommentsAndStrings(File.ReadAllText(file));

                foreach (var word in ForbiddenInKernel)
                {
                    if (Regex.IsMatch(code, $@"\b{word}\b", RegexOptions.IgnoreCase))
                    {
                        offences.Add($"{Path.GetRelativePath(root, file)}: '{word}'");
                    }
                }
            }
        }

        Assert.True(
            offences.Count == 0,
            "Game vocabulary found in the kernel. It belongs in JavaScript or in a component "
            + "definition, not in C# (ARCHITECTURE.md §3.11):\n  "
            + string.Join("\n  ", offences));
    }

    [Fact]
    public void Every_mcp_tool_is_announced_by_orient()
    {
        var (declared, capabilities) = ToolSurface();

        var missing = declared
            .Where(name => !capabilities.Contains($"\"{name} —", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These tools exist but orient() does not mention them, so a cold model would never "
            + "learn they are available (ARCHITECTURE.md §7.9):\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The direction that actually failed in practice. orient once advertised writing component
    /// definitions and world data when no such tool existed; a cold model believed it and planned
    /// around a capability that was not there. Over-promising is as damaging as going stale.
    /// </summary>
    [Fact]
    public void Orient_announces_nothing_that_is_not_a_tool()
    {
        var (declared, capabilities) = ToolSurface();

        var advertised = Regex.Matches(capabilities, @"""(?<name>[a-z_]+) —")
            .Select(m => m.Groups["name"].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(advertised);

        var phantom = advertised.Except(declared, StringComparer.Ordinal).ToList();

        Assert.True(
            phantom.Count == 0,
            "orient() advertises capabilities with no corresponding MCP tool. A cold model will "
            + "plan around these and fail:\n  "
            + string.Join("\n  ", phantom));
    }

    [Fact]
    public void The_tool_budget_is_not_exceeded()
    {
        var (declared, _) = ToolSurface();

        // §7.1: twelve, permanently. New capability becomes a procedure or an operation behind an
        // existing tool — never a thirteenth tool. If this fails, that is the conversation to have.
        Assert.True(
            declared.Count <= 12,
            $"{declared.Count} MCP tools declared; the budget is 12 (ARCHITECTURE.md §7.1).");
    }

    /// <summary>Reads the declared tool names and orient's capability block from source.</summary>
    private static (List<string> Declared, string Capabilities) ToolSurface()
    {
        var root = RepositoryRoot();
        var toolsDirectory = Path.Combine(root, "DantesRoleplay.MCPServer", "Tools");

        Assert.True(Directory.Exists(toolsDirectory), $"Expected tools at {toolsDirectory}.");

        var declared = new List<string>();

        foreach (var file in EnumerateSource(toolsDirectory))
        {
            foreach (Match match in Regex.Matches(
                File.ReadAllText(file),
                @"McpServerTool\s*\(\s*Name\s*=\s*""(?<name>[^""]+)"""))
            {
                declared.Add(match.Groups["name"].Value);
            }
        }

        Assert.NotEmpty(declared);

        var orient = File.ReadAllText(Path.Combine(toolsDirectory, "OrientTool.cs"));
        var start = orient.IndexOf("Capabilities() =>", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not find the Capabilities() block in OrientTool.cs.");

        var end = orient.IndexOf("];", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not find the end of the Capabilities() block.");

        return (declared, orient[start..end]);
    }

    private static IEnumerable<string> EnumerateSource(string directory) =>
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            // Migrations are generated, and their content mirrors the schema rather than adding to it.
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// Walks up from the test binary until it finds the solution file. Keeps the guards working
    /// regardless of the build output layout.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.slnx").Any() || directory.EnumerateFiles("*.sln").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find the solution file above {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// Removes comments and string contents so the guard reads CODE, not prose. Without this the
    /// vocabulary test would fire on the very comments that explain why the rule exists.
    /// </summary>
    private static string StripCommentsAndStrings(string source)
    {
        var withoutBlockComments = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        var withoutLineComments = Regex.Replace(withoutBlockComments, @"//[^\n]*", " ");
        var withoutRawStrings = Regex.Replace(withoutLineComments, "\"\"\".*?\"\"\"", "\"\"", RegexOptions.Singleline);
        return Regex.Replace(withoutRawStrings, @"""(\\.|[^""\\])*""", "\"\"");
    }
}
