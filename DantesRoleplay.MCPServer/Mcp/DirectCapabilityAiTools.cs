using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.AI;
using DantesRoleplay.Authorization;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>
/// Publishes one local-AI tool per underlying MCP capability kind. The model never sees or calls
/// the orient/query/commit multiplexers; this adapter invokes their current in-process dispatcher
/// while both transports are being converged on shared capability owners.
/// </summary>
public sealed class DirectCapabilityAiToolSource(
    IServiceProvider services,
    IPrivateOperatorAuthorizationPolicy authorization) : ISystemAiToolSource
{
    private static readonly MethodInfo QueryMethod = typeof(QueryMcpTool).GetMethod(nameof(QueryMcpTool.QueryAsync))!;
    private static readonly MethodInfo CommitMethod = typeof(CommitMcpTool).GetMethod(nameof(CommitMcpTool.CommitAsync))!;

    public IReadOnlyList<IAiTool> CreateTools(SystemAiToolSourceContext context)
    {
        var authorizer = new InvocationAuthorizer(authorization, context.Invocation);
        var result = new List<IAiTool>();
        result.AddRange(McpVerbCatalog.QueryKinds.Where(value => value.Name != "capabilities")
            .Select(value => (IAiTool)new DirectQueryTool(services, authorizer, value)));
        result.AddRange(McpVerbCatalog.CommitKinds
            .Select(value => (IAiTool)new DirectCommitTool(services, authorizer, context.ToolApproval, value)));
        return result;
    }

    private abstract class DirectTool(
        IServiceProvider services,
        IPrivateOperatorRequestAuthorizer authorizer,
        MethodInfo method) : IAiTool
    {
        private static readonly JsonSerializerOptions OutputJson = new(JsonSerializerDefaults.Web);
        protected IServiceProvider Services { get; } = services;
        protected IPrivateOperatorRequestAuthorizer Authorizer { get; } = authorizer;
        protected MethodInfo Method { get; } = method;
        public abstract AiToolDefinition Definition { get; }

        public abstract Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default);

        protected async Task<AiToolResult> InvokeDispatcherAsync(
            object target,
            IReadOnlySet<string> modelParameters,
            string kind,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            try
            {
                var values = Method.GetParameters().Select(parameter => Value(
                    parameter, modelParameters, kind, arguments, cancellationToken)).ToArray();
                var pending = (Task<ToolEnvelope>)Method.Invoke(target, values)!;
                var envelope = await pending;
                if (!envelope.Ok)
                    return AiToolResult.Failure(envelope.Error?.Code ?? "DIRECT_CAPABILITY_FAILED",
                        envelope.Error is null ? "The direct capability failed."
                            : $"{envelope.Error.Why} Recovery: {envelope.Error.Fix}");
                return AiToolResult.Success(JsonSerializer.Serialize(envelope, OutputJson));
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                return AiToolResult.Failure("DIRECT_CAPABILITY_FAILED", Bound(exception.InnerException.Message));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return AiToolResult.Failure("DIRECT_CAPABILITY_FAILED", Bound(exception.Message));
            }
        }

        private object? Value(
            ParameterInfo parameter,
            IReadOnlySet<string> modelParameters,
            string kind,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            if (parameter.Name == "kind") return kind;
            if (parameter.ParameterType == typeof(CancellationToken)) return cancellationToken;
            if (parameter.ParameterType == typeof(IPrivateOperatorRequestAuthorizer)) return Authorizer;
            if (modelParameters.Contains(parameter.Name!))
            {
                if (!arguments.TryGetProperty(parameter.Name!, out var value))
                    return parameter.HasDefaultValue ? parameter.DefaultValue : null;
                if (parameter.Name == "payload" && value.ValueKind == JsonValueKind.Object)
                    return value.GetRawText();
                return value.Deserialize(parameter.ParameterType);
            }
            return Services.GetService(parameter.ParameterType) ??
                (parameter.HasDefaultValue ? parameter.DefaultValue :
                    throw new InvalidOperationException($"Direct capability dependency '{parameter.ParameterType.Name}' is unavailable."));
        }

        protected static string Schema(IEnumerable<string> names, MethodInfo method, bool payloadRequired = false)
        {
            var parameters = method.GetParameters().ToDictionary(value => value.Name!, StringComparer.Ordinal);
            var properties = new JsonObject();
            foreach (var name in names)
            {
                if (name == "payload")
                {
                    properties[name] = new JsonObject { ["type"] = "object" };
                    continue;
                }
                properties[name] = TypeSchema(parameters[name].ParameterType);
            }
            var root = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = properties
            };
            if (payloadRequired) root["required"] = new JsonArray("payload");
            return root.ToJsonString();
        }

        private static JsonNode TypeSchema(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(string)) return new JsonObject { ["type"] = "string" };
            if (type == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
            if (type == typeof(int) || type == typeof(long)) return new JsonObject { ["type"] = "integer" };
            if (type == typeof(string[])) return new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" }
            };
            throw new InvalidOperationException($"Unsupported direct AI parameter type '{type.Name}'.");
        }

        protected static string ToolName(string prefix, string kind)
        {
            var normalized = new string(kind.Select(value => char.IsAsciiLetterOrDigit(value) ? value : '_').ToArray());
            var name = $"{prefix}_{normalized}";
            return name.Length <= 64 ? name : name[..64];
        }

        private static string Bound(string value) => value.Length <= 500 ? value : value[..500];
    }

    private sealed class DirectQueryTool : DirectTool
    {
        private readonly QueryKindSpec spec;
        private readonly IReadOnlySet<string> inputs;

        public DirectQueryTool(IServiceProvider services, IPrivateOperatorRequestAuthorizer authorizer,
            QueryKindSpec spec) : base(services, authorizer, QueryMethod)
        {
            this.spec = spec;
            inputs = spec.Reads.ToHashSet(StringComparer.Ordinal);
            Definition = new(ToolName("read", spec.Name), spec.Returns, Schema(inputs, QueryMethod));
        }

        public override AiToolDefinition Definition { get; }

        public override Task<AiToolResult> InvokeAsync(AiToolInvocation invocation,
            CancellationToken cancellationToken = default) =>
            InvokeDispatcherAsync(new QueryMcpTool(), inputs, spec.Name, invocation.Arguments, cancellationToken);
    }

    private sealed class DirectCommitTool : DirectTool
    {
        private static readonly IReadOnlySet<string> Inputs =
            new HashSet<string>(["payload", "intent", "proceduresUsed", "dryRun"], StringComparer.Ordinal);
        private readonly IAiToolApprovalGate? approval;
        private readonly CommitKindSpec spec;

        public DirectCommitTool(IServiceProvider services, IPrivateOperatorRequestAuthorizer authorizer,
            IAiToolApprovalGate? approval, CommitKindSpec spec) : base(services, authorizer, CommitMethod)
        {
            this.approval = approval;
            this.spec = spec;
            Definition = new(ToolName("write", spec.Name), spec.Summary +
                " Non-preview execution requires trusted host confirmation.", Schema(Inputs, CommitMethod, true));
        }

        public override AiToolDefinition Definition { get; }

        public override async Task<AiToolResult> InvokeAsync(AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            var preview = invocation.Arguments.TryGetProperty("dryRun", out var dryRun) && dryRun.GetBoolean();
            if (!preview && (approval is null || !await approval.ConfirmAsync(new(
                    Definition.Name, Definition.Description, invocation.Arguments.Clone()), cancellationToken)))
                return AiToolResult.Failure("AI_TOOL_CONFIRMATION_REQUIRED",
                    "Trusted host confirmation is required before this direct write can run.");
            return await InvokeDispatcherAsync(new CommitMcpTool(), Inputs, spec.Name,
                invocation.Arguments, cancellationToken);
        }
    }

    private sealed class InvocationAuthorizer(
        IPrivateOperatorAuthorizationPolicy policy,
        SystemCapabilityInvocationContext context) : IPrivateOperatorRequestAuthorizer
    {
        public PrivateOperatorAuthorizationDecision Authorize(PrivateOperatorCapability capability) =>
            policy.Evaluate(new(context.Principal, capability, context.Scope, context.CorrelationId));
    }
}
