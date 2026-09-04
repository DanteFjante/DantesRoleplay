using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024IntentRecipeTests
{
    public static TheoryData<string, string, object> IntentFamilies => new()
    {
        { "travel", "exploration/dnd2024.mechanic.travel.execute", new { travel = new { journeyId = "journey.1", exposureScheduleId = "exposure.1", mode = "walk", pace = "normal", expectedRouteRevision = 1, expectedRouteFingerprint = new string('A', 64), expectedClockRevision = 0 } } },
        { "explore", "campaign/dnd2024.mechanic.campaign.location-visit.record", new { status = "current", summary = "Entered the location.", memory = "The visit is established.", gmContext = (string?)null } },
        { "negotiate", "social/dnd2024.mechanic.social.attitude.transition", new { relationshipId = "attitude.1", previousAttitudeId = (string?)null, nextAttitudeId = "dnd2024.vocabulary.attitude.indifferent", visibility = "party", reasonFacts = Array.Empty<object>(), evidenceReceiptIds = Array.Empty<string>(), expectedRevision = 0 } },
        { "combat", "combat/dnd2024.mechanic.weapon-attack", new { ability = "str" } },
        { "loot", "data/dnd2024.mechanic.item.transfer", new { slot = "inventory" } },
        { "rest", "data/dnd2024.mechanic.rest.complete", new { hitDice = Array.Empty<object>() } },
        { "downtime", "downtime/dnd2024.mechanic.downtime.complete", new { expectedDefinitionRevision = 1, expectedDefinitionFingerprint = new string('B', 64) } }
    };

    [Theory]
    [MemberData(nameof(IntentFamilies))]
    public void Current_intent_family_can_be_learned_as_an_exact_value_free_recipe(
        string family,
        string mechanicPath,
        object validInput)
    {
        var mechanic = ReadMechanic(mechanicPath);
        var requirements = MechanicRequirements.Parse(mechanic.Requirements);
        var privateRoles = requirements.Roles.Keys.ToDictionary(
            role => role, role => "entity.private-" + role, StringComparer.Ordinal);
        var inputJson = JsonSerializer.Serialize(validInput);

        var template = InteractionRecipeTemplate.FromProposal(ApplicationIdentifier.Parse("dnd2024"), new([
            new("resolve-" + family, InteractionPlanStepKind.Action, mechanic.Id, 1,
                mechanic.ContentHash, [], privateRoles, inputJson)
        ]));

        var step = Assert.Single(template.Steps);
        Assert.Equal(mechanic.Id, step.QualifiedId);
        Assert.Equal(mechanic.ContentHash, step.ContractFingerprint);
        Assert.NotEmpty(step.InputBindings);
        Assert.DoesNotContain("entity.private", template.CanonicalJson, StringComparison.Ordinal);
        Assert.DoesNotContain(InteractionCanonicalJson.CanonicalizeObject(inputJson),
            template.CanonicalJson, StringComparison.Ordinal);
        Assert.StartsWith("dnd2024.recipe.",
            InteractionRecipeIds.Create(ApplicationIdentifier.Parse("dnd2024"), template.Fingerprint),
            StringComparison.Ordinal);
    }

    private static MechanicFile ReadMechanic(string relative)
    {
        var path = Path.Combine(Catalog(), "applications", "dnd2024", "mechanics",
            relative.Replace('/', Path.DirectorySeparatorChar));
        return MechanicFile.Parse(File.ReadAllText(path + ".md"), relative + ".md",
            File.ReadAllText(path + ".js"));
    }

    private static string Catalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }
}
