using DantesRoleplay.Mechanics;
using DantesRoleplay.RuleAccess;

namespace DantesRoleplay.Tests;

/// <summary>
/// The escape tests. ARCHITECTURE.md §2 calls arbitrary AI-written JavaScript the major risk of
/// this design, and everything else in the system assumes this file passes.
///
/// Each one is written the way an attacker or a confused LLM would actually write it, not as a
/// polite check that a flag is set. "AllowClr is not called" is a claim about source; "this
/// mechanic tried to read a file and could not" is evidence.
/// </summary>
public sealed class SandboxTests
{
    private static readonly JintMechanicEngine Engine = new();

    private static Task<MechanicRunResult> RunAsync(string source, ExecutionLimits? limits = null) =>
        Engine.RunAsync(source, new MechanicProjection { Seed = 12345 }, limits ?? ExecutionLimits.Default);

    // ---- it works at all ---------------------------------------------------------------

    [Fact]
    public async Task A_mechanic_returns_narration_and_effects()
    {
        var result = await RunAsync("""
            return {
              narration: ctx.roles.subject.name + ' braces.',
              effects: [{ type: 'component.merge', entityId: ctx.roles.subject.id,
                          definitionId: 'stats', data: '{"guard":2}' }]
            };
            """.Replace("ctx.roles.subject", "({ id: 'orban', name: 'Orban' })"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal("Orban braces.", result.Output.Narration);
        Assert.Single(result.Output.Effects);
        Assert.Equal("component.merge", result.Output.Effects[0].Type);
        Assert.Equal("orban", result.Output.Effects[0].EntityId);
    }

    [Fact]
    public async Task A_mechanic_is_handed_exactly_what_the_projection_contained()
    {
        var projection = new MechanicProjection
        {
            StateSpaceId = "campaign-7",
            Seed = 1,
            Input = """{"intensity":3}""",
            Roles =
            {
                ["subject"] = new EntityProjection(
                    "orban",
                    "Orban",
                    new Dictionary<string, string> { ["stats"] = """{"strength":12}""" })
            }
        };

        var result = await Engine.RunAsync("""
            var stats = JSON.parse(ctx.roles.subject.components.stats);
            return { narration: ctx.stateSpaceId + ' ' + ctx.roles.subject.name + ' ' + stats.strength + ' ' + ctx.input.intensity };
            """, projection, ExecutionLimits.Default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("campaign-7 Orban 12 3", result.Output.Narration);
    }

    [Fact]
    public async Task Projected_roles_components_and_input_are_deeply_frozen()
    {
        var projection = new MechanicProjection
        {
            Seed = 1,
            Input = """{"nested":{"value":3}}""",
            Roles =
            {
                ["subject"] = new EntityProjection("orban", "Orban",
                    new Dictionary<string, string> { ["stats"] = """{"strength":12}""" })
            }
        };

        var result = await Engine.RunAsync("""
            var rejected = 0;
            try { ctx.roles.subject.name = 'Changed'; } catch (error) { rejected++; }
            try { ctx.roles.subject.components.stats = '{}'; } catch (error) { rejected++; }
            try { ctx.input.nested.value = 4; } catch (error) { rejected++; }
            return { narration: [
              Object.isFrozen(ctx.roles), Object.isFrozen(ctx.roles.subject),
              Object.isFrozen(ctx.roles.subject.components), Object.isFrozen(ctx.input),
              Object.isFrozen(ctx.input.nested), rejected,
              ctx.roles.subject.name, ctx.roles.subject.components.stats, ctx.input.nested.value
            ].join('|') };
            """, projection, ExecutionLimits.Default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("true|true|true|true|true|3|Orban|{\"strength\":12}|3", result.Output.Narration);
    }

    [Fact]
    public async Task Host_execution_identity_is_separate_from_input_and_deeply_frozen()
    {
        var projection = new MechanicProjection
        {
            Seed = 1,
            Input = """{"execution":{"operationId":"caller-forged"}}""",
            Execution = new MechanicExecutionContext(
                "0123456789abcdef0123456789abcdef",
                "fedcba9876543210fedcba9876543210",
                "11111111111111111111111111111111",
                3)
        };

        var result = await Engine.RunAsync("""
            var rejected = 0;
            try { ctx.execution.operationId = 'caller-forged'; } catch (error) { rejected++; }
            return { narration: [
              Object.isFrozen(ctx.execution), rejected,
              ctx.execution.rootOperationId, ctx.execution.operationId,
              ctx.execution.parentOperationId, ctx.execution.invocationOrdinal,
              ctx.input.execution.operationId
            ].join('|') };
            """, projection, ExecutionLimits.Default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(
            "true|1|0123456789abcdef0123456789abcdef|fedcba9876543210fedcba9876543210|11111111111111111111111111111111|3|caller-forged",
            result.Output.Narration);
    }

    [Fact]
    public async Task Read_only_projection_exposes_no_execution_identity()
    {
        var result = await RunAsync("return { narration: String(ctx.execution) };");

        Assert.True(result.Ok, result.Error);
        Assert.Equal("null", result.Output.Narration);
    }

    [Fact]
    public async Task The_projection_reaches_javascript_with_javascript_naming()
    {
        // Found by the spike, and it would have been invisible: with .NET's default naming the
        // projection arrives as ctx.roles.subject.Name, so every mechanic written the obvious way
        // reads undefined and produces a confidently wrong answer instead of an error.
        var projection = new MechanicProjection
        {
            Seed = 1,
            Roles =
            {
                // A definition id with a capital in it, because recasing dictionary KEYS to match
                // would be the same bug wearing the opposite hat.
                ["subject"] = new EntityProjection(
                    "orban",
                    "Orban",
                    new Dictionary<string, string> { ["Stats"] = "{}" },
                    ContainerSlot: "carried")
            }
        };

        var result = await Engine.RunAsync("""
            var s = ctx.roles.subject;
            return { narration: [
              typeof s.id, typeof s.name, typeof s.containerSlot,
              Object.keys(s.components)[0]
            ].join(' ') };
            """, projection, ExecutionLimits.Default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("string string string Stats", result.Output.Narration);
    }

    // ---- the escape attempts -----------------------------------------------------------

    [Theory]
    // Jint's CLR bridge, which only exists when AllowClr() has been called. It is not called.
    [InlineData("var t = System.IO.File; return { narration: 'reached System' };")]
    [InlineData("var t = importNamespace('System.IO'); return { narration: 'imported' };")]
    [InlineData("var t = clr.System.IO.File; return { narration: 'reached clr' };")]
    [InlineData("var t = System.Reflection.Assembly; return { narration: 'reflected' };")]
    // Host environments this is not. A mechanic that finds one of these has escaped the model.
    [InlineData("return { narration: require('fs').readFileSync('/etc/passwd', 'utf8') };")]
    [InlineData("return { narration: String(process.env) };")]
    [InlineData("return { narration: String(fetch('http://example.com')) };")]
    [InlineData("var t = new XMLHttpRequest(); return { narration: 'net' };")]
    public async Task The_ways_out_are_all_shut(string escape)
    {
        var result = await RunAsync(escape);

        Assert.False(result.Ok, $"A mechanic reached something it should not: {escape}");
    }

    [Fact]
    public async Task A_mechanic_cannot_reach_the_dotnet_type_system_through_an_object_it_was_given()
    {
        // The subtler escape: not a global, but walking up from a value the host handed in. It
        // fails because nothing the host hands in is a .NET object — the boundary is JSON text,
        // so there is no object graph to walk.
        var projection = new MechanicProjection
        {
            Seed = 1,
            Roles = { ["subject"] = new EntityProjection("orban", "Orban", new Dictionary<string, string>()) }
        };

        var result = await Engine.RunAsync("""
            var probe = ctx.roles.subject.GetType
                     || ctx.roles.subject.constructor.GetType
                     || (ctx.roles.subject.constructor.prototype && ctx.roles.subject.constructor.prototype.GetType);
            return { narration: probe ? 'REACHED CLR' : 'plain javascript object' };
            """, projection, ExecutionLimits.Default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("plain javascript object", result.Output.Narration);
    }

    // ---- the limits ---------------------------------------------------------------------

    [Fact]
    public async Task A_loop_that_never_ends_is_stopped_by_the_statement_budget()
    {
        var result = await RunAsync(
            "var n = 0; while (true) { n = n + 1; } return { narration: 'unreachable' };",
            new ExecutionLimits { MaxStatements = 20_000 });

        Assert.False(result.Ok);
        Assert.Equal("statements", result.LimitHit);
        Assert.Contains("never ends", result.Error);
    }

    [Fact]
    public async Task Runaway_allocation_is_stopped_by_the_memory_limit()
    {
        var result = await RunAsync(
            "var a = []; for (var i = 0; i < 100000000; i++) { a.push('xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx'); } return { narration: 'unreachable' };",
            new ExecutionLimits { MemoryBytes = 4 * 1024 * 1024, MaxStatements = 100_000_000 });

        Assert.False(result.Ok);

        // Either ceiling is a correct outcome; the point is that it stopped rather than taking
        // the process with it.
        Assert.Contains(result.LimitHit, new[] { "memory", "statements", "timeout" });
    }

    [Fact]
    public async Task Runaway_recursion_fails_as_a_limit_rather_than_a_stack_overflow()
    {
        // A genuine StackOverflowException cannot be caught in .NET and would kill the process,
        // so this limit is the difference between a bad rule and an outage.
        var result = await RunAsync(
            "function down(n) { return down(n + 1); } return { narration: String(down(0)) };",
            new ExecutionLimits { MaxRecursionDepth = 32 });

        Assert.False(result.Ok);
        Assert.Equal("recursion", result.LimitHit);
    }

    [Fact]
    public async Task A_mechanic_proposing_absurdly_many_effects_is_refused()
    {
        var result = await RunAsync("""
            var out = [];
            for (var i = 0; i < 500; i++) { out.push({ type: 'entity.delete', entityId: 'e' + i }); }
            return { effects: out };
            """, new ExecutionLimits { MaxEffects = 50 });

        Assert.False(result.Ok);
        Assert.Equal("effects", result.LimitHit);
    }

    // ---- author error is an outcome, not an exception ------------------------------------

    [Fact]
    public async Task A_syntax_error_comes_back_as_a_message_the_author_can_act_on()
    {
        var result = await RunAsync("return { narration: 'unclosed ");

        Assert.False(result.Ok);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task A_thrown_value_comes_back_as_a_message_rather_than_taking_down_the_caller()
    {
        var result = await RunAsync("throw new Error('that target is out of range');");

        Assert.False(result.Ok);
        Assert.Contains("out of range", result.Error);
    }

    [Fact]
    public async Task A_mechanic_that_returns_nothing_is_told_what_to_return()
    {
        var result = await RunAsync("var x = 1;");

        Assert.False(result.Ok);
        Assert.Contains("narration", result.Error);
    }

    [Fact]
    public async Task Sloppy_code_fails_loudly_instead_of_creating_a_global()
    {
        // Strict mode. An undeclared assignment is a typo often enough that silence is the wrong
        // answer, and the author is an LLM reading the error.
        var result = await RunAsync("total = 5; return { narration: String(total) };");

        Assert.False(result.Ok);
    }

    // ---- reproducibility: what makes a chance-based rule reviewable ----------------------

    [Fact]
    public async Task The_same_seed_produces_the_same_outcome()
    {
        const string source = """
            var rolls = [];
            for (var i = 0; i < 5; i++) { rolls.push(ctx.randomInt(1, 20)); }
            return { narration: rolls.join(',') };
            """;

        var first = await Engine.RunAsync(source, new MechanicProjection { Seed = 987654 }, ExecutionLimits.Default);
        var again = await Engine.RunAsync(source, new MechanicProjection { Seed = 987654 }, ExecutionLimits.Default);
        var other = await Engine.RunAsync(source, new MechanicProjection { Seed = 987655 }, ExecutionLimits.Default);

        Assert.True(first.Ok, first.Error);

        // With the seed recorded in the audit log, "why did that happen?" stays answerable months
        // later. Without it, a rule that decides outcomes by chance can never be reviewed.
        Assert.Equal(first.Output.Narration, again.Output.Narration);
        Assert.NotEqual(first.Output.Narration, other.Output.Narration);
    }

    [Fact]
    public async Task The_random_source_stays_inside_its_bounds()
    {
        var result = await RunAsync("""
            var lo = 99, hi = -1;
            for (var i = 0; i < 2000; i++) {
              var n = ctx.randomInt(1, 6);
              if (n < lo) { lo = n; }
              if (n > hi) { hi = n; }
            }
            return { narration: lo + '-' + hi };
            """);

        Assert.True(result.Ok, result.Error);

        // Inclusive at both ends, because every table-top convention is, and an off-by-one here
        // would be invisible in play and wrong in every rule at once.
        Assert.Equal("1-6", result.Output.Narration);
    }

    // ---- supervision ---------------------------------------------------------------------

    [Fact]
    public async Task What_the_mechanic_logged_comes_back_with_the_result()
    {
        var result = await RunAsync("""
            ctx.log('rolled 14');
            ctx.log('threshold was 12');
            return { narration: 'success' };
            """);

        Assert.True(result.Ok, result.Error);

        // Not debugging garnish: approving a rule an AI wrote means seeing what it did, and
        // "it worked" is not reviewable.
        Assert.Equal(["rolled 14", "threshold was 12"], result.Log);
    }

    [Fact]
    public async Task A_mechanic_cannot_flood_the_log()
    {
        var result = await RunAsync("""
            for (var i = 0; i < 5000; i++) { ctx.log('line ' + i); }
            return { narration: 'done' };
            """, new ExecutionLimits { MaxLogLines = 10, MaxStatements = 200_000 });

        Assert.True(result.Ok, result.Error);
        Assert.Equal(10, result.Log.Count);
    }

    [Fact]
    public async Task Effects_pushed_onto_ctx_are_accepted_as_well_as_returned_ones()
    {
        // Two reasonable ways to write the same thing. Failing on the one that is merely not the
        // documented one teaches nothing and costs the author a round trip.
        var result = await RunAsync("""
            ctx.effects.push({ type: 'entity.delete', entityId: 'torch' });
            return { narration: 'the torch burns out' };
            """);

        Assert.True(result.Ok, result.Error);
        Assert.Single(result.Output.Effects);
        Assert.Equal("torch", result.Output.Effects[0].EntityId);
    }
}
