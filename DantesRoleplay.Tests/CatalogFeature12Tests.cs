using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

/// <summary>Feature 12 admits, restores, and spends one complete participant turn budget.</summary>
public sealed class CatalogFeature12Tests : IDisposable
{
    private const string Budget = "dnd2024.turn-budget";
    private const string Encounter = "encounter.dnd2024.feature-10.training";
    private const string Hero = "creature.dnd2024.feature-10.hero";
    private const string Target = "creature.dnd2024.feature-10.training-target";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-12-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_records_and_corrects_closed_turn_budgets_and_revises_feature_10_fixtures()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.Contains(contents.Components, component => component.Id == Budget && !string.IsNullOrWhiteSpace(component.Schema));
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.turn-budget"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.turn-budget.write"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.turn-budget.spend"));
        AssertBudget(Component(await world.GetEntityAsync("creature.dnd2024.feature-10.hero"), Budget), true, true, true, true, 30, 30);
        AssertBudget(Component(await world.GetEntityAsync("creature.dnd2024.feature-10.training-target"), Budget), true, true, true, true, 30, 30);

        const string subject = "fixture.catalog.f12.subject";
        await world.CreateEntityAsync("Turn-budget subject", subject);
        var runner = CreateRunner(db, world, mechanics);
        var recorded = await runner.RunAsync(Request("record turn budget", subject, Full("record", true, true, true, true, 30, 30)));
        Assert.True(recorded.Ok, recorded.Error?.Why);
        Assert.Equal("mechanic.dnd2024.turn-budget.write", recorded.Mechanic?.Id);
        Assert.Equal(1, recorded.AppliedCount);
        Assert.Equal(EffectType.ComponentAdd, Assert.Single(recorded.Output!.Effects).Type);
        AssertBudget(Component(await world.GetEntityAsync(subject), Budget), true, true, true, true, 30, 30);

