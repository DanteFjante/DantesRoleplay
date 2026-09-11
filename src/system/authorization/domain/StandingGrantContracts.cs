using DantesRoleplay.Interactions;
using DantesRoleplay.Applications;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DantesRoleplay.Authorization;

// Coordinator review proposal. No production implementation or registration is supplied here.
[JsonConverter(typeof(StandingGrantCapabilityJsonConverter))]
public enum StandingGrantCapability { Author, Validate, Activate, Execute }

public sealed class StandingGrantCapabilityJsonConverter()
    : JsonStringEnumConverter<StandingGrantCapability>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonConverter(typeof(StandingGrantScopeJsonConverter))]
public enum StandingGrantScope { Application, StateSpace }

public sealed class StandingGrantScopeJsonConverter()
    : JsonStringEnumConverter<StandingGrantScope>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

/// <summary>
/// An exact immutable grant reference resolved from InteractionInvocationHost.GrantReference.
/// GrantId is the revision family, not additional caller-supplied authority. Every evaluation also
/// compares Revision with the family's current revision, including a revoked terminal revision.
/// A reference maps uniquely to one (GrantId, Revision); mismatched stored bindings fail closed.
/// </summary>
public sealed record StandingGrantRevision(
    string GrantReference, string GrantId, int Revision, string ContentFingerprint,
    string PrincipalReference, ApplicationIdentifier ApplicationId, StandingGrantScope Scope, string? StateSpaceId,
    IReadOnlyList<StandingGrantCapability> Capabilities,
    IReadOnlyList<string> DefinitionIds, IReadOnlyList<string> EffectKinds,
    int MaximumOperations, DateTime ExpiresAtUtc, bool Revoked, string IssuedByOperationId);

/// <summary>Constructed by the operation owner from resolved definitions/effects, never authored claims.</summary>
public sealed record StandingGrantRequirement(
    StandingGrantCapability Capability,
    StandingGrantScope Scope,
    IReadOnlyList<string> DefinitionIds,
    IReadOnlyList<string> EffectKinds);

/// <summary>A diagnostic decision, not a transferable permission token.</summary>
public sealed record StandingGrantDecision(
    bool Allowed, string Code, StandingGrantRevision? Grant, AuthorizationAuditEvidence Evidence);

public interface IStandingGrantPolicy
{
    /// <summary>
    /// Resolve the host's exact reference; recheck current revision/revocation, principal, exact
    /// application/scope, expiry, required capability, definition/effect subsets and the host's
    /// configured budget/deadline ceiling. Empty allow-lists authorize nothing; no wildcard selectors.
    /// The operation owner must atomically consume the host ledger once before doing work, including
    /// retries. A commit owner repeats authorization in its write transaction without consuming twice
    /// or rejecting its already-reserved final allowance merely because RemainingOperations is zero.
    /// Never cache a decision or grant additional budget from this check.
    /// Application scope requires null StateSpaceId and is required for global definition publication.
    /// StateSpace scope requires the exact host state space. Scope kinds never imply one another:
    /// a narrow execution grant cannot publish global definitions, nor can an application authoring
    /// grant execute in arbitrary states. The operation owner determines the actual mutation scope.
    /// Neither scope grants global system procedure authoring. That remains operator-only or
    /// unavailable until a separate system-level policy exists; ApplicationIdentifier.System is
    /// not an installed application grant target.
    /// </summary>
    Task<StandingGrantDecision> EvaluateAsync(InteractionInvocationHost host,
        StandingGrantRequirement requirement, CancellationToken cancellationToken = default);
}

public static class StandingGrantLimits
{
    public const int Definitions = 64;
    public const int EffectKinds = 64;
    public const int IdentifierCharacters = 200;
    public const int MaximumOperations = 16;
}
