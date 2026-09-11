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
}