        var corrected = await runner.RunAsync(Request("correct turn budget", subject, Full("correct", false, true, false, true, 25, 30)));
        Assert.True(corrected.Ok, corrected.Error?.Why);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(corrected.Output!.Effects).Type);
        AssertBudget(Component(await world.GetEntityAsync(subject), Budget), false, true, false, true, 25, 30);
        using var correctionData = JsonDocument.Parse(corrected.Output.Data);
        Assert.Equal("correct", correctionData.RootElement.GetProperty("mode").GetString());
        Assert.True(correctionData.RootElement.GetProperty("previous").GetProperty("action").GetBoolean());
    }

    [Fact]
    public async Task Turn_budget_writer_rejects_missing_existing_corrupt_and_noncanonical_state_without_changes()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);
        const string subject = "fixture.catalog.f12.reject";
        const string sibling = "fixture.catalog.f12.sibling";
        await world.CreateEntityAsync("Turn-budget subject", subject);
        await world.CreateEntityAsync("Untouched sibling", sibling);

        var missingCorrect = await runner.RunAsync(Request("correct turn budget", subject, Full("correct", true, true, true, true, 0, 0)));
        Assert.False(missingCorrect.Ok);
        Assert.DoesNotContain((await world.GetEntityAsync(subject))!.Components, component => component.DefinitionId == Budget);
        Assert.True((await runner.RunAsync(Request("record turn budget", subject, Full("record", true, true, true, true, 30, 30)))).Ok);
        var before = Component(await world.GetEntityAsync(subject), Budget);
        var siblingBefore = (await world.GetEntityAsync(sibling))!.Components.ToArray();

        foreach (var input in new[]
                 {
                     Full("record", true, true, true, true, 30, 30),
                     Full("correct", true, true, true, true, 1001, 30),
                     """{"mode":"correct","action":true,"bonusAction":true,"reaction":true,"freeInteraction":true,"movementRemainingFeet":30,"movementMaximumFeet":30}""",
                     """{"mode":"correct","action":"true","bonusAction":true,"reaction":true,"freeInteraction":true,"movementRemainingFeet":30,"movementMaximumFeet":30}""",
                     """{"mode":"correct","action":true,"bonusAction":true,"reaction":true,"freeInteraction":true,"movementRemainingFeet":30,"movementMaximumFeet":30,"sourceRef":{}}"""
                 })
        {
            var rejected = await runner.RunAsync(Request("correct turn budget", subject, input));
            Assert.False(rejected.Ok, input);
            Assert.Equal(before, Component(await world.GetEntityAsync(subject), Budget));
            Assert.Empty((await world.GetEntityAsync(sibling))!.Components.Except(siblingBefore));
        }

        const string corrupt = "fixture.catalog.f12.corrupt";
        await world.CreateEntityAsync("Corrupt turn budget", corrupt);
        await world.SetComponentAsync(corrupt, Budget, """{"action":true,"bonusAction":true,"reaction":true,"freeInteraction":true,"movementRemainingFeet":1001,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Actions; Bonus Actions; Reactions; Interacting with Objects; Combat > Your Turn"}}""");
        var corruptCorrect = await runner.RunAsync(Request("correct turn budget", corrupt, Full("correct", true, true, true, true, 30, 30)));
        Assert.False(corruptCorrect.Ok);
    }

    [Fact]
    public async Task Starting_and_advancing_turns_restore_only_the_newly_active_participant_budget()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.turn-budget.read"));
        var runner = CreateRunner(db, world, mechanics);
        var initiative = await runner.RunAsync(new ActionRequest
        {
            Intent = "set the encounter initiative order",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = JsonSerializer.Serialize(new { participants = new Dictionary<string, object> { [Hero] = new { }, [Target] = new { } } }),
            Seed = 100
        });
        Assert.True(initiative.Ok, initiative.Error?.Why);
        Assert.True((await runner.RunAsync(Request("correct turn budget", Hero, Full("correct", false, false, false, false, 0, 30)))).Ok);
        Assert.True((await runner.RunAsync(Request("correct turn budget", Target, Full("correct", false, false, false, false, 5, 30)))).Ok);

        var started = await runner.RunAsync(new ActionRequest
        {
            Intent = "start encounter turns",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = "{}",
            Seed = 712
        });
        Assert.True(started.Ok, started.Error?.Why);
        Assert.Equal(2, started.AppliedCount);
        AssertBudget(Component(await world.GetEntityAsync(Hero), Budget), true, true, true, true, 30, 30);
        AssertBudget(Component(await world.GetEntityAsync(Target), Budget), false, false, false, false, 5, 30);

        var advanced = await runner.RunAsync(new ActionRequest
        {
            Intent = "advance encounter turn",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = "{}",
            Seed = 712
        });
        Assert.True(advanced.Ok, advanced.Error?.Why);
        Assert.Equal(2, advanced.AppliedCount);
        AssertBudget(Component(await world.GetEntityAsync(Hero), Budget), true, true, true, true, 30, 30);
        AssertBudget(Component(await world.GetEntityAsync(Target), Budget), true, true, true, true, 30, 30);
    }

    [Fact]
    public async Task Turn_budget_reader_reports_absent_state_and_start_rejects_an_unadmitted_roster_member()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);
        const string unadmitted = "fixture.catalog.f12.unadmitted";
        await world.CreateEntityAsync("Unadmitted participant", unadmitted);
        var diagnostic = await runner.RunAsync(new ActionRequest
        {
            Intent = "inspect action-economy budget diagnostics",
            RoleEntityIds = new Dictionary<string, string> { ["participant"] = unadmitted },
            Input = "{}",
            Seed = 712
        });
        Assert.True(diagnostic.Ok, diagnostic.Error?.Why);
        Assert.Empty(diagnostic.Output!.Effects);
        using (var data = JsonDocument.Parse(diagnostic.Output.Data))
        {
            Assert.False(data.RootElement.GetProperty("present").GetBoolean());
            Assert.False(data.RootElement.GetProperty("valid").GetBoolean());
            Assert.Equal("absent", data.RootElement.GetProperty("problem").GetString());
            Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("budget").ValueKind);
        }

        var initiative = await runner.RunAsync(new ActionRequest
        {
            Intent = "set the encounter initiative order",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = JsonSerializer.Serialize(new { participants = new Dictionary<string, object> { [Hero] = new { }, [Target] = new { } } }),
            Seed = 100
        });
        Assert.True(initiative.Ok, initiative.Error?.Why);
        var removal = new Effect { Type = EffectType.ComponentRemove, EntityId = Target, DefinitionId = Budget };
        Assert.True((await new EffectApplier(db, world).ApplyAsync([removal])).Applied);
        var start = await runner.RunAsync(new ActionRequest
        {
            Intent = "start encounter turns",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = "{}",
            Seed = 712
        });
        Assert.False(start.Ok);
        Assert.DoesNotContain((await world.GetEntityAsync(Encounter))!.Components, component => component.DefinitionId == "dnd2024.encounter-turn-state");
    }

    [Fact]
    public async Task Active_participants_spend_each_resource_while_off_turn_reactions_are_the_only_exception()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);
        var initiative = await runner.RunAsync(new ActionRequest
        {
            Intent = "set the encounter initiative order",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = JsonSerializer.Serialize(new { participants = new Dictionary<string, object> { [Hero] = new { }, [Target] = new { } } }),
            Seed = 100
        });
        Assert.True(initiative.Ok, initiative.Error?.Why);
        Assert.True((await runner.RunAsync(new ActionRequest
        {
            Intent = "start encounter turns",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = "{}",
            Seed = 712
        })).Ok);

        foreach (var (intent, resource) in new[]
                 {
                     ("spend my action", "action"),
                     ("use my bonus action", "bonusAction"),
                     ("use my free interaction", "freeInteraction")
                 })
        {
            var spent = await SpendAsync(runner, intent, Hero, Encounter, JsonSerializer.Serialize(new { resource }));
            Assert.True(spent.Ok, spent.Error?.Why);
            Assert.Equal("mechanic.dnd2024.turn-budget.spend", spent.Mechanic?.Id);
            Assert.Equal(EffectType.ComponentSet, Assert.Single(spent.Output!.Effects).Type);
            var once = Component(await world.GetEntityAsync(Hero), Budget);
            var repeated = await SpendAsync(runner, intent, Hero, Encounter, JsonSerializer.Serialize(new { resource }));
            Assert.False(repeated.Ok);
            Assert.Equal(once, Component(await world.GetEntityAsync(Hero), Budget));
        }

        var firstMove = await SpendAsync(runner, "move 15 feet", Hero, Encounter, """{"resource":"movement","feet":15}""");
        Assert.True(firstMove.Ok, firstMove.Error?.Why);
        AssertBudget(Component(await world.GetEntityAsync(Hero), Budget), false, false, true, false, 15, 30);
        var secondMove = await SpendAsync(runner, "move 15 feet", Hero, Encounter, """{"resource":"movement","feet":15}""");
        Assert.True(secondMove.Ok, secondMove.Error?.Why);
        AssertBudget(Component(await world.GetEntityAsync(Hero), Budget), false, false, true, false, 0, 30);
        var exhaustedHero = Component(await world.GetEntityAsync(Hero), Budget);
        foreach (var input in new[] { """{"resource":"movement","feet":5}""", """{"resource":"movement"}""", """{"resource":"action","feet":5}""" })
        {
            Assert.False((await SpendAsync(runner, "move 15 feet", Hero, Encounter, input)).Ok, input);
            Assert.Equal(exhaustedHero, Component(await world.GetEntityAsync(Hero), Budget));
        }

        var targetBefore = Component(await world.GetEntityAsync(Target), Budget);
        foreach (var input in new[]
                 {
                     """{"resource":"action"}""", """{"resource":"bonusAction"}""", """{"resource":"freeInteraction"}""", """{"resource":"movement","feet":5}"""
                 })
        {
            Assert.False((await SpendAsync(runner, "spend my action", Target, Encounter, input)).Ok, input);
            Assert.Equal(targetBefore, Component(await world.GetEntityAsync(Target), Budget));
        }
        var offTurnReaction = await SpendAsync(runner, "use my reaction", Target, Encounter, """{"resource":"reaction"}""");
        Assert.True(offTurnReaction.Ok, offTurnReaction.Error?.Why);
        Assert.False((await SpendAsync(runner, "use my reaction", Target, Encounter, """{"resource":"reaction"}""")).Ok);

        const string outsider = "fixture.catalog.f12.outsider";
        await world.CreateEntityAsync("Outsider", outsider);
        Assert.True((await runner.RunAsync(Request("record turn budget", outsider, Full("record", true, true, true, true, 30, 30)))).Ok);
        var outsiderBefore = Component(await world.GetEntityAsync(outsider), Budget);
        Assert.False((await SpendAsync(runner, "use my reaction", outsider, Encounter, """{"resource":"reaction"}""")).Ok);
        Assert.Equal(outsiderBefore, Component(await world.GetEntityAsync(outsider), Budget));

        var advanced = await runner.RunAsync(new ActionRequest
        {
            Intent = "advance encounter turn",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = "{}",
            Seed = 712
        });
        Assert.True(advanced.Ok, advanced.Error?.Why);
        AssertBudget(Component(await world.GetEntityAsync(Target), Budget), true, true, true, true, 30, 30);
        Assert.True((await SpendAsync(runner, "use my reaction", Target, Encounter, """{"resource":"reaction"}""")).Ok);

        Assert.True((await runner.RunAsync(new ActionRequest
        {
            Intent = "end combat turns",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter },
            Input = "{}",
            Seed = 712
        })).Ok);
        var endedBudget = Component(await world.GetEntityAsync(Target), Budget);
        Assert.False((await SpendAsync(runner, "spend my action", Target, Encounter, """{"resource":"action"}""")).Ok);
        Assert.Equal(endedBudget, Component(await world.GetEntityAsync(Target), Budget));
    }

    private static string Full(string mode, bool action, bool bonusAction, bool reaction, bool freeInteraction, int remaining, int maximum) =>
        JsonSerializer.Serialize(new { mode, action, bonusAction, reaction, freeInteraction, movementRemainingFeet = remaining });

    private static ActionRequest Request(string intent, string subject, string input) => new()
    {
        Intent = intent,
        Input = input,
        Seed = 712,
        RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject }
    };

    private static Task<ActionRunResult> SpendAsync(ActionRunner runner, string intent, string subject, string encounter, string input) =>
        runner.RunAsync(new ActionRequest
        {
            Intent = intent,
            Input = input,
            Seed = 712,
            RoleEntityIds = new Dictionary<string, string> { ["subject"] = subject, ["encounter"] = encounter }
        });

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static string Component(EntitySnapshot? entity, string definitionId) =>
        Assert.Single(entity!.Components, component => component.DefinitionId == definitionId).Data;

    private static void AssertBudget(string data, bool action, bool bonusAction, bool reaction, bool freeInteraction, int remaining, int maximum)
    {
        using var document = JsonDocument.Parse(data);
        var budget = document.RootElement;
        Assert.Equal(6, budget.EnumerateObject().Count());
        Assert.Equal(action, budget.GetProperty("action").GetBoolean());
        Assert.Equal(bonusAction, budget.GetProperty("bonusAction").GetBoolean());
        Assert.Equal(reaction, budget.GetProperty("reaction").GetBoolean());
        Assert.Equal(freeInteraction, budget.GetProperty("freeInteraction").GetBoolean());
        Assert.Equal(remaining, budget.GetProperty("movementRemainingFeet").GetInt32());
        Assert.False(budget.TryGetProperty("movementMaximumFeet", out _));
        Assert.Equal("source.dnd2024.srd-5.2.1", budget.GetProperty("sourceRef").GetProperty("sourceId").GetString());
    }

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json");
            if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!;
        }
        throw new DirectoryNotFoundException("Could not locate the repository catalog.");
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }
}
