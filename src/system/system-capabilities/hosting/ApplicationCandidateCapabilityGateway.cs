using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>
/// Shared website/Codex adapter over the selected-application handlers. The adapter supplies only
/// authenticated application context and stable command identity; current standing grants and
/// exact targets are selected and reauthorized by the handlers and application owners.
/// </summary>
public sealed class ApplicationCandidateCapabilityGateway(ISystemCapabilityCatalog catalog)
    : IApplicationCandidateCapabilityGateway
{
    public ApplicationCandidateCapabilityDiscoveryResult Discover(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string correlationId)
    {
        var result = catalog.Discover(ApplicationCandidateCapabilityAccess.Context(
            principal, applicationId, Correlation(correlationId)));
        return new(result.Ok,
            Array.AsReadOnly(result.Capabilities.Select(Project).ToArray()),
            result.Error,
            result.AuthorizationEvidence);
    }

    public async Task<ApplicationCandidateCapabilityInvocationResult> InvokeAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string capabilityId,
        string inputJson,
        string? idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var context = ApplicationCandidateCapabilityAccess.Context(
            principal, applicationId, Correlation(correlationId));
        var discovery = catalog.Discover(context);
        if (!discovery.Ok)
            return Failure(capabilityId, "", discovery.Error);
        var descriptor = discovery.Capabilities.SingleOrDefault(value =>
            string.Equals(value.Id, capabilityId, StringComparison.Ordinal));
        if (descriptor is null)
            return Failure(capabilityId, "", new(
                "APPLICATION_AUTHORING_CAPABILITY_DENIED",
                "The requested capability is outside the application-authoring surface.",
                "Use one of the discovered application-candidate capabilities.", []));

        if (descriptor.Mode == SystemCapabilityMode.Read)
        {
            var read = await catalog.ReadAsync(capabilityId, inputJson, context, cancellationToken);
            return new(read.Ok, read.CapabilityId, descriptor.ModeName, read.Data,
                "", "", read.Error);
        }

        if (!ValidIdempotencyKey(idempotencyKey))
            return Failure(capabilityId, descriptor.ModeName, new(
                "APPLICATION_AUTHORING_IDEMPOTENCY_REQUIRED",
                "A bounded stable idempotency key is required for selected-application writes.",
                "Retry with the same idempotency key for the same logical command.", []));
        var preflight = await catalog.PreflightWriteAsync(
            capabilityId, descriptor.Fingerprint, inputJson, [], context, cancellationToken);
        if (!preflight.Ok || preflight.Preflight is null)
            return Failure(capabilityId, descriptor.ModeName, preflight.Error);
        var requestToken = RequestToken(principal, applicationId, capabilityId, idempotencyKey!);
        var write = await catalog.ExecuteWriteAsync(
            capabilityId,
            descriptor.Fingerprint,
            inputJson,
            new(
                context,
                requestToken,
                $"Invoke {capabilityId} for {applicationId.Value}.",
                descriptor.ProcedureIds,
                preflight.AuthorizationEvidence,
                preflight.Preflight.ExecutionEvidenceJson),
            cancellationToken);
        return new(write.Ok, write.CapabilityId, descriptor.ModeName, write.Data,
            write.OperationId, write.ReadBackFingerprint, write.Error);
    }

    private static ApplicationCandidateCapabilityInvocationResult Failure(
        string capabilityId,
        string mode,
        SystemCapabilityError? error) => new(
            false,
            ApplicationCandidateCapabilityAccess.Supports(capabilityId) ? capabilityId : "",
            mode,
            null,
            "",
            "",
            error ?? new("APPLICATION_AUTHORING_UNAVAILABLE",
                "Application candidate authoring is unavailable.",
                "Retry after the current application and standing-grant owners are available.", []));

    private static string RequestToken(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string capabilityId,
        string idempotencyKey) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            "dantes-roleplay/application-authoring-request/v1\n" + principal.PrincipalId + "\n"
            + applicationId.Value + "\n" + capabilityId + "\n" + idempotencyKey)))[..32];

    private static bool ValidIdempotencyKey(string? value) => value is { Length: > 0 and <= 200 }
        && value == value.Trim() && !value.Any(char.IsControl);

    private static string Correlation(string? value) => value is { Length: > 0 and <= 128 }
        && !value.Any(char.IsControl) ? value : "application-authoring";

    private static ApplicationCandidateCapabilityDescriptor Project(SystemCapabilityDescriptor descriptor)
    {
        var required = RequiredCapabilities(descriptor.Id);
        return new(descriptor.Id, descriptor.Version, descriptor.Fingerprint, descriptor.Owner,
            descriptor.Description, descriptor.ModeName, descriptor.InputSchemaJson, descriptor.OutputSchemaJson,
            Array.AsReadOnly(descriptor.ProcedureIds.ToArray()), required, RequiresConfirmation: false,
            RequiresIdempotencyKey: descriptor.Mode == SystemCapabilityMode.Write);
    }

    private static StandingGrantCapability[] RequiredCapabilities(string id) => id switch
    {
        SystemCapabilityIds.ApplicationCandidateInspect => [StandingGrantCapability.Read],
        SystemCapabilityIds.ApplicationCandidateIntentUpdate => [StandingGrantCapability.Read, StandingGrantCapability.Author],
        SystemCapabilityIds.ApplicationCandidateCatalogCompare => [StandingGrantCapability.Read, StandingGrantCapability.Author],
        SystemCapabilityIds.ApplicationCandidateWrite => [StandingGrantCapability.Author],
        SystemCapabilityIds.ApplicationCandidateValidate => [StandingGrantCapability.Validate],
        SystemCapabilityIds.ApplicationCandidateActivate => [StandingGrantCapability.Activate],
        SystemCapabilityIds.ApplicationCandidateRecover => [StandingGrantCapability.Author],
        SystemCapabilityIds.ApplicationCandidateReviewSubmit or SystemCapabilityIds.ApplicationCandidateReviewCancel =>
            [StandingGrantCapability.Read, StandingGrantCapability.Validate],
        SystemCapabilityIds.ApplicationCandidateReviewRead => [StandingGrantCapability.Read],
        SystemCapabilityIds.InnerWorkerSubmit => [StandingGrantCapability.Execute],
        SystemCapabilityIds.InnerWorkerRead => [StandingGrantCapability.ReadTask],
        SystemCapabilityIds.InnerWorkerCancel => [StandingGrantCapability.CancelTask],
        _ => throw new InvalidOperationException("The selected-application capability set is inconsistent.")
    };
}
