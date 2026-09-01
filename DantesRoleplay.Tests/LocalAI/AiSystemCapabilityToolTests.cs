using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.Tests;

public sealed class AiSystemCapabilityToolTests
{
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Authorized_system_capability_is_callable_in_process_without_mcp()
    {
        var handler = new ReadHandler();
        var catalog = new SystemCapabilityCatalog(
            [handler], new BoundedJsonSchemaValidator(), new PrivateOperatorAuthorizationPolicy());
        var context = new SystemCapabilityInvocationContext(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "local-loopback"),
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "ai-test");
        var tools = SystemCapabilityAiTools.CreateReadTools(catalog, context);
        var provider = new ToolCallingProvider();
        var service = new AiService([provider], tools);

        var result = await service.SendTaskAsync(
            "test", "model", "read the value", allowedTools: ["system_test_read"]);

        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal("finished", result.Text);
        Assert.Equal("wanted", handler.Key);
        Assert.Equal("{\"value\":\"found\"}", provider.ToolResult);
    }

    [Fact]
    public async Task Authorized_write_uses_preflight_trusted_confirmation_and_idempotent_execution()
    {
        var handler = new WriteHandler();
        var catalog = new SystemCapabilityCatalog(
            [], new BoundedJsonSchemaValidator(), new PrivateOperatorAuthorizationPolicy(), [handler]);
        var context = Context();
        var tools = SystemCapabilityAiTools.CreateTools(catalog, context, new ApprovalGate());
        var provider = new ToolCallingProvider(
            "system_test_write",
            "{\"value\":\"wanted\"}");
        var service = new AiService([provider]);

        var result = await service.SendAgentRequestAsync(
            new("system.operator", "System Operator", "You administer the generic system."),
            new("test", "model", [new(AiMessageRole.User, "save the value")], AiRequestKind.Task),
            tools);

        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Equal(1, handler.PreflightCalls);
        Assert.Equal("wanted", handler.Saved);
        Assert.Contains("\"operationId\":\"operation-test\"", provider.ToolResult, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Write_without_a_trusted_confirmation_gate_does_not_execute()
    {
        var handler = new WriteHandler();
        var catalog = new SystemCapabilityCatalog(
            [], new BoundedJsonSchemaValidator(), new PrivateOperatorAuthorizationPolicy(), [handler]);
        var tool = Assert.Single(SystemCapabilityAiTools.CreateTools(catalog, Context()));

        var result = await tool.InvokeAsync(new(
            "call", tool.Definition.Name,
            JsonSerializer.SerializeToElement(new { value = "wanted" }),
            AiRequestKind.Task));

        Assert.False(result.Ok);
        Assert.Equal("SYSTEM_CAPABILITY_CONFIRMATION_REQUIRED", result.ErrorCode);
        Assert.Equal("", handler.Saved);
    }

    [Fact]
    public async Task Direct_tools_cannot_escape_the_originating_application_context()
    {
        var provider = new ToolCallingProvider(
            "context_probe",
            "{\"applicationId\":\"other-app\"}");
        var agent = new SystemAiAgentService([new ContextProbeSource()], new AiService([provider]));
        var context = Context() with
        {
            ApplicationId = ApplicationIdentifier.Parse("origin-app"),
            ResolutionFingerprint = new string('A', 64)
        };

        var result = await agent.SendAsync(
            new("test.agent", "Test agent", "Test direct application context."),
            new("test", "model", [new(AiMessageRole.User, "probe another application")], AiRequestKind.Task),
            context);

        Assert.True(result.Ok, result.ErrorMessage);
        Assert.Contains("AI_APPLICATION_CONTEXT_DENIED", provider.ToolResult, StringComparison.Ordinal);
    }

    private static SystemCapabilityInvocationContext Context() => new(
        TrustedPrincipalContext.VerifiedPrincipal(Principal, "local-loopback"),
        PrivateOperatorAuthorizationPolicy.PrivateHostScope,
        "ai-test");

    private sealed class ReadHandler : ISystemReadCapabilityHandler
    {
        public string Key { get; private set; } = "";
        public SystemCapabilityRegistration Registration { get; } = new(
            "system.test.read",
            1,
            "test",
            "Read a test value.",
            SystemCapabilityMode.Read,
            """{"type":"object","additionalProperties":false,"required":["key"],"properties":{"key":{"type":"string"}}}""",
            """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"string"}}}""",
            ["procedure.system.test.read"],
            PrivateOperatorCapability.Read,
            SystemCapabilitySensitivity.PublicMetadata,
            false,
            false);

        public Task<SystemCapabilityHandlerResult> ReadAsync(
            JsonElement input,
            CancellationToken cancellationToken = default)
        {
            Key = input.GetProperty("key").GetString()!;
            return Task.FromResult(SystemCapabilityHandlerResult.Success(
                JsonSerializer.SerializeToElement(new { value = "found" })));
        }
    }

    private sealed class ToolCallingProvider : IAiProvider
    {
        private readonly string _toolName;
        private readonly string _argumentsJson;
        private int _calls;
        public string ToolResult { get; private set; } = "";
        public AiProviderInfo Info { get; } = new("test", "Test");

        public ToolCallingProvider(
            string toolName = "system_test_read",
            string argumentsJson = "{\"key\":\"wanted\"}")
        {
            _toolName = toolName;
            _argumentsJson = argumentsJson;
        }

        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiModel>>([Model()]);

        public Task<AiProviderResponse> SendAsync(
            AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            _calls++;
            if (_calls == 1)
                return Task.FromResult(new AiProviderResponse(
                    true, Model(), "", "", [new("one", _toolName, _argumentsJson)]));
            ToolResult = request.Messages.Single(value => value.Role == AiMessageRole.Tool).Content;
            return Task.FromResult(new AiProviderResponse(true, Model(), "finished", "", []));
        }

        private static AiModel Model() => new(
            "test", "model", "Model",
            AiModelCapabilities.Messages | AiModelCapabilities.Tasks | AiModelCapabilities.Tools,
            []);
    }

    private sealed class ApprovalGate : ISystemCapabilityAiWriteApprovalGate
    {
        public Task<SystemCapabilityAiApprovalDecision> ConfirmAsync(
            SystemCapabilityAiApprovalRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SystemCapabilityAiApprovalDecision(
                true,
                "0123456789abcdef0123456789abcdef",
                "The operator confirmed the test write."));
    }

    private sealed class ContextProbeSource : ISystemAiToolSource
    {
        public IReadOnlyList<IAiTool> CreateTools(SystemAiToolSourceContext context) => [new ContextProbeTool()];
    }

    private sealed class ContextProbeTool : IAiTool
    {
        public AiToolDefinition Definition { get; } = new(
            "context_probe",
            "Probe an application-bound direct tool.",
            """{"type":"object","additionalProperties":false,"required":["applicationId"],"properties":{"applicationId":{"type":"string"}}}""");

        public Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AiToolResult.Success("{\"unexpected\":true}"));
    }

    private sealed class WriteHandler : ISystemWriteCapabilityHandler
    {
        public int PreflightCalls { get; private set; }
        public string Saved { get; private set; } = "";
        public SystemCapabilityRegistration Registration { get; } = new(
            "system.test.write",
            1,
            "test",
            "Write a test value.",
            SystemCapabilityMode.Write,
            """{"type":"object","additionalProperties":false,"required":["value"],"properties":{"value":{"type":"string"}}}""",
            """{"type":"object","additionalProperties":false,"required":["saved"],"properties":{"saved":{"type":"string"}}}""",
            ["procedure.system.test.write"],
            PrivateOperatorCapability.Modify,
            SystemCapabilitySensitivity.PrivateOperatorMetadata,
            true,
            true);

        public Task<SystemCapabilityWritePreflight> PreflightAsync(
            JsonElement input,
            IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps,
            CancellationToken cancellationToken = default)
        {
            PreflightCalls++;
            return Task.FromResult(SystemCapabilityWritePreflight.Ready(
                new string('A', 64),
                $"Save '{input.GetProperty("value").GetString()}'.",
                ["test:value"]));
        }

        public Task<SystemCapabilityWriteHandlerResult> ExecuteAsync(
            JsonElement input,
            SystemCapabilityWriteExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Saved = input.GetProperty("value").GetString()!;
            return Task.FromResult(SystemCapabilityWriteHandlerResult.Success(
                JsonSerializer.SerializeToElement(new { saved = Saved }),
                "operation-test",
                new string('B', 64)));
        }
    }
}
