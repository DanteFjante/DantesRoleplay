using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.AI;
using DantesRoleplay.Applications;
using DantesRoleplay.Capabilities;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.SystemCapabilities;

public static class SystemCapabilityIds
{
    public const string Applications = "system.applications";
    public const string Sources = "system.sources";
    public const string ApplicationPreview = "system.application-preview";
    public const string Dependencies = "system.dependencies";
    public const string ApplicationRegister = "system.application.register";
    public const string SourceRegister = "system.source.register";
    public const string ExtensionRegister = "system.extension.register";
    public const string ComponentTypeRegister = "system.component-type.register";
    public const string ApplicationActivate = "system.application.activate";
    public const string StateSpaceCreate = "system.state-space.create";
    public const string StateSpaceUpgrade = "system.state-space.upgrade";
    public const string StateSpaceAdoptLegacy = "system.state-space.adopt-legacy";
    public const string MechanicSandboxDrafts = "system.mechanic-sandbox.drafts";
    public const string MechanicSandboxDraft = "system.mechanic-sandbox.draft";
    public const string MechanicSandboxPromote = "system.mechanic-sandbox.promote";
    public const string InteractionContextPack = "system.interaction-context-pack";
    public const string InteractionRecipes = "system.interaction-recipes";
    public const string InteractionRecipeReview = "system.interaction-recipe.review";
    public const string MechanicOpportunities = "system.mechanic-opportunities";
}

public enum SystemCapabilityMode
{
    Read,
    Write
}

public enum SystemCapabilitySensitivity
{
    PublicMetadata,
    PrivateOperatorMetadata,
    Secret
}

public sealed record SystemCapabilityRegistration(
    string Id,
    int Version,
    string Owner,
    string Description,
    SystemCapabilityMode Mode,
    string InputSchemaJson,
    string OutputSchemaJson,
    IReadOnlyList<string> ProcedureIds,
    PrivateOperatorCapability RequiredCapability,
    SystemCapabilitySensitivity Sensitivity,
    bool RequiresConfirmation,
    bool RequiresIdempotencyKey);

public sealed record SystemCapabilityDescriptor(
    string Id,
    int Version,
    string Fingerprint,
    string Owner,
    string Description,
    SystemCapabilityMode Mode,
    string ModeName,
    string InputSchemaProfile,
    string InputSchemaJson,
    string InputSchemaHash,
    string OutputSchemaProfile,
    string OutputSchemaJson,
    string OutputSchemaHash,
    IReadOnlyList<string> ProcedureIds,
    PrivateOperatorCapability RequiredCapability,
    string RequiredCapabilityName,
    SystemCapabilitySensitivity Sensitivity,
    string SensitivityName,
    bool RequiresConfirmation,
    bool RequiresIdempotencyKey)
{
    public CapabilityContractDescriptor Contract => SystemCapabilityContractAdapter.Create(this);
}

