using DantesRoleplay.DataAccess;
using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

public sealed class EffectApplierTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static async Task<(WorldStore World, EffectApplier Applier)> SetUpAsync(
        DantesRoleplayDbContext db,
        params string[] definitionIds)
    {
        var world = new WorldStore(db);

        foreach (var id in definitionIds)
        {
            await world.DefineComponentAsync(id, id, $"Test definition {id}.");
        }

        return (world, new EffectApplier(db, world));
    }

    private static Effect Create(string id, string name) =>
        new() { Type = EffectType.EntityCreate, EntityId = id, Name = name };

    // ---- the vocabulary is structural, not about any game ------------------------------

    [Fact]
    public void The_effect_vocabulary_contains_no_game_words()
    {
        // §3.11. The kernel may say "component"; it may never say "damage", "hitpoints" or
        // "condition". A verb per game concept is how the last system ossified.
        string[] forbidden =
            ["damage", "heal", "condition", "stat", "item", "resource", "hp", "spell", "attack"];

        foreach (var verb in EffectType.All)
        {
            foreach (var word in forbidden)
            {
                Assert.DoesNotContain(word, verb, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---- shape validation, no database needed ------------------------------------------

    [Fact]
    public void An_unknown_verb_is_answered_with_the_list_of_known_ones()
    {
        var problem = EffectValidation.Check(new Effect { Type = "entity.destroy" });

        Assert.NotNull(problem);
        Assert.Contains("entity.destroy", problem);
        Assert.Contains(EffectType.EntityDelete, problem);
    }

    [Fact]
    public void A_missing_field_names_the_field_and_the_verb()
    {
        var problem = EffectValidation.Check(new Effect { Type = EffectType.ComponentSet, EntityId = "e1" });

        Assert.NotNull(problem);
        Assert.Contains("definitionId", problem);
        Assert.Contains(EffectType.ComponentSet, problem);
    }

    [Fact]
    public void Component_data_must_be_a_json_object()
    {
        var array = EffectValidation.Check(new Effect
        {
            Type = EffectType.ComponentSet,
            EntityId = "e1",
            DefinitionId = "stats",
            Data = "[1,2,3]"
        });

        var broken = EffectValidation.Check(new Effect
        {
            Type = EffectType.ComponentSet,
            EntityId = "e1",
            DefinitionId = "stats",
            Data = "{oops"
        });

        Assert.NotNull(array);
        Assert.NotNull(broken);
    }

    [Fact]
    public void Entity_create_demands_an_explicit_id_and_says_why()
    {
        var problem = EffectValidation.Check(new Effect { Type = EffectType.EntityCreate, Name = "Orban" });

        Assert.NotNull(problem);
        Assert.Contains("entityId", problem);
        Assert.Contains("validated before", problem);
    }

    // ---- the atomicity guarantee -------------------------------------------------------

    [Fact]
    public async Task A_list_that_fails_late_applies_none_of_it()
    {
        // The TravelRoleplay failure §3.8 exists to prevent: four good effects and one bad one
        // used to leave four applied and no record of the half-change.
        await using var db = _fixture.CreateContext();
        var (world, applier) = await SetUpAsync(db, "stats");
        await world.CreateEntityAsync("Orban", "orban");

        var result = await applier.ApplyAsync(
        [
            new Effect { Type = EffectType.ComponentSet, EntityId = "orban", DefinitionId = "stats", Data = """{"strength":12}""" },
            Create("sword", "Sword"),
            new Effect { Type = EffectType.ContainmentMove, EntityId = "sword", ToEntityId = "orban", Slot = "carried" },
            new Effect { Type = EffectType.ComponentSet, EntityId = "ghost", DefinitionId = "stats", Data = "{}" }
        ]);

        Assert.False(result.Valid);
        Assert.False(result.Applied);
        Assert.Equal(0, result.Count);

        var orban = await world.GetEntityAsync("orban");
        Assert.NotNull(orban);
        Assert.Empty(orban.Components);
        Assert.Null(await world.GetEntityAsync("sword"));
    }

    [Fact]
    public async Task A_valid_list_applies_as_one_unit()
    {
        await using var db = _fixture.CreateContext();
        var (world, applier) = await SetUpAsync(db, "stats");

        var result = await applier.ApplyAsync(
        [
            Create("orban", "Orban"),
            new Effect { Type = EffectType.ComponentAdd, EntityId = "orban", DefinitionId = "stats", Data = """{"strength":12}""" },
            Create("sword", "Sword"),
            new Effect { Type = EffectType.ContainmentMove, EntityId = "sword", ToEntityId = "orban", Slot = "carried" },
            new Effect { Type = EffectType.RelationshipCreate, EntityId = "orban", ToEntityId = "sword", Kind = "attuned" }
        ]);

        Assert.True(result.Valid);
        Assert.True(result.Applied);
        Assert.Equal(5, result.Count);

        var orban = await world.GetEntityAsync("orban");
        Assert.NotNull(orban);
        Assert.Contains(orban.Components, c => c.DefinitionId == "stats" && c.Data.Contains("12"));

        var contents = await world.GetContentsAsync("orban");
        Assert.Single(contents);
        Assert.Equal("carried", contents[0].Slot);
    }

    [Fact]
    public async Task An_effect_may_depend_on_one_earlier_in_the_same_list()
    {
        // Validation simulates the batch, so "create it then use it" is legal even though the
        // entity does not exist in the database when validation runs.
        await using var db = _fixture.CreateContext();
        var (world, applier) = await SetUpAsync(db, "stats");

        var result = await applier.ApplyAsync(
        [
            Create("goblin", "Goblin"),
            new Effect { Type = EffectType.ComponentSet, EntityId = "goblin", DefinitionId = "stats", Data = """{"hp":7}""" }
        ]);

        Assert.True(result.Valid);
        Assert.NotNull(await world.GetEntityAsync("goblin"));
    }

    // ---- reporting ---------------------------------------------------------------------

    [Fact]
    public async Task Every_fault_is_reported_at_once_with_its_position()
    {
        // Low-context callers get one round trip, not one per mistake (§7).
        await using var db = _fixture.CreateContext();
        var (_, applier) = await SetUpAsync(db);

        var result = await applier.ApplyAsync(
        [
            new Effect { Type = "entity.spawn", EntityId = "x" },
            new Effect { Type = EffectType.ComponentSet, EntityId = "nobody", DefinitionId = "stats", Data = "{}" }
        ]);

        Assert.False(result.Valid);
        Assert.Equal(2, result.Problems.Count);
        Assert.Equal(0, result.Problems[0].Index);
        Assert.Equal(1, result.Problems[1].Index);
    }

    [Fact]
    public async Task An_undeclared_component_definition_names_the_tool_that_declares_it()
    {
        await using var db = _fixture.CreateContext();
        var (world, applier) = await SetUpAsync(db);
        await world.CreateEntityAsync("Orban", "orban");

        var result = await applier.ApplyAsync(
            [new Effect { Type = EffectType.ComponentSet, EntityId = "orban", DefinitionId = "stats", Data = "{}" }]);

        Assert.False(result.Valid);
        Assert.Contains("define_component", result.Problems[0].Problem);
    }

    [Fact]
    public async Task A_malformed_create_does_not_make_every_later_effect_look_broken()
    {
        await using var db = _fixture.CreateContext();
        var (_, applier) = await SetUpAsync(db, "stats");

        var result = await applier.ApplyAsync(
        [
            new Effect { Type = EffectType.EntityCreate, EntityId = "goblin" }, // no name
            new Effect { Type = EffectType.ComponentSet, EntityId = "goblin", DefinitionId = "stats", Data = "{}" }
        ]);

        Assert.False(result.Valid);
        Assert.Single(result.Problems);
        Assert.Equal(0, result.Problems[0].Index);
    }

    // ---- silent no-ops are treated as faults -------------------------------------------

    [Fact]
    public async Task Adding_a_component_that_is_already_there_is_a_fault_that_offers_the_alternative()
    {
        await using var db = _fixture.CreateContext();
        var (world, applier) = await SetUpAsync(db, "stats");
        await world.CreateEntityAsync("Orban", "orban");
        await world.SetComponentAsync("orban", "stats", """{"strength":12}""");

        var result = await applier.ApplyAsync(
            [new Effect { Type = EffectType.ComponentAdd, EntityId = "orban", DefinitionId = "stats", Data = "{}" }]);

        Assert.False(result.Valid);
        Assert.Contains("component.set", result.Problems[0].Problem);
        Assert.Contains("component.merge", result.Problems[0].Problem);
    }

    [Fact]
    public async Task Removing_something_that_is_not_there_is_a_fault_rather_than_a_silent_no_op()
    {
        await using var db = _fixture.CreateContext();
        var (world, applier) = await SetUpAsync(db, "stats");
        await world.CreateEntityAsync("Orban", "orban");
        await world.CreateEntityAsync("Sword", "sword");

        var component = await applier.ApplyAsync(
            [new Effect { Type = EffectType.ComponentRemove, EntityId = "orban", DefinitionId = "stats" }]);

        var relationship = await applier.ApplyAsync(
        [
            new Effect { Type = EffectType.RelationshipRemove, EntityId = "orban", ToEntityId = "sword", Kind = "attuned" }
        ]);

        Assert.False(component.Valid);
        Assert.False(relationship.Valid);
    }

    [Fact]
    public async Task A_deleted_entity_id_stays_taken()
    {
        await using var db = _fixture.CreateContext();
        var (world, applier) = await SetUpAsync(db);
        await world.CreateEntityAsync("Orban", "orban");
        await world.DeleteEntityAsync("orban");

        var reuse = await applier.ApplyAsync([Create("orban", "A different Orban")]);
        var revive = await applier.ApplyAsync(
            [new Effect { Type = EffectType.EntityDelete, EntityId = "orban" }]);

        Assert.False(reuse.Valid);
        Assert.Contains("permanent", reuse.Problems[0].Problem);
        Assert.False(revive.Valid); // already gone — not alive, so not deletable
    }

    // ---- dry run -----------------------------------------------------------------------

    [Fact]
    public async Task A_dry_run_reports_the_same_verdict_and_writes_nothing()
    {
        await using var db = _fixture.CreateContext();
        var (world, applier) = await SetUpAsync(db, "stats");

        Effect[] effects =
        [
            Create("goblin", "Goblin"),
            new Effect { Type = EffectType.ComponentSet, EntityId = "goblin", DefinitionId = "stats", Data = """{"hp":7}""" }
        ];

        var dry = await applier.ApplyAsync(effects, dryRun: true);

        Assert.True(dry.Valid);
        Assert.False(dry.Applied);
        Assert.Equal(0, dry.Count);
        Assert.Null(await world.GetEntityAsync("goblin"));

        // Same list, same verdict, now for real. A clean dry run has to mean something.
        var wet = await applier.ApplyAsync(effects);

        Assert.True(wet.Applied);
        Assert.NotNull(await world.GetEntityAsync("goblin"));
    }

    // ---- failures the store finds, not the validator ------------------------------------

    [Fact]
    public async Task A_cycle_formed_by_the_batch_itself_rolls_the_batch_back()
    {
        // Validation cannot see this one: both entities exist and both moves are individually
        // legal. The store rejects the second, and the first must not survive.
        await using var db = _fixture.CreateContext();
        var (world, applier) = await SetUpAsync(db);
        await world.CreateEntityAsync("Bag", "bag");
        await world.CreateEntityAsync("Box", "box");

        var result = await applier.ApplyAsync(
        [
            new Effect { Type = EffectType.ContainmentMove, EntityId = "bag", ToEntityId = "box" },
            new Effect { Type = EffectType.ContainmentMove, EntityId = "box", ToEntityId = "bag" }
        ]);

        Assert.False(result.Applied);
        Assert.Equal(1, result.Problems[0].Index);
        Assert.Empty(await world.GetContentsAsync("box"));
    }

    [Fact]
    public async Task An_empty_list_is_a_no_op_not_an_error()
    {
        await using var db = _fixture.CreateContext();
        var (_, applier) = await SetUpAsync(db);

        var result = await applier.ApplyAsync([]);

        Assert.True(result.Valid);
        Assert.Equal(0, result.Count);
    }
}
