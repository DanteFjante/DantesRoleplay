using System.Text.Json;
using System.Text.RegularExpressions;

namespace DantesRoleplay.Tests;

public sealed partial class Dnd2024NamespaceContainmentTests
{
    private static readonly string[] LegacyPrefixes =
    [
        "procedure.mechanic.dnd2024",
        "procedure.play.dnd2024",
        "mechanic.dnd2024",
        "ruleset.dnd2024",
        "source.dnd2024",
        "content.dnd2024",
        "currency.dnd2024",
        "item.dnd2024"
    ];

    [Fact]
    public void Authored_application_records_are_owned_by_the_dnd2024_namespace()
    {
        var application = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024");
        var problems = new List<string>();

        foreach (var path in Directory.EnumerateFiles(application, "*.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && !id.GetString()!.StartsWith("dnd2024.", StringComparison.Ordinal))
                problems.Add($"JSON ID '{id.GetString()}' in {Relative(path)} is outside dnd2024.");
        }

        foreach (var path in Directory.EnumerateFiles(application, "*.md", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            var id = FrontMatterValue(text, "id");
            var category = FrontMatterValue(text, "category");
            if (id is not null && !id.StartsWith("dnd2024.", StringComparison.Ordinal))
                problems.Add($"Markdown ID '{id}' in {Relative(path)} is outside dnd2024.");
            if (category is not null && !category.StartsWith("dnd2024.", StringComparison.Ordinal))
                problems.Add($"Markdown category '{category}' in {Relative(path)} is outside dnd2024.");

            if (id is not null && (path.Contains($"{Path.DirectorySeparatorChar}mechanics{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                   || path.Contains($"{Path.DirectorySeparatorChar}procedures{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), id, StringComparison.Ordinal))
                    problems.Add($"Filename and ID disagree in {Relative(path)}: expected {id}.md.");
                if (path.Contains($"{Path.DirectorySeparatorChar}mechanics{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !File.Exists(Path.ChangeExtension(path, ".js")))
                    problems.Add($"Mechanic sidecar is missing for {Relative(path)}.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Current_dnd2024_sources_do_not_use_inverted_or_unqualified_application_ids()
    {
        var root = RepositoryRoot();
        var roots = new[]
        {
            Path.Combine(root, "catalog", "applications", "dnd2024"),
            Path.Combine(root, "src", "system", "web-interface", "dnd2024")
        };
        var problems = new List<string>();

        foreach (var sourceRoot in roots)
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                     .Where(IsTextSource))
        {
            var text = File.ReadAllText(path);
            foreach (var prefix in LegacyPrefixes)
                if (text.Contains(prefix, StringComparison.Ordinal))
                    problems.Add($"Legacy prefix '{prefix}' remains in {Relative(path)}.");
            if (UnqualifiedGameCore().IsMatch(text))
                problems.Add($"Unqualified D&D game.core reference remains in {Relative(path)}.");
            if (text.Contains("dnd2024.dnd2024.", StringComparison.Ordinal))
                problems.Add($"Double-qualified ID remains in {Relative(path)}.");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Dnd2024_extension_entity_ids_are_application_qualified()
    {
        var extensions = Path.Combine(RepositoryRoot(), "catalog", "extensions", "dnd2024");
        var problems = new List<string>();
        foreach (var path in Directory.EnumerateFiles(extensions, "*.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && !id.GetString()!.StartsWith("dnd2024.", StringComparison.Ordinal))
                problems.Add($"Extension ID '{id.GetString()}' in {Relative(path)} is outside dnd2024.");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static bool IsTextSource(string path) =>
        Path.GetExtension(path) is ".json" or ".md" or ".js" or ".ts";

    private static string? FrontMatterValue(string text, string key)
    {
        var match = Regex.Match(text, $"(?m)^{Regex.Escape(key)}:\\s*(\\S+)\\s*$",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string Relative(string path) => Path.GetRelativePath(RepositoryRoot(), path);

    [GeneratedRegex("(?<!dnd2024\\.)game\\.core\\.", RegexOptions.CultureInvariant)]
    private static partial Regex UnqualifiedGameCore();

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
