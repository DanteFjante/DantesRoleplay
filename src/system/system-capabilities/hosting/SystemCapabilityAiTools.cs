using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.DataAccess.Composition;

/// <summary>
/// Creates authorization-scoped AI tools over the existing in-process system capability catalog.
/// No HTTP or MCP adapter is involved. Writes still use preflight, trusted confirmation, and the
/// capability's existing idempotent execution boundary.
/// </summary>
public static class SystemCapabilityAiTools
{
    public sealed record Options(
        bool IncludeWriteCapabilities = true,
        bool IncludeSecretCapabilities = false);

    public static IReadOnlyList<IAiTool> CreateTools(
        ISystemCapabilityCatalog catalog,
        SystemCapabilityInvocationContext context,
        ISystemCapabilityAiWriteApprovalGate? writeApprovalGate = null,
        Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(context);
        options ??= new();
        var discovery = catalog.Discover(context);
        if (!discovery.Ok) return [];
        return discovery.Capabilities
            .Where(value => options.IncludeSecretCapabilities ||
                            value.Sensitivity != SystemCapabilitySensitivity.Secret)
            .Where(value => options.IncludeWriteCapabilities || value.Mode == SystemCapabilityMode.Read)
            .Select(value => value.Mode == SystemCapabilityMode.Read
                ? (IAiTool)new SystemCapabilityReadAiTool(catalog, context, value)
                : new SystemCapabilityWriteAiTool(catalog, context, value, writeApprovalGate))
            .ToArray();
    }

    public static IReadOnlyList<IAiTool> CreateReadTools(
        ISystemCapabilityCatalog catalog,
        SystemCapabilityInvocationContext context) =>
        CreateTools(catalog, context, options: new(IncludeWriteCapabilities: false));

    private sealed class SystemCapabilityReadAiTool(
        ISystemCapabilityCatalog catalog,
        SystemCapabilityInvocationContext context,
        SystemCapabilityDescriptor descriptor) : IAiTool
    {
        public AiToolDefinition Definition { get; } = new(
            ToolName(descriptor.Id),
            $"Read the in-process system capability '{descriptor.Id}'. {descriptor.Description}",
            descriptor.InputSchemaJson);

        public async Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            var result = await catalog.ReadAsync(
                descriptor.Id,
                invocation.Arguments.GetRawText(),
                context with { CorrelationId = Correlation(invocation.CallId) },
                cancellationToken);
            if (!result.Ok || result.Data is null)
                return AiToolResult.Failure(
                    result.Error?.Code ?? "SYSTEM_CAPABILITY_FAILED",
                    result.Error?.Message ?? "The system capability did not return data.");
            return AiToolResult.Success(result.Data.Value.GetRawText());
        }
    }

    private sealed class SystemCapabilityWriteAiTool(
        ISystemCapabilityCatalog catalog,
        SystemCapabilityInvocationContext context,
        SystemCapabilityDescriptor descriptor,
        ISystemCapabilityAiWriteApprovalGate? approvalGate) : IAiTool
    {
        public AiToolDefinition Definition { get; } = new(
            ToolName(descriptor.Id),
            $"Write through the in-process system capability '{descriptor.Id}'. " +
            $"Trusted confirmation and an idempotency token are required. {descriptor.Description}",
            descriptor.InputSchemaJson);

        public async Task<AiToolResult> InvokeAsync(
            AiToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            var invocationContext = context with { CorrelationId = Correlation(invocation.CallId) };
            var inputJson = invocation.Arguments.GetRawText();
            var checkedWrite = await catalog.PreflightWriteAsync(
                descriptor.Id,
                descriptor.Fingerprint,
                inputJson,
                [],
                invocationContext,
                cancellationToken);
            if (!checkedWrite.Ok || checkedWrite.Preflight is null)
                return Failure(checkedWrite.Error, "SYSTEM_CAPABILITY_PREFLIGHT_FAILED",
                    "The system capability preflight failed.");
            if (!checkedWrite.Preflight.Ok ||
                checkedWrite.Preflight.Status != SystemCapabilityPreflightStatuses.Ready)
                return AiToolResult.Failure(
                    "SYSTEM_CAPABILITY_NOT_READY",
                    JsonSerializer.Serialize(new
                    {
                        checkedWrite.Preflight.SafeSummary,
                        checkedWrite.Preflight.DeferredStepIds
                    }));
            if (approvalGate is null)
                return ConfirmationRequired(checkedWrite.Preflight);

            var decision = await approvalGate.ConfirmAsync(new(
                descriptor,
                invocation.Arguments.Clone(),
                checkedWrite.Preflight,
                invocationContext), cancellationToken);
            if (!ValidApproval(decision))
                return decision?.Approved == false
                    ? AiToolResult.Failure("SYSTEM_CAPABILITY_WRITE_DECLINED", "The trusted host declined this write.")
                    : AiToolResult.Failure("SYSTEM_CAPABILITY_APPROVAL_INVALID", "The trusted approval evidence is invalid.");

            var executed = await catalog.ExecuteWriteAsync(
                descriptor.Id,
                descriptor.Fingerprint,
                inputJson,
                new(
                    invocationContext,
                    decision!.RequestToken,
                    decision.Intent,
                    descriptor.ProcedureIds,
                    checkedWrite.AuthorizationEvidence,
                    checkedWrite.Preflight.ExecutionEvidenceJson),
                cancellationToken);
            if (!executed.Ok || executed.Data is null)
                return Failure(executed.Error, "SYSTEM_CAPABILITY_WRITE_FAILED",
                    "The system capability write failed.");
            return AiToolResult.Success(JsonSerializer.Serialize(new
            {
                data = executed.Data.Value,
                executed.OperationId,
                executed.ReadBackFingerprint
            }));
        }

        private static AiToolResult ConfirmationRequired(SystemCapabilityWritePreflight preflight) =>
            AiToolResult.Failure(
                "SYSTEM_CAPABILITY_CONFIRMATION_REQUIRED",
                JsonSerializer.Serialize(new
                {
                    preflight.SafeSummary,
                    preflight.AffectedReferences,
                    preflight.PreconditionFingerprint
                }));

        private static bool ValidApproval(SystemCapabilityAiApprovalDecision? decision) =>
            decision is { Approved: true, RequestToken.Length: 32 } &&
            decision.RequestToken.All(value => char.IsAsciiDigit(value) || value is >= 'a' and <= 'f') &&
            !string.IsNullOrWhiteSpace(decision.Intent) && decision.Intent.Length <= 8_000;
    }

    private static AiToolResult Failure(
        SystemCapabilityError? error,
        string defaultCode,
        string defaultMessage) =>
        AiToolResult.Failure(error?.Code ?? defaultCode, error?.Message ?? defaultMessage);

    private static string Correlation(string callId)
    {
        var value = $"ai-tool:{callId}";
        return value.Length <= 128 ? value : value[..128];
    }

    private static string ToolName(string capabilityId)
    {
        var name = new string(capabilityId.Select(value => char.IsLetterOrDigit(value) || value is '_' or '-'
            ? value : '_').ToArray());
        return name.Length <= 64 ? name : name[..64];
    }
}

public sealed class SystemCapabilityAiToolSource(
    ISystemCapabilityCatalog catalog) : ISystemAiToolSource
{
    public IReadOnlyList<IAiTool> CreateTools(SystemAiToolSourceContext context) =>
        SystemCapabilityAiTools.CreateTools(
            catalog,
            context.Invocation,
            context.CapabilityWriteApproval);
}
