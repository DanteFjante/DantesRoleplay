namespace DantesRoleplay.Authorization;

public enum InstallationOperatorMembershipStatus { Member, Denied, Unavailable }

/// <summary>Current installation membership only; never a capability, authorization token or audit receipt.</summary>
public sealed record InstallationOperatorMembershipDecision(InstallationOperatorMembershipStatus Status, string Code);

/// <summary>
/// Re-evaluates the host's authoritative installation-operator membership on every call.
/// Authentication alone is insufficient. Each owning command separately validates its transition,
/// correlation and transaction and records its own audit. Missing membership resolution is unavailable;
/// no default allow or private-operator compatibility fallback is implied by this contract.
/// </summary>
public interface IInstallationOperatorMembershipPolicy
{
    Task<InstallationOperatorMembershipDecision> EvaluateAsync(TrustedPrincipalContext principal,
        CancellationToken cancellationToken = default);
}
