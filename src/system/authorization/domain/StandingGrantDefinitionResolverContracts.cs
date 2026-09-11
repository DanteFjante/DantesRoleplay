using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.Authorization;

/// <summary>An exact definition selection, not ownership or grant evidence.</summary>
public sealed record StandingGrantDefinitionSelection(
    string DefinitionId, string Kind, int Revision, string ContentFingerprint);

public enum StandingGrantDefinitionResolutionStatus { Available, Denied, Unavailable }

/// <summary>Available requires a target freshly resolved by its owner; other states have no target.</summary>
public sealed record StandingGrantDefinitionResolution(
    StandingGrantDefinitionResolutionStatus Status, string Code, StandingGrantDefinitionTarget? Target);

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
public interface IStandingGrantDefinitionResolver
{
    Task<StandingGrantDefinitionResolution> ResolveAsync(InteractionInvocationHost host,
        StandingGrantDefinitionSelection selection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Candidate path resolves the exact owner-materialized retained candidate and its registered
    /// source. It cannot substitute current active bytes or accept a caller-deserialized snapshot.
    /// Unimplemented candidate materialization returns Unavailable, never active-definition evidence.
    /// </summary>
    Task<StandingGrantDefinitionResolution> ResolveCandidateAsync(InteractionInvocationHost host,
        ApplicationCandidateSnapshot candidate, StandingGrantDefinitionSelection selection,
        CancellationToken cancellationToken = default);
}
