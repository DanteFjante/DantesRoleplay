using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class DirectCapabilityAiToolTests
{
    [Fact]
    public async Task Every_mcp_kind_is_exposed_as_a_fine_grained_direct_tool_without_transport_verbs()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var source = new DirectCapabilityAiToolSource(services, new PrivateOperatorAuthorizationPolicy());
        var tools = source.CreateTools(new(
            new("operator", "Operator", "Operate the system."),
            new("test", "model", [new(AiMessageRole.User, "work")]),
            new(PrivateOperatorPrincipal.Create("test", "operator"),
                PrivateOperatorAuthorizationPolicy.PrivateHostScope, "direct-ai-test"),
            null,
            null,
            () => []));

        Assert.Equal(McpVerbCatalog.QueryKinds.Count - 1 + McpVerbCatalog.CommitKinds.Count, tools.Count);
        Assert.Equal(tools.Count, tools.Select(value => value.Definition.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(tools, value => value.Definition.Name is "orient" or "query" or "commit");
        Assert.Contains(tools, value => value.Definition.Name == "read_world");
        Assert.Contains(tools, value => value.Definition.Name == "read_mechanics");
        Assert.Contains(tools, value => value.Definition.Name == "write_application_action_execute");
        Assert.DoesNotContain(tools, value => value.Definition.Name is "write_component" or
            "write_effects" or "write_mechanic" or "write_action");
        Assert.Contains(tools, value => value.Definition.Name == "write_system_trigger_scheduling");
        foreach (var name in new[] { "read_system_catalog_search", "read_system_feature_search" })
        {
            var tool = Assert.Single(tools, value => value.Definition.Name == name);
            using var schema = JsonDocument.Parse(tool.Definition.InputSchemaJson);
            Assert.False(schema.RootElement.GetProperty("properties").TryGetProperty("overlayProfileId", out _));
        }
        foreach (var tool in tools)
            using (JsonDocument.Parse(tool.Definition.InputSchemaJson)) { }

        Assert.All(McpVerbCatalog.Descriptors, descriptor =>
        {
            Assert.Matches("^[0-9A-F]{64}$", descriptor.Fingerprint);
            Assert.NotEmpty(descriptor.Examples);
            Assert.NotEmpty(descriptor.Errors);
            Assert.NotEmpty(descriptor.RecoveryActions);
        });
        var actionDescriptor = Assert.Single(McpVerbCatalog.CommitKinds,
            value => value.Name == "application.action.execute").Descriptor;
        using var actionSchema = JsonDocument.Parse(actionDescriptor.Input.SchemaJson);
        var payload = actionSchema.RootElement.GetProperty("properties").GetProperty("payload");
        Assert.False(payload.GetProperty("additionalProperties").GetBoolean());
        Assert.True(payload.GetProperty("properties").TryGetProperty("qualifiedMechanicId", out _));
        Assert.True(payload.GetProperty("properties").TryGetProperty("mechanicVersion", out _));
        using var actionOutputSchema = JsonDocument.Parse(actionDescriptor.Output.SchemaJson);
        var actionData = actionOutputSchema.RootElement.GetProperty("properties").GetProperty("data");
        Assert.True(actionData.GetProperty("properties").TryGetProperty("affectedEntityIds", out _));
        Assert.True(actionData.GetProperty("properties").TryGetProperty("receipt", out _));
        Assert.True(actionData.GetProperty("properties").TryGetProperty("nextActions", out _));

        using var arguments = JsonDocument.Parse("{\"payload\":{\"idempotencyKey\":\"action.1\"," +
            "\"applicationId\":\"fixture\",\"stateSpaceId\":\"space.1\"," +
            "\"qualifiedMechanicId\":\"fixture.mechanic.exact\",\"mechanicVersion\":1," +
            "\"contentFingerprint\":\"" + new string('A', 64) + "\",\"roleEntityIds\":{},\"input\":{}}}");
        var directAction = Assert.Single(tools,
            value => value.Definition.Name == "write_application_action_execute");
        var denied = await directAction.InvokeAsync(new("call.1", directAction.Definition.Name,
            arguments.RootElement.Clone(), AiRequestKind.Message));
        Assert.False(denied.Ok);
        Assert.Equal("AI_TOOL_CONFIRMATION_REQUIRED", denied.ErrorCode);
    }

    [Fact]
    public void Typed_system_capabilities_suppress_the_legacy_mcp_duplicate()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var authorization = new PrivateOperatorAuthorizationPolicy();
        var systemCatalog = new SystemCapabilityCatalog(
            [new ApplicationsSystemCapabilityHandler(new InMemoryApplicationRegistry())],
            new BoundedJsonSchemaValidator(), authorization);
        var source = new DirectCapabilityAiToolSource(services, authorization, systemCatalog);
        var tools = source.CreateTools(new(
            new("operator", "Operator", "Operate the system."),
            new("test", "model", [new(AiMessageRole.User, "work")]),
            new(PrivateOperatorPrincipal.Create("test", "operator"),
                PrivateOperatorAuthorizationPolicy.PrivateHostScope, "direct-ai-dedup-test"),
            null, null, () => []));

        Assert.DoesNotContain(tools, value => value.Definition.Name == "read_system_applications");
    }
}
