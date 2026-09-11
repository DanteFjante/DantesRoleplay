using DantesRoleplay.Mechanics;
using Acornima.Ast;
using Xunit.Abstractions;

namespace DantesRoleplay.Tests;

public sealed class PreparedMechanicProgramTests(ITestOutputHelper output)
{
    private static readonly MechanicProjection Projection = new() { Seed = 424242 };

    [Fact]
    public async Task Repeated_exact_source_reuses_one_prepared_program_and_reports_each_phase()
    {
        var measurements = new List<MechanicRunMeasurements>();
        var engine = new JintMechanicEngine(true, measurements.Add);
        const string source = "return { narration: String(ctx.randomInt(1, 1000)) };";

        var first = await engine.RunAsync(source, Projection, ExecutionLimits.Default);
        var second = await engine.RunAsync(source, Projection, ExecutionLimits.Default);

        Assert.True(first.Ok, first.Error);
        Assert.True(second.Ok, second.Error);
        Assert.Equal(first.Output.Narration, second.Output.Narration);
        Assert.Equal(2, measurements.Count);
        Assert.False(measurements[0].PreparationCacheHit);
        Assert.True(measurements[1].PreparationCacheHit);
        Assert.All(measurements, measurement =>
        {
            Assert.True(measurement.Preparation >= TimeSpan.Zero);
            Assert.True(measurement.ContextConstruction >= TimeSpan.Zero);
            Assert.True(measurement.Execution >= TimeSpan.Zero);
        });
        output.WriteLine(
            "cold preparation={0:F3}ms context={1:F3}ms execution={2:F3}ms; " +
            "warm preparation={3:F3}ms context={4:F3}ms execution={5:F3}ms",
            measurements[0].Preparation.TotalMilliseconds,
            measurements[0].ContextConstruction.TotalMilliseconds,
            measurements[0].Execution.TotalMilliseconds,
            measurements[1].Preparation.TotalMilliseconds,
            measurements[1].ContextConstruction.TotalMilliseconds,
            measurements[1].Execution.TotalMilliseconds);

        var statistics = engine.PreparedProgramCacheStatistics;
        Assert.Equal(1, statistics.EntryCount);
        Assert.Equal(1, statistics.PreparationCount);
        Assert.Equal(1, statistics.CacheHitCount);
    }