public sealed record SystemCapabilityInvocationContext(
    TrustedPrincipalContext Principal,
    string Scope,
    string CorrelationId)
{
    public ApplicationIdentifier? ApplicationId { get; init; }
    public string ResolutionFingerprint { get; init; } = "";
    public string StateSpaceId { get; init; } = "";

    public static SystemCapabilityInvocationContext FromAuthorization(
        AuthorizationAuditEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var principal = evidence.Allowed &&
            TrustedPrincipalContext.IsValidPrincipalId(evidence.PrincipalReference) &&
            Bounded(evidence.AuthenticationMethod, 64)
            ? TrustedPrincipalContext.VerifiedPrincipal(
                evidence.PrincipalReference,
                evidence.AuthenticationMethod)
            : TrustedPrincipalContext.Unauthenticated(
                Bounded(evidence.ReasonCode, 80) ? evidence.ReasonCode : "PRIVATE_OPERATOR_UNAUTHENTICATED");
        return new(
            principal,
            Bounded(evidence.Scope, 80) ? evidence.Scope : "invalid",
            Bounded(evidence.CorrelationId, 128) ? evidence.CorrelationId : "invalid");
    }

    private static bool Bounded(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
}

public sealed record SystemCapabilityError(
    string Code,
    string Message,
    string Recovery,
    IReadOnlyList<SchemaDiagnostic> Diagnostics);

public sealed record SystemCapabilityReadResult(
    bool Ok,
    string CapabilityId,
    string DescriptorFingerprint,
    JsonElement? Data,
    SystemCapabilityError? Error,
    AuthorizationAuditEvidence AuthorizationEvidence);

public sealed record SystemCapabilityDiscoveryResult(
    bool Ok,
    IReadOnlyList<SystemCapabilityDescriptor> Capabilities,
    SystemCapabilityError? Error,
    AuthorizationAuditEvidence AuthorizationEvidence);

public sealed record SystemCapabilityHandlerResult(
    bool Ok,
    JsonElement? Data,
    SystemCapabilityError? Error)
{
    public static SystemCapabilityHandlerResult Success(JsonElement data) =>
        new(true, data.Clone(), null);

    public static SystemCapabilityHandlerResult Failure(
        string code,
        string message,
        string recovery) =>
        new(false, null, new(code, message, recovery, []));
}

public interface ISystemReadCapabilityHandler
{
    SystemCapabilityRegistration Registration { get; }

    Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input,
        CancellationToken cancellationToken = default);

    Task<SystemCapabilityHandlerResult> ReadAsync(
        JsonElement input,
        SystemCapabilityInvocationContext context,
        CancellationToken cancellationToken = default) => ReadAsync(input, cancellationToken);
}

public static class SystemCapabilityPreflightStatuses
{
    public const string Ready = "ready";
    public const string Deferred = "deferred";
}

public sealed record SystemCapabilityEarlierStep(
    string StepId,
    string CapabilityId,
    string InputJson);

public sealed record SystemCapabilityWritePreflight(
    bool Ok,
    string Status,
    string PreconditionFingerprint,
    string SafeSummary,
    IReadOnlyList<string> AffectedReferences,
    IReadOnlyList<string> DeferredStepIds,
    string ExecutionEvidenceJson,
    SystemCapabilityError? Error)
{
    public static SystemCapabilityWritePreflight Ready(
        string fingerprint,
        string summary,
        IReadOnlyList<string> affectedReferences,
        string executionEvidenceJson = "{}") =>
        new(true, SystemCapabilityPreflightStatuses.Ready, fingerprint, summary,
            affectedReferences, [], executionEvidenceJson, null);

    public static SystemCapabilityWritePreflight Deferred(
        string fingerprint,
        string summary,
        IReadOnlyList<string> affectedReferences,
        IReadOnlyList<string> deferredStepIds,
        string executionEvidenceJson = "{}") =>
        new(true, SystemCapabilityPreflightStatuses.Deferred, fingerprint, summary,
            affectedReferences, deferredStepIds, executionEvidenceJson, null);

    public static SystemCapabilityWritePreflight Failure(
        string code,
        string message,
        string recovery) =>
        new(false, "", "", "", [], [], "{}", new(code, message, recovery, []));
}

public sealed record SystemCapabilityWriteExecutionContext(
    SystemCapabilityInvocationContext Invocation,
    string RequestToken,
    string Intent,
    IReadOnlyList<string> ProceduresUsed,
    AuthorizationAuditEvidence AuthorizationEvidence,
    string ExecutionEvidenceJson = "{}");

public sealed record SystemCapabilityWriteHandlerResult(
    bool Ok,
    JsonElement? Data,
    string OperationId,
    string ReadBackFingerprint,
    SystemCapabilityError? Error)
{
    public static SystemCapabilityWriteHandlerResult Success(
        JsonElement data,
        string operationId,
        string readBackFingerprint) =>
        new(true, data.Clone(), operationId, readBackFingerprint, null);

    public static SystemCapabilityWriteHandlerResult Failure(
        string code,
        string message,
        string recovery,
        string operationId = "") =>
        new(false, null, operationId, "", new(code, message, recovery, []));
}

public interface ISystemWriteCapabilityHandler
{
    SystemCapabilityRegistration Registration { get; }

    Task<SystemCapabilityWritePreflight> PreflightAsync(
        JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps,
        CancellationToken cancellationToken = default);

