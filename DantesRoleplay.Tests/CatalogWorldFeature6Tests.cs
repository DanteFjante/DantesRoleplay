using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class CatalogWorldFeature6Tests : IDisposable
{
    private readonly SqliteFixture _fixture=new(); private readonly string _copy=Path.Combine(Path.GetTempPath(),$"world-feature-06-{Guid.NewGuid():n}");
    public void Dispose(){_fixture.Dispose();if(Directory.Exists(_copy))Directory.Delete(_copy,true);}
    [Fact]
    public async Task Agenda_advance_reveals_the_fixed_clue_once_in_the_same_root_chain()
    {
        Copy(Catalog(), _copy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db)).ApplyAsync(_copy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        var runner = CreateRunner(db, world, mechanics);
        var action = await AdvanceAsync(runner);

        Assert.True(action.Ok, action.Error?.Why);
        Assert.Equal("advanced", AgendaState((await world.GetEntityAsync("faction.feature-03.fixture"))!));
        Assert.Equal("revealed", State((await world.GetEntityAsync("clue.feature-04.oren-letter"))!, "game.core.world.clue"));
        Assert.Equal("party", Visibility((await world.GetEntityAsync("clue.feature-04.oren-letter"))!, "game.core.world.clue"));

        var events = await new EventLedger(db).FindAsync(rootOperationId: action.OperationId);
        Assert.Equal(2, events.Count);
        Assert.Equal(new[] { 0, 1 }, events.Select(e => e.Sequence));
        Assert.Equal(new[] { 0, 1 }, events.Select(e => e.Depth));
        Assert.All(events, e => Assert.Equal("world.component.replaced", e.TypeId));
        Assert.Equal(events[0].Id, events[1].CausationId);
        var execution = Assert.Single(await db.EventExecutions.AsNoTracking().ToListAsync());
        Assert.Equal("subscription.game.core.world.clue.reveal-on-faction-agenda", execution.SubscriptionId);
        Assert.Equal(1, execution.EffectCount);

        var repeat = await AdvanceAsync(runner);
        Assert.False(repeat.Ok);
        Assert.Equal("revealed", State((await world.GetEntityAsync("clue.feature-04.oren-letter"))!, "game.core.world.clue"));
        Assert.Equal(2, (await new EventLedger(db).FindAsync(rootOperationId: action.OperationId)).Count);
        Assert.Single(await db.EventExecutions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Fresh_import_binds_a_catalog_fixed_role_and_rolls_back_a_missing_target()
    {
        Copy(Catalog(), _copy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var importer = new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db));

        var imported = await importer.ApplyAsync(_copy, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        var subscription = await new SubscriptionStore(db).GetAsync("subscription.game.core.world.clue.reveal-on-faction-agenda");
        Assert.Equal("{\"clue\":\"clue.feature-04.oren-letter\"}", subscription!.FixedRoleEntityIdsJson);

        var invalidCopy = Path.Combine(Path.GetTempPath(), $"world-feature-06-missing-{Guid.NewGuid():n}");
        try
        {
            Copy(Catalog(), invalidCopy);
            var path = CatalogLayout.ToFileSystemPath(invalidCopy,
                CatalogLayout.Subscription("subscription.game.core.world.clue.reveal-on-faction-agenda"));
            await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("clue.feature-04.oren-letter", "clue.feature-04.missing", StringComparison.Ordinal));
            using var invalidFixture = new SqliteFixture();
            await using var invalidDb = invalidFixture.CreateContext();
            var invalidImporter = new CatalogImporter(invalidDb, new MechanicStore(invalidDb), new ProcedureStore(invalidDb), new WorldStore(invalidDb), new EventTypeStore(invalidDb), new SubscriptionStore(invalidDb));

            var error = await Assert.ThrowsAsync<ArgumentException>(() => invalidImporter.ApplyAsync(invalidCopy, new CatalogImportOptions()));

            Assert.Equal("Missing entities: clue.feature-04.missing.", error.Message);
            Assert.False(await new SubscriptionStore(invalidDb).ExistsAsync("subscription.game.core.world.clue.reveal-on-faction-agenda"));
        }
        finally
        {
            if (Directory.Exists(invalidCopy)) Directory.Delete(invalidCopy, true);
        }
    }

    [Fact]
    public async Task A_wrong_component_event_does_not_route_the_fixture_reaction()
    {
        Copy(Catalog(), _copy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);

        var result = await new CatalogMechanicTestHarness(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(),
            new EffectApplier(db, world,
                new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)),
                new EventLedger(db),
                new EventRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db))),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()))
            .RunAsync(new ActionRequest
            {
                Intent = "advance world time",
                RoleEntityIds = new Dictionary<string, string> { ["world"] = "world.feature-01.fixture" },
                Input = "{\"minutes\":1}",
                Seed = 607
            });

        Assert.True(result.Ok, result.Error?.Why);
        Assert.Equal("unrevealed", State((await world.GetEntityAsync("clue.feature-04.oren-letter"))!, "game.core.world.clue"));
        var executions = await db.EventExecutions.AsNoTracking().ToListAsync();
        Assert.DoesNotContain(executions, execution => execution.SubscriptionId == "subscription.game.core.world.clue.reveal-on-faction-agenda");
        var closure = Assert.Single(executions);
        Assert.Equal("subscription.game.core.world.condition.sync-route-closure", closure.SubscriptionId);
        Assert.Equal(0, closure.EffectCount);
    }

    [Fact]
    public async Task An_already_revealed_clue_is_not_replaced_again()
    {
        Copy(Catalog(), _copy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        await world.SetComponentAsync("clue.feature-04.oren-letter", "game.core.world.clue", """{"status":"revealed","summary":"An unsent letter names the observatory and asks Oren to keep a family promise.","provenance":"A folded letter hidden among Oren's papers.","visibility":"party"}""");

        var result = await AdvanceAsync(CreateRunner(db, world, mechanics));

        Assert.True(result.Ok, result.Error?.Why);
        Assert.Equal("revealed", State((await world.GetEntityAsync("clue.feature-04.oren-letter"))!, "game.core.world.clue"));
        Assert.Equal("party", Visibility((await world.GetEntityAsync("clue.feature-04.oren-letter"))!, "game.core.world.clue"));
        Assert.Single(await new EventLedger(db).FindAsync(rootOperationId: result.OperationId));
        Assert.Equal(0, Assert.Single(await db.EventExecutions.AsNoTracking().ToListAsync()).EffectCount);
    }

    [Fact]
    public async Task A_corrupt_fixed_clue_rolls_back_the_source_agenda_advance()
    {
        Copy(Catalog(), _copy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        await world.SetComponentAsync("clue.feature-04.oren-letter", "game.core.world.clue", """{"status":"unrevealed","summary":"An unsent letter names the observatory and asks Oren to keep a family promise.","provenance":"A folded letter hidden among Oren's papers.","visibility":"party"}""");

        var result = await AdvanceAsync(CreateRunner(db, world, mechanics));

        Assert.False(result.Ok);
        Assert.Equal("ready", AgendaState((await world.GetEntityAsync("faction.feature-03.fixture"))!));
        Assert.Empty(await db.EventExecutions.AsNoTracking().ToListAsync());
        Assert.Empty(await new EventLedger(db).FindAsync(rootOperationId: result.OperationId));
    }
    private static CatalogMechanicTestHarness CreateRunner(DantesRoleplayDbContext db, WorldStore world, MechanicStore mechanics) =>
        new(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(),
            new EffectApplier(db, world,
                new GuardRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db)),
                new EventLedger(db),
                new EventRouter(db, new MechanicStore(db), new ProjectionResolver(db), new JintMechanicEngine(), new WorldStore(db))),
            new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));

    private static Task<ActionRunResult> AdvanceAsync(CatalogMechanicTestHarness runner) => runner.RunAsync(new ActionRequest
    {
        Intent = "advance faction agenda",
        RoleEntityIds = new Dictionary<string, string> { ["faction"] = "faction.feature-03.fixture" },
        Input = "{}",
        Seed = 606
    });

    private static string State(EntitySnapshot entity, string definitionId)
    {
        using var document = System.Text.Json.JsonDocument.Parse(entity.Components.Single(x => x.DefinitionId == definitionId).Data);
        return document.RootElement.GetProperty("status").GetString()!;
    }

    private static string Visibility(EntitySnapshot entity, string definitionId)
    {
        using var document = System.Text.Json.JsonDocument.Parse(entity.Components.Single(x => x.DefinitionId == definitionId).Data);
        return document.RootElement.GetProperty("visibility").GetString()!;
    }

    private static string AgendaState(EntitySnapshot faction)
    {
        using var document = System.Text.Json.JsonDocument.Parse(faction.Components.Single(x => x.DefinitionId == "game.core.world.faction").Data);
        return document.RootElement.GetProperty("agenda").GetProperty("state").GetString()!;
    }
    private static string Catalog(){for(var d=new DirectoryInfo(AppContext.BaseDirectory);d is not null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"DantesRoleplay.slnx")))return Path.Combine(d.FullName,"catalog");throw new DirectoryNotFoundException();}
    private static void Copy(string s,string t){Directory.CreateDirectory(t);foreach(var d in Directory.EnumerateDirectories(s,"*",SearchOption.AllDirectories))Directory.CreateDirectory(Path.Combine(t,Path.GetRelativePath(s,d)));foreach(var f in Directory.EnumerateFiles(s,"*",SearchOption.AllDirectories))File.Copy(f,Path.Combine(t,Path.GetRelativePath(s,f)));
        WorldFeatureFixture.RestoreRelationships(s, t);
    }
}
