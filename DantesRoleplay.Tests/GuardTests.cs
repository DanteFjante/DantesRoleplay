using System.Text.Json;
using System.Text.RegularExpressions;
using DantesRoleplay.Effects;
using DantesRoleplay.MCPServer.Tools;

namespace DantesRoleplay.Tests;

/// <summary>
/// Tests that guard architectural invariants rather than behaviour.
///
/// Both describe failures that are invisible until they are expensive: game logic leaking into
/// the kernel (§3.11), and the operating manual drifting away from what the system can actually
/// do (§7.9). The second one has now failed for real — a cold-model test found orient claiming
/// capabilities that did not exist — so the drift check runs in BOTH directions, and since the
/// three-verb migration it runs at the level that can actually drift now: the kinds, not the
/// three tool names.
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
        "armour", "armor", "monster", "dice", "combat", "dungeon", "loot"
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
    public void Exactly_three_public_verbs_are_exposed()
    {
        var declared = DeclaredToolNames();

        // The old handlers remain in source as the implementation, but only the three registered
        // dispatchers are public MCP tools.
        Assert.Equal(
            new[] { "commit", "orient", "query" },
            declared.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The direction that actually failed in practice, at the level it can fail now. orient once
    /// advertised writing component definitions and world data when no such tool existed; a cold
    /// model believed it and planned around a capability that was not there.
    /// </summary>
    [Fact]
    public void Orient_announces_exactly_the_registered_verbs()
    {
        using var announcement = JsonSerializer.SerializeToDocument(VerbSurface.Announcement());

        var announced = announcement.RootElement
            .EnumerateObject()
            .Select(p => p.Name.ToLowerInvariant())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(DeclaredToolNames().Order(StringComparer.Ordinal).ToArray(), announced);
    }

    /// <summary>
    /// D9, the read side. Every kind the catalog offers is a case the dispatcher actually handles,
    /// and every case it handles is offered — so a session can neither be told about a kind that
    /// fails nor be denied one that works.
    ///
    /// `capabilities` is answered by the protocol before dispatch (it describes the surface rather
    /// than reading the database), so it is excluded here and covered by its own behaviour test.
    /// </summary>
    [Fact]
    public void Query_dispatch_handles_exactly_the_advertised_kinds()
    {
        var advertised = VerbSurface.QueryKindNames
            .Where(k => k != "capabilities")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(advertised, DispatchedKinds("QueryTool.cs"));
    }

    /// <summary>D9, the write side.</summary>
    [Fact]
    public void Commit_dispatch_handles_exactly_the_advertised_kinds()
    {
        var advertised = VerbSurface.CommitKindNames.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(advertised, DispatchedKinds("CommitTool.cs"));
    }

    /// <summary>
    /// The names the twelve-tool surface used, kept here so nothing can quietly reintroduce one.
    /// `VERB_HISTORY.md` maps them to the calls that replaced them; old audit rows still carry
    /// them, and that is correct history, but no running code may suggest making one.
    /// </summary>
    internal static readonly string[] RetiredVerbs =
    [
        "find_procedures", "get_procedure", "write_procedure", "describe_world", "get_entities",
        "define_component", "apply_effects", "find_mechanics", "write_mechanic", "run_action",
        "history"
    ];

    /// <summary>
    /// The same list minus <c>history</c>, for scanning prose. "history" is an ordinary English
    /// word that seeded contracts use correctly ("read the history"), so matching it as a bare
    /// word there would be a guard that cries wolf. In source it is only ever the retired call.
    /// </summary>
    internal static IEnumerable<string> RetiredVerbsInProse =>
        RetiredVerbs.Where(v => v != "history");

    /// <summary>
    /// The failure this test exists for actually shipped. A regex adapter rewrote the handlers'
    /// literal recovery calls by prefix, so `write_procedure(id: "x", ...)` became
    /// `commit(kind: "procedure", id: "x", ...)` — commit takes `payload`, not `id`, so every
    /// write-side `fix` named a call the protocol would reject. §7.4 says a failure is an
    /// instruction; an instruction that cannot be followed is worse than silence, because the
    /// session spends its recovery attempt on it.
    ///
    /// Scans source text rather than only string literals: a comment naming a retired call is
    /// stale documentation, and this file is one of the few places that can notice.
    /// </summary>
    [Fact]
    public void No_recovery_call_names_a_verb_that_no_longer_exists()
    {
        var root = RepositoryRoot();

        var files = EnumerateSource(Path.Combine(root, "DantesRoleplay.MCPServer"))
            // The action runner owns its own audit rows and its own error text, so it emits
            // recovery calls without passing through the dispatchers.
            .Append(Path.Combine(root, "DantesRoleplay.DataAccess", "ActionRunner.cs"))
            .Where(File.Exists);

        var offences = new List<string>();

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);

            foreach (var verb in RetiredVerbs)
            {
                foreach (Match match in Regex.Matches(source, $@"\b{verb}\s*\("))
                {
                    var line = source.Take(match.Index).Count(c => c == '\n') + 1;
                    offences.Add($"{Path.GetRelativePath(root, file)}:{line}: '{verb}('");
                }
            }
        }

        Assert.True(
            offences.Count == 0,
            "These name a call that no longer exists. A session following one gets a protocol "
            + "error instead of a recovery (VERB_MIGRATION.md D7):\n  "
            + string.Join("\n  ", offences));
    }

    /// <summary>
    /// The kind lists a client reads FIRST are the tool descriptions, and an attribute cannot be
    /// built from <see cref="VerbSurface"/> — it has to be a compile-time constant. So they are
    /// hand-maintained copies, and this is what stops one drifting: every kind the surface serves
    /// has to be named in the description a session chooses the tool by.
    /// </summary>
    [Fact]
    public void Both_dispatchers_name_every_kind_in_the_description_a_client_reads()
    {
        AssertDescribed("QueryTool.cs", VerbSurface.QueryKindNames);
        AssertDescribed("CommitTool.cs", VerbSurface.CommitKindNames);

        static void AssertDescribed(string fileName, IReadOnlyList<string> kinds)
        {
            var source = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "DantesRoleplay.MCPServer", "Tools", fileName));

            // Only the [Description] attributes — the prose that ships in tools/list.
            var described = string.Join(
                " ",
                Regex.Matches(source, @"\[Description\((?<text>.*?)\)\]", RegexOptions.Singleline)
                    .Select(m => m.Groups["text"].Value));

            var missing = kinds
                .Where(kind => !described.Contains($"{kind}", StringComparison.Ordinal))
                .ToList();

            Assert.True(
                missing.Count == 0,
                $"{fileName} serves these kinds but its tool description never names them, so a "
                + "session reading tools/list does not know they exist:\n  "
                + string.Join("\n  ", missing));
        }
    }

    /// <summary>
    /// The effect vocabulary is the one part of the surface that lives in the kernel. It reached
    /// clients through the old `apply_effects` description and briefly reached nobody at all after
    /// that class stopped being registered — five of the nine verbs were then discoverable only by
    /// sending a wrong one and reading the rejection.
    /// </summary>
    [Fact]
    public void Every_effect_type_is_documented_in_the_catalog()
    {
        Assert.Equal(
            EffectType.All.Order(StringComparer.Ordinal).ToArray(),
            VerbSurface.EffectVocabulary.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Every commit example has to be a payload that would actually parse. An example that only
    /// looks like JSON is worse than none: it is quoted verbatim into the next call.
    /// </summary>
    [Fact]
    public void Every_commit_example_payload_is_a_valid_json_object()
    {
        foreach (var kind in VerbSurface.CommitKinds)
        {
            var document = JsonDocument.Parse(kind.Example);

            Assert.True(
                document.RootElement.ValueKind == JsonValueKind.Object,
                $"The example payload for commit kind '{kind.Name}' is not a JSON object.");
        }
    }

    /// <summary>
    /// Reads the kind literals from a dispatcher's switch. Source rather than reflection on
    /// purpose: the question is what that switch handles, and only the switch can answer it.
    /// </summary>
    private static string[] DispatchedKinds(string fileName)
    {
        var file = Path.Combine(
            RepositoryRoot(), "DantesRoleplay.MCPServer", "Tools", fileName);

        Assert.True(File.Exists(file), $"Expected a dispatcher at {file}.");

        var source = File.ReadAllText(file);
        var start = source.IndexOf("return normalizedKind switch", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find the dispatch switch in {fileName}.");

        var end = source.IndexOf("_ => throw", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find the end of the dispatch switch in {fileName}.");

        return [.. Regex.Matches(source[start..end], @"""(?<kind>[a-z-]+)""\s*(?:when[^=]*?)?=>")
            .Select(m => m.Groups["kind"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The tool names actually registered with the MCP server, read from source.</summary>
    private static List<string> DeclaredToolNames()
    {
        var root = RepositoryRoot();
        var toolsDirectory = Path.Combine(root, "DantesRoleplay.MCPServer", "Tools");

        Assert.True(Directory.Exists(toolsDirectory), $"Expected tools at {toolsDirectory}.");

        var declared = new List<string>();

        // Scans the whole host project rather than one file: registration has already moved once,
        // from Program.cs into ServerConfiguration, and a guard that stops looking when code moves
        // is a guard that reports success because it found nothing.
        var registeredTypes = EnumerateSource(Path.Combine(root, "DantesRoleplay.MCPServer"))
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"WithTools<(?<type>[A-Za-z0-9_]+)>"))
            .Select(m => m.Groups["type"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(registeredTypes);

        foreach (var type in registeredTypes)
        {
            var file = Path.Combine(toolsDirectory, $"{type}.cs");
            Assert.True(File.Exists(file), $"Registered MCP tool type has no source file: {type}.");

            foreach (Match match in Regex.Matches(
                File.ReadAllText(file),
                @"McpServerTool\s*\(\s*Name\s*=\s*""(?<name>[^""]+)"""))
            {
                declared.Add(match.Groups["name"].Value);
            }
        }

        Assert.NotEmpty(declared);

        return declared;
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
