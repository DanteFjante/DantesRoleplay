using DantesRoleplay.MCPServer;
using DantesRoleplay.Retrieval;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace DantesRoleplay.Interactions.Tests;

public sealed class InteractionOuterProviderSelectionTests
{
    [Fact]
    public async Task Local_outer_uses_fixed_outer_contracts_and_rejects_identity_drift()
    {
        var local = new CapturingLocal(new LocalModelIdentity("ollama", "outer-model", "digest", "outer-profile"),
            "{\"decision\":\"delegate\",\"intentText\":\"Resolve the declared action.\"}");
        var provider = new LocalInteractionOuterProvider(new InteractionOuterLocalCompletionProvider(
            local, new() { Model = "outer-model", Profile = "outer-profile", MaximumOutputBytes = 4_000 }));

        var result = await provider.DecideAsync(new("I attempt an action.", "INNER_UNKNOWN",
            new("unknown", "INNER_UNKNOWN", "No route.", ["safe.evidence"],
                "interaction-receipt.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            new("fixture-app", "fixture-space", 1, new string('A', 64), new string('B', 64)),
            [new("player", "I attempt an action.")]));

        Assert.True(result.Available);
        Assert.Equal(InteractionOuterDecision.Delegate, result.Decision);
        Assert.NotNull(local.Request);
        Assert.Equal(InteractionOuterProtocol.OuterTurnTask, local.Request!.TaskClass);
        Assert.Equal(InteractionOuterProtocol.OuterTurnPrompt, local.Request.SystemPrompt);
        Assert.Equal(InteractionOuterProtocol.OuterTurnSchema, local.Request.ResponseSchema);
        using var observation = JsonDocument.Parse(local.Request.UserPrompt);
        Assert.Equal("INNER_UNKNOWN", observation.RootElement.GetProperty("PriorSafeResultCode").GetString());
        Assert.Equal("unknown", observation.RootElement.GetProperty("PriorSafeResolution")
            .GetProperty("Status").GetString());
        Assert.Equal("interaction-receipt.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            observation.RootElement.GetProperty("PriorSafeResolution").GetProperty("ReceiptReference").GetString());
        Assert.Equal("fixture-app",
            observation.RootElement.GetProperty("BoundApplication").GetProperty("ApplicationId").GetString());
        Assert.Equal("player", observation.RootElement.GetProperty("VisibleTranscript")[0]
            .GetProperty("Role").GetString());
        Assert.Contains("BoundApplication is authoritative only for the exact application",
            local.Request.SystemPrompt, StringComparison.Ordinal);

        local.Identity = local.Identity with { Profile = "inner-profile" };
        var drift = await provider.DecideAsync(new("I attempt another action."));

        Assert.False(drift.Available);
        Assert.Equal("LOCAL_OUTER_MODEL_IDENTITY_MISMATCH", drift.Code);
    }

    [Fact]
    public async Task Local_outer_task_agenda_uses_its_closed_no_tools_contract()
    {
        var local = new CapturingLocal(new LocalModelIdentity("ollama", "outer-model", "digest", "outer-profile"),
            "{\"tasks\":[{\"intentText\":\"Prepare\",\"dependsOn\":[],\"batches\":[{\"intentText\":\"Inspect\"},{\"intentText\":\"Act\"}]}]}");
        var provider = new LocalInteractionOuterProvider(new InteractionOuterLocalCompletionProvider(
            local, new() { Model = "outer-model", Profile = "outer-profile", MaximumOutputBytes = 4_000 }));

        var result = await provider.CreateAgendaAsync(new("Prepare safely."));

        Assert.True(result.Available);
        Assert.Equal(2, Assert.Single(result.Agenda!.Tasks).Batches.Count);
        Assert.Equal(InteractionOuterProtocol.TaskAgendaTask, local.Request!.TaskClass);
        Assert.Equal(InteractionOuterProtocol.TaskAgendaPrompt, local.Request.SystemPrompt);
        Assert.Equal(InteractionOuterProtocol.TaskAgendaSchema, local.Request.ResponseSchema);
        Assert.DoesNotContain("maxItems", local.Request.ResponseSchema, StringComparison.Ordinal);
        Assert.Contains("one task and one", local.Request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("batch for a single lookup", local.Request.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selected_local_provider_never_calls_remote()
    {
        var local = new Adapter(InteractionOuterProviderKind.Local, "LOCAL");
        var remote = new Adapter(InteractionOuterProviderKind.Remote, "REMOTE");
        var provider = new SelectedInteractionOuterProvider(new()
        {
            Provider = InteractionOuterProviderKind.Local
        }, [local, remote]);

        var result = await provider.DecideAsync(new("Respond locally."));

        Assert.True(result.Available);
        Assert.Equal("LOCAL", result.Code);
        Assert.Equal(1, local.TurnCalls);
        Assert.Equal(0, remote.TurnCalls);
    }

    [Fact]
    public async Task Selected_remote_provider_never_calls_local_for_narration()
    {
        var local = new Adapter(InteractionOuterProviderKind.Local, "LOCAL");
        var remote = new Adapter(InteractionOuterProviderKind.Remote, "REMOTE");
        var provider = new SelectedInteractionOuterProvider(new()
        {
            Provider = InteractionOuterProviderKind.Remote
        }, [local, remote]);

        var result = await provider.NarrateAsync(new("Message", "succeeded", "OK", [], []));

        Assert.True(result.Available);
        Assert.Equal("REMOTE", result.Code);
        Assert.Equal(0, local.NarrationCalls);
        Assert.Equal(1, remote.NarrationCalls);
    }

    [Fact]
    public async Task Selected_local_task_agenda_never_calls_remote()
    {
        var local = new Adapter(InteractionOuterProviderKind.Local, "LOCAL");
        var remote = new Adapter(InteractionOuterProviderKind.Remote, "REMOTE");
        var provider = new SelectedInteractionOuterProvider(new()
        {
            Provider = InteractionOuterProviderKind.Local
        }, [local, remote]);

        var result = await provider.CreateAgendaAsync(new("Bounded goal"));

        Assert.True(result.Available);
        Assert.Equal("LOCAL", result.Code);
        Assert.Equal(1, local.AgendaCalls);
        Assert.Equal(0, remote.AgendaCalls);
    }
}

public sealed class ConfiguredInteractionOuterProviderOptionsTests
{
    [Fact]
    public void Local_outer_settings_use_a_distinct_profile_and_outer_task_allowlist()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InteractionOuter:Provider"] = "local",
            ["InteractionOuter:Local:Enabled"] = "true",
            ["InteractionOuter:Local:Endpoint"] = "http://127.0.0.1:11435",
            ["InteractionOuter:Local:Model"] = "outer-model",
            ["InteractionOuter:Local:Profile"] = "outer-profile",
            ["InteractionOuter:Local:MaxOutputTokens"] = "512"
        }).Build();

        var options = new InteractionOuterHostOptions(configuration);

        Assert.Equal(InteractionOuterProviderKind.Local, options.Selection.Provider);
        Assert.True(options.LocalCompletion.Enabled);
        Assert.Equal("outer-model", options.LocalCompletion.Model);
        Assert.Equal("outer-profile", options.LocalCompletion.Profile);
        Assert.Equal([InteractionOuterProtocol.NarrationTask, InteractionOuterProtocol.OuterTurnTask,
                InteractionPlannerProtocol.TaskClass, InteractionOuterProtocol.TaskAgendaTask],
            options.LocalCompletion.AllowedTaskClasses.Order(StringComparer.Ordinal));
        Assert.Equal("outer-profile", options.LocalAdapter.Profile);
    }

    [Theory]
    [InlineData("automatic")]
    [InlineData("remote-fallback")]
    public void Unsupported_provider_modes_fail_during_startup_configuration(string mode)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InteractionOuter:Provider"] = mode
        }).Build();

