using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;

namespace DantesRoleplay.Tests;

public sealed class ApplicationCapabilityContractTests
{
    private static readonly ApplicationIdentifier ApplicationId = ApplicationIdentifier.Parse("fixture");

    [Fact]
    public void Application_mechanics_and_queries_share_the_outer_contract_without_losing_catalog_provenance()
    {
        var requirements = JsonSerializer.Serialize(new
        {
            roles = new
            {
                subject = new { components = new[] { "fixture.stats" }, description = "Selected subject." }
            }
        });
        var mechanicJson = JsonSerializer.Serialize(new
        {
            id = "fixture.mechanic.example",
            requirements,
            status = "active"
        });
        var mechanic = Record("mechanic", "fixture.mechanic.example", mechanicJson);

        var mechanicContract = ApplicationCapabilityContractAdapter.Create(ApplicationId, mechanic, "space.1");

        Assert.Equal(mechanic.ContentFingerprint, mechanicContract.SourceFingerprint);
        Assert.Equal("application-mechanic", mechanicContract.SourceKind);
        Assert.Equal("generic", mechanicContract.Input.Status);
        Assert.True(mechanicContract.Operations.SupportsPreview);
        Assert.Equal("subject", Assert.Single(mechanicContract.Roles).Name);

        const string outputSchema = "{\"type\":\"object\",\"additionalProperties\":false}";
        var queryJson = JsonSerializer.Serialize(new
        {
            id = "fixture.query.example",
            category = "query.example",
            name = "Example query",
            description = "Reads one example projection.",
            matches = new[] { "read example" },
            roles = new Dictionary<string, string> { ["subject"] = "Selected subject." },
            executor = "projection",
            projection = new
            {
                qualifiedId = "fixture.projection.example",
                version = 1,
                contentHash = Hash("projection"),
                outputSchemaHash = Hash(outputSchema)
            },
            outputSchema = JsonSerializer.Deserialize<JsonElement>(outputSchema),
            exposure = "model-visible",
            status = "active"
        });
        var query = Record("query", "fixture.query.example", queryJson);

        var queryContract = ApplicationCapabilityContractAdapter.Create(ApplicationId, query, "space.1");

        Assert.Equal(query.ContentFingerprint, queryContract.SourceFingerprint);
        Assert.Equal("application-query", queryContract.SourceKind);
        Assert.True(queryContract.Operations.ReadsState);
        Assert.False(queryContract.Operations.ChangesState);
        Assert.Equal("authored", queryContract.Output.Status);
        Assert.Equal("model-visible-query", queryContract.Authorization.Policy);
    }

    [Fact]
    public void Authored_mechanic_input_schema_is_exposed_with_a_schema_valid_example()
    {
        var requirements = JsonSerializer.Serialize(new
        {
            roles = new { },
            inputSchema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "locationId", "summary" },
                properties = new
                {
                    locationId = new { type = "string", minLength = 1 },
                    summary = new { type = "string", minLength = 1 }
                }
            }
        });
        var content = JsonSerializer.Serialize(new
        {
            id = "fixture.mechanic.location-shell",
            requirements,
            status = "active"
        });

        var contract = ApplicationCapabilityContractAdapter.Create(
            ApplicationId, Record("mechanic", "fixture.mechanic.location-shell", content), "space.1");

        Assert.Equal("authored", contract.Input.Status);
        using var schema = JsonDocument.Parse(contract.Input.SchemaJson);
        Assert.Equal(["locationId", "summary"], schema.RootElement.GetProperty("required")
            .EnumerateArray().Select(value => value.GetString()!).ToArray());
        using var example = JsonDocument.Parse(Assert.Single(contract.Examples).InputJson);
        Assert.Equal(["locationId", "summary"], example.RootElement.EnumerateObject()
            .Select(value => value.Name).ToArray());
    }

    private static CatalogRecordDefinition Record(string kind, string id, string content) =>
        new("fixture", kind, id, "Example", "An example capability.", [], [], kind,
            "active", 1, content, Hash(content), "fixture", $"{kind}.json");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
