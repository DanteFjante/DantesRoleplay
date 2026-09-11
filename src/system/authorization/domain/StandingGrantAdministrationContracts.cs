using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace DantesRoleplay.Authorization;

/// <summary>
/// An operator request, never issuer authority. The store computes its next revision, exact grant
/// reference, content fingerprint and operation evidence. Zero expects a new family; a positive
/// expectation compares the exact current revision. Revoke preserves every subject/scope/allowance
/// binding of the current revision and appends a terminal revoked successor. No implicit revival.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StandingGrantMutationRequest(
    [property: JsonRequired] StandingGrantIssuerMutation Mutation,
    [property: JsonRequired] string GrantId,
    [property: JsonRequired] int ExpectedCurrentRevision,
    [property: JsonRequired] string PrincipalReference,
    [property: JsonRequired, JsonConverter(typeof(StandingGrantApplicationIdentifierJsonConverter))] ApplicationIdentifier ApplicationId,
    [property: JsonRequired] StandingGrantScope Scope,
    [property: JsonRequired] string? StateSpaceId,
    [property: JsonRequired] IReadOnlyList<StandingGrantCapability> Capabilities,
    [property: JsonRequired] StandingGrantDefinitionAllowance Definitions,
    [property: JsonRequired] IReadOnlyList<string> EffectKinds,
    [property: JsonRequired] int MaximumOperations,
    [property: JsonRequired] DateTime ExpiresAtUtc);

public sealed class StandingGrantApplicationIdentifierJsonConverter : JsonConverter<ApplicationIdentifier>
{
    public override ApplicationIdentifier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("An exact application identifier is required.");
        try { return ApplicationIdentifier.Parse(reader.GetString()!); }
        catch (ArgumentException exception) { throw new JsonException("The application identifier is invalid.", exception); }
    }
    public override void Write(Utf8JsonWriter writer, ApplicationIdentifier value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public interface IStandingGrantAdministration
{
    /// <summary>
    /// The owner acquires its immediate SQLite writer transaction, rechecks current installation
    /// operator membership through IStandingGrantIssuerPolicy even on replay, performs exact CAS,
    /// and appends the grant/current pointer/existing Operation receipt atomically. Same command
    /// plus payload replays its receipt; changed payload conflicts. No issuer defaults or nested
    /// transaction commits. Invited/AI callers cannot issue grants by authenticating or authoring.
    /// </summary>
    Task<InteractionInvocationResult> MutateAsync(TrustedPrincipalContext issuer,
        StandingGrantMutationRequest request, string commandId,
        CancellationToken cancellationToken = default);
}
