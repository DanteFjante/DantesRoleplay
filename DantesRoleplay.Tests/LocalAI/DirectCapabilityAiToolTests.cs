using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Authorization;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class DirectCapabilityAiToolTests
{
    [Fact]
    public void Every_mcp_kind_is_exposed_as_a_fine_grained_direct_tool_without_transport_verbs()
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
        Assert.Contains(tools, value => value.Definition.Name == "write_effects");
        Assert.Contains(tools, value => value.Definition.Name == "write_system_trigger_scheduling");
        foreach (var name in new[] { "read_system_catalog_search", "read_system_feature_search" })
        {
            var tool = Assert.Single(tools, value => value.Definition.Name == name);
            using var schema = JsonDocument.Parse(tool.Definition.InputSchemaJson);
            Assert.False(schema.RootElement.GetProperty("properties").TryGetProperty("overlayProfileId", out _));
        }
        foreach (var tool in tools)
            using (JsonDocument.Parse(tool.Definition.InputSchemaJson)) { }
    }
}