    [Fact]
    public async Task Concurrent_calls_share_preparation_but_keep_fresh_realms()
    {
        var engine = new JintMechanicEngine(true);
        const string source = """
            globalThis.invocationCount = (globalThis.invocationCount || 0) + 1;
            Object.prototype.preparedCacheMark = 'local';
            return { narration: globalThis.invocationCount + '|' + ({}).preparedCacheMark };
            """;

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            Task.Run(() => engine.RunAsync(source, Projection, ExecutionLimits.Default))));

        Assert.All(results, result =>
        {
            Assert.True(result.Ok, result.Error);
            Assert.Equal("1|local", result.Output.Narration);
        });
        var statistics = engine.PreparedProgramCacheStatistics;
        Assert.Equal(1, statistics.PreparationCount);
        Assert.Equal(31, statistics.CacheHitCount);
    }

    [Fact]
    public async Task Source_is_validated_alone_before_it_can_touch_wrapper_text()
    {
        var engine = new JintMechanicEngine(true);
        const string breakout = """
            return { narration: 'inside' };
            }); globalThis.wrapperEscaped = true; (function (ctx) {
            return { narration: 'outside' };
            """;

        var result = await engine.RunAsync(breakout, Projection, ExecutionLimits.Default);

        Assert.False(result.Ok);
        Assert.Equal(0, engine.PreparedProgramCacheStatistics.EntryCount);
        Assert.Equal(0, engine.PreparedProgramCacheStatistics.PreparationCount);
    }

    [Fact]
    public async Task Prepared_mechanic_has_no_lexical_access_to_harness_locals()
    {
        var engine = new JintMechanicEngine(true);

        var result = await engine.RunAsync(
            "return { narration: typeof log + '|' + typeof random + '|' + typeof payload };",
            Projection,
            ExecutionLimits.Default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("undefined|undefined|undefined", result.Output.Narration);
    }

    [Fact]
    public async Task Disabled_cache_prepares_fresh_and_preserves_seeded_output()
    {
        var measurements = new List<MechanicRunMeasurements>();
        var engine = new JintMechanicEngine(false, measurements.Add);
        const string source = "return { narration: String(ctx.randomInt(1, 1000000)) };";

        var first = await engine.RunAsync(source, Projection, ExecutionLimits.Default);
        var second = await engine.RunAsync(source, Projection, ExecutionLimits.Default);

        Assert.True(first.Ok, first.Error);
        Assert.True(second.Ok, second.Error);
        Assert.Equal(first.Output.Narration, second.Output.Narration);
        Assert.All(measurements, measurement => Assert.False(measurement.PreparationCacheHit));
        Assert.Equal(0, engine.PreparedProgramCacheStatistics.EntryCount);
        Assert.Equal(2, engine.PreparedProgramCacheStatistics.PreparationCount);
        Assert.Equal(0, engine.PreparedProgramCacheStatistics.CacheHitCount);
    }

    [Fact]
    public async Task Oversized_source_runs_without_being_retained()
    {
        var engine = new JintMechanicEngine(
            preparedProgramCacheEnabled: true,
            preparedProgramCountLimit: 4,
            preparedProgramSourceBytesLimit: 128);
        var source = "/*" + new string('x', 256) + "*/ return { narration: 'large' };";

        var first = await engine.RunAsync(source, Projection, ExecutionLimits.Default);
        var second = await engine.RunAsync(source, Projection, ExecutionLimits.Default);

        Assert.True(first.Ok, first.Error);
        Assert.True(second.Ok, second.Error);
        Assert.Equal("large", first.Output.Narration);
        Assert.Equal("large", second.Output.Narration);
        var statistics = engine.PreparedProgramCacheStatistics;
        Assert.Equal(0, statistics.EntryCount);
        Assert.Equal(2, statistics.PreparationCount);
        Assert.Equal(0, statistics.CacheHitCount);
    }

    [Fact]
    public async Task Cache_evicts_least_recently_used_program_at_its_entry_bound()
    {
        var engine = new JintMechanicEngine(
            preparedProgramCacheEnabled: true,
            preparedProgramCountLimit: 2,
            preparedProgramSourceBytesLimit: 4096);

        await RunNarration(engine, "one");
        await RunNarration(engine, "two");
        await RunNarration(engine, "one");
        await RunNarration(engine, "three");
        await RunNarration(engine, "two");

        var statistics = engine.PreparedProgramCacheStatistics;
        Assert.Equal(2, statistics.EntryCount);
        Assert.Equal(4, statistics.PreparationCount);
        Assert.Equal(1, statistics.CacheHitCount);
        Assert.Equal(2, statistics.EvictionCount);
    }

    [Fact]
    public async Task Cache_evicts_programs_at_its_total_source_byte_bound()
    {
        const long sourceByteLimit = 128;
        var engine = new JintMechanicEngine(
            preparedProgramCacheEnabled: true,
            preparedProgramCountLimit: 10,
            preparedProgramSourceBytesLimit: sourceByteLimit);
        var firstSource = "/*" + new string('a', 48) + "*/ return { narration: 'one' };";
        var secondSource = "/*" + new string('b', 48) + "*/ return { narration: 'two' };";
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(firstSource) <= sourceByteLimit);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(secondSource) <= sourceByteLimit);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(firstSource + secondSource) > sourceByteLimit);

        var first = await engine.RunAsync(firstSource, Projection, ExecutionLimits.Default);
        var second = await engine.RunAsync(secondSource, Projection, ExecutionLimits.Default);

        Assert.True(first.Ok, first.Error);
        Assert.True(second.Ok, second.Error);
        var statistics = engine.PreparedProgramCacheStatistics;
        Assert.Equal(1, statistics.EntryCount);
        Assert.True(statistics.RetainedSourceBytes <= sourceByteLimit);
        Assert.Equal(1, statistics.EvictionCount);
    }

    [Fact]
    public async Task Cache_bounds_retained_parser_work_independently_of_source_bytes()
    {
        var engine = new JintMechanicEngine(
            preparedProgramCacheEnabled: true,
            preparedProgramCountLimit: 10,
            preparedProgramSourceBytesLimit: 4096,
            preparedProgramComplexityLimit: 1);
        const string source = "return { narration: 'valid but too complex to retain' };";

        var first = await engine.RunAsync(source, Projection, ExecutionLimits.Default);
        var second = await engine.RunAsync(source, Projection, ExecutionLimits.Default);

        Assert.True(first.Ok, first.Error);
        Assert.True(second.Ok, second.Error);
        var statistics = engine.PreparedProgramCacheStatistics;
        Assert.Equal(0, statistics.EntryCount);
        Assert.Equal(0, statistics.RetainedComplexity);
        Assert.Equal(2, statistics.PreparationCount);
        Assert.Equal(2, statistics.EvictionCount);
    }

    [Fact]
    public async Task Hard_source_and_preparse_complexity_fences_reject_before_retention()
    {
        var engine = new JintMechanicEngine(true);
        var oversized = "/*" + new string('x', JintMechanicEngine.MaximumMechanicSourceBytes) + "*/";
        var unary = "return { narration: String("
            + new string('!', JintMechanicEngine.MaximumMechanicExpressionComplexity + 1) + "true) };";
        var assignment = "var a; "
            + string.Concat(Enumerable.Repeat("a=", JintMechanicEngine.MaximumMechanicExpressionComplexity + 1))
            + "1; return { narration: String(a) };";
        var arrow = "return { narration: String(typeof ("
            + string.Concat(Enumerable.Repeat("x=>", JintMechanicEngine.MaximumMechanicExpressionComplexity + 1))
            + "x)) };";
        var nested = new string('{', JintMechanicEngine.MaximumMechanicLexicalNesting + 1)
            + "return { narration: 'nested' };"
            + new string('}', JintMechanicEngine.MaximumMechanicLexicalNesting + 1);
        var tooManyTokens = string.Concat(Enumerable.Repeat(
            "0;",
            JintMechanicEngine.MaximumMechanicTokens / 2 + 1));
        var deepAst = "var a = {}; return { narration: String(a"
            + string.Concat(Enumerable.Repeat(".a", JintMechanicEngine.MaximumMechanicAstDepth + 1))
            + ") };";
        var labels = string.Concat(Enumerable.Range(
                0,
                JintMechanicEngine.MaximumMechanicExpressionComplexity + 1).Select(index => $"label{index}:"))
            + "return { narration: 'labelled' };";
        var doStatements = string.Concat(Enumerable.Repeat(
                "do ",
                JintMechanicEngine.MaximumMechanicExpressionComplexity + 1))
            + ";"
            + string.Concat(Enumerable.Repeat(
                " while (false);",
                JintMechanicEngine.MaximumMechanicExpressionComplexity + 1))
            + " return { narration: 'do' };";
        var awaits = "async function nested(value) { return "
            + string.Concat(Enumerable.Repeat(
                "await ",
                JintMechanicEngine.MaximumMechanicExpressionComplexity + 1))
            + "value; } return { narration: 'await' };";
        var yields = "function* nested(value) { return "
            + string.Concat(Enumerable.Repeat(
                "yield ",
                JintMechanicEngine.MaximumMechanicExpressionComplexity + 1))
            + "value; } return { narration: 'yield' };";

        var results = new[]
        {
            await engine.RunAsync(oversized, Projection, ExecutionLimits.Default),
            await engine.RunAsync(unary, Projection, ExecutionLimits.Default),
            await engine.RunAsync(assignment, Projection, ExecutionLimits.Default),
            await engine.RunAsync(arrow, Projection, ExecutionLimits.Default),
            await engine.RunAsync(nested, Projection, ExecutionLimits.Default),
            await engine.RunAsync(tooManyTokens, Projection, ExecutionLimits.Default),
            await engine.RunAsync(deepAst, Projection, ExecutionLimits.Default),
            await engine.RunAsync(labels, Projection, ExecutionLimits.Default),
            await engine.RunAsync(doStatements, Projection, ExecutionLimits.Default),
            await engine.RunAsync(awaits, Projection, ExecutionLimits.Default),
            await engine.RunAsync(yields, Projection, ExecutionLimits.Default)
        };

        Assert.All(results, result => Assert.False(result.Ok));
        Assert.Contains("source exceeds", results[0].Error, StringComparison.OrdinalIgnoreCase);
        Assert.All(results.Skip(1).Take(3), result =>
            Assert.Contains("recursive operators", result.Error, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("lexical levels", results[4].Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lexical tokens", results[5].Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("syntax tree", results[6].Error, StringComparison.OrdinalIgnoreCase);
        Assert.All(results.Skip(7), result =>
            Assert.Contains("recursive operators", result.Error, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, engine.PreparedProgramCacheStatistics.EntryCount);
        Assert.Equal(0, engine.PreparedProgramCacheStatistics.PreparationCount);
        Assert.Equal(10, engine.PreparedProgramCacheStatistics.PreparationFailureCount);
    }

    [Fact]
    public async Task Syntax_like_text_inside_literals_does_not_consume_preparse_recursion_budget()
    {
        var engine = new JintMechanicEngine(true);
        var text = string.Concat(Enumerable.Repeat(
            "( [ { if else do while await yield = += => ? : ! ~ new typeof ",
            JintMechanicEngine.MaximumMechanicExpressionComplexity + 1));
        var source = "return { narration: " + System.Text.Json.JsonSerializer.Serialize(text) + " };";

        var result = await engine.RunAsync(source, Projection, ExecutionLimits.Default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(text, result.Output.Narration);
    }

    [Fact]
    public void Literal_bigint_expressions_are_not_evaluated_during_untrusted_preparation()
    {
        // A small shift proves folding is disabled without allocating a hostile folded value.
        var prepared = JintMechanicEngine.PrepareMechanicProgram("return { data: { value: 1n << 12n } }; ");
        var pending = new Stack<Node>();
        pending.Push(Assert.IsType<Script>(prepared.Program));
        var expressions = new List<BinaryExpression>();
        while (pending.TryPop(out var node))
        {
            if (node is BinaryExpression binary) expressions.Add(binary);
            foreach (var child in node.ChildNodes) pending.Push(child);
        }
        // Jint stores a JintConstantExpression in UserData when folding a literal binary node.
        Assert.Null(Assert.Single(expressions).UserData);
    }

    [Fact]
    public async Task Shallow_sibling_control_blocks_do_not_accumulate_recursive_depth()
    {
        var source = string.Concat(Enumerable.Repeat("if (true) { let value = 1; } ", 129))
            + "return { narration: 'flat' };";
        var result = await new JintMechanicEngine().RunAsync(source, Projection, ExecutionLimits.Default);
        Assert.True(result.Ok, result.Error);
        Assert.Equal("flat", result.Output.Narration);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Healthy_waiter_retries_after_leader_personal_cancellation_or_deadline(bool cancelLeader)
    {
        using var leaderStarted = new ManualResetEventSlim();
        using var releaseLeader = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var preparations = 0;
        var engine = new JintMechanicEngine(true, preparationStarted: _ =>
        {
            if (Interlocked.Increment(ref preparations) != 1) return;
            leaderStarted.Set();
            if (!releaseLeader.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test gate expired.");
        });
        const string source = "return { narration: 'healthy' };";
        var leader = Task.Run(() => engine.RunAsync(source, Projection,
            ExecutionLimits.Default with { Timeout = TimeSpan.FromMilliseconds(cancelLeader ? 5000 : 150) }, cancellation.Token));
        Assert.True(leaderStarted.Wait(TimeSpan.FromSeconds(2)));
        var healthy = Task.Run(() => engine.RunAsync(source, Projection,
            ExecutionLimits.Default with { Timeout = TimeSpan.FromSeconds(5) }));
        try
        {
            Assert.True(SpinWait.SpinUntil(() => engine.PreparedProgramCacheStatistics.PendingPreparations >= 2,
                TimeSpan.FromSeconds(2)));
            if (cancelLeader) cancellation.Cancel();
            else await Task.Delay(200);
        }
        finally { releaseLeader.Set(); }
        var results = await Task.WhenAll(leader, healthy).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(results[0].Ok);
        Assert.Equal(cancelLeader ? "cancelled" : "timeout", results[0].LimitHit);
        Assert.True(results[1].Ok, results[1].Error);
        Assert.Equal("healthy", results[1].Output.Narration);
        Assert.Equal(1, engine.PreparedProgramCacheStatistics.PreparationFailureCount);
        Assert.Equal(1, engine.PreparedProgramCacheStatistics.PreparationCount);
        Assert.Equal(0, engine.PreparedProgramCacheStatistics.PendingPreparations);
        Assert.Equal(0, engine.PreparedProgramCacheStatistics.ActivePreparations);
    }

    [Fact]
    public async Task Same_key_waiters_observe_their_own_cancellation_and_deadline()
    {
        using var leaderStarted = new ManualResetEventSlim();
        using var releaseLeader = new ManualResetEventSlim();
        const string source = "return { narration: 'shared' };";
        var engine = new JintMechanicEngine(
            true,
            preparationStarted: candidate =>
            {
                if (candidate != source) return;
                leaderStarted.Set();
                if (!releaseLeader.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Test preparation gate was not released.");
            });

        var leader = Task.Run(() => engine.RunAsync(source, Projection, ExecutionLimits.Default));
        Assert.True(leaderStarted.Wait(TimeSpan.FromSeconds(2)));
        try
        {
            using var cancelled = new CancellationTokenSource();
            var cancelledWaiter = Task.Run(() => engine.RunAsync(
                source,
                Projection,
                ExecutionLimits.Default,
                cancelled.Token));
            Assert.True(SpinWait.SpinUntil(
                () => engine.PreparedProgramCacheStatistics.PendingPreparations >= 2,
                TimeSpan.FromSeconds(2)));
            cancelled.Cancel();
            var cancelledResult = await cancelledWaiter;
            Assert.False(cancelledResult.Ok);
            Assert.Equal("cancelled", cancelledResult.LimitHit);

            var deadlineResult = await Task.Run(() => engine.RunAsync(
                source,
                Projection,
                ExecutionLimits.Default with { Timeout = TimeSpan.FromMilliseconds(25) }));
            Assert.False(deadlineResult.Ok);
            Assert.Equal("timeout", deadlineResult.LimitHit);
        }
        finally
        {
            releaseLeader.Set();
        }

        var leaderResult = await leader;
        Assert.True(leaderResult.Ok, leaderResult.Error);
    }

    [Fact]
    public async Task Blocked_cold_key_does_not_hold_cache_lock_or_block_an_unrelated_key()
    {
        using var blockedStarted = new ManualResetEventSlim();
        using var releaseBlocked = new ManualResetEventSlim();
        const string blockedSource = "return { narration: 'blocked' };";
        const string unrelatedSource = "return { narration: 'unrelated' };";
        var engine = new JintMechanicEngine(
            true,
            maximumConcurrentPreparations: 2,
            preparationStarted: candidate =>
            {
                if (candidate != blockedSource) return;
                blockedStarted.Set();
                if (!releaseBlocked.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Test preparation gate was not released.");
            });

        var blocked = Task.Run(() => engine.RunAsync(blockedSource, Projection, ExecutionLimits.Default));
        Assert.True(blockedStarted.Wait(TimeSpan.FromSeconds(2)));
        try
        {
            var unrelated = await Task.Run(() => engine.RunAsync(
                unrelatedSource,
                Projection,
                ExecutionLimits.Default)).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(unrelated.Ok, unrelated.Error);
        }
        finally
        {
            releaseBlocked.Set();
        }

        var blockedResult = await blocked;
        Assert.True(blockedResult.Ok, blockedResult.Error);
        Assert.Equal(2, engine.PreparedProgramCacheStatistics.PeakActivePreparations);
    }

    [Fact]
    public async Task Pending_preparations_are_bounded_and_failed_leader_can_retry()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        const string firstSource = "return { narration: 'first' };";
        var engine = new JintMechanicEngine(
            true,
            maximumConcurrentPreparations: 1,
            maximumPendingPreparations: 2,
            preparationStarted: candidate =>
            {
                if (candidate != firstSource) return;
                firstStarted.Set();
                if (!releaseFirst.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Test preparation gate was not released.");
            });

        var first = Task.Run(() => engine.RunAsync(firstSource, Projection, ExecutionLimits.Default));
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(2)));
        var second = Task.Run(() => engine.RunAsync(
            "return { narration: 'second' };",
            Projection,
            ExecutionLimits.Default));
        try
        {
            Assert.True(SpinWait.SpinUntil(
                () => engine.PreparedProgramCacheStatistics.PendingPreparations == 2,
                TimeSpan.FromSeconds(2)));
            var rejected = await engine.RunAsync(
                "return { narration: 'capacity' };",
                Projection,
                ExecutionLimits.Default);
            Assert.False(rejected.Ok);
            Assert.Contains("capacity", rejected.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            releaseFirst.Set();
        }
        Assert.True((await first).Ok);
        Assert.True((await second).Ok);
        Assert.Equal(2, engine.PreparedProgramCacheStatistics.PeakPendingPreparations);

        var failedAttempts = 0;
        const string retrySource = "return { narration: 'retry' };";
        var retryEngine = new JintMechanicEngine(
            true,
            preparationStarted: candidate =>
            {
                if (candidate == retrySource && Interlocked.Increment(ref failedAttempts) == 1)
                    throw new InvalidOperationException("deterministic preparation failure");
            });
        var failed = await retryEngine.RunAsync(retrySource, Projection, ExecutionLimits.Default);
        var retried = await retryEngine.RunAsync(retrySource, Projection, ExecutionLimits.Default);
        Assert.False(failed.Ok);
        Assert.True(retried.Ok, retried.Error);
        Assert.Equal(1, retryEngine.PreparedProgramCacheStatistics.PreparationFailureCount);
        Assert.Equal(1, retryEngine.PreparedProgramCacheStatistics.PreparationCount);
        Assert.Equal(1, retryEngine.PreparedProgramCacheStatistics.EntryCount);
    }

    [Fact]
    public void Catalog_mechanics_fit_preparation_resource_fences()
    {
        using var catalog = CatalogTestTemplate.CopyRepositoryCatalog("prepared-mechanic-programs");
        var roots = new[] { Path.Combine(catalog.Root, "mechanics") }
            .Concat(Directory.EnumerateDirectories(Path.Combine(catalog.Root, "applications"))
                .Select(application => Path.Combine(application, "mechanics")).Where(Directory.Exists)).ToArray();
        var measured = roots.SelectMany(root => Directory.EnumerateFiles(root, "*.js", SearchOption.AllDirectories))
            .Select(path => (Path: path, Complexity: JintMechanicEngine.InspectMechanicProgramComplexity(
                File.ReadAllText(path))))
            .ToArray();

        Assert.NotEmpty(measured);
        var largest = measured.MaxBy(value => value.Complexity.SourceBytes);
        var densest = measured.MaxBy(value => value.Complexity.TokenCount);
        var mostNested = measured.MaxBy(value => value.Complexity.LexicalNesting);
        var mostRecursive = measured.MaxBy(value => value.Complexity.ExpressionComplexity);
        var deepest = measured.MaxBy(value => value.Complexity.AstDepth);
        output.WriteLine(
            "catalog files={0}; max bytes={1} ({2}); max tokens={3} ({4}); " +
            "max lexical nesting={5} ({6}); max expression complexity={7} ({8}); max AST depth={9} ({10})",
            measured.Length,
            largest.Complexity.SourceBytes,
            Path.GetFileName(largest.Path),
            densest.Complexity.TokenCount,
            Path.GetFileName(densest.Path),
            mostNested.Complexity.LexicalNesting,
            Path.GetFileName(mostNested.Path),
            mostRecursive.Complexity.ExpressionComplexity,
            Path.GetFileName(mostRecursive.Path),
            deepest.Complexity.AstDepth,
            Path.GetFileName(deepest.Path));
    }

    [Fact]
    public async Task Cancellation_before_preparation_does_not_populate_cache()
    {
        var engine = new JintMechanicEngine(true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await engine.RunAsync(
            "return { narration: 'unreachable' };",
            Projection,
            ExecutionLimits.Default,
            cancellation.Token);

        Assert.False(result.Ok);
        Assert.Equal("cancelled", result.LimitHit);
        Assert.Equal(0, engine.PreparedProgramCacheStatistics.PreparationCount);
    }

    [Fact]
    public async Task Diagnostics_failure_cannot_replace_the_mechanic_result()
    {
        var engine = new JintMechanicEngine(true, _ => throw new InvalidOperationException("listener failed"));

        var result = await engine.RunAsync(
            "return { narration: 'completed' };",
            Projection,
            ExecutionLimits.Default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("completed", result.Output.Narration);
    }

    private static async Task RunNarration(JintMechanicEngine engine, string narration)
    {
        var result = await engine.RunAsync(
            $"return {{ narration: '{narration}' }};",
            Projection,
            ExecutionLimits.Default);
        Assert.True(result.Ok, result.Error);
    }
}
