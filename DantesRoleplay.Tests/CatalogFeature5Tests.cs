using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

/// <summary>
/// Feature 5 is authored in the catalog, rather than through a one-off live write. This is its
/// integration gate: import those exact files into a new database and use the imported parent and
/// child mechanics to write one encounter-owned Initiative-order snapshot.
/// </summary>
public sealed class CatalogFeature5Tests : IDisposable
{
    private const string EncounterOrder = "dnd2024.encounter-initiative-order";
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"feature-5-catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();

        if (Directory.Exists(_catalogCopy))
        {
            Directory.Delete(_catalogCopy, recursive: true);
        }
    }

    [Fact]
    public async Task Imported_catalog_runs_the_feature_5_encounter_order_parent()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);

        var imported = await new CatalogImporter(
            db,
            mechanics,
            new ProcedureStore(db),
            world).ApplyAsync(_catalogCopy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.initiative.roll"));
        Assert.NotNull(await mechanics.GetAsync("mechanic.dnd2024.encounter-initiative-order"));

        await world.CreateEntityAsync("Feature 5 encounter", "fixture.catalog.f5.encounter");
        await CreateParticipantAsync(world, "Alpha", "fixture.catalog.f5.alpha", dexterity: 16);
        await CreateParticipantAsync(world, "Bravo", "fixture.catalog.f5.bravo", dexterity: 10);
        await CreateParticipantAsync(world, "Charlie", "fixture.catalog.f5.charlie", dexterity: 10);

        foreach (var participant in new[]
                 {
                     "fixture.catalog.f5.alpha",
                     "fixture.catalog.f5.bravo",
                     "fixture.catalog.f5.charlie"
                 })
        {
            await world.MoveAsync(participant, "fixture.catalog.f5.encounter", "participant");
        }

        var result = await CreateRunner(db, world, mechanics).RunAsync(new ActionRequest
        {
            Intent = "set the encounter initiative order",
            RoleEntityIds = new Dictionary<string, string>
            {
                ["encounter"] = "fixture.catalog.f5.encounter"
            },
            Input = """
                {
                  "participants": {
                    "fixture.catalog.f5.alpha": {},
                    "fixture.catalog.f5.bravo": {},
                    "fixture.catalog.f5.charlie": {}
                  },
                  "tieDecisions": [["fixture.catalog.f5.bravo", "fixture.catalog.f5.charlie"]]
                }
                """,
            Seed = 108
        });

        Assert.True(result.Ok, result.Error?.Why);
        Assert.Equal("mechanic.dnd2024.encounter-initiative-order", result.Mechanic?.Id);
        Assert.Equal(1, result.AppliedCount);
        Assert.DoesNotContain(
            result.Output!.Effects,
            effect => effect.EntityId != "fixture.catalog.f5.encounter");

        var encounter = await world.GetEntityAsync("fixture.catalog.f5.encounter");
        var snapshot = Assert.Single(
            encounter!.Components,
            component => component.DefinitionId == EncounterOrder);

        using var document = JsonDocument.Parse(snapshot.Data);
        var order = document.RootElement.GetProperty("order").EnumerateArray().ToList();
        Assert.Collection(
            order,
            entry => AssertParticipant(entry, "fixture.catalog.f5.bravo", 18),
            entry => AssertParticipant(entry, "fixture.catalog.f5.charlie", 18),
            entry => AssertParticipant(entry, "fixture.catalog.f5.alpha", 15));

        Assert.Equal("source.dnd2024.srd-5.2.1", document.RootElement
            .GetProperty("sourceRef")
            .GetProperty("sourceId")
            .GetString());

        foreach (var participant in new[]
                 {
                     "fixture.catalog.f5.alpha",
                     "fixture.catalog.f5.bravo",
                     "fixture.catalog.f5.charlie"
                 })
        {
            var entity = await world.GetEntityAsync(participant);
            Assert.DoesNotContain(entity!.Components, component => component.DefinitionId == EncounterOrder);
        }
    }

    [Fact]
    public async Task Imported_catalog_handles_slice_2_ties_disadvantage_replay_and_rejection()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var importer = new CatalogImporter(db, mechanics, new ProcedureStore(db), world);
        var imported = await importer.ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        var runner = CreateRunner(db, world, mechanics);

        // The SRD leaves tied combatants to the players/GM. The same roll results with the
        // authorised choice reversed must therefore reverse only that tied group.
        var reversedTie = await CreateEncounterAsync(world, "fixture.catalog.f5.reverse");
        var reverseResult = await runner.RunAsync(Request(
            reversedTie,
            Input(reversedTie, [[reversedTie.Charlie, reversedTie.Bravo]]),
            seed: 108));
        Assert.True(reverseResult.Ok, reverseResult.Error?.Why);
        await AssertOrderAsync(world, reversedTie.Encounter,
            (reversedTie.Charlie, 18), (reversedTie.Bravo, 18), (reversedTie.Alpha, 15));

        // An untied seed needs no decision, and the parent must derive the order rather than
        // accepting it from input.
        var untied = await CreateEncounterAsync(world, "fixture.catalog.f5.untied");
        var untiedResult = await runner.RunAsync(Request(untied, Input(untied), seed: 100));
        Assert.True(untiedResult.Ok, untiedResult.Error?.Why);
        await AssertOrderAsync(world, untied.Encounter,
            (untied.Alpha, 7), (untied.Charlie, 4), (untied.Bravo, 1));

        // Circumstances are forwarded to each child separately; only Alpha receives Surprise.
        var disadvantaged = await CreateEncounterAsync(world, "fixture.catalog.f5.disadvantage");
        var disadvantageResult = await runner.RunAsync(Request(
            disadvantaged,
            Input(disadvantaged, alphaDisadvantage: true),
            seed: 100));
        Assert.True(disadvantageResult.Ok, disadvantageResult.Error?.Why);
        var alphaChild = disadvantageResult.Projection!.Children["initiative"]
            .Single(child => child.RoleEntityIds["subject"] == disadvantaged.Alpha);
        using (var childData = JsonDocument.Parse(alphaChild.Output.Data))
        {
            Assert.Equal("disadvantage", childData.RootElement.GetProperty("rollMode").GetString());
            Assert.Equal(2, childData.RootElement.GetProperty("rolls").GetArrayLength());
        }

        // A recorded order is immutable for the encounter. A second run cannot quietly replace it.
        var repeat = await runner.RunAsync(Request(untied, Input(untied), seed: 100));
        Assert.False(repeat.Ok);
        Assert.Equal("MECHANIC_FAILED", repeat.Error?.Code);
        await AssertOrderAsync(world, untied.Encounter,
            (untied.Alpha, 7), (untied.Charlie, 4), (untied.Bravo, 1));

        // Remove the disposable snapshot through the ordinary effect path, then replay the same
        // parent seed and inputs. The exact serialised snapshot must reproduce.
        var original = await SnapshotAsync(world, untied.Encounter);
        var removal = new Effect
        {
            Type = EffectType.ComponentRemove,
            EntityId = untied.Encounter,
            DefinitionId = EncounterOrder
        };
        var applier = new EffectApplier(db, world);
        Assert.True((await applier.ApplyAsync([removal], dryRun: true)).Valid);
        Assert.True((await applier.ApplyAsync([removal])).Applied);

        var replay = await runner.RunAsync(Request(untied, Input(untied), seed: 100));
        Assert.True(replay.Ok, replay.Error?.Why);
        Assert.Equal(original, await SnapshotAsync(world, untied.Encounter));

        // Parent-only and incomplete inputs cannot create an order, including before a valid
        // order has ever existed on an encounter.
        await world.CreateEntityAsync("Empty Feature 5 encounter", "fixture.catalog.f5.empty");
        var empty = await runner.RunAsync(new ActionRequest
        {
            Intent = "set the encounter initiative order",
            RoleEntityIds = new Dictionary<string, string> { ["encounter"] = "fixture.catalog.f5.empty" },
            Input = """{"participants":{}}""",
            Seed = 1
        });
        Assert.False(empty.Ok);
        Assert.Equal("MECHANIC_FAILED", empty.Error?.Code);
        Assert.DoesNotContain(
            (await world.GetEntityAsync("fixture.catalog.f5.empty"))!.Components,
            component => component.DefinitionId == EncounterOrder);

        var missingTie = await CreateEncounterAsync(world, "fixture.catalog.f5.missing-tie");
        var tieRequired = await runner.RunAsync(Request(missingTie, Input(missingTie), seed: 108));
        Assert.False(tieRequired.Ok);
        Assert.Equal("MECHANIC_FAILED", tieRequired.Error?.Code);
        Assert.DoesNotContain(
            (await world.GetEntityAsync(missingTie.Encounter))!.Components,
            component => component.DefinitionId == EncounterOrder);

        var rejected = await CreateEncounterAsync(world, "fixture.catalog.f5.rejected");
        var extraKey = await runner.RunAsync(Request(
            rejected,
            """{"participants":{},"round":1}""",
            seed: 1));
        Assert.False(extraKey.Ok);
        Assert.Null((await world.GetEntityAsync(rejected.Encounter))!.Components
            .SingleOrDefault(component => component.DefinitionId == EncounterOrder));

        var missingInput = await runner.RunAsync(Request(
            rejected,
            """{"participants":{"fixture.catalog.f5.rejected.alpha":{}}}""",
            seed: 1));
        Assert.False(missingInput.Ok);
        Assert.Equal("COMPOSITION_FAILED", missingInput.Error?.Code);
        Assert.Null((await world.GetEntityAsync(rejected.Encounter))!.Components
            .SingleOrDefault(component => component.DefinitionId == EncounterOrder));
    }

    private static async Task CreateParticipantAsync(WorldStore world, string name, string id, int dexterity)
    {
        await world.CreateEntityAsync(name, id);
        await world.SetComponentAsync(
            id,
            "dnd2024.abilities",
            $$"""{"str":10,"dex":{{dexterity}},"con":10,"int":10,"wis":10,"cha":10}""");
    }

    private static async Task<EncounterFixture> CreateEncounterAsync(WorldStore world, string prefix)
    {
        var encounter = prefix + ".encounter";
        var alpha = prefix + ".alpha";
        var bravo = prefix + ".bravo";
        var charlie = prefix + ".charlie";

        await world.CreateEntityAsync("Feature 5 encounter", encounter);
        await CreateParticipantAsync(world, "Alpha", alpha, dexterity: 16);
        await CreateParticipantAsync(world, "Bravo", bravo, dexterity: 10);
        await CreateParticipantAsync(world, "Charlie", charlie, dexterity: 10);

        foreach (var participant in new[] { alpha, bravo, charlie })
        {
            await world.MoveAsync(participant, encounter, "participant");
        }

        return new EncounterFixture(encounter, alpha, bravo, charlie);
    }

    private static ActionRequest Request(EncounterFixture fixture, string input, long seed) => new()
    {
        Intent = "set the encounter initiative order",
        RoleEntityIds = new Dictionary<string, string> { ["encounter"] = fixture.Encounter },
        Input = input,
        Seed = seed
    };

    private static string Input(
        EncounterFixture fixture,
        IReadOnlyList<string[]>? tieDecisions = null,
        bool alphaDisadvantage = false)
    {
        var participants = new Dictionary<string, object>
        {
            [fixture.Alpha] = alphaDisadvantage
                ? new { rollCircumstances = new[] { new { kind = "disadvantage", source = "surprised" } } }
                : new { },
            [fixture.Bravo] = new { },
            [fixture.Charlie] = new { }
        };

        var input = new Dictionary<string, object> { ["participants"] = participants };
        if (tieDecisions is not null)
        {
            input["tieDecisions"] = tieDecisions;
        }

        return JsonSerializer.Serialize(input);
    }

    private static async Task AssertOrderAsync(
        WorldStore world,
        string encounterId,
        params (string Id, int Initiative)[] expected)
    {
        using var document = JsonDocument.Parse(await SnapshotAsync(world, encounterId));
        var actual = document.RootElement.GetProperty("order").EnumerateArray().ToList();

        Assert.Equal(expected.Length, actual.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            AssertParticipant(actual[index], expected[index].Id, expected[index].Initiative);
        }
    }

    private static async Task<string> SnapshotAsync(WorldStore world, string encounterId)
    {
        var encounter = await world.GetEntityAsync(encounterId);
        return Assert.Single(encounter!.Components, component => component.DefinitionId == EncounterOrder).Data;
    }

    private static ActionRunner CreateRunner(
        DantesRoleplayDbContext db,
        WorldStore world,
        MechanicStore mechanics) =>
        new(
            db,
            mechanics,
            new ProjectionResolver(db),
            new JintMechanicEngine(),
            new EffectApplier(db, world),
            new OperationLog(db),
            new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static void AssertParticipant(JsonElement entry, string id, int initiative)
    {
        Assert.Equal(id, entry.GetProperty("participantId").GetString());
        Assert.Equal(initiative, entry.GetProperty("initiative").GetInt32());
    }

    private static string RepositoryCatalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var catalog = Path.Combine(directory.FullName, "catalog", "manifest.json");
            if (File.Exists(catalog))
            {
                return Path.GetDirectoryName(catalog)!;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository catalog.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
        }
    }

    private sealed record EncounterFixture(string Encounter, string Alpha, string Bravo, string Charlie);
}
