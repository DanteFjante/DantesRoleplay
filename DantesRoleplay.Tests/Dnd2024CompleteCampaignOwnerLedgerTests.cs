using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024CompleteCampaignOwnerLedgerTests
{
    private static readonly string[] InputRoots =
    [
        "catalog/applications/dnd2024/components",
        "catalog/applications/dnd2024/content",
        "catalog/applications/dnd2024/mechanics",
        "catalog/applications/dnd2024/procedures",
        "catalog/components",
        "ruleset/dnd2024/adoption/evidence/retained-archive-inventory-13a.json",
        "ruleset/dnd2024/evidence/modeling/canonical-component-crosswalk.json",
        "ruleset/dnd2024/adoption/evidence/coverage-matrix-1b.json",
        "ruleset/dnd2024/adoption/evidence/slice-8-closure.json",
        "src/system/web-interface/dnd2024/src/server/game-server-context.js"
    ];

    private static readonly IReadOnlyDictionary<string, int> ExpectedCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["activeMechanics"] = 69,
            ["activeRetiredContractMechanics"] = 13,
            ["duplicateToolIdentityGroups"] = 14,
            ["duplicateToolIdentityRecords"] = 28,
            ["emptyActivityMemberships"] = 382,
            ["emptyBackgroundGrantRefs"] = 4,
            ["emptyChoiceOptionGrantRefs"] = 130,
            ["emptyFeatGrantRefs"] = 17,
            ["emptyFeatureRuleGrantRefs"] = 229,
            ["emptyProgressionDefinitions"] = 24,
            ["emptyProgressionLevels"] = 480,
            ["emptySpeciesGrantRefs"] = 9,
            ["miscategorizedTools"] = 2
        };

    private static readonly string[] ExpectedRetiredContractMechanics =
    [
        "mechanic.dnd2024.character-abilities.resolve",
        "mechanic.dnd2024.character-content-definition.record",
        "mechanic.dnd2024.character.basic.create",
        "mechanic.dnd2024.conditions.write",
        "mechanic.dnd2024.d20-test.state-effects",
        "mechanic.dnd2024.item-activity.use",
        "mechanic.dnd2024.item.transfer",
        "mechanic.dnd2024.rest.begin",
        "mechanic.dnd2024.rest.interrupt",
        "mechanic.dnd2024.rest.progress",
        "mechanic.dnd2024.species-skillful.resolve",
        "mechanic.dnd2024.species-versatile-skilled.resolve",
        "mechanic.dnd2024.turn-budget.spend"
    ];

    [Fact]
    public void Ledger_has_a_deterministic_closed_input_fingerprint_and_observed_counts()
    {
        var root = RepositoryRoot();
        using var document = Ledger(root);
        var ledger = document.RootElement;

        Assert.Equal("dnd2024-complete-campaign-owner-ledger/v1", ledger.GetProperty("format").GetString());
        Assert.Equal(InputRoots, Strings(ledger.GetProperty("inputRoots")));

        var files = InputFiles(root);
        Assert.Equal(files.Count, ledger.GetProperty("inputFileCount").GetInt32());
        Assert.Equal(InputFingerprint(root, files), ledger.GetProperty("inputSha256").GetString());

        var counts = ledger.GetProperty("counts");
        Assert.Equal(ExpectedCounts.Keys.OrderBy(value => value, StringComparer.Ordinal),
            counts.EnumerateObject().Select(property => property.Name).OrderBy(value => value, StringComparer.Ordinal));
        foreach (var (name, expected) in ExpectedCounts)
            Assert.Equal(expected, counts.GetProperty(name).GetInt32());
    }

    [Fact]
    public void Ledger_retains_exact_active_retired_contract_mechanics()
    {
        var root = RepositoryRoot();
        using var document = Ledger(root);
        var listed = document.RootElement.GetProperty("retiredContractMechanics")
            .EnumerateArray()
            .Select(row => row.GetProperty("mechanicId").GetString())
            .ToArray();

        Assert.Equal(ExpectedRetiredContractMechanics, listed);
        Assert.Equal(listed.OrderBy(value => value, StringComparer.Ordinal), listed);
        Assert.All(document.RootElement.GetProperty("retiredContractMechanics").EnumerateArray(), row =>
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("disposition").GetString())));

        var activeMechanics = ActiveMechanics(root);
        Assert.Equal(ExpectedCounts["activeMechanics"], activeMechanics.Count);
        Assert.All(listed, id => Assert.Contains(id, activeMechanics));
    }

    [Fact]
    public void Ledger_retains_duplicate_tool_identities_and_current_category_anomalies()
    {
        var root = RepositoryRoot();
        using var document = Ledger(root);
        var ledger = document.RootElement;
        var tools = ToolDefinitions(root);
        var duplicateGroups = tools.GroupBy(tool => Normalize(tool.Name), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedCounts["duplicateToolIdentityGroups"], duplicateGroups.Length);
        Assert.Equal(ExpectedCounts["duplicateToolIdentityRecords"], duplicateGroups.Sum(group => group.Count()));
        Assert.Equal(duplicateGroups.Select(group => group.Key), Strings(ledger.GetProperty("duplicateToolNormalizedNames")));

        var anomalies = ledger.GetProperty("miscategorizedTools").EnumerateArray().ToArray();
        Assert.Equal(ExpectedCounts["miscategorizedTools"], anomalies.Length);
        Assert.Equal(anomalies.Select(row => row.GetProperty("id").GetString()!).OrderBy(value => value, StringComparer.Ordinal),
            anomalies.Select(row => row.GetProperty("id").GetString()!));
        foreach (var anomaly in anomalies)
        {
            var id = anomaly.GetProperty("id").GetString()!;
            var tool = Assert.Single(tools, candidate => candidate.Id == id);
            Assert.Equal(anomaly.GetProperty("currentCategoryId").GetString(), tool.CategoryId);
            Assert.False(string.IsNullOrWhiteSpace(anomaly.GetProperty("disposition").GetString()));
        }
    }

    [Fact]
    public void Ledger_conflicts_and_unresolved_candidates_have_existing_evidence()
    {
        var root = RepositoryRoot();
        using var document = Ledger(root);
        var ledger = document.RootElement;
        var conflicts = ledger.GetProperty("conflicts").EnumerateArray().ToArray();
        var conflictIds = conflicts.Select(row => row.GetProperty("id").GetString()!).ToArray();

        Assert.Equal(conflictIds.OrderBy(value => value, StringComparer.Ordinal), conflictIds);
        Assert.Equal(
            [
                "campaign-root-qualified-owner",
                "map-presentation-selection",
                "retired-mechanic-contract-owners",
                "tool-category-anomalies",
                "tool-identity-duplicates",
                "world-root-qualified-owner"
            ], conflictIds);
        foreach (var conflict in conflicts)
        {
            Assert.Equal("conflicting", conflict.GetProperty("state").GetString());
            foreach (var path in Strings(conflict.GetProperty("evidencePaths")))
                Assert.True(File.Exists(Path.Combine(root, path)) || Directory.Exists(Path.Combine(root, path)), path);
        }

        var candidates = ledger.GetProperty("unresolvedCandidates").EnumerateArray().ToArray();
        Assert.Equal(candidates.Select(row => row.GetProperty("id").GetString()).OrderBy(value => value, StringComparer.Ordinal),
            candidates.Select(row => row.GetProperty("id").GetString()));
        Assert.All(candidates, row => Assert.Equal("missing", row.GetProperty("state").GetString()));
    }

    private static JsonDocument Ledger(string root) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
        "ruleset", "dnd2024", "adoption", "evidence", "complete-campaign-owner-ledger.json")));

    private static IReadOnlyList<string> InputFiles(string root) => InputRoots.SelectMany(input =>
        Directory.Exists(Path.Combine(root, input))
            ? Directory.EnumerateFiles(Path.Combine(root, input), "*", SearchOption.AllDirectories)
            : [Path.Combine(root, input)])
        .Select(path => Relative(root, path))
        .OrderBy(path => path, StringComparer.Ordinal)
        .ToArray();

    private static string InputFingerprint(string root, IReadOnlyList<string> files)
    {
        var entries = files.Select(path => path + "\0" + Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)))).AsSpan()).ToLowerInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", entries)))).ToLowerInvariant();
    }

    private static IReadOnlyCollection<string> ActiveMechanics(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "catalog", "applications", "dnd2024", "mechanics"), "*.md",
                SearchOption.AllDirectories)
            .Select(path => File.ReadAllText(path))
            .Where(text => Regex.IsMatch(text, "^status: active\\s*$", RegexOptions.Multiline))
            .Select(text => Regex.Match(text, "^id: (.+)$", RegexOptions.Multiline).Groups[1].Value)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<ToolDefinition> ToolDefinitions(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "catalog", "applications", "dnd2024", "content", "entities", "equipment"),
                "*.json", SearchOption.AllDirectories)
            .Select(path => JsonDocument.Parse(File.ReadAllText(path)))
            .Select(document =>
            {
                using (document)
                {
                    var record = document.RootElement;
                    if (!record.TryGetProperty("components", out var components) ||
                        !components.TryGetProperty("dnd2024.item.tool", out var tool))
                        return null;
                    return new ToolDefinition(
                        record.GetProperty("id").GetString()!,
                        record.GetProperty("name").GetString()!,
                        tool.GetProperty("category").GetProperty("entityId").GetString()!);
                }
            })
            .Where(tool => tool is not null)
            .Cast<ToolDefinition>()
            .ToArray();

    private static IReadOnlyList<string> Strings(JsonElement array) => array.EnumerateArray()
        .Select(value => value.GetString()!)
        .ToArray();

    private static string Normalize(string name) => string.Concat(name.Normalize(NormalizationForm.FormD)
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant));

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "catalog")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed record ToolDefinition(string Id, string Name, string CategoryId);
}
