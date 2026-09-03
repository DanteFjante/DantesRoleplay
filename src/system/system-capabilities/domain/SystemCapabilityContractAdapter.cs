using System.Text.Json.Nodes;
using DantesRoleplay.Capabilities;

namespace DantesRoleplay.SystemCapabilities;

public static class SystemCapabilityContractAdapter
{
    public static CapabilityContractDescriptor Create(SystemCapabilityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var properties = JsonNode.Parse(descriptor.InputSchemaJson)?["properties"] as JsonObject;
        var requiresApplication = properties?.ContainsKey("applicationId") == true;
        var requiresStateSpace = properties?.ContainsKey("stateSpaceId") == true;
        var context = new List<string>();
        if (requiresApplication) context.Add("application-id");
        if (requiresStateSpace) context.Add("state-space-id");
        context.Add("trusted-principal");
        var example = CapabilityContractBuilder.MinimalExample(descriptor.InputSchemaJson);
        return CapabilityContractBuilder.Create(
            descriptor.Id,
            descriptor.Version,
            "system-capability",
            descriptor.Fingerprint,
            descriptor.Owner,
            descriptor.Id,
            descriptor.Description,
            CapabilityContractLifecycle.Active,
            descriptor.Mode == SystemCapabilityMode.Read
                ? new(true, false, false)
                : new(true, true, true),
            descriptor.InputSchemaJson,
            CapabilityContractSchemaStatus.Authored,
            descriptor.OutputSchemaJson,
            CapabilityContractSchemaStatus.Authored,
            new("system", requiresApplication, requiresStateSpace, context),
            [],
            new("private-operator", descriptor.RequiredCapabilityName, descriptor.SensitivityName),
            descriptor.RequiresConfirmation,
            descriptor.RequiresIdempotencyKey,
            descriptor.ProcedureIds,
            [new("minimal", example)],
            [
                new("SYSTEM_CAPABILITY_INPUT_INVALID", "The capability input does not satisfy its declared schema.", "Reload the descriptor and correct the input."),
                new("PRIVATE_OPERATOR_DENIED", "The current principal is not authorized for this capability.", "Use an authorized private-operator context."),
                new("SYSTEM_CAPABILITY_STALE", "The capability or its preconditions changed.", "Reload the descriptor and repeat preflight.")
            ],
            [new(descriptor.Id, "Reload this exact capability contract and retry with current input.", example)],
            descriptor.InputSchemaProfile,
            descriptor.OutputSchemaProfile,
            descriptor.InputSchemaHash,
            descriptor.OutputSchemaHash);
    }
}
