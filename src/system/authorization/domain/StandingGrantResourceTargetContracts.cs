using DantesRoleplay.Interactions;

namespace DantesRoleplay.Authorization;

/// <summary>
/// Resolves one fixed, host-owned resource kind. Exact resolution must read the requested retained
/// revision and fingerprint; it must never substitute the current resource. Returned targets are
/// ownership evidence only and remain subject to the standing-grant policy and scope checks.
/// </summary>
public interface IStandingGrantResourceTargetOwner
{
    string Kind { get; }

    Task<StandingGrantTargetResolution> ResolveAsync(
        InteractionInvocationHost host,
        StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken = default);

    Task<StandingGrantTargetResolution> ResolveCurrentAsync(
        InteractionInvocationHost host,
        string exactDefinitionId,
        CancellationToken cancellationToken = default);
}
