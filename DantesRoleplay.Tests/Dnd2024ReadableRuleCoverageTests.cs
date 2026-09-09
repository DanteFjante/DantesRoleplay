using System.Text.Json;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024ReadableRuleCoverageTests
{
    [Fact]
    public void Reviewed_coverage_links_published_articles_to_installed_authority_and_records_gaps()
    {
        var root = RepositoryRoot();
        using var matrix = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "DantesRoleplay.Tests", "Fixtures", "dnd2024-readable-rule-coverage.json")));
        var topics = matrix.RootElement.GetProperty("topics").EnumerateArray().ToArray();
        Assert.Equal(1, matrix.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("dnd2024", matrix.RootElement.GetProperty("applicationId").GetString());
        Assert.Equal(12, topics.Length);
        Assert.Equal(12, topics.Select(value => value.GetProperty("topic").GetString()).Distinct().Count());

        var installedIds = Directory.GetFiles(Path.Combine(root, "catalog", "applications", "dnd2024"),
                "*.md", SearchOption.AllDirectories)
            .SelectMany(File.ReadLines)
            .Where(line => line.StartsWith("id: ", StringComparison.Ordinal))
            .Select(line => line[4..].Trim())
            .ToHashSet(StringComparer.Ordinal);
        var coreArticleFiles = Directory.GetFiles(Path.Combine(root, "catalog", "applications", "dnd2024",
            "content", "entities", "readable-rules"), "*.json", SearchOption.AllDirectories);
        var articleById = coreArticleFiles.Select(path => JsonDocument.Parse(File.ReadAllText(path)))
            .ToDictionary(document => document.RootElement.GetProperty("id").GetString()!, StringComparer.Ordinal);

        foreach (var topic in topics)
        {
            var status = topic.GetProperty("status").GetString();
            var articleIds = Texts(topic, "articleIds");
            var mechanicIds = Texts(topic, "mechanicIds");
            var procedureIds = Texts(topic, "procedureIds");
            if (status == "gap")
            {
                Assert.Empty(articleIds);
                Assert.Empty(mechanicIds);
                Assert.Empty(procedureIds);
                Assert.True(topic.TryGetProperty("gap", out var gap) && gap.GetString()!.Length >= 80);
                continue;
            }

            Assert.Equal("published", status);
            Assert.NotEmpty(articleIds);
            Assert.NotEmpty(mechanicIds.Concat(procedureIds));
            foreach (var authorityId in mechanicIds.Concat(procedureIds))
                Assert.Contains(authorityId, installedIds);
            foreach (var articleId in articleIds)
            {
                var article = Assert.Contains(articleId, articleById);
                var readable = article.RootElement.GetProperty("components")
                    .GetProperty("game.core.rules.readable");
                Assert.Equal("published", readable.GetProperty("presentationStatus").GetString());
                Assert.NotEmpty(readable.GetProperty("citations").EnumerateArray());
                var articleAuthority = Texts(readable, "mechanicIds").Concat(Texts(readable, "procedureIds"));
                Assert.All(mechanicIds.Concat(procedureIds), id => Assert.Contains(id, articleAuthority));
            }
        }

        Assert.Equal(13, articleById.Count);
        Assert.Contains("dnd2024.rule.characters.character-sheet", articleById.Keys);
        Assert.Contains("dnd2024.rule.combat.weapon-attacks", articleById.Keys);
        Assert.Contains("dnd2024.rule.resting.long-rests", articleById.Keys);

        var extensionArticle = Path.Combine(root, "catalog", "extensions", "dnd2024", "caldris-homebrew",
            "content", "entities", "character-creation", "species",
            "dnd2024.extension.caldris.content.species.half-elf.v1.json");
        using var extension = JsonDocument.Parse(File.ReadAllText(extensionArticle));
        Assert.Equal("published", extension.RootElement.GetProperty("components")
            .GetProperty("game.core.rules.readable").GetProperty("presentationStatus").GetString());

        foreach (var article in articleById.Values) article.Dispose();
    }

    private static string[] Texts(JsonElement owner, string name) =>
        owner.GetProperty(name).EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
