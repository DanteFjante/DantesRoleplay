using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>
/// Private operator adapter over the standing-grant owner. Installation-operator membership,
/// revision compare-and-swap and replay remain inside IStandingGrantAdministration.
/// </summary>
internal sealed class StandingGrantAdministrationSystemCapabilityHandler(IServiceProvider services)
    : ISystemWriteCapabilityHandler
{
    private const string Recovery =
        "Inspect the current application and target ownership, then retry with the same command identity or a corrected exact predecessor.";

    public SystemCapabilityRegistration Registration { get; } = new(
        SystemCapabilityIds.StandingGrantAdmin,
        1,
        "authorization",
        "Issue, replace, or revoke one exact application standing-grant revision through the installation-operator owner.",
        SystemCapabilityMode.Write,
        InputSchema,
        OutputSchema,
        ["procedure.system.standing-grant.admin"],
        PrivateOperatorCapability.Modify,
        SystemCapabilitySensitivity.PrivateOperatorMetadata,
        true,
        true);

    public Task<SystemCapabilityWritePreflight> PreflightAsync(
        JsonElement input,
        IReadOnlyList<SystemCapabilityEarlierStep> earlierSteps,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var request = Deserialize(input);
            var fingerprint = Fingerprint(input);
            return Task.FromResult(SystemCapabilityWritePreflight.Ready(
                fingerprint,
                $"{request.Mutation} standing grant '{request.GrantId}' at expected revision {request.ExpectedCurrentRevision}.",
                [$"standing-grant:{request.GrantId}", $"application:{request.ApplicationId.Value}", request.PrincipalReference],
                JsonSerializer.Serialize(new { inputFingerprint = fingerprint })));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InteractionContractException)
        {
            return Task.FromResult(SystemCapabilityWritePreflight.Failure(
                "INVALID_STANDING_GRANT_REQUEST",
                "The standing-grant mutation request is invalid.",
                Recovery));
        }
    }

    public async Task<SystemCapabilityWriteHandlerResult> ExecuteAsync(
        JsonElement input,
        SystemCapabilityWriteExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = Deserialize(input);
            if (!MatchesEvidence(context.ExecutionEvidenceJson, Fingerprint(input)))
                return Failure("SYSTEM_CAPABILITY_PREFLIGHT_STALE");
            var owner = services.GetService<IStandingGrantAdministration>();
            if (owner is null) return Failure("STANDING_GRANT_ADMINISTRATION_UNAVAILABLE");
            var result = await owner.MutateAsync(
                context.Invocation.Principal, request, context.RequestToken, cancellationToken);
            if (result.Tag != InteractionInvocationResultTag.Committed || result.Receipt is null)
                return Failure(result.Code, result.SafeMessage);
            return SystemCapabilityWriteHandlerResult.Success(JsonSerializer.SerializeToElement(new
            {
                status = result.WireTag,
                code = result.Code,
                message = result.SafeMessage,
                operationId = result.Receipt.OperationId,
                requestFingerprint = result.Receipt.RequestFingerprint
            }), result.Receipt.OperationId, result.Receipt.RequestFingerprint);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InteractionContractException)
        {
            return Failure("INVALID_STANDING_GRANT_REQUEST");
        }
    }

    private static StandingGrantMutationRequest Deserialize(JsonElement input) =>
        JsonSerializer.Deserialize<StandingGrantMutationRequest>(input.GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new JsonException("The standing-grant request is absent.");

    private static string Fingerprint(JsonElement input) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(InteractionCanonicalJson.CanonicalizeObject(input.GetRawText()))));

    private static bool MatchesEvidence(string json, string fingerprint)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.EnumerateObject().Count() == 1
                && document.RootElement.TryGetProperty("inputFingerprint", out var value)
                && value.ValueKind == JsonValueKind.String && value.GetString() == fingerprint;
        }
        catch (JsonException) { return false; }
    }

    private static SystemCapabilityWriteHandlerResult Failure(string code, string? message = null) =>
        SystemCapabilityWriteHandlerResult.Failure(code,
            message ?? "The standing-grant mutation was rejected.", Recovery);

    private const string InputSchema = """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,
        "required":["mutation","grantId","expectedCurrentRevision","principalReference","applicationId","scope","stateSpaceId","capabilities","definitions","effectKinds","maximumOperations","expiresAtUtc"],
        "properties":{
          "mutation":{"type":"string","enum":["issue","replace","revoke"]},
          "grantId":{"type":"string","minLength":1,"maxLength":160},
          "expectedCurrentRevision":{"type":"integer","minimum":0},
          "principalReference":{"type":"string","minLength":1,"maxLength":80},
          "applicationId":{"type":"string","minLength":1,"maxLength":63},
          "scope":{"type":"string","enum":["application","stateSpace"]},
          "stateSpaceId":{"anyOf":[{"type":"string","minLength":1,"maxLength":200},{"type":"null"}]},
          "capabilities":{"type":"array","minItems":1,"maxItems":7,"uniqueItems":true,"items":{"type":"string","enum":["author","validate","activate","execute","read","readTask","cancelTask"]}},
          "definitions":{"type":"object","additionalProperties":false,"required":["mode","exactIds","applicationOwnedNamespaces"],"properties":{
            "mode":{"type":"string","enum":["exactIds","applicationOwned"]},
            "exactIds":{"type":"array","maxItems":64,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":200}},
            "applicationOwnedNamespaces":{"type":"array","maxItems":16,"items":{"type":"object","additionalProperties":false,"required":["namespaceId","includeDescendants","definitionKinds"],"properties":{
              "namespaceId":{"type":"string","minLength":1,"maxLength":200},"includeDescendants":{"type":"boolean"},
              "definitionKinds":{"type":"array","minItems":1,"maxItems":7,"uniqueItems":true,"items":{"type":"string","enum":["mechanic","procedure","query","component-type","component-definition","information-source","web-page"]}}
            }}}
          }},
          "effectKinds":{"type":"array","maxItems":64,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":200}},
          "maximumOperations":{"type":"integer","minimum":1,"maximum":16},
          "expiresAtUtc":{"type":"string","minLength":1,"maxLength":64}
        }}
        """;

    private const string OutputSchema =
        "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"status\",\"code\",\"message\",\"operationId\",\"requestFingerprint\"],\"properties\":{\"status\":{\"type\":\"string\",\"enum\":[\"committed\"]},\"code\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200},\"message\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":1000},\"operationId\":{\"type\":\"string\",\"minLength\":32,\"maxLength\":32},\"requestFingerprint\":{\"type\":\"string\",\"minLength\":64,\"maxLength\":64}}}";
}
