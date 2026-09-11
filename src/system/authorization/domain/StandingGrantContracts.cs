using DantesRoleplay.Interactions;
using DantesRoleplay.Applications;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.ApplicationActivation;

namespace DantesRoleplay.Authorization;

// Shared grant contracts. The coordinator owns production registration of their SQLite services.
[JsonConverter(typeof(StandingGrantCapabilityJsonConverter))]
public enum StandingGrantCapability { Author, Validate, Activate, Execute, Read, ReadTask, CancelTask }

public sealed class StandingGrantCapabilityJsonConverter()
    : JsonStringEnumConverter<StandingGrantCapability>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonConverter(typeof(StandingGrantScopeJsonConverter))]
public enum StandingGrantScope { Application, StateSpace }

public sealed class StandingGrantScopeJsonConverter()
    : JsonStringEnumConverter<StandingGrantScope>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonConverter(typeof(StandingGrantDefinitionModeJsonConverter))]
public enum StandingGrantDefinitionMode { ExactIds, ApplicationOwned }

public sealed class StandingGrantDefinitionModeJsonConverter()
    : JsonStringEnumConverter<StandingGrantDefinitionMode>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

/// <summary>
/// An explicit operator-selected namespace, optionally including its dot-segment descendants, and
/// exact definition kinds within it. Kinds use existing owner vocabulary: mechanic, procedure, query,
/// component-type, component-definition, information-source or web-page. Query namespace authorization requires the coordinator's
/// verified query-to-namespace ownership mapping; no new namespace kind is registered by this type.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StandingGrantNamespaceAllowance(
    [property: JsonRequired] string NamespaceId,
    [property: JsonRequired] bool IncludeDescendants,
    [property: JsonRequired] IReadOnlyList<string> DefinitionKinds);

/// <summary>
/// ExactIds is the non-expanding default and requires an empty ApplicationOwnedNamespaces list.
/// ApplicationOwned must be explicitly selected and requires an empty ExactIds list plus at least
/// one namespace/kind allowance. It covers existing AND future definitions within those operator-
/// configured boundaries, avoiding per-ID grant changes. It grants no capability or authority by
/// itself. No wildcards, aliases, system/global records, inherited/base/extension-owned definitions,
/// cross-application namespaces, or automatic widening when a namespace changes owner are allowed.
/// Empty exact IDs deny all definitions; empty namespace/kind allowances never mean unrestricted.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StandingGrantDefinitionAllowance(
    [property: JsonRequired] StandingGrantDefinitionMode Mode,
    [property: JsonRequired] IReadOnlyList<string> ExactIds,
    [property: JsonRequired] IReadOnlyList<StandingGrantNamespaceAllowance> ApplicationOwnedNamespaces);

/// <summary>
/// Resolved by the operation owner from the exact active or candidate definition and the enabled,
/// reviewed namespace/ownership registration. Textual prefix membership is never ownership proof.
/// EvidenceReference must be rehydrated and revalidated by the policy; caller JSON is not evidence.
/// New candidates may resolve an intended identity through its registered namespace before that ID
/// exists; their revision/fingerprint pin the candidate bytes, not a claim of current publication.
/// Candidate carries that distinct durable origin. Null means active evidence. Candidate targets
/// permit application authoring/validation/activation/inspection only, never execution or task access.
/// </summary>
public sealed record StandingGrantDefinitionTarget(
    string DefinitionId, string Kind, ApplicationIdentifier OwnerApplicationId, string NamespaceId,
    string OwnershipEvidenceReference, int Revision, string ContentFingerprint,
    ApplicationCandidateReference? Candidate = null,
    StandingGrantActivationOrigin? RetainedActivation = null,
    StandingGrantCatalogSelectionOrigin? CatalogSelection = null);

/// <summary>Owner-produced retained activation provenance captured at task admission, never inferred for legacy tasks.</summary>
public sealed record StandingGrantActivationOrigin(
    int ActivationRevision, string ActivationFingerprint, int ApplicationRevision, string ApplicationFingerprint);

/// <summary>
/// Owner-produced provenance for a definition selected from a current registered catalog source
/// before it has been retained as a candidate. The opaque root and source registration are
/// rehydrated by the standing-grant resolver; this record never carries a host filesystem path.
/// </summary>
public sealed record StandingGrantCatalogSelectionOrigin(
    string AllowedRootId, string SourceId, string SourceRegistrationFingerprint, string RelativePath);

/// <summary>
/// Loaded from the durable task owner, not the caller: read/cancel requires its stored principal,
/// application, state and exact selected definition to match the current host and resolved target.
/// Cancellation propagation is resolved through declared child edges only, never dependency edges
/// or arbitrary matching tasks. Reauthorize each affected child before altering it.
/// </summary>
public sealed record StandingGrantTaskTarget(
    SystemTaskDurableHandle Handle, string PrincipalReference, ApplicationIdentifier ApplicationId,
    string StateSpaceId, SystemTaskSelectedDefinition SelectedDefinition);

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
    StandingGrantDefinitionAllowance Definitions, IReadOnlyList<string> EffectKinds,
    int MaximumOperations, DateTime ExpiresAtUtc, bool Revoked, string IssuedByOperationId);

