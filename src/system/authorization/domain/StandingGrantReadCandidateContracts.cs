using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.Authorization;

public enum StandingGrantReadCandidateStatus { Available, Unavailable }

public sealed record StandingGrantReadCandidateResult(StandingGrantReadCandidateStatus Status, string Code,
    IReadOnlyList<StandingGrantRevision> Candidates);

public interface IStandingGrantReadCandidateReader
{
    Task<StandingGrantReadCandidateResult> ReadAsync(TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId, CancellationToken cancellationToken = default);

    /// <summary>Lists bounded current candidates for one host-selected scope and capability set.</summary>
    Task<StandingGrantReadCandidateResult> ReadAsync(TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId, StandingGrantScope scope, string? stateSpaceId,
        IReadOnlySet<StandingGrantCapability> requiredCapabilities,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new StandingGrantReadCandidateResult(StandingGrantReadCandidateStatus.Unavailable,
            "STANDING_GRANT_READ_CANDIDATES_UNAVAILABLE", Array.Empty<StandingGrantRevision>()));
}
