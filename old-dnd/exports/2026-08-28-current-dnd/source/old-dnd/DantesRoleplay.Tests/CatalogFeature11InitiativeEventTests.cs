using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class CatalogFeature11InitiativeEventTests : IDisposable
{
    private const string Encounter = "encounter.dnd2024.feature-10.training", Hero = "creature.dnd2024.feature-10.hero", Target = "creature.dnd2024.feature-10.training-target";
    private readonly SqliteFixture _fixture = new();
    private readonly string _copy = Path.Combine(Path.GetTempPath(), $"feature-11-initiative-events-{Guid.NewGuid():n}");

    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_copy)) Directory.Delete(_copy, true); }

    [Fact]
    public async Task Recorded_initiative_order_emits_closed_events_in_final_order_and_failures_emit_none()
    {
        Copy(Catalog(), _copy);
        await using var db = _fixture.CreateContext(); var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        Assert.False((await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db)).ApplyAsync(_copy, new CatalogImportOptions())).Aborted);
        Assert.NotNull(await new EventTypeStore(db).GetAsync("dnd2024.initiative.rolled"));
        var runner = new ActionRunner(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world, null, new EventLedger(db)), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
        var result = await runner.RunAsync(new ActionRequest { Intent = "set the encounter initiative order", RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter }, Input = JsonSerializer.Serialize(new { participants = new Dictionary<string, object> { [Hero] = new { }, [Target] = new { } } }), Seed = 100 });
        Assert.True(result.Ok, result.Error?.Why); Assert.Equal(1, result.AppliedCount);
        var ledger = new EventLedger(db); var events = (await ledger.FindAsync(rootOperationId: result.OperationId)).Where(value => value.TypeId == "dnd2024.initiative.rolled").ToArray();
        Assert.Equal(2, events.Length);
        var order = JsonDocument.Parse((await world.GetEntityAsync(Encounter))!.Components.Single(value => value.DefinitionId == "dnd2024.encounter-initiative-order").Data).RootElement.GetProperty("order").EnumerateArray().Select(value => value.GetProperty("participantId").GetString()).ToArray();
        var details = new List<EventDetail>();
        foreach (var summary in events) details.Add((await ledger.GetAsync(summary.Id))!);
        Assert.Equal(order, details.Select(value => JsonDocument.Parse(value.PayloadJson).RootElement.GetProperty("subjectId").GetString()).ToArray());
        foreach (var detail in details)
        {
            using var payload = JsonDocument.Parse(detail.PayloadJson);
            Assert.Equal(new[] { payload.RootElement.GetProperty("subjectId").GetString()!, Encounter }, detail.EntityIds);
            Assert.Equal(Encounter, payload.RootElement.GetProperty("encounterId").GetString());
            Assert.Equal("source.dnd2024.srd-5.2.1", payload.RootElement.GetProperty("sourceRef").GetProperty("sourceId").GetString());
        }
        var repeat = await runner.RunAsync(new ActionRequest { Intent = "set the encounter initiative order", RoleEntityIds = new Dictionary<string, string> { ["encounter"] = Encounter }, Input = "{}", Seed = 100 });
        Assert.False(repeat.Ok); Assert.Empty(await ledger.FindAsync(rootOperationId: repeat.OperationId));
    }

    private static string Catalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) { var manifest = Path.Combine(directory.FullName, "catalog", "manifest.json"); if (File.Exists(manifest)) return Path.GetDirectoryName(manifest)!; } throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file))); }
}