    Task<SystemCapabilityWriteHandlerResult> ExecuteAsync(
        JsonElement input,
        SystemCapabilityWriteExecutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record SystemCapabilityWritePreflightResult(
    bool Ok,
    string CapabilityId,
    string DescriptorFingerprint,
    SystemCapabilityWritePreflight? Preflight,
    SystemCapabilityError? Error,
    AuthorizationAuditEvidence AuthorizationEvidence);

public sealed record SystemCapabilityWriteResult(
    bool Ok,
    string CapabilityId,
    string DescriptorFingerprint,
    JsonElement? Data,
    string OperationId,
    string ReadBackFingerprint,
    SystemCapabilityError? Error,
    AuthorizationAuditEvidence AuthorizationEvidence);

public interface ISystemCapabilityCatalog
{
    SystemCapabilityDiscoveryResult Discover(SystemCapabilityInvocationContext context);

    Task<SystemCapabilityReadResult> ReadAsync(
        string capabilityId,
        string inputJson,
        SystemCapabilityInvocationContext context,
        CancellationToken cancellationToken = default);

    Task<SystemCapabilityWritePreflightResult> PreflightWriteAsync(
        string capabilityId,
        string descriptorFingerprint,
        string inputJson,
        IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps,
        SystemCapabilityInvocationContext context,
        CancellationToken cancellationToken = default);

    Task<SystemCapabilityWriteResult> ExecuteWriteAsync(
        string capabilityId,
        string descriptorFingerprint,
        string inputJson,
        SystemCapabilityWriteExecutionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Evidence shown to a trusted host before an AI-originated write is allowed to commit.
/// The model cannot implement this gate or approve its own request.
/// </summary>
public sealed record SystemCapabilityAiApprovalRequest(
    SystemCapabilityDescriptor Capability,
    JsonElement Arguments,
    SystemCapabilityWritePreflight Preflight,
    SystemCapabilityInvocationContext Invocation);

public sealed record SystemCapabilityAiApprovalDecision(
    bool Approved,
    string RequestToken,
    string Intent)
{
    public static SystemCapabilityAiApprovalDecision Denied() => new(false, "", "");
}

public interface ISystemCapabilityAiWriteApprovalGate
{
    Task<SystemCapabilityAiApprovalDecision> ConfirmAsync(
        SystemCapabilityAiApprovalRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs an identified AI agent with the system capabilities authorized for one trusted context.
/// </summary>
public interface ISystemAiAgentService
{
    Task<AiResponse> SendAsync(
        AiAgentProfile profile,
        AiRequest request,
        SystemCapabilityInvocationContext context,
        ISystemCapabilityAiWriteApprovalGate? writeApprovalGate = null,
        IAiToolApprovalGate? toolApprovalGate = null,
        CancellationToken cancellationToken = default);
}

public sealed record SystemAiToolSourceContext(
    AiAgentProfile Profile,
    AiRequest Request,
    SystemCapabilityInvocationContext Invocation,
    ISystemCapabilityAiWriteApprovalGate? CapabilityWriteApproval,
    IAiToolApprovalGate? ToolApproval,
    Func<IReadOnlyList<IAiTool>> AuthorizedTools);

/// <summary>Contributes context-bound direct tools to a local AI request.</summary>
public interface ISystemAiToolSource
{
    IReadOnlyList<IAiTool> CreateTools(SystemAiToolSourceContext context);
}

public sealed class SystemCapabilityConfigurationException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}

public static class SystemCapabilityDescriptorFingerprint
{
    public static string Compute(SystemCapabilityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            descriptor.Id,
            descriptor.Version,
            descriptor.Owner,
            descriptor.Description,
            mode = descriptor.ModeName,
            descriptor.InputSchemaProfile,
            descriptor.InputSchemaHash,
            descriptor.OutputSchemaProfile,
            descriptor.OutputSchemaHash,
            descriptor.ProcedureIds,
            requiredCapability = descriptor.RequiredCapabilityName,
            sensitivity = descriptor.SensitivityName,
            descriptor.RequiresConfirmation,
            descriptor.RequiresIdempotencyKey
        });
        return Convert.ToHexString(SHA256.HashData(canonical));
    }
}
