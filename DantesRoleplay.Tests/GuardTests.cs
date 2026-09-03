using System.Text.Json;
using System.Text.RegularExpressions;
using DantesRoleplay.MCPServer.Mcp;

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
        // DataAccess now also owns the sandbox, and it is the most tempting place to break the
        // rule: the moment someone adds a game-specific helper, every game inherits it.
        "DantesRoleplay.DataAccess"
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
    public void Local_ai_has_no_game_system_dependency_or_vocabulary()
    {
        var root = RepositoryRoot();
        var projectDirectory = Path.Combine(root, "DantesRoleplay.LocalAI");
        var project = Path.Combine(projectDirectory, "DantesRoleplay.LocalAI.csproj");
        Assert.True(File.Exists(project), $"Expected the local-AI project at {project}.");

        var projectText = File.ReadAllText(project);
        Assert.DoesNotContain("ProjectReference", projectText, StringComparison.Ordinal);

        var forbidden = new[]
        {
            "campaign", "world", "character", "quest", "story", "dnd2024", "mechanic",
            "procedure", "knowledge"
        };
        var offences = new List<string>();
        foreach (var file in EnumerateSource(projectDirectory))
        {
            var source = File.ReadAllText(file);
            foreach (var word in forbidden)
                if (Regex.IsMatch(source, $@"\b{word}\b", RegexOptions.IgnoreCase))
                    offences.Add($"{NormalizedRelativePath(root, file)}: '{word}'");
        }

        Assert.True(
            offences.Count == 0,
            "Local AI contains game-system vocabulary or consumer identifiers:\n  "
            + string.Join("\n  ", offences));
    }

    [Fact]
    public void Generic_projects_do_not_compile_application_or_game_adapter_sources()
    {
        var root = RepositoryRoot();
        var projects = new[]
        {
            Path.Combine(root, "DantesRoleplay", "DantesRoleplay.csproj"),
            Path.Combine(root, "DantesRoleplay.DataAccess", "DantesRoleplay.DataAccess.csproj"),
            Path.Combine(root, "DantesRoleplay.Tests", "DantesRoleplay.Tests.csproj")
        };
        var forbidden = new[] { "src\\game-adapters", "src/game-adapters", "src\\applications", "src/applications" };
        var offences = projects
            .SelectMany(project => forbidden
                .Where(value => File.ReadAllText(project).Contains(value, StringComparison.OrdinalIgnoreCase))
                .Select(value => $"{NormalizedRelativePath(root, project)}: '{value}'"))
            .ToArray();

        Assert.True(offences.Length == 0,
            "Generic projects must not compile application or game-adapter source trees:\n  "
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

    [Fact]
    public void Only_public_mcp_adapters_declare_mcp_tool_attributes()
    {
        var project = Path.Combine(RepositoryRoot(), "DantesRoleplay.MCPServer");
        var attributed = EnumerateSource(project)
            .Where(file => File.ReadAllText(file).Contains("[McpServerTool", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(project, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Mcp/CommitMcpTool.cs", "Mcp/OrientMcpTool.cs", "Mcp/QueryMcpTool.cs"],
            attributed);
    }

    /// <summary>
    /// Orientation consumes these descriptors directly. Every active descriptor must therefore
    /// identify one normal dispatcher route. Superseded compatibility routes are physically
    /// absent rather than hidden behind a second dispatcher inventory.
    /// </summary>
    [Fact]
    public void Capability_descriptors_name_only_registered_callable_routes()
    {
        var activeQueries = McpVerbCatalog.Descriptors
            .Where(value => value.Id.StartsWith("mcp.query.", StringComparison.Ordinal))
            .Select(value => value.Id["mcp.query.".Length..]).Order(StringComparer.Ordinal);
        var activeCommits = McpVerbCatalog.Descriptors
            .Where(value => value.Id.StartsWith("mcp.commit.", StringComparison.Ordinal))
            .Select(value => value.Id["mcp.commit.".Length..]).Order(StringComparer.Ordinal);
        Assert.Equal(McpVerbCatalog.QueryKindNames.Order(StringComparer.Ordinal), activeQueries);
        Assert.Equal(McpVerbCatalog.CommitKindNames.Order(StringComparer.Ordinal), activeCommits);
    }

    [Fact]
    public void Superseded_generic_write_routes_and_runner_sources_are_physically_absent()
    {
        foreach (var kind in new[] { "component", "effects", "mechanic", "action" })
        {
            Assert.DoesNotContain(kind, McpVerbCatalog.CommitKindNames);
            Assert.DoesNotContain(McpVerbCatalog.Descriptors,
                value => value.Id == $"mcp.commit.{kind}");
        }

        var root = RepositoryRoot();
        foreach (var path in new[]
        {
            Path.Combine("DantesRoleplay.MCPServer", "Handlers", "ActionHandler.cs"),
            Path.Combine("DantesRoleplay.MCPServer", "Handlers", "MechanicActionInformationExecutor.cs"),
            Path.Combine("src", "system", "actions", "domain", "IActionRunner.cs"),
            Path.Combine("src", "system", "actions", "persistence", "ActionRunner.cs"),
            Path.Combine("src", "system", "actions", "hosting", "ActionsComponentRegistration.cs")
        })
            Assert.False(File.Exists(Path.Combine(root, path)), $"Superseded source still exists: {path}");
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
        var advertised = McpVerbCatalog.QueryKindNames
            .Where(k => k != "capabilities")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(advertised, DispatchedKinds("QueryMcpTool.cs"));
    }

    /// <summary>D9, the write side. Only current registered routes are callable.</summary>
    [Fact]
    public void Commit_dispatch_handles_exactly_the_advertised_kinds()
    {
        var advertised = McpVerbCatalog.CommitKindNames.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(advertised, DispatchedKinds("CommitMcpTool.cs"));
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
    /// built from <see cref="McpVerbCatalog"/> — it has to be a compile-time constant. So they are
    /// hand-maintained copies, and this is what stops one drifting: every kind the surface serves
    /// has to be named in the description a session chooses the tool by.
    /// </summary>
    [Fact]
    public void Both_dispatchers_name_every_kind_in_the_description_a_client_reads()
    {
        AssertDescribed("QueryMcpTool.cs", McpVerbCatalog.QueryKindNames);
        AssertDescribed("CommitMcpTool.cs", McpVerbCatalog.CommitKindNames);

        static void AssertDescribed(string fileName, IReadOnlyList<string> kinds)
        {
            var source = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "DantesRoleplay.MCPServer", "Mcp", fileName));

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
    /// The other direction, and the one that actually bit: a description may not advertise a kind
    /// the dispatcher does not serve. Seven — campaign-resume, story-plan, session-recap,
    /// quest-summary, journey-plan, itinerary-plan and knowledge-answer — were named in the query
    /// tool's own description while being absent from both the catalog and the switch. The guard
    /// above only checked catalog ⊆ description, so the drift passed CI, and two separate sessions
    /// spent hours concluding that a capability was "blocked" when it had simply never existed.
    ///
    /// Only the enumerated list is checked, not the surrounding prose: that list is what a client
    /// reads as the closed set, and it is the part that must be true.
    /// </summary>
    [Fact]
    public void Neither_dispatcher_advertises_a_kind_it_does_not_serve()
    {
        AssertOnlyRealKinds("QueryMcpTool.cs", McpVerbCatalog.QueryKindNames,
            ["kind is one of:", "Closed kind:"]);
        AssertOnlyRealKinds("CommitMcpTool.cs", McpVerbCatalog.CommitKindNames,
            ["Change state with", "Closed kind:"]);

        static void AssertOnlyRealKinds(
            string fileName,
            IReadOnlyList<string> kinds,
            IReadOnlyList<string> listMarkers)
        {
            var source = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "DantesRoleplay.MCPServer", "Mcp", fileName));
            var described = string.Join(
                " ",
                Regex.Matches(source, @"\[Description\((?<text>.*?)\)\]", RegexOptions.Singleline)
                    .Select(m => m.Groups["text"].Value));

            // Strings are concatenated with + across lines in the attribute; join the fragments.
            described = Regex.Replace(described, "\"\\s*\\+\\s*\"", " ");

            // Then drop the quotes themselves. A list ends at "… or history.\"", where the period
            // is followed by a quote rather than whitespace, and the scan would otherwise run on
            // into the next attribute and treat its prose as advertised kinds.
            described = described.Replace("\"", " ", StringComparison.Ordinal);
            var known = kinds.ToHashSet(StringComparer.Ordinal);
            var advertised = new List<string>();

            foreach (var marker in listMarkers)
            {
                var start = described.IndexOf(marker, StringComparison.Ordinal);
                if (start < 0) continue;
                start += marker.Length;

                // A kind never contains a space, so a period followed by whitespace ends the list
                // without truncating a dotted kind such as system.catalog.browse.
                var end = Regex.Match(described[start..], @"\.\s").Index;
                var list = end > 0 ? described[start..(start + end)] : described[start..];

                advertised.AddRange(Regex.Split(list, @",|\bor\b")
                    .Select(value => value.Trim().Trim('"', '\\', ' '))
                    .Where(value => value.Length > 0));
            }

            Assert.True(advertised.Count > 0, $"{fileName}: no enumerated kind list was found to check.");

            var phantom = advertised.Where(value => !known.Contains(value)).Distinct(StringComparer.Ordinal).ToList();
            Assert.True(
                phantom.Count == 0,
                $"{fileName} advertises these in its tool description but does not serve them, so a "
                + "session that trusts the description gets UNKNOWN_KIND and reasonably concludes the "
                + "capability is missing:\n  "
                + string.Join("\n  ", phantom));
        }
    }

    /// <summary>
    /// A contract that names a call the protocol does not serve is worse than a contract that
    /// names none: a session reads it, makes the call, gets UNKNOWN_KIND, and concludes the
    /// capability is missing. `procedure.contract.create` already forbids this in prose — "Never
    /// name a call in `governs` or in the body that `query(kind: \"capabilities\")` does not
    /// list" — and the catalog broke it in twenty-one places, so the rule gets a test.
    ///
    /// A literal `"..."` is a placeholder, not a claim, and is skipped.
    /// </summary>
    [Fact]
    public void No_catalog_contract_names_a_verb_kind_the_protocol_does_not_serve()
    {
        var catalog = Path.Combine(RepositoryRoot(), "catalog");
        var commit = McpVerbCatalog.CommitKindNames.ToHashSet(StringComparer.Ordinal);
        var query = McpVerbCatalog.QueryKindNames.ToHashSet(StringComparer.Ordinal);
        var citation = new Regex(@"(?<verb>commit|query)\(\s*kind\s*:\s*""(?<kind>[^""]+)""");
        var phantom = new List<string>();
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(catalog, "*.md", SearchOption.AllDirectories))
        {
            scanned++;
            foreach (Match match in citation.Matches(File.ReadAllText(file)))
            {
                var kind = match.Groups["kind"].Value;
                if (kind == "...") continue;
                var served = match.Groups["verb"].Value == "commit" ? commit : query;
                if (served.Contains(kind)) continue;
                phantom.Add($"{Path.GetRelativePath(catalog, file)}: {match.Groups["verb"].Value}(kind: \"{kind}\")");
            }
        }

        Assert.True(scanned > 0, "No catalog contracts were found to check.");
        Assert.True(
            phantom.Count == 0,
            "These contracts instruct a caller to make a call the protocol does not serve:\n  "
            + string.Join("\n  ", phantom.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// Every commit example has to be a payload that would actually parse. An example that only
    /// looks like JSON is worse than none: it is quoted verbatim into the next call.
    /// </summary>
    [Fact]
    public void Every_commit_example_payload_is_a_valid_json_object()
    {
        foreach (var kind in McpVerbCatalog.CommitKinds)
        {
            var document = JsonDocument.Parse(kind.Descriptor.Examples[0].InputJson);
            var payload = document.RootElement.GetProperty("payload");

            Assert.True(
                payload.ValueKind == JsonValueKind.Object,
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
            RepositoryRoot(), "DantesRoleplay.MCPServer", "Mcp", fileName);

        Assert.True(File.Exists(file), $"Expected a dispatcher at {file}.");

        var source = File.ReadAllText(file);
        var start = source.IndexOf("return normalizedKind switch", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find the dispatch switch in {fileName}.");

        var end = source.IndexOf("_ => throw", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find the end of the dispatch switch in {fileName}.");

        return [.. Regex.Matches(source[start..end], @"""(?<kind>[a-z.-]+)""\s*(?:when[^=]*?)?=>")
            .Select(m => m.Groups["kind"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The tool names actually registered with the MCP server, read from source.</summary>
    private static List<string> DeclaredToolNames()
    {
        var root = RepositoryRoot();
        var toolsDirectory = Path.Combine(root, "DantesRoleplay.MCPServer", "Mcp");

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
        EnumerateAllSource(directory)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            // Migrations are generated, and their content mirrors the schema rather than adding to it.
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    private static IEnumerable<string> EnumerateAllSource(string directory) =>
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string NormalizedRelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

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

    private static string StripComments(string source)
    {
        var withoutBlockComments = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlockComments, @"//[^\n]*", " ");
    }

    private sealed record ComponentManifest(
        string Name,
        string Classification,
        string Status,
        IReadOnlyList<string> Owns,
        IReadOnlyList<string> MayDependOn);
}
