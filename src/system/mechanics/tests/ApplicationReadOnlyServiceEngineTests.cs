using System.Diagnostics;
using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.Mechanics.Tests;

public sealed class ApplicationReadOnlyServiceEngineTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task Frozen_service_wrappers_keep_captured_intrinsics_and_hide_native_callbacks()
    {
        var capabilities = new Capabilities();
        var run = await new JintMechanicEngine().RunServiceAsync("""
            JSON.parse = function () { throw new Error('mutated parse'); };
            JSON.stringify = function () { throw new Error('mutated stringify'); };
            var read = ctx.services.read('inspect', { value: ctx.input.value });
            var progress = ctx.services.progress({ phase: 'read' });
            var unavailable = ctx.services.action('ignored');
            var mutations = 0;
            try { ctx.services.read = function () {}; } catch (_) { mutations++; }
            try { read.tag = 'forged'; } catch (_) { mutations++; }
            return { data: {
              readTag: read.tag,
              readDataJson: read.dataJson,
              progress: progress,
              unavailableTag: unavailable.tag,
              mutations: mutations,
              nativeHidden: typeof globalThis.__boundServices === 'undefined'
                && typeof globalThis.serviceRead === 'undefined'
                && typeof globalThis.serviceProgress === 'undefined'
            }};
            """, new MechanicProjection { Input = "{\"value\":7}" }, ExecutionLimits.ReadModel, capabilities);

        Assert.True(run.Ok, run.Error);
        Assert.Equal("inspect", capabilities.Alias);
        Assert.Equal("{\"value\":7}", capabilities.InputJson);
        Assert.Equal("{\"phase\":\"read\"}", capabilities.ProgressJson);
        using var output = JsonDocument.Parse(run.Output.Data);
        Assert.Equal("completed", output.RootElement.GetProperty("readTag").GetString());
        Assert.Equal("{\"value\":8}", output.RootElement.GetProperty("readDataJson").GetString());
        Assert.Equal("accepted", output.RootElement.GetProperty("progress").GetString());
        Assert.Equal("unavailable", output.RootElement.GetProperty("unavailableTag").GetString());
        Assert.Equal(2, output.RootElement.GetProperty("mutations").GetInt32());
        Assert.True(output.RootElement.GetProperty("nativeHidden").GetBoolean());
    }

    [Fact]
    public async Task Malformed_progress_attempts_are_bounded_before_the_native_channel()
    {
        var capabilities = new Capabilities();
        var run = await new JintMechanicEngine().RunServiceAsync("""
            var cyclic = {}; cyclic.self = cyclic;
            var rejected = 0;
            for (var i = 0; i < 32; i++) {
              try { ctx.services.progress(cyclic); } catch (_) { rejected++; }
            }
            try { ctx.services.progress({ phase: 'late' }); } catch (_) { rejected++; }
            return { data: { rejected: rejected } };
            """, new MechanicProjection(), ExecutionLimits.ReadModel, capabilities);

        Assert.True(run.Ok, run.Error);
        Assert.Equal(0, capabilities.ProgressCalls);
        using var output = JsonDocument.Parse(run.Output.Data);
        Assert.Equal(33, output.RootElement.GetProperty("rejected").GetInt32());
    }

    [Fact]
    public async Task Noncooperating_read_is_bounded_by_remaining_computation_time()
    {
        var capabilities = new NeverCompletingCapabilities();
        var elapsed = Stopwatch.StartNew();
        var run = await new JintMechanicEngine().RunServiceAsync(
            "ctx.services.read('inspect', {}); return {data:{unexpected:true}};",
            new MechanicProjection(),
            ExecutionLimits.ReadModel with { Timeout = TimeSpan.FromMilliseconds(100) },
            capabilities);
        elapsed.Stop();

        Assert.False(run.Ok);
        Assert.Equal("cancelled", run.LimitHit);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2));
        capabilities.Complete();
        await Task.Delay(25);
        Assert.Equal(1, capabilities.Calls);
    }

    [Fact]
    public async Task Service_state_and_capabilities_are_isolated_per_run()
    {
        const string source = """
            globalThis.serviceCounter = (globalThis.serviceCounter || 0) + 1;
            var read = ctx.services.read('inspect', { call: globalThis.serviceCounter });
            return {data:{counter:globalThis.serviceCounter,value:read.dataJson}};
            """;
        var firstCapabilities = new Capabilities(11);
        var secondCapabilities = new Capabilities(22);
        var engine = new JintMechanicEngine();

        var first = await engine.RunServiceAsync(
            source, new MechanicProjection(), ExecutionLimits.ReadModel, firstCapabilities);
        var second = await engine.RunServiceAsync(
            source, new MechanicProjection(), ExecutionLimits.ReadModel, secondCapabilities);

        Assert.True(first.Ok, first.Error);
        Assert.True(second.Ok, second.Error);
        Assert.Contains("\"counter\":1", first.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\"counter\":1", second.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\\\"value\\\":11", first.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\\\"value\\\":22", second.Output.Data, StringComparison.Ordinal);
        Assert.Equal("{\"call\":1}", firstCapabilities.InputJson);
        Assert.Equal("{\"call\":1}", secondCapabilities.InputJson);
    }

    [Fact]
    public async Task Ordinary_mechanics_receive_no_service_surface()
    {
        var run = await new JintMechanicEngine().RunAsync(
            "return {data:{services:typeof ctx.services}};",
            new MechanicProjection(),
            ExecutionLimits.ReadModel);

        Assert.True(run.Ok, run.Error);
        Assert.Equal("{\"services\":\"undefined\"}", run.Output.Data);
    }

    private class Capabilities(int value = 8) : IApplicationReadOnlyServiceCapabilities
    {
        public string Alias { get; private set; } = "";
        public string InputJson { get; private set; } = "";
        public string ProgressJson { get; private set; } = "";
        public int ProgressCalls { get; private set; }

        public virtual Task<InteractionInvocationResult> ReadAsync(
            string alias,
            string inputJson,
            CancellationToken cancellationToken = default)
        {
            Alias = alias;
            InputJson = inputJson;
            return Task.FromResult(InteractionInvocationResult.Completed(
                JsonSerializer.Serialize(new { value }),
                new(Hash, Hash, Hash, Hash, Hash)));
        }

        public ApplicationServiceProgressDisposition TryWriteProgress(string dataJson)
        {
            ProgressCalls++;
            ProgressJson = dataJson;
            return ApplicationServiceProgressDisposition.Accepted;
        }
    }

    private sealed class NeverCompletingCapabilities : IApplicationReadOnlyServiceCapabilities
    {
        private readonly TaskCompletionSource<InteractionInvocationResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public Task<InteractionInvocationResult> ReadAsync(
            string alias,
            string inputJson,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return _completion.Task;
        }

        public ApplicationServiceProgressDisposition TryWriteProgress(string dataJson) =>
            ApplicationServiceProgressDisposition.Accepted;

        public void Complete() => _completion.TrySetResult(InteractionInvocationResult.Completed(
            "{}", new(Hash, Hash, Hash, Hash, Hash)));
    }
}
