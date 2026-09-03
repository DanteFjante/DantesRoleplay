using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.Tests;

/// <summary>
/// The rules that ship with the system. These are the first thing a new session reads and copies,
/// so a shipped rule that does not work is worse than shipping none — it teaches the wrong shape
/// and costs the agent a debugging round before it has learned anything.
/// </summary>
public sealed class BootstrapRuleTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly JintMechanicEngine Engine = new();

    private static async Task<(WorldStore World, MechanicStore Store, ProjectionResolver Resolver, EffectApplier Applier)>
        SetUpAsync(DantesRoleplayDbContext db)
    {
        var world = new WorldStore(db);
        await world.DefineComponentAsync("fixture.legacy.stats", "Stats", "Numeric attributes.");
        await world.CreateEntityAsync("Orban", "orban");
        await world.SetComponentAsync("orban", "fixture.legacy.stats", """{"vigour":10,"resolve":4}""");

        var store = new MechanicStore(db);
        await new MechanicSeeder(store).SeedAsync();

        return (world, store, new ProjectionResolver(db), new EffectApplier(db, world));
    }

    private static async Task<MechanicRunResult> RunAsync(
        DantesRoleplayDbContext db,
        MechanicStore store,
        ProjectionResolver resolver,
        string id,
        string input)
    {
        var mechanic = await store.GetAsync(id);
        Assert.NotNull(mechanic);

        var resolved = await resolver.ResolveAsync(
            MechanicRequirements.Parse(mechanic.Requirements),
            new Dictionary<string, string> { ["subject"] = "orban" },
            input,
            seed: 4242);

        Assert.True(resolved.Ok, string.Join("; ", resolved.Problems));

        return await Engine.RunAsync(mechanic.Source, resolved.Projection!, ExecutionLimits.Default);
    }

    // ---- the files themselves ----------------------------------------------------------

    [Fact]
    public void Every_shipped_rule_parses_and_declares_what_it_reads()
    {
        var files = MechanicSeeder.Load();

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            Assert.StartsWith("mechanic.", file.Id);
            Assert.NotEmpty(file.Source);
            Assert.NotEmpty(file.Matches);

            // The fences are there so the files read as documents. They must not survive into the
            // engine, where a stray ``` is a syntax error in a rule nobody edited.
            Assert.DoesNotContain("```", file.Source);
            Assert.DoesNotContain("```", file.Requirements);

            var requirements = MechanicRequirements.Parse(file.Requirements);
            Assert.True(requirements.Roles.Count > 0
                || requirements.EffectComponentIds.Count > 0 && requirements.InputSchema is not null,
                $"{file.Id} must declare projected reads, or authored input plus the component family created from it.");
        }
    }

    [Fact]
    public void The_fingerprint_moves_when_any_authored_field_changes()
    {
        // A field missing from the hash cannot be edited at all: the seeder sees no change and
        // ignores the edit forever. That exact bug shipped once with a procedure's Governs field.
        var baseline = new MechanicFile(
            "mechanic.test", "check", "Name", "Description", "matches", "{}", "return {};", "", MechanicStatus.Active);

        var variants = new[]
        {
            baseline with { Category = "other" },
            baseline with { Name = "Different" },
            baseline with { Description = "Different" },
            baseline with { Matches = "different" },
            baseline with { Requirements = """{"roles":{}}""" },
            baseline with { Source = "return { narration: 'x' };" },
            baseline with { Scope = "campaign.one" },
            baseline with { Status = MechanicStatus.Draft }
        };

        foreach (var variant in variants)
        {
            Assert.NotEqual(baseline.ContentHash, variant.ContentHash);
        }
    }

    [Fact]
    public async Task Seeding_twice_writes_nothing_the_second_time()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);
        var seeder = new MechanicSeeder(store);

        var first = await seeder.SeedAsync();
        var second = await seeder.SeedAsync();

        Assert.True(first > 0);
        Assert.Equal(0, second);

        // Still version 1 — a restart that appended a version to every rule would fill the history
        // with revisions nobody made.
        var seeded = await store.GetAsync("mechanic.check.threshold");
        Assert.NotNull(seeded);
        Assert.Equal(1, seeded.Version);
    }

    // ---- they actually run --------------------------------------------------------------

    [Fact]
    public async Task The_check_rule_resolves_and_reports_how_it_got_there()
    {
        await using var db = _fixture.CreateContext();
        var (_, store, resolver, _) = await SetUpAsync(db);

        var run = await RunAsync(db, store, resolver, "mechanic.check.threshold",
            """{"field":"vigour","threshold":12}""");

        Assert.True(run.Ok, run.Error);
        Assert.Contains("Orban", run.Output.Narration);

        // The working is shown. A rule that announces an outcome without saying how it got there
        // cannot be argued with, which is the opposite of what a referee needs.
        Assert.Single(run.Log);
        Assert.Contains("rolled", run.Log[0]);
        Assert.Contains("vigour 10", run.Log[0]);

        // Deciding is not changing. This rule answers a question and touches nothing.
        Assert.Empty(run.Output.Effects);
    }

    [Fact]
    public async Task The_check_rule_gives_the_same_answer_for_the_same_seed()
    {
        await using var db = _fixture.CreateContext();
        var (_, store, resolver, _) = await SetUpAsync(db);

        var first = await RunAsync(db, store, resolver, "mechanic.check.threshold", """{"field":"vigour"}""");
        var again = await RunAsync(db, store, resolver, "mechanic.check.threshold", """{"field":"vigour"}""");

        Assert.Equal(first.Output.Narration, again.Output.Narration);
    }

    [Fact]
    public async Task The_adjust_rule_changes_one_number_and_leaves_the_others()
    {
        await using var db = _fixture.CreateContext();
        var (world, store, resolver, applier) = await SetUpAsync(db);

        var run = await RunAsync(db, store, resolver, "mechanic.value.adjust",
            """{"field":"vigour","by":-3}""");

        Assert.True(run.Ok, run.Error);

        var applied = await applier.ApplyAsync(run.Output.Effects);
        Assert.True(applied.Valid, string.Join("; ", applied.Problems.Select(p => p.Problem)));

        var after = await world.GetEntityAsync("orban");
        var stats = after!.Components.Single(c => c.DefinitionId == "fixture.legacy.stats").Data;

        Assert.Contains("\"vigour\":7", stats);

        // merge, not set. The rule sent one number and the others survived — the failure §P9 was
        // written about.
        Assert.Contains("\"resolve\":4", stats);
    }

    [Fact]
    public async Task Clamping_is_opt_in_and_reports_what_actually_happened()
    {
        await using var db = _fixture.CreateContext();
        var (_, store, resolver, _) = await SetUpAsync(db);

        var unclamped = await RunAsync(db, store, resolver, "mechanic.value.adjust",
            """{"field":"vigour","by":-40}""");

        var clamped = await RunAsync(db, store, resolver, "mechanic.value.adjust",
            """{"field":"vigour","by":-40,"min":0}""");

        Assert.Contains("-30", unclamped.Output.Narration);
        Assert.Contains("0", clamped.Output.Narration);

        // "asked for -40" stays in the log even when the result was floored, so how much it
        // actually cost is still answerable — which matters when another rule reads the result.
        Assert.Contains("asked for -40", clamped.Log[0]);
    }

    // ---- they fail usefully ---------------------------------------------------------------

    [Fact]
    public async Task A_missing_argument_is_answered_with_what_to_pass()
    {
        await using var db = _fixture.CreateContext();
        var (_, store, resolver, _) = await SetUpAsync(db);

        var run = await RunAsync(db, store, resolver, "mechanic.value.adjust", "{}");

        Assert.False(run.Ok);
        Assert.Contains("input.field", run.Error);
        Assert.Contains("\"field\"", run.Error);
    }

    [Fact]
    public async Task Naming_a_number_that_does_not_exist_lists_the_ones_that_do()
    {
        await using var db = _fixture.CreateContext();
        var (_, store, resolver, _) = await SetUpAsync(db);

        var run = await RunAsync(db, store, resolver, "mechanic.check.threshold",
            """{"field":"cunning"}""");

        Assert.False(run.Ok);
        Assert.Contains("cunning", run.Error);

        // Naming the alternatives turns a dead end into the next call.
        Assert.Contains("vigour", run.Error);
        Assert.Contains("resolve", run.Error);
    }

    [Fact]
    public async Task A_subject_with_no_numbers_at_all_says_so_rather_than_treating_them_as_zero()
    {
        await using var db = _fixture.CreateContext();
        var (world, store, resolver, _) = await SetUpAsync(db);
        await world.CreateEntityAsync("A door", "door");

        var mechanic = await store.GetAsync("mechanic.check.threshold");

        var resolved = await resolver.ResolveAsync(
            MechanicRequirements.Parse(mechanic!.Requirements),
            new Dictionary<string, string> { ["subject"] = "door" },
            """{"field":"vigour"}""",
            seed: 1);

        var run = await Engine.RunAsync(mechanic.Source, resolved.Projection!, ExecutionLimits.Default);

        // Treating an absent component as zeroes would silently make every door competent at
        // everything, which is exactly the kind of wrong answer that survives for weeks.
        Assert.False(run.Ok);
        Assert.Contains("stats", run.Error);
    }
}
