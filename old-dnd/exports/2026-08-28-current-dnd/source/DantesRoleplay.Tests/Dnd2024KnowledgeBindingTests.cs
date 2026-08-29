using DantesRoleplay.Knowledge;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024KnowledgeBindingTests
{
    [Fact]
    public void Authored_metadata_parses_as_the_closed_current_binding()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, "catalog", "applications", "dnd2024", "metadata",
            "authorized-knowledge.json");
        var text = File.ReadAllText(path);

        Assert.True(KnowledgeApplicationBindingDocument.TryParse(text, "dnd2024", out var vocabulary));
        var binding = vocabulary.Bind("dnd2024", "dnd2024-main",
            "campaign.thalorien.brackenford", new('A', 64));
        binding.Validate();

        Assert.Equal("dnd2024.game.core.campaign.character-participation",
            binding.ParticipationComponentTypeId);
        Assert.Equal("dnd2024.game.core.campaign.has-character-participation",
            binding.CampaignParticipationRelationshipKind);
        var secret = Assert.Single(binding.KnowledgeKinds,
            value => value.ComponentTypeId == "dnd2024.game.core.world.secret");
        Assert.Equal("statement", secret.PresentationKind);
    }

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
        throw new InvalidOperationException("Repository root not found.");
    }
}
