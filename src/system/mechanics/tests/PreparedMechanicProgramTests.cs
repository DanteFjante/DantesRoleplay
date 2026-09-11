using DantesRoleplay.Mechanics;
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
        Assert.Equal(default, engine.PreparedProgramCacheStatistics);
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
        const long sourceByteLimit = 256;
        var engine = new JintMechanicEngine(
            preparedProgramCacheEnabled: true,
            preparedProgramCountLimit: 10,
            preparedProgramSourceBytesLimit: sourceByteLimit);
        var firstSource = "/*" + new string('a', 48) + "*/ return { narration: 'one' };";
        var secondSource = "/*" + new string('b', 48) + "*/ return { narration: 'two' };";
        Assert.True(firstSource.Length * sizeof(char) <= sourceByteLimit);
        Assert.True(secondSource.Length * sizeof(char) <= sourceByteLimit);
        Assert.True((firstSource.Length + secondSource.Length) * sizeof(char) > sourceByteLimit);

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
