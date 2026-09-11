using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Interactions;
using System.Diagnostics.CodeAnalysis;

namespace DantesRoleplay.Authorization;

/// <summary>Structural proposal checks only; an eventual policy must rehydrate ownership and evidence.</summary>
public static class StandingGrantContractRules
{
    private static readonly HashSet<string> Kinds = ["mechanic", "procedure", "component-type", "component-definition", "query", "information-source", "web-page"];

    public static void ValidateIssuerTransition(StandingGrantIssuerRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        if (requirement.Revision is null || !Enum.IsDefined(requirement.Mutation)
            || requirement.ExpectedCurrentRevision < 0 || requirement.ExpectedCurrentRevision == int.MaxValue
            || requirement.Revision.Revision != requirement.ExpectedCurrentRevision + 1
            || (requirement.Mutation == StandingGrantIssuerMutation.Issue) != (requirement.ExpectedCurrentRevision == 0)
            || requirement.Revision.Revoked != (requirement.Mutation == StandingGrantIssuerMutation.Revoke))
            Fail("INVALID_STANDING_GRANT_TRANSITION", "Issuance, replacement and revocation require an exact predecessor and next revision.");
        ValidateConfiguration(requirement.Revision);
        // Only the store can prove that this predecessor is still current in the grant family.
    }

    public static void ValidateConfiguration(StandingGrantRevision grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (!Enum.IsDefined(grant.Scope) || grant.ApplicationId is null || grant.ApplicationId.IsSystem || grant.Revision < 1 || grant.MaximumOperations is < 1 or > 16 || grant.ExpiresAtUtc.Kind != DateTimeKind.Utc)
            Fail("INVALID_STANDING_GRANT", "The grant has an invalid application, revision, budget, scope, or expiry.");
        ScopePair(grant.Scope, grant.StateSpaceId);
        var capabilities = Distinct(grant.Capabilities, 7, "INVALID_STANDING_GRANT_CAPABILITIES");
        if (capabilities.Count == 0 || capabilities.Any(value => !Enum.IsDefined(value)) || capabilities.Any(value => !ScopeAllows(value, grant.Scope)))
            Fail("INVALID_STANDING_GRANT_CAPABILITIES", "The capability set does not match the grant scope.");
        ValidateAllowance(grant.ApplicationId, grant.Definitions);
        foreach (var effect in Distinct(grant.EffectKinds, StandingGrantLimits.EffectKinds, "INVALID_STANDING_GRANT_EFFECTS"))
            Id(effect, "INVALID_STANDING_GRANT_EFFECTS");
        Id(grant.GrantReference, "INVALID_STANDING_GRANT"); Id(grant.GrantId, "INVALID_STANDING_GRANT"); Id(grant.PrincipalReference, "INVALID_STANDING_GRANT"); Id(grant.IssuedByOperationId, "INVALID_STANDING_GRANT");
        Hash(grant.ContentFingerprint);
    }

    public static void ValidateRequirement(InteractionInvocationHost host, StandingGrantRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(requirement);
        if (!Enum.IsDefined(requirement.Capability) || !Enum.IsDefined(requirement.Scope) || !ScopeAllows(requirement.Capability, requirement.Scope)) Fail("INVALID_STANDING_GRANT_REQUIREMENT", "The capability does not support the requested scope.");
        var targets = requirement.Definitions?.ToArray() ?? Fail<IReadOnlyList<StandingGrantDefinitionTarget>>("INVALID_STANDING_GRANT_TARGETS", "Definitions are required.");
        if (targets.Count is < 1 or > StandingGrantLimits.Definitions) Fail("INVALID_STANDING_GRANT_TARGETS", "The definition target count is invalid.");
        foreach (var target in targets) ValidateTarget(host.ApplicationRevision.ApplicationId, target);
        if (targets.Any(target => target.Kind == CatalogNamespaceKinds.InformationSource)
            && (requirement.Scope != StandingGrantScope.Application
                || requirement.Capability is not (StandingGrantCapability.Read or StandingGrantCapability.Author)))
            Fail("STANDING_GRANT_INFORMATION_SCOPE_DENIED", "Information sources authorize application knowledge reads and writes only.");
        if (targets.Any(target => target.Kind == CatalogNamespaceKinds.WebPage)
            && (requirement.Scope != StandingGrantScope.Application
                || requirement.Capability is not (StandingGrantCapability.Read or StandingGrantCapability.Author
                    or StandingGrantCapability.Validate or StandingGrantCapability.Activate)))
            Fail("STANDING_GRANT_WEB_PAGE_SCOPE_DENIED", "Web pages authorize application authoring and reads only.");
        if (targets.Any(target => target.Candidate is not null)
            && (requirement.Scope != StandingGrantScope.Application
                || requirement.Capability is not (StandingGrantCapability.Author or StandingGrantCapability.Validate
                    or StandingGrantCapability.Activate or StandingGrantCapability.Read)))
            Fail("STANDING_GRANT_CANDIDATE_SCOPE_DENIED", "Candidate targets authorize application authoring or inspection only.");
        if (targets.Any(target => target.RetainedActivation is not null)
            && (requirement.Scope != StandingGrantScope.StateSpace
                || requirement.Capability is not (StandingGrantCapability.ReadTask or StandingGrantCapability.CancelTask)))
            Fail("STANDING_GRANT_RETAINED_SCOPE_DENIED", "Historical targets authorize only the stored task's read or cancellation.");
        if (targets.Select(target => target.DefinitionId).Distinct(StringComparer.Ordinal).Count() != targets.Count)
            Fail("INVALID_STANDING_GRANT_TARGETS", "Definition targets must have distinct identities.");
        var effects = Distinct(requirement.EffectKinds, StandingGrantLimits.EffectKinds, "INVALID_STANDING_GRANT_EFFECTS");
        foreach (var effect in effects) Id(effect, "INVALID_STANDING_GRANT_EFFECTS");
        if (requirement.Capability is StandingGrantCapability.Read or StandingGrantCapability.ReadTask or StandingGrantCapability.CancelTask && effects.Count != 0) Fail("INVALID_STANDING_GRANT_EFFECTS", "Pure reads and task reads/cancellation cannot request effects.");
        var taskCapability = requirement.Capability is StandingGrantCapability.ReadTask or StandingGrantCapability.CancelTask;
        if (taskCapability != (requirement.Task is not null)) Fail("INVALID_STANDING_GRANT_TASK", "Task target presence must match the task capability.");
        if (requirement.Task is not null) ValidateTask(host, requirement.Task, targets);
    }

