using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.Web.Hosting;

internal sealed class UnavailableApplicationCandidateCapabilityGateway
    : IApplicationCandidateCapabilityGateway
{
    public ApplicationCandidateCapabilityDiscoveryResult Discover(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string correlationId) => new(false, [], Error(), Evidence(principal, correlationId));

    public Task<ApplicationCandidateCapabilityInvocationResult> InvokeAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string capabilityId,
        string inputJson,
        string? idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default) => Task.FromResult(new ApplicationCandidateCapabilityInvocationResult(
            false, ApplicationCandidateCapabilityAccess.Supports(capabilityId) ? capabilityId : "", "",
            null, "", "", Error()));

    private static SystemCapabilityError Error() => new(
        "APPLICATION_AUTHORING_UNAVAILABLE",
        "Application candidate authoring is not registered in this host.",
        "Use a host with the application activation and standing-grant components registered.", []);

    private static AuthorizationAuditEvidence Evidence(
        TrustedPrincipalContext principal,
        string correlationId) => new(
            principal.Verified ? principal.PrincipalId : "",
            principal.Verified ? principal.AuthenticationMethod : "",
            "application.authoring",
            ApplicationCandidateCapabilityAccess.Scope,
            string.IsNullOrWhiteSpace(correlationId) ? "application-authoring" : correlationId[..Math.Min(128, correlationId.Length)],
            false,
            "APPLICATION_AUTHORING_UNAVAILABLE");
}