/// <summary>Constructed by the operation owner from resolved definitions/effects, never authored claims.</summary>
[JsonConverter(typeof(RejectStandingGrantRequirementJsonConverter))]
public sealed record StandingGrantRequirement(
    StandingGrantCapability Capability,
    StandingGrantScope Scope,
    IReadOnlyList<StandingGrantDefinitionTarget> Definitions,
    IReadOnlyList<string> EffectKinds,
    StandingGrantTaskTarget? Task = null);

public sealed class RejectStandingGrantRequirementJsonConverter : JsonConverter<StandingGrantRequirement>
{
    public override StandingGrantRequirement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("Grant requirements and task evidence must be resolved by their owning services.");
    public override void Write(Utf8JsonWriter writer, StandingGrantRequirement value, JsonSerializerOptions options) =>
        throw new JsonException("Owner-resolved grant requirements are not a transport request or receipt.");
}

/// <summary>A diagnostic decision, not a transferable permission token.</summary>
public sealed record StandingGrantDecision(
    bool Allowed, string Code, StandingGrantRevision? Grant, AuthorizationAuditEvidence Evidence);

[JsonConverter(typeof(StandingGrantIssuerMutationJsonConverter))]
public enum StandingGrantIssuerMutation { Issue, Replace, Revoke }

public sealed class StandingGrantIssuerMutationJsonConverter()
    : JsonStringEnumConverter<StandingGrantIssuerMutation>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

/// <summary>
/// Owner-resolved transition: Issue expects absence (zero) and writes revision one; Replace/Revoke
/// expect the exact positive current revision and append its successor to the same grant family.
/// Only Revoke writes Revoked=true. The store rechecks this CAS after issuer authorization in the
/// same writer transaction. This shape supplies no issuer identity or membership evidence.
/// </summary>
public sealed record StandingGrantIssuerRequirement(
    StandingGrantIssuerMutation Mutation, int ExpectedCurrentRevision, StandingGrantRevision Revision);

/// <summary>
/// Required issuer seam for creating, replacing or revoking a grant revision. The implementation
/// must resolve current installation-operator membership from an authoritative host-owned source,
/// independently of authentication and the grantee's standing permissions. Verified=true proves
/// identity only. Neither an invited principal nor an AI principal becomes an issuer by being
/// verified, possessing Author/Activate, or invoking the private-operator compatibility policy.
/// Missing membership resolution fails unavailable; no default issuer or authentication-method
/// heuristic is permitted. Recheck membership and the exact proposed revision in the same SQLite
/// writer transaction as the grant/current-pointer/audit mutation. Decisions are not cached tokens.
/// The host adapters for invited/AI callers must never route through PrivateOperatorAuthorizationPolicy.
/// </summary>
public interface IStandingGrantIssuerPolicy
{
    Task<StandingGrantIssuerDecision> EvaluateAsync(TrustedPrincipalContext issuer,
        StandingGrantIssuerRequirement requirement, string commandId,
        CancellationToken cancellationToken = default);
}

public sealed record StandingGrantIssuerDecision(
    bool Allowed, string Code, AuthorizationAuditEvidence Evidence);

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
    /// Application scope requires null StateSpaceId and is required for global definition publication
    /// and state-free pure execution. Pure execution also requires Read, permits no effect kinds and
    /// creates no world-state commit. StateSpace scope requires the exact host state space. Scope
    /// kinds never imply one another: a state execution grant cannot publish global definitions,
    /// nor can an application authoring grant execute in arbitrary states. The operation owner
    /// determines the actual mutation scope.
    /// Neither scope grants global system procedure authoring. That remains operator-only or
    /// unavailable until a separate system-level policy exists; ApplicationIdentifier.System is
    /// not an installed application grant target.
    /// Read inspects application definitions/candidates only with Application scope, or state query
    /// data only with exact StateSpace scope as selected by the owner. ReadTask/CancelTask are always
    /// StateSpace-only and require the stored task tuple above; they are never aliases for Execute.
    /// Pure Read/ReadTask/CancelTask requirements must contain zero EffectKinds and do not require
    /// an action effect grant. They authorize no action execution or world-state effects.
    /// Definition allowances are checked against actual kind and verified current ownership, even
    /// in ExactIds mode. ApplicationOwned requires an explicit matching namespace/kind allowance;
    /// a definition's name alone cannot prove ownership. Unsupported owner mappings fail unavailable.
    /// The implementation shares the operation owner's scoped database connection and existing
    /// transaction. Mutation admission requires its SQLite writer transaction; freshly reload the
    /// grant family/current revision and authoritative ownership inside that transaction. Never
    /// open or commit an independent transaction, or authorize from an earlier tracked grant row.
    /// This applies to task enqueue/cancellation and each declared child cancellation as well as
    /// publication. Reads require a consistent current-authority view but no world-state effects.
    /// Grant use never grants issuance: invited/AI callers do not use the private-operator policy.
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
    public const int ApplicationOwnedNamespaces = 16;
    public const int DefinitionKindsPerNamespace = 7;
}
