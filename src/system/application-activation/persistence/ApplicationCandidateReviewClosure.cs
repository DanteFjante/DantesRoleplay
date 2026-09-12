using System.Collections.Immutable;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.ApplicationActivation;

internal enum ApplicationCandidateReviewClosureReadStatus { NotApplicable, Available, Rejected }

internal sealed record ApplicationCandidateReviewClosureDocument(
    StandingGrantDefinitionReference Definition,
    ApplicationCandidateReviewDocumentRole Role,
    ActivatedApplicationDocument Document,
    ImmutableArray<byte> RetainedBytes);

internal sealed record ApplicationCandidateReviewAlternativeEvidence(
    StandingGrantDefinitionReference Target, string ContractJson,
    StandingGrantActivationOrigin RetainedOrigin);

/// <summary>
/// Owner-issued exact source and dependency closure for one explicit review grammar. Implementations
/// must not expose public construction from caller-supplied completeness claims or review DTOs.
/// </summary>
internal interface IApplicationCandidateReviewClosureEvidence
{
    string Grammar { get; }
    ApplicationCandidateReference Candidate { get; }
    string SelectionEvidenceFingerprint { get; }
    string EvidenceFingerprint { get; }
    ImmutableArray<ApplicationCandidateReviewClosureDocument> ReviewDocuments { get; }
    ImmutableArray<StandingGrantDefinitionReference> Dependencies { get; }
    ImmutableArray<ApplicationCandidateReviewAlternativeEvidence> ReviewAlternatives { get; }
}

internal sealed record ApplicationCandidateReviewClosureReadResult(
    ApplicationCandidateReviewClosureReadStatus Status,
    IApplicationCandidateReviewClosureEvidence? Evidence = null)
{
    internal static ApplicationCandidateReviewClosureReadResult NotApplicable() =>
        new(ApplicationCandidateReviewClosureReadStatus.NotApplicable);
    internal static ApplicationCandidateReviewClosureReadResult Available(IApplicationCandidateReviewClosureEvidence evidence) =>
        new(ApplicationCandidateReviewClosureReadStatus.Available, evidence);
    internal static ApplicationCandidateReviewClosureReadResult Rejected() =>
        new(ApplicationCandidateReviewClosureReadStatus.Rejected);
}

/// <summary>Claims and proves one explicit candidate review grammar.</summary>
internal interface IApplicationCandidateReviewClosureReader
{
    string Grammar { get; }
    Task<ApplicationCandidateReviewClosureReadResult> ReadAsync(
        InteractionInvocationHost host,
        ApplicationCandidateReference candidate,
        ApplicationCandidateSelectionEvidence selection,
        CancellationToken cancellationToken = default);
}
