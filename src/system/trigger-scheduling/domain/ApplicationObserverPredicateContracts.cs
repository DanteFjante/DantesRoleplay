using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.TriggerScheduling;

/// <summary>
/// Immutable observer-owned selection for one exact catalog mechanic. The source registration and
/// activation origin are evidence to rehydrate through their owners; neither is grant authority.
/// </summary>
public sealed record ApplicationObserverPredicateSelection(
    ApplicationIdentifier ApplicationId,
    StandingGrantDefinitionReference Mechanic,
    StandingGrantActivationOrigin CatalogOrigin,
    string SourceRegistrationFingerprint,
    string CanonicalRequirementsJson,
    string RequirementsFingerprint,
    IReadOnlyDictionary<string, string> RoleEntityIds);

/// <summary>
/// Host-materialized immutable predicate input. A source transaction captures this value; a later
/// worker evaluates only this projection and never substitutes current state or caller snapshots.
/// </summary>
public sealed record ApplicationObserverPredicateCapturedInput(
    ApplicationObserverPredicateSelection Selection,
    MechanicProjection Projection,
    string MappingFingerprint,
    string InputFingerprint,
    string ProjectionFingerprint,
    string CaptureFingerprint);

public sealed record ApplicationObserverPredicateEvidence(
    ApplicationObserverPredicateSelection Selection,
    string MappingFingerprint,
    string InputFingerprint,
    string ProjectionFingerprint,
    string CaptureFingerprint);

public sealed record ApplicationObserverPredicateResult(
    bool Evaluated,
    bool Matches,
    string Code,
    ApplicationObserverPredicateEvidence? Evidence = null);

/// <summary>
/// Captures a projection while the source writer still has its admitted view. Implementations
/// revalidate exact ownership and current Read before materializing any state.
/// </summary>
public interface IApplicationObserverPredicateInputCapture
{
    Task<ApplicationObserverPredicateCapturedInput> CaptureAsync(
        InteractionInvocationHost host,
        ApplicationObserverPredicateSelection selection,
        string admittedInputJson,
        string? admittedEventJson,
        long seed,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Evaluates a retained projection outside the source writer. Implementations revalidate current
/// Read, execute the exact selected mechanic, and accept only the closed {"matches":boolean}
/// envelope with no effects, events, child mechanics, notifications, or services.
/// </summary>
public interface IApplicationObserverPredicateEvaluator
{
    Task<ApplicationObserverPredicateResult> EvaluateAsync(
        InteractionInvocationHost freshHost,
        ApplicationObserverPredicateCapturedInput captured,
        CancellationToken cancellationToken = default);
}
