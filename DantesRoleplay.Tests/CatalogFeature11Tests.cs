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

/// <summary>Feature 11 Slice 1 starts an encounter from its immutable Initiative snapshot.</summary>
public sealed class CatalogFeature11Tests : IDisposable
{
    private const string Order = "dnd2024.encounter-initiative-order";
    private const string State = "dnd2024.encounter-turn-state";
    private const string Budget = "dnd2024.turn-budget";
    private const string Speed = "dnd2024.speed";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-11-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Imported_catalog_starts_a_valid_encounter_at_the_first_initiative_participant()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.mechanic.dnd2024.encounter-turn-lifecycle"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.encounter-turn.start"));

        var encounter = await CreateEncounterAsync(world, "fixture.catalog.f11.valid", ["alpha", "bravo"]);
        var runner = CreateRunner(db, world, mechanics);
        var beforeAlpha = await ComponentsAsync(world, encounter.Alpha);
        var beforeBravo = await ComponentsAsync(world, encounter.Bravo);

        var started = await runner.RunAsync(new ActionRequest
        {
            Intent = "start encounter turns",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = encounter.Id },
            Input = "{}",
            Seed = 711
        });

        Assert.True(started.Ok, started.Error?.Why);
        Assert.Equal("mechanic.dnd2024.encounter-turn.start", started.Mechanic?.Id);
        Assert.Equal(2, started.AppliedCount);
        var effect = Assert.Single(started.Output!.Effects, candidate => candidate.DefinitionId == State);
        Assert.Equal(EffectType.ComponentAdd, effect.Type);
        Assert.Equal(encounter.Id, effect.EntityId);
        AssertTurnState(effect.Data!, "active", 1, 0);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(started.Output.Effects, candidate => candidate.DefinitionId == Budget).Type);
        using (var resultData = JsonDocument.Parse(started.Output.Data))
        {
            Assert.Equal("encounter-turn-start", resultData.RootElement.GetProperty("test").GetString());
            Assert.Equal(encounter.Alpha, resultData.RootElement.GetProperty("activeParticipantId").GetString());
            Assert.Equal(2, resultData.RootElement.GetProperty("participantCount").GetInt32());
        }
        AssertTurnState(Component(await world.GetEntityAsync(encounter.Id), State), "active", 1, 0);
        Assert.Equal(beforeAlpha, await ComponentsAsync(world, encounter.Alpha));
        Assert.Equal(beforeBravo, await ComponentsAsync(world, encounter.Bravo));

        var repeat = await runner.RunAsync(new ActionRequest
        {
            Intent = "begin combat turns",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = encounter.Id },
            Input = "{}",
            Seed = 711
        });
        Assert.False(repeat.Ok);
        AssertTurnState(Component(await world.GetEntityAsync(encounter.Id), State), "active", 1, 0);

        var initiativePhrase = await runner.RunAsync(new ActionRequest
        {
            Intent = "start the encounter",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = encounter.Id },
            Input = JsonSerializer.Serialize(new { participants = new Dictionary<string, object> { [encounter.Alpha] = new { }, [encounter.Bravo] = new { } } }),
            Seed = 711
        });
        Assert.Equal("mechanic.dnd2024.encounter-initiative-order", initiativePhrase.Mechanic?.Id);
        Assert.False(initiativePhrase.Ok);
    }

    [Fact]
    public async Task Start_rejects_closed_input_and_drifted_or_corrupt_snapshots_without_writing_state()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);

        var valid = await CreateEncounterAsync(world, "fixture.catalog.f11.closed", ["alpha"]);
        foreach (var input in new[] { "{\"round\":1}", "{\"status\":\"active\"}", "{\"effects\":[]}" })
        {
            var rejected = await runner.RunAsync(new ActionRequest
            {
                Intent = "start encounter turns",
                RoleEntityIds = new Dictionary<string, string> { ["encounter"] = valid.Id },
                Input = input,
                Seed = 711
            });
            Assert.False(rejected.Ok, input);
            Assert.DoesNotContain((await world.GetEntityAsync(valid.Id))!.Components, component => component.DefinitionId == State);
        }

        var drifted = await CreateEncounterAsync(world, "fixture.catalog.f11.drifted", ["alpha", "bravo"]);
        await world.MoveAsync(drifted.Bravo, null);
        var rosterRejected = await runner.RunAsync(new ActionRequest
        {
            Intent = "start encounter turns",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = drifted.Id },
            Input = "{}",
            Seed = 711
        });
        Assert.False(rosterRejected.Ok);
        Assert.DoesNotContain((await world.GetEntityAsync(drifted.Id))!.Components, component => component.DefinitionId == State);

        var corrupt = await CreateEncounterAsync(world, "fixture.catalog.f11.corrupt", ["alpha"]);
        await world.SetComponentAsync(corrupt.Id, Order, """{"order":[{"participantId":"fixture.catalog.f11.corrupt.alpha","initiative":4},{"participantId":"fixture.catalog.f11.corrupt.alpha","initiative":3}],"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Combat > The Order of Combat > Initiative"}}""");
        var corruptRejected = await runner.RunAsync(new ActionRequest
        {
            Intent = "start encounter turns",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = corrupt.Id },
            Input = "{}",
            Seed = 711
        });
        Assert.False(corruptRejected.Ok);
        Assert.DoesNotContain((await world.GetEntityAsync(corrupt.Id))!.Components, component => component.DefinitionId == State);
    }

    [Fact]
    public async Task Imported_catalog_advances_one_turn_and_wraps_only_after_the_final_participant()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.encounter-turn.advance"));
        var runner = CreateRunner(db, world, mechanics);

        var encounter = await CreateEncounterAsync(world, "fixture.catalog.f11.advance", ["alpha", "bravo"]);
        Assert.True((await StartAsync(runner, encounter.Id)).Ok);
        var orderBefore = Component(await world.GetEntityAsync(encounter.Id), Order);

        var next = await runner.RunAsync(new ActionRequest
        {
            Intent = "advance encounter turn",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = encounter.Id },
            Input = "{}",
            Seed = 711
        });
        Assert.True(next.Ok, next.Error?.Why);
        Assert.Equal("mechanic.dnd2024.encounter-turn.advance", next.Mechanic?.Id);
        Assert.Equal(2, next.AppliedCount);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(next.Output!.Effects, candidate => candidate.DefinitionId == State).Type);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(next.Output.Effects, candidate => candidate.DefinitionId == Budget).Type);
        AssertTurnState(Component(await world.GetEntityAsync(encounter.Id), State), "active", 1, 1);
        using (var data = JsonDocument.Parse(next.Output.Data))
        {
            Assert.Equal(encounter.Alpha, data.RootElement.GetProperty("previousParticipantId").GetString());
            Assert.Equal(encounter.Bravo, data.RootElement.GetProperty("activeParticipantId").GetString());
            Assert.False(data.RootElement.GetProperty("startedNewRound").GetBoolean());
        }

        var wrapped = await runner.RunAsync(new ActionRequest
        {
            Intent = "next encounter turn",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = encounter.Id },
            Input = "{}",
            Seed = 711
        });
        Assert.True(wrapped.Ok, wrapped.Error?.Why);
        AssertTurnState(Component(await world.GetEntityAsync(encounter.Id), State), "active", 2, 0);
        using (var data = JsonDocument.Parse(wrapped.Output!.Data))
        {
            Assert.Equal(encounter.Bravo, data.RootElement.GetProperty("previousParticipantId").GetString());
            Assert.Equal(encounter.Alpha, data.RootElement.GetProperty("activeParticipantId").GetString());
            Assert.True(data.RootElement.GetProperty("startedNewRound").GetBoolean());
        }
        Assert.Equal(orderBefore, Component(await world.GetEntityAsync(encounter.Id), Order));

        var single = await CreateEncounterAsync(world, "fixture.catalog.f11.single", ["alpha"]);
        Assert.True((await StartAsync(runner, single.Id)).Ok);
        var singleAdvance = await runner.RunAsync(new ActionRequest
        {
            Intent = "advance combat turn",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = single.Id },
            Input = "{}",
            Seed = 711
        });
        Assert.True(singleAdvance.Ok, singleAdvance.Error?.Why);
        AssertTurnState(Component(await world.GetEntityAsync(single.Id), State), "active", 2, 0);
    }

    [Fact]
    public async Task Advance_rejects_missing_or_invalid_lifecycle_state_without_mutating_the_order()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        var runner = CreateRunner(db, world, mechanics);
        var encounter = await CreateEncounterAsync(world, "fixture.catalog.f11.advance-reject", ["alpha"]);
        var orderBefore = Component(await world.GetEntityAsync(encounter.Id), Order);

        var missing = await runner.RunAsync(new ActionRequest
        {
            Intent = "advance encounter turn",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = encounter.Id },
            Input = "{}",
            Seed = 711
        });
        Assert.False(missing.Ok);
        Assert.Equal(orderBefore, Component(await world.GetEntityAsync(encounter.Id), Order));

        Assert.True((await StartAsync(runner, encounter.Id)).Ok);
        var stateBefore = Component(await world.GetEntityAsync(encounter.Id), State);
        foreach (var input in new[] { "{\"round\":2}", "{\"turnIndex\":0}" })
        {
            var rejected = await runner.RunAsync(new ActionRequest
            {
                Intent = "advance encounter turn",
                RoleEntityIds = new Dictionary<string, string> { ["encounter"] = encounter.Id },
                Input = input,
                Seed = 711
            });
            Assert.False(rejected.Ok, input);
            Assert.Equal(stateBefore, Component(await world.GetEntityAsync(encounter.Id), State));
        }

        await world.SetComponentAsync(encounter.Id, State, """{"status":"ended","round":1,"turnIndex":0,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Combat > The Order of Combat"}}""");
        var ended = await runner.RunAsync(new ActionRequest
        {
            Intent = "advance encounter turn",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = encounter.Id },
            Input = "{}",
            Seed = 711
        });
        Assert.False(ended.Ok);
        Assert.Equal(orderBefore, Component(await world.GetEntityAsync(encounter.Id), Order));
    }

    [Fact]
    public async Task Imported_catalog_explicitly_ends_an_active_encounter_once_without_changing_its_history()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.encounter-turn.end"));
        var runner = CreateRunner(db, world, mechanics);
        var encounter = await CreateEncounterAsync(world, "fixture.catalog.f11.end", ["alpha", "bravo"]);
        Assert.True((await StartAsync(runner, encounter.Id)).Ok);
        Assert.True((await runner.RunAsync(new ActionRequest
        {
            Intent = "advance encounter turn",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = encounter.Id },
            Input = "{}",
            Seed = 711
        })).Ok);
        var orderBefore = Component(await world.GetEntityAsync(encounter.Id), Order);

        var ended = await runner.RunAsync(new ActionRequest
        {
            Intent = "end encounter turns",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = encounter.Id },
            Input = "{}",
            Seed = 711
        });
        Assert.True(ended.Ok, ended.Error?.Why);
        Assert.Equal("mechanic.dnd2024.encounter-turn.end", ended.Mechanic?.Id);
        Assert.Equal(1, ended.AppliedCount);
        Assert.Equal(EffectType.ComponentSet, Assert.Single(ended.Output!.Effects).Type);
        AssertTurnState(Component(await world.GetEntityAsync(encounter.Id), State), "ended", 1, 1);
        Assert.Equal(orderBefore, Component(await world.GetEntityAsync(encounter.Id), Order));
        using (var data = JsonDocument.Parse(ended.Output.Data))
        {
            Assert.Equal("encounter-turn-end", data.RootElement.GetProperty("test").GetString());
            Assert.Equal(encounter.Bravo, data.RootElement.GetProperty("finalParticipantId").GetString());
            Assert.False(data.RootElement.TryGetProperty("activeParticipantId", out _));
        }

        foreach (var intent in new[] { "end combat turns", "advance encounter turn", "start encounter turns" })
        {
            var rejected = await runner.RunAsync(new ActionRequest
            {
                Intent = intent,
                RoleEntityIds = new Dictionary<string, string> { ["encounter"] = encounter.Id },
                Input = "{}",
                Seed = 711
            });
            Assert.False(rejected.Ok, intent);
            AssertTurnState(Component(await world.GetEntityAsync(encounter.Id), State), "ended", 1, 1);
            Assert.Equal(orderBefore, Component(await world.GetEntityAsync(encounter.Id), Order));
        }
    }

    private static async Task<Encounter> CreateEncounterAsync(WorldStore world, string prefix, string[] order)
    {
        var encounter = $"{prefix}.encounter";
        await world.CreateEntityAsync("Feature 11 encounter", encounter);
        var entries = new List<object>();
        var ids = new List<string>();
        for (var index = 0; index < order.Length; index++)
        {
            var id = $"{prefix}.{order[index]}";
            ids.Add(id);
            await world.CreateEntityAsync(order[index], id);
            await world.MoveAsync(id, encounter, "participant");
            await world.SetComponentAsync(id, Speed, SpeedJson());
            await world.SetComponentAsync(id, Budget, TurnBudgetJson());
            entries.Add(new { participantId = id, initiative = 20 - index });
        }
        await world.SetComponentAsync(encounter, Order, JsonSerializer.Serialize(new
        {
            order = entries,
            sourceRef = new { sourceId = "source.dnd2024.srd-5.2.1", locator = "Playing the Game > Combat > The Order of Combat > Initiative" }
        }));
        return new Encounter(encounter, ids[0], ids.Count > 1 ? ids[1] : string.Empty);
    }

    private static Task<ActionRunResult> StartAsync(ActionRunner runner, string encounterId) => runner.RunAsync(new ActionRequest
    {
        Intent = "start encounter turns",
        RoleEntityIds = new Dictionary<string, string> { ["encounter"] = encounterId },
        Input = "{}",
        Seed = 711
    });

    private static string SpeedJson() => """{"walkFeet":30,"burrowFeet":0,"climbFeet":0,"flyFeet":0,"swimFeet":0,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Rules Glossary > Speed"}}""";
    private static string TurnBudgetJson() => """{"action":true,"bonusAction":true,"reaction":true,"freeInteraction":true,"movementRemainingFeet":30,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Actions; Bonus Actions; Reactions; Interacting with Objects; Combat > Your Turn"}}""";

    private static ActionRunner CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static async Task<Dictionary<string, string>> ComponentsAsync(WorldStore world, string entityId) =>
        (await world.GetEntityAsync(entityId))!.Components.ToDictionary(component => component.DefinitionId, component => component.Data, StringComparer.Ordinal);

    private static string Component(EntitySnapshot? entity, string definitionId) =>
        Assert.Single(entity!.Components, component => component.DefinitionId == definitionId).Data;

    private static void AssertTurnState(string data, string status, int round, int turnIndex)
    {
        using var document = JsonDocument.Parse(data);
        var state = document.RootElement;
        Assert.Equal(status, state.GetProperty("status").GetString());
        Assert.Equal(round, state.GetProperty("round").GetInt32());
        Assert.Equal(turnIndex, state.GetProperty("turnIndex").GetInt32());
        Assert.Equal("source.dnd2024.srd-5.2.1", state.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        Assert.Equal("Playing the Game > Combat > The Order of Combat", state.GetProperty("sourceRef").GetProperty("locator").GetString());
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

    private sealed record Encounter(string Id, string Alpha, string Bravo);
}
