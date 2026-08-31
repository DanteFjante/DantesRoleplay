using DantesRoleplay.DataAccess;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.Tests;

/// <summary>
/// The join. Everything else in this solution is one half or the other; these tests are the seam
/// where AI-written JavaScript changes the world, and they are the closest thing to a proof that
/// the premise of the whole project works.
///
/// The chain is: a rule nobody wrote in C# → a sandbox that cannot reach anything → proposed
/// effects → validation → one transaction. No step in it knows what a game is.
/// </summary>
public sealed class MechanicToWorldTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly JintMechanicEngine Engine = new();

    [Fact]
    public async Task A_rule_written_in_javascript_changes_the_world_and_nothing_in_csharp_knows_what_it_did()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var applier = new EffectApplier(db, world);

        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");
        await world.CreateEntityAsync("Orban", "orban");
        await world.SetComponentAsync("orban", "stats", """{"vigour":10,"resolve":4}""");

        // A rule authored at runtime. There is no C# anywhere that knows what "vigour" is, that
        // spending it is a thing, or that four is the cost — that is the entire point.
        const string source = """
            var stats = JSON.parse(ctx.roles.subject.components.stats);
            var cost = ctx.input.cost;

            if (stats.vigour < cost) {
              return { narration: ctx.roles.subject.name + ' is too spent.', effects: [] };
            }

            ctx.log('vigour ' + stats.vigour + ' minus ' + cost);

            return {
              narration: ctx.roles.subject.name + ' pushes on.',
              effects: [{
                type: 'component.merge',
                entityId: ctx.roles.subject.id,
                definitionId: 'stats',
                data: JSON.stringify({ vigour: stats.vigour - cost })
              }]
            };
            """;

        var snapshot = await world.GetEntityAsync("orban");
        Assert.NotNull(snapshot);

        var projection = new MechanicProjection
        {
            Seed = 42,
            Input = """{"cost":4}""",
            Roles =
            {
                ["subject"] = new EntityProjection(
                    snapshot.Id,
                    snapshot.Name,
                    snapshot.Components.ToDictionary(c => c.DefinitionId, c => c.Data))
            }
        };

        var run = await Engine.RunAsync(source, projection, ExecutionLimits.Default);

        Assert.True(run.Ok, run.Error);
        Assert.Equal("Orban pushes on.", run.Output.Narration);
        Assert.Contains("vigour 10 minus 4", run.Log);

        var applied = await applier.ApplyAsync(run.Output.Effects);

        Assert.True(applied.Valid, string.Join("; ", applied.Problems.Select(p => p.Problem)));
        Assert.Equal(1, applied.Count);

        var after = await world.GetEntityAsync("orban");
        Assert.NotNull(after);

        // merge, not set: the rule sent only vigour, and resolve survived.
        Assert.Contains("\"vigour\":6", after.Components[0].Data);
        Assert.Contains("\"resolve\":4", after.Components[0].Data);
    }

    [Fact]
    public async Task A_rule_that_proposes_something_incoherent_changes_nothing_and_says_why()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var applier = new EffectApplier(db, world);

        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");
        await world.CreateEntityAsync("Orban", "orban");
        await world.SetComponentAsync("orban", "stats", """{"vigour":10}""");

        // Three effects: two fine, one naming an entity that does not exist. A mechanic getting
        // this wrong is ordinary — it is written by an LLM mid-session — and the guarantee is that
        // being wrong costs nothing.
        var run = await Engine.RunAsync("""
            return { narration: 'a hasty rule', effects: [
              { type: 'component.merge', entityId: 'orban', definitionId: 'stats', data: '{"vigour":9}' },
              { type: 'entity.create', entityId: 'echo', name: 'Echo' },
              { type: 'containment.move', entityId: 'echo', toEntityId: 'a-place-that-was-never-made' }
            ] };
            """, new MechanicProjection { Seed = 1 }, ExecutionLimits.Default);

        Assert.True(run.Ok, run.Error);
        Assert.Equal(3, run.Output.Effects.Count);

        var applied = await applier.ApplyAsync(run.Output.Effects);

        Assert.False(applied.Valid);
        Assert.Equal(0, applied.Count);
        Assert.Equal(2, applied.Problems[0].Index);
        Assert.Contains("Unknown entity", applied.Problems[0].Problem);

        // Nothing survived — not the change that was fine, not the entity that was created first.
        var after = await world.GetEntityAsync("orban");
        Assert.NotNull(after);
        Assert.Contains("\"vigour\":10", after.Components[0].Data);
        Assert.Null(await world.GetEntityAsync("echo"));
    }

    [Fact]
    public async Task A_hostile_rule_stored_like_any_other_still_cannot_touch_the_database()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var store = new MechanicStore(db);

        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");

        // Written through the ordinary authoring path, because that is how it would arrive.
        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.suspect.probe",
            Category = "check",
            Name = "Probe",
            Matches = "probe",
            Requirements = """{"roles":{"subject":{"components":["stats"]}}}""",
            Source = """
                var reached = [];
                try { reached.push(String(System.IO.File)); } catch (e) { }
                try { reached.push(String(require('fs'))); } catch (e) { }
                try { reached.push(String(process.mainModule)); } catch (e) { }
                return { narration: reached.length === 0 ? 'nothing reachable' : reached.join('|') };
                """
        });

        var stored = await store.GetAsync("mechanic.suspect.probe");
        Assert.NotNull(stored);

        var run = await Engine.RunAsync(stored.Source, new MechanicProjection { Seed = 1 }, ExecutionLimits.Default);

        Assert.True(run.Ok, run.Error);
        Assert.Equal("nothing reachable", run.Output.Narration);

        // And the world is exactly as it was. The only way state changes is the applier, and this
        // rule was never given one.
        Assert.Empty(await world.FindEntitiesAsync());
    }
}
