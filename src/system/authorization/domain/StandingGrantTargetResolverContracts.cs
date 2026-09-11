using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.Authorization;

/// <summary>An exact definition selection, not ownership or grant evidence.</summary>
public sealed record StandingGrantDefinitionReference(
    string DefinitionId, string Kind, int Revision, string ContentFingerprint);

public enum StandingGrantTargetResolutionStatus { Available, Denied, Unavailable }

/// <summary>Available requires a target freshly resolved by its owner; other states have no target.</summary>
public sealed record StandingGrantTargetResolution(
    StandingGrantTargetResolutionStatus Status, string Code, StandingGrantDefinitionTarget? Target,
    StandingGrantActivationOrigin? CurrentActivation = null);

/// <summary>
/// Trusted resolution before permission evaluation. Active definitions must match exact retained
/// winners and source registration ApplicationId, and their namespace must be enabled and reviewed
/// for the exact kind. CatalogNamespaceDefinition.Owner is a domain label, never an application ID.
/// Namespace prefixes, hashes or caller-supplied evidence strings alone prove no ownership.
/// Same-application grants exclude borrowed/base/extension definitions unless a separately reviewed
/// import binding exists. Unsupported ownership or definition mappings return Unavailable.
/// The policy repeats resolution and compares the complete target, including source/namespace
/// evidence, in the caller's transaction; a previously resolved target is not transferable authority.
/// Reads/discovery filter unavailable or denied targets before revealing any content or metadata.
/// </summary>
public interface IStandingGrantTargetResolver
{
    /// <summary>Historical task lookup uses only the task owner's retained admission provenance.</summary>
    Task<StandingGrantTargetResolution> ResolveRetainedAsync(InteractionInvocationHost host,
        StandingGrantActivationOrigin origin, StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Unavailable,
            "STANDING_GRANT_RETAINED_TARGET_UNAVAILABLE", null));

    /// <summary>
    /// Rehydrates active evidence or the exact Candidate tuple from owner storage in the current
    /// transaction, then compares every target field. An evidence string is never origin authority.
    /// </summary>
    Task<StandingGrantTargetResolution> RevalidateAsync(InteractionInvocationHost host,
        StandingGrantDefinitionTarget target, CancellationToken cancellationToken = default) =>
        Task.FromResult(new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Unavailable,
            "STANDING_GRANT_REVALIDATION_UNAVAILABLE", null));

    /// <summary>Exact current ID lookup for owners whose existing query contract has no definition hash.</summary>
    Task<StandingGrantTargetResolution> ResolveCurrentAsync(InteractionInvocationHost host,
        string exactDefinitionId, string kind, CancellationToken cancellationToken = default) =>
        Task.FromResult(new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Unavailable,
            "STANDING_GRANT_CURRENT_TARGET_UNAVAILABLE", null));

    Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host,
        StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default);

    /// <summary>Typed lookup only; the owner freshly verifies retained metadata, selected bytes and current ownership.</summary>
    Task<StandingGrantTargetResolution> ResolveCandidateReferenceAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Unavailable,
            "STANDING_GRANT_CANDIDATE_OWNER_UNAVAILABLE", null));

    /// <summary>
    /// Candidate path resolves the exact owner-materialized retained candidate and its registered
    /// source. It cannot substitute current active bytes or accept a caller-deserialized snapshot.
    /// Unimplemented candidate materialization returns Unavailable, never active-definition evidence.
    /// </summary>
    Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host,
        ApplicationCandidateSnapshot candidate, StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken = default);
}
