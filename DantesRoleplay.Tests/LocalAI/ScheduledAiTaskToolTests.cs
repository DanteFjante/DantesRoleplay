using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.TriggerScheduling;

namespace DantesRoleplay.Tests;

public sealed class ScheduledAiTaskToolTests
{
    [Fact]
    public async Task Scheduled_ai_task_uses_existing_trigger_owner_and_embeds_bounded_agent_request()
    {
        var scheduling = new CapturingScheduling();
        var source = new ScheduledAiTaskToolSource(scheduling,
            new PrivateOperatorAuthorizationPolicy(), new StaticActivation());
        var principal = PrivateOperatorPrincipal.Create("test", "operator");
        var tools = source.CreateTools(new(
            new("operator", "Operator", "Operate."),
            new("test", "model", [new(AiMessageRole.User, "schedule")]),
            new(principal, PrivateOperatorAuthorizationPolicy.PrivateHostScope, "schedule-test"),
            null, null, () => []));
        var tool = Assert.Single(tools);
        var arguments = JsonSerializer.SerializeToElement(new
        {
            requestToken = "0123456789abcdef0123456789abcdef",
            operation = "one-time.register",
            applicationId = "example",
            schedule = new
            {
                id = "example.ai-task",
                version = 1,
                dueAtUtc = "2026-09-01T10:00:00Z",
                misfirePolicy = "fire-once",
                lifecycle = "active"
            },
            agent = new { id = "world.steward", name = "World Steward", identity = "Maintain the world." },
            provider = "ollama",
            model = "model",
            task = "Inspect the next session.",
            preview = true
        });

        var result = await tool.InvokeAsync(new("schedule-call", tool.Definition.Name, arguments, AiRequestKind.Task));

        Assert.True(result.Ok, result.ErrorMessage);
        Assert.NotNull(scheduling.Command);
        using var value = JsonDocument.Parse(scheduling.Command!.Value.Json);
        var notification = value.RootElement.GetProperty("notification");
        Assert.Equal("system.local-ai.task", notification.GetProperty("topic").GetString());
        Assert.Contains("Inspect the next session", notification.GetProperty("body").GetString(), StringComparison.Ordinal);
    }

    private sealed class CapturingScheduling : ITriggerSchedulingAdministrationService
    {
        public TriggerSchedulingAdministrationCommand? Command { get; private set; }
        public Task<TriggerSchedulingAdministrationView> QueryAsync(TriggerSchedulingAdministrationQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TriggerSchedulingAdministrationResult> PreviewAsync(TriggerSchedulingAdministrationCommand command,
            TriggerSchedulingAdministrationContext context, CancellationToken cancellationToken = default)
        {
            Command = command;
            using var value = JsonDocument.Parse(command.Value.Json);
            return Task.FromResult(new TriggerSchedulingAdministrationResult(command.Operation,
                command.ApplicationId, "preview", "operation.preview", value.RootElement.Clone()));
        }
        public Task<TriggerSchedulingAdministrationResult> CommitAsync(TriggerSchedulingAdministrationCommand command,
            TriggerSchedulingAdministrationContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StaticActivation : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId)
        {
            var hash = new string('A', 64);
            return new(applicationId, 1, 1, hash, hash, hash, hash, hash, hash,
                "coverage-v1", true, [], [], "operation", DateTime.UtcNow)
            {
                ResolutionFingerprint = new string('B', 64)
            };
        }
    }
}
