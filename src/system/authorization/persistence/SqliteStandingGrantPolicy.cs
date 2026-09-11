using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization;

/// <summary>Scoped grant evaluation; issuance and registration remain separate owners.</summary>
public sealed class SqliteStandingGrantPolicy(DantesRoleplayDbContext db, IStandingGrantTargetResolver resolver) : IStandingGrantPolicy
{
    public async Task<StandingGrantDecision> EvaluateAsync(InteractionInvocationHost host, StandingGrantRequirement requirement, CancellationToken cancellationToken = default)
    {
        try { StandingGrantContractRules.ValidateRequirement(host, requirement); }
        catch (InteractionContractException e) { return Decision(false, e.Code, null, host, requirement.Scope); }
        if (requirement.Scope == StandingGrantScope.StateSpace
            && (host.StateSpaceId is null || host.StateRevision is null))
            return Decision(false, "STANDING_GRANT_STATE_SCOPE_REQUIRED", null, host, requirement.Scope);
        var mutation = requirement.Capability is StandingGrantCapability.Author or StandingGrantCapability.Validate or StandingGrantCapability.Activate or StandingGrantCapability.Execute or StandingGrantCapability.CancelTask;
        if (mutation && db.Database.CurrentTransaction is null) return Decision(false, "STANDING_GRANT_TRANSACTION_REQUIRED", null, host, requirement.Scope);
        // One view must span the current grant and every source/namespace rehydration. A resolver's
        // own later snapshot cannot repair an earlier grant read that raced with revocation.
        await using var readView = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
        var row = await (from record in db.Set<StandingGrantRevisionRecord>().AsNoTracking()
                         join current in db.Set<StandingGrantCurrentRecord>().AsNoTracking() on new { record.GrantId, record.Revision } equals new { current.GrantId, current.Revision }
                         where record.GrantReference == host.GrantReference select record).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return Decision(false, "STANDING_GRANT_NOT_CURRENT", null, host, requirement.Scope);
        StandingGrantRevision grant;
        try { grant = Parse(row); }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InteractionContractException)
        { return Decision(false, "STANDING_GRANT_INVALID", null, host, requirement.Scope); }
        if (grant.Revoked || host.Budget.DeadlineUtc <= DateTime.UtcNow || host.Budget.DeadlineUtc > grant.ExpiresAtUtc || grant.PrincipalReference != host.Principal.PrincipalId || grant.ApplicationId != host.ApplicationRevision.ApplicationId || grant.Scope != requirement.Scope || grant.StateSpaceId is not null && grant.StateSpaceId != host.StateSpaceId || !grant.Capabilities.Contains(requirement.Capability) || host.Budget.MaximumOperations > grant.MaximumOperations)
            return Decision(false, "STANDING_GRANT_DENIED", grant, host, requirement.Scope);
        foreach (var target in requirement.Definitions)
        {
            var resolved = await resolver.RevalidateAsync(host, target, cancellationToken);
            if (resolved.Status == StandingGrantTargetResolutionStatus.Unavailable) return Decision(false, "STANDING_GRANT_TARGET_UNAVAILABLE", grant, host, requirement.Scope);
            if (resolved.Status != StandingGrantTargetResolutionStatus.Available || resolved.Target != target || !StandingGrantContractRules.MatchesDefinitionAllowance(grant.ApplicationId, grant.Definitions, resolved.Target)) return Decision(false, "STANDING_GRANT_TARGET_DENIED", grant, host, requirement.Scope);
        }
        if (requirement.EffectKinds.Any(effect => !grant.EffectKinds.Contains(effect, StringComparer.Ordinal))) return Decision(false, "STANDING_GRANT_EFFECT_DENIED", grant, host, requirement.Scope);
        return Decision(true, "STANDING_GRANT_ALLOWED", grant, host, requirement.Scope);
    }
    internal static StandingGrantRevision Parse(StandingGrantRevisionRecord row)
    {
        if (Encoding.UTF8.GetByteCount(row.PermissionsJson) > 16000)
            throw new JsonException("Stored grant permissions exceed their bound.");
        var canonical = InteractionCanonicalJson.Canonicalize(row.PermissionsJson);
        var permissions = JsonSerializer.Deserialize<StoredPermissions>(canonical, Wire)
            ?? throw new JsonException("Stored grant permissions are absent.");
        var scope = row.Scope switch
        {
            "application" => StandingGrantScope.Application,
            "stateSpace" => StandingGrantScope.StateSpace,
            _ => throw new JsonException("Stored grant scope is invalid.")
        };
        var expiry = DateTime.SpecifyKind(row.ExpiresAtUtc, DateTimeKind.Utc);
        var grant = new StandingGrantRevision(row.GrantReference, row.GrantId, row.Revision, row.ContentFingerprint,
            row.PrincipalReference, ApplicationIdentifier.Parse(row.ApplicationId), scope, row.StateSpaceId,
            permissions.Capabilities, permissions.Definitions, permissions.EffectKinds,
            row.MaximumOperations, expiry, row.Revoked, row.IssuedByOperationId);
        StandingGrantContractRules.ValidateConfiguration(grant);
        if (!string.Equals(row.ContentFingerprint, StandingGrantRevisionCanonicalization.ContentFingerprint(grant), StringComparison.Ordinal))
            throw new JsonException("Stored grant content fingerprint does not match its canonical revision.");
        return grant;
    }

    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record StoredPermissions(
        [property: JsonRequired] IReadOnlyList<StandingGrantCapability> Capabilities,
        [property: JsonRequired] StandingGrantDefinitionAllowance Definitions,
        [property: JsonRequired] IReadOnlyList<string> EffectKinds);
    private static StandingGrantDecision Decision(bool allowed, string code, StandingGrantRevision? grant, InteractionInvocationHost host, StandingGrantScope scope) => new(allowed, code, allowed ? grant : null, new(host.Principal.PrincipalId, host.Principal.AuthenticationMethod, "standing-grant", scope == StandingGrantScope.Application ? host.ApplicationRevision.ApplicationId.Value : host.StateSpaceId ?? string.Empty, host.CommandId, allowed, code));
}
