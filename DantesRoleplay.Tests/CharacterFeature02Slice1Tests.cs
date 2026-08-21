using System.Text.Json;
using DantesRoleplay.Characters;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using Json.Schema;

namespace DantesRoleplay.Tests;

public sealed class CharacterFeature02Slice1Tests : IDisposable
{
    private const string Definition = "dnd2024.character.ability-assignment-policy";
    private const string StandardArray = "content.dnd2024.ability-assignment.standard-array.v1";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"character-feature-02-slice-1-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Standard_array_policy_returns_canonical_scores_and_no_effects()
    {
        var setup = await ArrangeAsync();

        var plan = await setup.Validator.ValidateAsync(new(StandardArray, """{"wis":10,"cha":12,"str":15,"int":8,"con":13,"dex":14}"""));

        Assert.True(plan.Valid, plan.Problems.FirstOrDefault()?.Reason);
        Assert.Equal(1, plan.PolicyVersion);
        Assert.Empty(plan.Problems);
        using var scores = JsonDocument.Parse(plan.CanonicalScoresJson!);
        Assert.Equal(15, scores.RootElement.GetProperty("str").GetInt32());
        Assert.Equal(14, scores.RootElement.GetProperty("dex").GetInt32());
        Assert.Equal(13, scores.RootElement.GetProperty("con").GetInt32());
        Assert.Equal(8, scores.RootElement.GetProperty("int").GetInt32());
        Assert.Equal(10, scores.RootElement.GetProperty("wis").GetInt32());
        Assert.Equal(12, scores.RootElement.GetProperty("cha").GetInt32());
    }

    [Theory]
    [InlineData("""{"str":15,"dex":14,"con":13,"int":8,"wis":10,"cha":12,"modifier":3}""", "INVALID_SCORES")]
    [InlineData("""{"str":15,"dex":14,"con":13,"int":8,"wis":10}""", "INVALID_SCORES")]
    [InlineData("""{"str":15,"dex":14,"con":13,"int":8,"wis":10,"cha":11}""", "ASSIGNMENT_NOT_ALLOWED")]
    [InlineData("""{"str":15,"dex":14,"con":13,"int":8,"wis":10,"cha":12.5}""", "INVALID_SCORES")]
    public async Task Invalid_assignment_never_returns_a_canonical_result(string scores, string code)
    {
        var setup = await ArrangeAsync();

        var plan = await setup.Validator.ValidateAsync(new(StandardArray, scores));

        Assert.False(plan.Valid);
        Assert.Null(plan.CanonicalScoresJson);
        Assert.Equal(code, Assert.Single(plan.Problems).Code);
    }

    [Fact]
    public async Task Corrupt_policy_and_unknown_binding_are_rejected_without_actor_state()
    {
        var setup = await ArrangeAsync();
        await setup.World.CreateEntityAsync("Broken policy", "content.test.broken-policy.v1");
        await setup.World.SetComponentAsync("content.test.broken-policy.v1", Definition, """{"policyVersion":1,"sourceRef":{"sourceId":"unknown","locator":"x"},"scoreBounds":{"minimum":8,"maximum":15},"allocation":{"family":"fixed-multiset","values":[8,10,12,13,14,15]}}""");

        var corrupt = await setup.Validator.ValidateAsync(new("content.test.broken-policy.v1", """{"str":15,"dex":14,"con":13,"int":8,"wis":10,"cha":12}"""));
        var missing = await setup.Validator.ValidateAsync(new("content.test.missing-policy.v1", "{}"));

        Assert.False(corrupt.Valid); Assert.Equal("INVALID_POLICY", Assert.Single(corrupt.Problems).Code);
        Assert.False(missing.Valid); Assert.Equal("POLICY_NOT_FOUND", Assert.Single(missing.Problems).Code);
    }

    [Fact]
    public async Task Point_budget_is_supported_but_the_executable_declaration_remains_internal()
    {
        var setup = await ArrangeAsync();
        const string pointBudget = "content.test.point-budget.v1";
        await setup.World.CreateEntityAsync("Point budget", pointBudget);
        await setup.World.SetComponentAsync(pointBudget, Definition, """{"policyVersion":2,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Character Creation > Step 3: Ability Scores, PDF page 21"},"scoreBounds":{"minimum":8,"maximum":15},"allocation":{"family":"point-budget","budget":15,"costs":[{"score":8,"cost":0},{"score":9,"cost":1},{"score":10,"cost":2},{"score":11,"cost":3},{"score":12,"cost":4},{"score":13,"cost":5},{"score":14,"cost":7},{"score":15,"cost":9}]}}""");

        var valid = await setup.Validator.ValidateAsync(new(pointBudget, """{"str":13,"dex":12,"con":11,"int":10,"wis":9,"cha":8}"""));
        var wrongBudget = await setup.Validator.ValidateAsync(new(pointBudget, """{"str":15,"dex":12,"con":11,"int":10,"wis":9,"cha":8}"""));

        Assert.True(valid.Valid, valid.Problems.FirstOrDefault()?.Reason);
        Assert.False(wrongBudget.Valid); Assert.Equal("ASSIGNMENT_NOT_ALLOWED", Assert.Single(wrongBudget.Problems).Code);
        var mechanic = await setup.Mechanics.GetAsync("mechanic.dnd2024.character-ability-assignment-policy.validate");
        Assert.NotNull(mechanic); Assert.Equal(MechanicStatus.Draft, mechanic!.Status);
    }

    [Fact]
    public async Task Fixture_and_schema_are_source_cited_and_do_not_admit_an_actor_or_grant()
    {
        var contents = await CatalogReader.ReadAsync(RepositoryCatalog());
        var schema = JsonSchema.FromText(Assert.Single(contents.Components, x => x.Id == Definition).Schema);
        using var valid = JsonDocument.Parse("""{"policyVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Character Creation > Step 3: Ability Scores, PDF page 21"},"scoreBounds":{"minimum":8,"maximum":15},"allocation":{"family":"fixed-multiset","values":[8,10,12,13,14,15]}}""");
        using var forbidden = JsonDocument.Parse("""{"policyVersion":1,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"x"},"scoreBounds":{"minimum":8,"maximum":15},"allocation":{"family":"fixed-multiset","values":[8,10,12,13,14,15]},"actorId":"actor.any"}""");

        Assert.True(schema.Evaluate(valid.RootElement).IsValid);
        Assert.False(schema.Evaluate(forbidden.RootElement).IsValid);
        var fixture = Assert.Single(contents.Entities, x => x.Id == StandardArray);
        var policy = Assert.Single(fixture.Components, x => x.DefinitionId == Definition).Data;
        Assert.DoesNotContain("actorId", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("grant", policy, StringComparison.Ordinal);
    }

    private async Task<Setup> ArrangeAsync()
    {
        Copy(RepositoryCatalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        return new(world, mechanics, new CharacterAbilityAssignmentValidator(world));
    }

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "catalog", "manifest.json"))) return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    private sealed record Setup(WorldStore World, MechanicStore Mechanics, CharacterAbilityAssignmentValidator Validator);
}