    /// <summary>
    /// Only matches structural selector boundaries. A true result is NOT an authorization decision:
    /// the eventual policy must independently rehydrate the target's exact namespace/ownership
    /// evidence, recheck enabled/reviewed registrations and evaluate the current grant and caller.
    /// </summary>
    public static bool MatchesDefinitionAllowance(ApplicationIdentifier application, StandingGrantDefinitionAllowance allowance, StandingGrantDefinitionTarget target)
    {
        if (application is null || application.IsSystem) return false;
        try { ValidateAllowance(application, allowance); ValidateTarget(application, target); }
        catch (InteractionContractException) { return false; }
        if (allowance.Mode == StandingGrantDefinitionMode.ExactIds) return allowance.ExactIds.Contains(target.DefinitionId, StringComparer.Ordinal);
        return allowance.ApplicationOwnedNamespaces.Any(boundary => boundary.DefinitionKinds.Contains(target.Kind, StringComparer.Ordinal) && (target.NamespaceId == boundary.NamespaceId || boundary.IncludeDescendants && target.NamespaceId.StartsWith(boundary.NamespaceId + ".", StringComparison.Ordinal)));
    }

    private static void ValidateAllowance(ApplicationIdentifier app, StandingGrantDefinitionAllowance allowance)
    {
        if (app is null || app.IsSystem || allowance is null || !Enum.IsDefined(allowance.Mode))
            Fail("INVALID_STANDING_GRANT_ALLOWANCE", "An installed application and explicit definition mode are required.");
        var exact = Distinct(allowance.ExactIds, StandingGrantLimits.Definitions, "INVALID_STANDING_GRANT_ALLOWANCE");
        var boundaries = allowance.ApplicationOwnedNamespaces?.ToArray() ?? Fail<StandingGrantNamespaceAllowance[]>("INVALID_STANDING_GRANT_ALLOWANCE", "Namespace allowances are required.");
        if (boundaries.Length > StandingGrantLimits.ApplicationOwnedNamespaces || allowance.Mode == StandingGrantDefinitionMode.ExactIds && boundaries.Length != 0 || allowance.Mode == StandingGrantDefinitionMode.ApplicationOwned && (exact.Count != 0 || boundaries.Length == 0)) Fail("INVALID_STANDING_GRANT_ALLOWANCE", "The selected allowance mode has mixed or empty selectors.");
        foreach (var id in exact)
        {
            RecordId(id, "INVALID_STANDING_GRANT_ALLOWANCE");
            if (!Namespace(app, CatalogNamespaceIdentity.NamespaceOf(id)))
                Fail("INVALID_STANDING_GRANT_ALLOWANCE", "Exact identities must belong to this application's namespace.");
        }
        var seenNamespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var boundary in boundaries)
        {
            if (boundary is null || !Namespace(app, boundary.NamespaceId)) Fail("INVALID_STANDING_GRANT_NAMESPACE", "Namespace is not owned by the application.");
            if (!seenNamespaces.Add(boundary.NamespaceId))
                Fail("INVALID_STANDING_GRANT_NAMESPACE", "Namespace allowances must be distinct.");
            var kinds = Distinct(boundary.DefinitionKinds, StandingGrantLimits.DefinitionKindsPerNamespace, "INVALID_STANDING_GRANT_NAMESPACE");
            if (kinds.Count == 0 || kinds.Any(kind => !Kinds.Contains(kind))) Fail("INVALID_STANDING_GRANT_NAMESPACE", "Namespace kinds are unsupported.");
        }
    }

    private static void ValidateTarget(ApplicationIdentifier app, StandingGrantDefinitionTarget target)
    {
        if (target is null || app is null || app.IsSystem || target.OwnerApplicationId != app || !Kinds.Contains(target.Kind) || !Namespace(app, target.NamespaceId) || !RecordId(target.DefinitionId, "INVALID_STANDING_GRANT_TARGET") || !Id(target.OwnershipEvidenceReference, "INVALID_STANDING_GRANT_TARGET") || target.Revision < 1) Fail("INVALID_STANDING_GRANT_TARGET", "Definition target is invalid or cross-application.");
        Hash(target.ContentFingerprint);
        if (target.RetainedActivation is { } origin)
        {
            if (target.Candidate is not null || origin.ActivationRevision < 1 || origin.ApplicationRevision < 1)
                Fail("INVALID_STANDING_GRANT_TARGET", "The retained activation origin is invalid.");
            Hash(origin.ActivationFingerprint);
            Hash(origin.ApplicationFingerprint);
        }
        if (target.Candidate is { } candidate)
        {
            if (candidate.ApplicationId != app || candidate.Revision < 1
                || candidate.CandidateId is not { Length: 32 }
                || candidate.CandidateId.Any(c => !(char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')))
                Fail("INVALID_STANDING_GRANT_TARGET", "The candidate origin is invalid or cross-application.");
            Hash(candidate.ContentFingerprint);
        }
        if (CatalogNamespaceIdentity.NamespaceOf(target.DefinitionId) != target.NamespaceId) Fail("INVALID_STANDING_GRANT_TARGET", "Definition identity is outside its namespace.");
    }

    private static void ValidateTask(InteractionInvocationHost host, StandingGrantTaskTarget task, IReadOnlyList<StandingGrantDefinitionTarget> targets)
    {
        if (task.Handle is null || task.SelectedDefinition is null
            || task.PrincipalReference != host.Principal.PrincipalId
            || task.ApplicationId != host.ApplicationRevision.ApplicationId
            || task.StateSpaceId != host.StateSpaceId || targets.Count != 1
            || task.SelectedDefinition.ExactDefinitionId != targets[0].DefinitionId
            || task.SelectedDefinition.Version != targets[0].Revision
            || task.SelectedDefinition.Fingerprint != targets[0].ContentFingerprint)
            Fail("INVALID_STANDING_GRANT_TASK", "The stored task tuple does not match the host and exact target.");
    }
    private static bool ScopeAllows(StandingGrantCapability cap, StandingGrantScope scope) => cap switch { StandingGrantCapability.Author or StandingGrantCapability.Validate or StandingGrantCapability.Activate => scope == StandingGrantScope.Application, StandingGrantCapability.Execute or StandingGrantCapability.ReadTask or StandingGrantCapability.CancelTask => scope == StandingGrantScope.StateSpace, StandingGrantCapability.Read => true, _ => false };
    private static void ScopePair(StandingGrantScope scope, string? state)
    {
        if (scope == StandingGrantScope.Application && state is null) return;
        if (scope == StandingGrantScope.StateSpace)
        {
            Id(state, "INVALID_STANDING_GRANT_SCOPE");
            return;
        }
        Fail("INVALID_STANDING_GRANT_SCOPE", "Application scope requires null state and state scope requires an exact state.");
    }
    private static bool Namespace(ApplicationIdentifier app, string? value) =>
        app is not null && !app.IsSystem && value is not null
        && CatalogNamespaceIdentity.IsNamespaceId(value) && value != CatalogNamespaceIdentity.RootNamespaceId
        && (value == app.Value || value.StartsWith(app.Value + ".", StringComparison.Ordinal));
    private static bool RecordId(string? value, string code)
    {
        Id(value, code);
        try { CatalogNamespaceIdentity.ValidateRecordId(value!); return true; }
        catch (ArgumentException) { Fail(code, "Catalog record ID is invalid."); return false; }
    }
    private static bool Id(string? value, string code)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > StandingGrantLimits.IdentifierCharacters
            || value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            || value.Contains('*') || value.Contains('?')) Fail(code, "Identifier is invalid.");
        return true;
    }
    private static void Hash(string value) { if (value is not { Length: 64 } || value.Any(c => !(char.IsAsciiDigit(c) || c is >= 'A' and <= 'F'))) Fail("INVALID_STANDING_GRANT_HASH", "Fingerprint is invalid."); }
    private static IReadOnlyList<T> Distinct<T>(IReadOnlyList<T>? values, int maximum, string code) { if (values is null) Fail(code, "Collection is absent, too large, or contains duplicates."); if (values!.Count > maximum || values.Distinct().Count() != values.Count) Fail(code, "Collection is absent, too large, or contains duplicates."); return values; }
    [DoesNotReturn]
    private static void Fail(string code, string message) => throw new InteractionContractException(code, message);
    private static T Fail<T>(string code, string message) { Fail(code, message); return default!; }
}
