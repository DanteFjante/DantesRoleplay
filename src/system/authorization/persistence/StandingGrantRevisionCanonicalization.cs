using System.Text;
using System.Text.Json;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.Authorization;

/// <summary>Canonical storage and content identity for immutable standing-grant revisions.</summary>
internal static class StandingGrantRevisionCanonicalization
{
    private const string FingerprintDomain = "dantes-roleplay/standing-grant-revision/v1";
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    internal static string PermissionsJson(StandingGrantRevision grant)
    {
        var json = Canonical(new { grant.Capabilities, grant.Definitions, grant.EffectKinds });
        if (Encoding.UTF8.GetByteCount(json) > 16000) throw new ArgumentException("Grant permissions exceed their bound.");
        return json;
    }

    internal static string BindingJson(StandingGrantRevision grant) => Canonical(new
    {
        grant.PrincipalReference, applicationId = grant.ApplicationId.Value, grant.Scope, grant.StateSpaceId,
        permissions = JsonSerializer.Deserialize<JsonElement>(PermissionsJson(grant)), grant.MaximumOperations, grant.ExpiresAtUtc
    });

    internal static string RevisionJson(StandingGrantRevision grant) => Canonical(new
    {
        grant.GrantId, grant.GrantReference, grant.Revision, bindings = JsonSerializer.Deserialize<JsonElement>(BindingJson(grant)),
        grant.Revoked, grant.IssuedByOperationId
    });

    internal static string ContentFingerprint(StandingGrantRevision grant) =>
        InteractionCanonicalJson.Fingerprint(FingerprintDomain, RevisionJson(grant));

    private static string Canonical<T>(T value) =>
        InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(value, Wire));
}
