using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Capabilities;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;

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
        Assert.Equal([true, false], contract.Examples.Select(value => value.ExpectedValid).ToArray());
        using var example = JsonDocument.Parse(contract.Examples.Single(value => value.ExpectedValid).InputJson);
        Assert.Equal(["locationId", "summary"], example.RootElement.EnumerateObject()
            .Select(value => value.Name).ToArray());
    }

    [Fact]
    public void Field_based_object_queries_expose_the_host_transport_as_generated_not_authored()
    {
        var content = JsonSerializer.Serialize(new
        {
            id = "fixture.query.members",
            category = "world.members",
            name = "Members",
            description = "Lists authorized members.",
            matches = new[] { "list members" },
            roles = new Dictionary<string, string>
            {
                ["campaign"] = "The owning campaign.",
                ["member"] = "A listed member."
            },
            executor = "object-projection",
            profile = "application-object/v2",
            @object = new
            {
                qualifiedId = "fixture.object.members",
                version = 1,
                contentFingerprint = new string('A', 64)
            },
            collection = "members",
            exposure = "model-visible",
            status = "active"
        });

        var contract = ApplicationCapabilityContractAdapter.Create(ApplicationId,
            Record("query", "fixture.query.members", content), "space.1");

        Assert.Equal("generated", contract.Output.Status);
        Assert.Equal("{\"type\":\"object\"}", contract.Output.SchemaJson);
        Assert.Empty(CapabilityContractConformanceValidator.FindProblems(
            contract, new BoundedJsonSchemaValidator()));
        Assert.Contains("The output schema must reject unknown top-level properties.",
            CapabilityContractConformanceValidator.FindProblems(contract with
            {
                Output = contract.Output with { Status = CapabilityContractSchemaStatus.Authored }
            }, new BoundedJsonSchemaValidator()));
    }

    [Fact]
    public void Field_based_query_discovery_exposes_exact_component_field_and_write_provenance()
    {
        const string componentSchema = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}";
        var component = new EcsComponentReference("fixture.identity", 3,
            CapabilityContractBuilder.SchemaHash(componentSchema));
        var objectReference = new ProjectionReference("fixture.object.member", 2, new string('A', 64));
        var field = new ApplicationObjectFieldProvenance("/name", ["identity"], "member",
            component, "/name", true);
        var discovery = new ApplicationObjectDiscovery(objectReference,
            RegisteredApplicationObjectContract.FieldBasedContractProfileId,
            [new("source-1", ["identity"], "member", true, component,
                CapabilityContractBuilder.JsonSchemaProfile, componentSchema)],
            [field], [new("/name", "set", "identity", "/name", null)]);
        var content = JsonSerializer.Serialize(new
        {
            id = "fixture.query.member",
            category = "world.member",
            name = "Member",
            description = "Reads one member.",
            matches = new[] { "read member" },
            roles = new Dictionary<string, string> { ["member"] = "Selected member." },
            executor = "object-projection",
            profile = "application-object/v2",
            @object = new
            {
                qualifiedId = objectReference.QualifiedId,
                version = objectReference.Version,
                contentFingerprint = objectReference.ContentHash
            },
            collection = "members",
            exposure = "model-visible",
            status = "active"
        });
        var record = Record("query", "fixture.query.member", content);

        var legacyDescriptor = ApplicationCapabilityContractAdapter.Create(ApplicationId, record, "space.1");
        var contract = ApplicationCapabilityContractAdapter.Create(ApplicationId, record, "space.1", discovery);

        var objectContract = Assert.IsType<CapabilityObjectDiscoveryContract>(contract.ObjectDiscovery);
        Assert.Equal(objectReference.ContentHash, objectContract.ContentFingerprint);
        var source = Assert.Single(objectContract.Sources);
        Assert.Equal(component.SchemaHash, source.Schema.SchemaHash);
        Assert.Equal("fixture.identity", Assert.Single(contract.Roles).RequiredComponentIds.Single());
        Assert.Equal("/name", Assert.Single(objectContract.Fields).SourcePath);
        var write = Assert.Single(objectContract.Writes);
        Assert.Equal("source-1", write.SourceReference);
        Assert.Equal(["set"], write.Operations);
        Assert.NotEqual(legacyDescriptor.Fingerprint, contract.Fingerprint);
        Assert.DoesNotContain("ObjectDiscovery", JsonSerializer.Serialize(legacyDescriptor), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ApplicationCapabilityContractAdapter.Create(
            ApplicationId, record, "space.1", discovery with
            { Object = objectReference with { ContentHash = new string('B', 64) } }));
    }

    [Fact]
    public void Field_based_query_discovery_surfaces_parameter_and_write_provenance()
    {
        const string schema = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}";
        var component = new EcsComponentReference("fixture.identity", 1,
            CapabilityContractBuilder.SchemaHash(schema));
        var objectReference = new ProjectionReference("fixture.object.member", 3, new string('A', 64));
        var discovery = new ApplicationObjectDiscovery(objectReference,
            RegisteredApplicationObjectContract.FieldBasedContractProfileId,
            [new("source-1", ["member"], "member", true, component,
                CapabilityContractBuilder.JsonSchemaProfile, schema)],
            [new("/name", ["member"], "member", component, "/name", true)],
            [new("/name", "set", "member", "/name", null)]);
        var content = JsonSerializer.Serialize(new
        {
            id = "fixture.query.member",
            category = "member.read",
            name = "Member",
            description = "Reads a parameterized member.",
            matches = new[] { "read member" },
            roles = new Dictionary<string, string>
            {
                ["member"] = "Route member.",
                ["viewer"] = "Trusted viewer."
            },
            roleBindings = new Dictionary<string, object>
            {
                ["member"] = new { source = "route-entity" },
                ["viewer"] = new { source = "authorized-context", key = "seat.viewer" }
            },
            executor = "object-projection",
            profile = RegisteredApplicationObjectContract.FieldBasedContractProfileId,
            @object = new
            {
                qualifiedId = objectReference.QualifiedId,
                version = objectReference.Version,
                contentFingerprint = objectReference.ContentHash
            },
            collection = "members",
            exposure = "model-visible",
            status = "active"
        });

        var contract = ApplicationCapabilityContractAdapter.Create(
            ApplicationId, Record("query", "fixture.query.member", content), "space.1", discovery);

        var bindings = contract.ObjectDiscovery!.RoleBindings!;
        Assert.Contains(bindings, value => value is
            { Role: "member", Source: "route-entity", Pointer: null, Key: null });
        Assert.Contains(bindings, value => value is
            { Role: "viewer", Source: "authorized-context", Pointer: null, Key: "seat.viewer" });
        var write = Assert.Single(contract.ObjectDiscovery.Writes);
        Assert.Equal("source-1", write.SourceReference);
        Assert.Equal("/name", write.SourcePath);
    }

    [Fact]
    public void Field_based_query_discovery_accepts_relationship_resolved_collection_source_roles()
    {
        const string rootSchema = "{\"type\":\"object\",\"properties\":{\"title\":{\"type\":\"string\"}}}";
        const string rowSchema = "{\"type\":\"object\",\"properties\":{\"status\":{\"type\":\"string\"}}}";
        var root = new EcsComponentReference("fixture.campaign", 1,
            CapabilityContractBuilder.SchemaHash(rootSchema));
        var row = new EcsComponentReference("fixture.participation", 1,
            CapabilityContractBuilder.SchemaHash(rowSchema));
        var objectReference = new ProjectionReference("fixture.object.campaign", 4, new string('A', 64));
        var discovery = new ApplicationObjectDiscovery(objectReference,
            RegisteredApplicationObjectContract.FieldBasedContractProfileId,
            [
                new("source-1", ["campaign"], "campaign", true, root,
                    CapabilityContractBuilder.JsonSchemaProfile, rootSchema),
                new("source-2", ["collection", "party", "party", "to"], "participation", false, row,
                    CapabilityContractBuilder.JsonSchemaProfile, rowSchema)
            ],
            [
                new("/title", ["campaign"], "campaign", root, "/title", true),
                new("/party/*/status", ["collection", "party", "party", "to"],
                    "participation", row, "/status", false)
            ], []);
        var content = JsonSerializer.Serialize(new
        {
            id = "fixture.query.campaign",
            category = "campaign.summary",
            name = "Campaign",
            description = "Reads one campaign and its resolved party rows.",
            matches = new[] { "read campaign" },
            roles = new Dictionary<string, string> { ["campaign"] = "Selected campaign." },
            executor = "object-projection",
            profile = RegisteredApplicationObjectContract.FieldBasedContractProfileId,
            @object = new
            {
                qualifiedId = objectReference.QualifiedId,
                version = objectReference.Version,
                contentFingerprint = objectReference.ContentHash
            },
            collection = "party",
            exposure = "model-visible",
            status = "active"
        });
        var record = Record("query", "fixture.query.campaign", content);

        var contract = ApplicationCapabilityContractAdapter.Create(
            ApplicationId, record, "space.1", discovery);

        Assert.Equal(["campaign"], contract.Roles.Select(role => role.Name));
        Assert.Contains(contract.ObjectDiscovery!.Sources, source =>
            source.Role == "participation" && source.InputPath.SequenceEqual(
                ["collection", "party", "party", "to"]));
        Assert.Throws<ArgumentException>(() => ApplicationCapabilityContractAdapter.Create(
            ApplicationId, record, "space.1", discovery with
            {
                Sources = [discovery.Sources[0], discovery.Sources[1] with { InputPath = ["participation"] }]
            }));
    }

    [Fact]
    public void Collectionless_query_discovery_accepts_registered_reference_sources_without_binding_optional_roles()
    {
        const string schema = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}";
        var component = new EcsComponentReference("fixture.definition", 1,
            CapabilityContractBuilder.SchemaHash(schema));
        var objectReference = new ProjectionReference("fixture.object.item", 3, new string('A', 64));
        var discovery = new ApplicationObjectDiscovery(objectReference,
            RegisteredApplicationObjectContract.FieldBasedContractProfileId,
            [new("source-1", ["definition", "definition"], "definition", false,
                component, CapabilityContractBuilder.JsonSchemaProfile, schema)],
            [new("/definition/name", ["definition", "definition"], "definition",
                component, "/name", false)], []);
        var content = JsonSerializer.Serialize(new
        {
            id = "fixture.query.item",
            category = "item.read",
            name = "Item",
            description = "Reads one item and its declared reference.",
            matches = new[] { "read item" },
            roles = new Dictionary<string, string> { ["item"] = "Route item." },
            roleBindings = new Dictionary<string, object>
                { ["item"] = new { source = "route-entity" } },
            executor = "object-projection",
            profile = RegisteredApplicationObjectContract.FieldBasedContractProfileId,
            @object = new
            {
                qualifiedId = objectReference.QualifiedId,
                version = objectReference.Version,
                contentFingerprint = objectReference.ContentHash
            },
            exposure = "model-visible",
            status = "active"
        });

        var contract = ApplicationCapabilityContractAdapter.Create(
            ApplicationId, Record("query", "fixture.query.item", content), "space.1", discovery);

        Assert.Null(ApplicationQueryContract.Parse(content, ApplicationId).ObjectCollectionId);
        Assert.Contains(contract.ObjectDiscovery!.Sources, source =>
            source.Role == "definition" && source.InputPath.SequenceEqual(["definition", "definition"]));
        Assert.Equal("item", Assert.Single(contract.ObjectDiscovery.RoleBindings!).Role);
    }

    private static CatalogRecordDefinition Record(string kind, string id, string content) =>
        new("fixture", kind, id, "Example", "An example capability.", [], [], kind,
            "active", 1, content, Hash(content), "fixture", $"{kind}.json");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
