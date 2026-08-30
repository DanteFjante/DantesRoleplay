using System.Text.Json;
using DantesRoleplay.Mechanics;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024MovedMechanicTests
{
    private static readonly string[] MovedMechanics =
    [
        "checks/dnd2024.mechanic.dice",
        "movement/dnd2024.mechanic.speed.read",
        "movement/dnd2024.mechanic.speed.write",
        "combat/dnd2024.mechanic.healing.apply",
        "combat/dnd2024.mechanic.temporary-hit-points.write",
        "combat/dnd2024.mechanic.death-state.write",
        "data/dnd2024.mechanic.heroic-inspiration.grant",
        "proficiency/dnd2024.mechanic.character-experience.write"
    ];

    [Fact]
    public void Moved_mechanics_have_one_canonical_contract_and_current_component_owners()
    {
        var componentRoot = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024", "components");
        var componentIds = Directory.GetFiles(componentRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".schema.json", StringComparison.Ordinal))
            .Select(path => JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var relative in MovedMechanics)
        {
            var path = MechanicPath(relative);
            Assert.True(File.Exists(path + ".js"), relative + " is missing JavaScript.");
            Assert.True(File.Exists(path + ".md"), relative + " is missing its contract.");
            var contract = File.ReadAllText(path + ".md");
            Assert.Contains("category: dnd2024.ruleset.", contract, StringComparison.Ordinal);
            Assert.Contains("## Description", contract, StringComparison.Ordinal);
            Assert.Contains("## Requirements", contract, StringComparison.Ordinal);

            var jsonStart = contract.IndexOf("```json", StringComparison.Ordinal);
            var jsonEnd = contract.IndexOf("```", jsonStart + 7, StringComparison.Ordinal);
            Assert.True(jsonStart >= 0 && jsonEnd > jsonStart, relative + " has no JSON requirements block.");
            using var requirements = JsonDocument.Parse(contract[(jsonStart + 7)..jsonEnd].Trim());
            foreach (var componentId in RequirementComponents(requirements.RootElement))
                Assert.Contains(componentId, componentIds);
        }
    }

    [Fact]
    public async Task Moved_mechanics_execute_against_current_component_shapes()
    {
        var dice = await RunAsync("checks/dnd2024.mechanic.dice", "{}", []);
        Assert.True(dice.Ok, dice.Error);
        Assert.Empty(dice.Output.Effects);
        Assert.False((await RunAsync("checks/dnd2024.mechanic.dice", "{\"count\":101}", [])).Ok);
        Assert.False((await RunAsync("checks/dnd2024.mechanic.dice", "{\"cheat\":20}", [])).Ok);

        const string speedInput =
            "{\"mode\":\"record\",\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0}";
        var speed = await RunAsync("movement/dnd2024.mechanic.speed.write", speedInput, Subject());
        Assert.True(speed.Ok, speed.Error);
        var movement = Assert.Single(speed.Output.Effects);
        Assert.Equal("dnd2024.creature.movement", movement.DefinitionId);
        AssertCurrentSchema("dnd2024.creature.movement", movement.Data);
        var read = await RunAsync("movement/dnd2024.mechanic.speed.read", "{}",
            Subject((movement.DefinitionId, movement.Data)));
        Assert.True(read.Ok, read.Error);
        Assert.Contains("\"valid\":true", read.Output.Data, StringComparison.Ordinal);

        var healing = await RunAsync("combat/dnd2024.mechanic.healing.apply", "{\"amount\":20}",
            Subject(("dnd2024.creature.hit-points", "{\"current\":5,\"maximum\":10}")));
        Assert.True(healing.Ok, healing.Error);
        var healed = Assert.Single(healing.Output.Effects);
        AssertCurrentSchema("dnd2024.creature.hit-points", healed.Data);
        var capped = await RunAsync("combat/dnd2024.mechanic.healing.apply", "{\"amount\":1}",
            Subject(("dnd2024.creature.hit-points", healed.Data)));
        Assert.True(capped.Ok, capped.Error);
        Assert.Empty(capped.Output.Effects);

        var temporary = await RunAsync("combat/dnd2024.mechanic.temporary-hit-points.write",
            "{\"mode\":\"grant\",\"amount\":7}", Subject());
        Assert.True(temporary.Ok, temporary.Error);
        AssertCurrentSchema("dnd2024.creature.temporary-hit-points", Assert.Single(temporary.Output.Effects).Data);

        var death = await RunAsync("combat/dnd2024.mechanic.death-state.write",
            "{\"mode\":\"begin\"}", Subject());
        Assert.True(death.Ok, death.Error);
        AssertCurrentSchema("dnd2024.character.death-saves", Assert.Single(death.Output.Effects).Data);
        var deathCorrection = await RunAsync("combat/dnd2024.mechanic.death-state.write",
            "{\"mode\":\"correct\",\"successes\":1,\"failures\":0,\"stable\":false,\"dead\":false}",
            Subject(("dnd2024.character.death-saves",
                "{\"status\":\"dying\",\"successes\":0,\"failures\":0,\"lastChangeRef\":{\"operationId\":\"0123456789abcdef0123456789abcdef\"}}")));
        Assert.True(deathCorrection.Ok, deathCorrection.Error);
        AssertCurrentSchema("dnd2024.character.death-saves", Assert.Single(deathCorrection.Output.Effects).Data);
        Assert.Contains("lastChangeRef", Assert.Single(deathCorrection.Output.Effects).Data,
            StringComparison.Ordinal);

        var inspiration = await RunAsync("data/dnd2024.mechanic.heroic-inspiration.grant", "{}",
            Subject(("dnd2024.character.identity", "{\"pronouns\":\"they/them\"}")));
        Assert.True(inspiration.Ok, inspiration.Error);
        AssertCurrentSchema("dnd2024.character.heroic-inspiration", Assert.Single(inspiration.Output.Effects).Data);

        var experience = await RunAsync("proficiency/dnd2024.mechanic.character-experience.write",
            "{\"mode\":\"record\",\"total\":300}", Subject());
        Assert.True(experience.Ok, experience.Error);
        AssertCurrentSchema("dnd2024.character.experience", Assert.Single(experience.Output.Effects).Data);
    }

    private static async Task<MechanicRunResult> RunAsync(
        string relative,
        string input,
        Dictionary<string, EntityProjection> roles)
    {
        var source = await File.ReadAllTextAsync(MechanicPath(relative) + ".js");
        return await new JintMechanicEngine().RunAsync(source, new MechanicProjection
        {
            Input = input,
            Roles = roles,
            Seed = 4242
        }, ExecutionLimits.Default);
    }

    private static Dictionary<string, EntityProjection> Subject(params (string Id, string Data)[] components) =>
        new(StringComparer.Ordinal)
        {
            ["subject"] = new("subject", "Subject",
                components.ToDictionary(value => value.Id, value => value.Data, StringComparer.Ordinal))
        };

    private static void AssertCurrentSchema(string componentId, string valueJson)
    {
        var schema = File.ReadAllText(Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024",
            "components", componentId + ".schema.json"));
        var validator = new DantesRoleplay.SchemaValidation.BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
        Assert.Equal(DantesRoleplay.SchemaValidation.SchemaValueStatus.Valid,
            validator.Validate(compilation.ProfileId, compilation.NormalizedSchema, valueJson).Status);
    }

    private static IEnumerable<string> RequirementComponents(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
                if (property.Name is "components" or "contentComponentIds" or "targetComponentIds")
                {
                    foreach (var item in property.Value.EnumerateArray()) yield return item.GetString()!;
                }
                else
                    foreach (var component in RequirementComponents(property.Value)) yield return component;
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                foreach (var component in RequirementComponents(item)) yield return component;
    }

    private static string MechanicPath(string relative) => Path.Combine(
        RepositoryRoot(), "catalog", "applications", "dnd2024", "mechanics",
        relative.Replace('/', Path.DirectorySeparatorChar));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