        var error = Assert.Throws<InvalidOperationException>(() => new InteractionOuterHostOptions(configuration));

        Assert.Contains("local' or 'remote", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_loopback_local_endpoint_fails_during_startup_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InteractionOuter:Local:Endpoint"] = "https://example.com"
        }).Build();

        Assert.Throws<InvalidOperationException>(() => new InteractionOuterHostOptions(configuration));
    }
}

file sealed class CapturingLocal(LocalModelIdentity identity, string json) : ILocalStructuredCompletionProvider
{
    public LocalModelIdentity Identity { get; set; } = identity;
    public StructuredCompletionRequest? Request { get; private set; }

    public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new LocalModelStatus(true, Identity));

    public Task<StructuredCompletionResult> CompleteAsync(StructuredCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        Request = request;
        return Task.FromResult(new StructuredCompletionResult(Identity, json, 1));
    }
}

file sealed class Adapter(InteractionOuterProviderKind kind, string code) : IInteractionOuterProviderAdapter
{
    public InteractionOuterProviderKind Kind => kind;
    public int TurnCalls { get; private set; }
    public int NarrationCalls { get; private set; }
    public int AgendaCalls { get; private set; }

    public Task<InteractionOuterTurnResult> DecideAsync(InteractionOuterTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        TurnCalls++;
        return Task.FromResult(new InteractionOuterTurnResult(true, InteractionOuterDecision.Respond, "Safe", code));
    }

    public Task<InteractionNarrationResult> NarrateAsync(InteractionNarrationRequest request,
        CancellationToken cancellationToken = default)
    {
        NarrationCalls++;
        return Task.FromResult(new InteractionNarrationResult(true, "Safe", code));
    }

    public Task<InteractionTaskAgendaResult> CreateAgendaAsync(InteractionTaskAgendaRequest request,
        CancellationToken cancellationToken = default)
    {
        AgendaCalls++;
        return Task.FromResult(new InteractionTaskAgendaResult(true,
            InteractionTaskAgenda.Parse(JsonSerializer.Serialize(new
            {
                tasks = new[] { new { intentText = request.GoalText, dependsOn = Array.Empty<int>(),
                    batches = new[] { new { intentText = request.GoalText } } } }
            })), code));
    }
}
