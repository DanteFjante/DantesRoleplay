using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.SystemCapabilities;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Web.Hosting;

public sealed record ControlSystemCapabilityDocument(
    string Id,
    int Version,
    string Fingerprint,
    string Owner,
    string Description,
    string Mode,
    JsonElement InputSchema,
    string InputSchemaHash,
    IReadOnlyList<string> ProcedureIds,
    bool RequiresConfirmation,
    bool RequiresIdempotencyKey);

/// <summary>Projects one currently authorized, non-secret capability contract for browser controls.</summary>
public sealed class ControlSystemCapabilityExplorer(ISystemCapabilityCatalog? capabilities = null)
{
    public ControlSystemCapabilityDocument? Get(
        AuthorizationAuditEvidence authorization,
        string capabilityId)
    {
        ValidateId(capabilityId);
        if (capabilities is null)
            throw Error("SYSTEM_CAPABILITY_UNAVAILABLE",
                "System capability discovery is unavailable.", StatusCodes.Status503ServiceUnavailable);

        var result = capabilities.Discover(
            SystemCapabilityInvocationContext.FromAuthorization(authorization));
        if (!result.Ok)
        {
            var code = result.Error?.Code ?? "SYSTEM_CAPABILITY_UNAVAILABLE";
            var status = code is "PRIVATE_OPERATOR_UNAUTHENTICATED" or
                "PRIVATE_OPERATOR_WRONG_SCOPE" or "PRIVATE_OPERATOR_DENIED"
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status503ServiceUnavailable;
            throw Error(code, result.Error?.Message ??
                "System capability discovery is unavailable.", status);
        }

        var descriptor = result.Capabilities.SingleOrDefault(value =>
            value.Sensitivity != SystemCapabilitySensitivity.Secret &&
            string.Equals(value.Id, capabilityId, StringComparison.Ordinal));
        if (descriptor is null) return null;

        JsonElement inputSchema;
        try
        {
            using var schema = JsonDocument.Parse(descriptor.InputSchemaJson);
            inputSchema = schema.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Error("SYSTEM_CAPABILITY_SCHEMA_UNAVAILABLE",
                "The system capability input contract is unavailable.",
                StatusCodes.Status503ServiceUnavailable);
        }

        return new(
            descriptor.Id,
            descriptor.Version,
            descriptor.Fingerprint,
            descriptor.Owner,
            descriptor.Description,
            descriptor.ModeName,
            inputSchema,
            descriptor.InputSchemaHash,
            Array.AsReadOnly(descriptor.ProcedureIds.ToArray()),
            descriptor.RequiresConfirmation,
            descriptor.RequiresIdempotencyKey);
    }

    private static void ValidateId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 120 ||
            !value.StartsWith("system.", StringComparison.Ordinal) ||
            value.Any(character => !(char.IsAsciiLetterLower(character) ||
                char.IsAsciiDigit(character) || character is '.' or '-')))
            throw Error("SYSTEM_CAPABILITY_ID_INVALID",
                "The system capability ID is invalid.", StatusCodes.Status400BadRequest);
    }

    private static ControlAssistantException Error(string code, string message, int status) =>
        new(code, message, status);
}
