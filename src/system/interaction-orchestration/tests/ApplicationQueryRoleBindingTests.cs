using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Interactions.Tests;

public sealed class ApplicationQueryRoleBindingTests
{
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("sample-app");

    [Fact]
    public void Explicit_route_input_and_authorized_context_roles_resolve_without_role_name_semantics()
    {
        var contract = ApplicationQueryContract.Parse(QueryJson(), App);
        var resolver = new ApplicationQueryRoleBindingResolver(new BoundedJsonSchemaValidator());

        var roles = resolver.Resolve(contract, """{"targetId":"entity.target"}""",
            new("entity.route", new Dictionary<string, string>
            {
                ["session.owner"] = "entity.owner"
            }));

        Assert.Equal("entity.route", roles["anchor"]);
        Assert.Equal("entity.target", roles["selected"]);
        Assert.Equal("entity.owner", roles["trusted"]);
    }

    [Fact]
    public void Caller_input_cannot_override_or_supply_an_authorized_context_binding()
    {
        var contract = ApplicationQueryContract.Parse(QueryJson(), App);
        var resolver = new ApplicationQueryRoleBindingResolver(new BoundedJsonSchemaValidator());
        var context = new ApplicationQueryRoleBindingContext("entity.route",
            new Dictionary<string, string> { ["session.owner"] = "entity.authorized" });

        var roles = resolver.Resolve(contract, """{"targetId":"entity.target"}""", context);
        Assert.Equal("entity.authorized", roles["trusted"]);
        var injected = Assert.Throws<ApplicationReadModelException>(() => resolver.Resolve(contract,
            """{"targetId":"entity.target","session.owner":"entity.attacker"}""", context));
        Assert.Equal("READ_MODEL_INPUT_INVALID", injected.Code);
        var missing = Assert.Throws<ApplicationReadModelException>(() => resolver.Resolve(contract,
            """{"targetId":"entity.target"}""", context with
            { AuthorizedRoleEntityIds = new Dictionary<string, string>() }));
        Assert.Equal("READ_MODEL_ROLES_UNAVAILABLE", missing.Code);
    }

    [Fact]
    public void Binding_declaration_round_trips_in_canonical_catalog_content()
    {
        var parsed = ApplicationQueryContract.Parse(QueryJson(), App);
        var roundTrip = ApplicationQueryContract.Parse(ApplicationCatalogRecordContent.QueryJson(parsed), App);

        Assert.Equal(parsed.RoleBindings, roundTrip.RoleBindings);
        Assert.Equal(parsed.InputSchemaJson, roundTrip.InputSchemaJson);
    }

    [Fact]
    public void Neutral_selection_round_trips_without_assigning_meaning_to_role_names()
    {
        var root = JsonNode.Parse(QueryJson())!.AsObject();
        root["selection"] = new JsonObject
        {
            ["queryId"] = "sample-app.query.selection-proof",
            ["targetRole"] = "trusted",
            ["resultPointer"] = "/result/a~1b~0c",
            ["roleBindings"] = new JsonObject
            {
                ["proof-root"] = "anchor",
                ["proof-context"] = "trusted"
            }
        };

        var parsed = ApplicationQueryContract.Parse(root.ToJsonString(), App);
        var roundTrip = ApplicationQueryContract.Parse(ApplicationCatalogRecordContent.QueryJson(parsed), App);

        Assert.Equal("sample-app.query.selection-proof", roundTrip.Selection!.QueryId);
        Assert.Equal("trusted", roundTrip.Selection.TargetRole);
        Assert.Equal("/result/a~1b~0c", roundTrip.Selection.ResultPointer);
        Assert.Equal("anchor", roundTrip.Selection.RoleBindings["proof-root"]);
        Assert.Equal("trusted", roundTrip.Selection.RoleBindings["proof-context"]);
    }

    [Fact]
    public void Collectionless_object_and_mechanic_role_bindings_round_trip_without_legacy_selection()
    {
        var scalar = JsonNode.Parse(QueryJson())!.AsObject();
        scalar.Remove("collection");
        var scalarContract = ApplicationQueryContract.Parse(scalar.ToJsonString(), App);
        var scalarContent = ApplicationCatalogRecordContent.QueryJson(scalarContract);
        var scalarRoundTrip = ApplicationQueryContract.Parse(scalarContent, App);
        Assert.Null(scalarRoundTrip.ObjectCollectionId);
        Assert.False(JsonNode.Parse(scalarContent)!.AsObject().ContainsKey("collection"));
        Assert.Equal(scalarContract.RoleBindings, scalarRoundTrip.RoleBindings);

        var mechanic = JsonNode.Parse(QueryJson())!.AsObject();
        mechanic["executor"] = ApplicationQueryContract.MechanicProjectionExecutor;
        mechanic.Remove("object");
        mechanic.Remove("collection");
        mechanic["projection"] = new JsonObject
        {
            ["qualifiedId"] = "sample-app.mechanic.fixture",
            ["version"] = 1,
            ["contentHash"] = new string('A', 64),
            ["outputSchemaHash"] = new string('B', 64)
        };
        var parsedMechanic = ApplicationQueryContract.Parse(mechanic.ToJsonString(), App);
        var mechanicRoundTrip = ApplicationQueryContract.Parse(
            ApplicationCatalogRecordContent.QueryJson(parsedMechanic), App);
        Assert.Equal(ApplicationQueryContract.MechanicProjectionExecutor, mechanicRoundTrip.Executor);
        Assert.Equal(parsedMechanic.RoleBindings, mechanicRoundTrip.RoleBindings);
    }

    [Fact]
    public void Declaration_requires_exact_roles_route_anchor_and_valid_source_metadata()
    {
        var missingRole = JsonNode.Parse(QueryJson())!.AsObject();
        missingRole["roleBindings"]!.AsObject().Remove("trusted");
        Assert.Throws<ArgumentException>(() => ApplicationQueryContract.Parse(missingRole.ToJsonString(), App));

        var noRoute = JsonNode.Parse(QueryJson())!.AsObject();
        noRoute["roleBindings"]!["anchor"] = new JsonObject
            { ["source"] = "authorized-context", ["key"] = "session.anchor" };
        Assert.Throws<ArgumentException>(() => ApplicationQueryContract.Parse(noRoute.ToJsonString(), App));

        var invalidPointer = JsonNode.Parse(QueryJson())!.AsObject();
        invalidPointer["roleBindings"]!["selected"]!["pointer"] = "/bad~2pointer";
        Assert.Throws<ArgumentException>(() => ApplicationQueryContract.Parse(invalidPointer.ToJsonString(), App));

        var legacySelection = JsonNode.Parse(QueryJson())!.AsObject();
        legacySelection["campaignSelection"] = new JsonObject
            { ["queryId"] = "sample-app.query.selection", ["entityIdField"] = "selectedId" };
        Assert.Throws<ArgumentException>(() => ApplicationQueryContract.Parse(legacySelection.ToJsonString(), App));

        var malformedSelection = JsonNode.Parse(QueryJson())!.AsObject();
        malformedSelection["selection"] = Selection("sample-app.query.proof", "anchor", "/bad~2pointer");
        Assert.Throws<ArgumentException>(() => ApplicationQueryContract.Parse(malformedSelection.ToJsonString(), App));

        var unknownTarget = JsonNode.Parse(QueryJson())!.AsObject();
        unknownTarget["selection"] = Selection("sample-app.query.proof", "missing", "/selectedId");
        Assert.Throws<ArgumentException>(() => ApplicationQueryContract.Parse(unknownTarget.ToJsonString(), App));

        var selfSelection = JsonNode.Parse(QueryJson())!.AsObject();
        selfSelection["selection"] = Selection("sample-app.query.parameterized-object", "anchor", "/selectedId");
        Assert.Throws<ArgumentException>(() => ApplicationQueryContract.Parse(selfSelection.ToJsonString(), App));
    }

    private static JsonObject Selection(string queryId, string targetRole, string pointer) => new()
    {
        ["queryId"] = queryId,
        ["targetRole"] = targetRole,
        ["resultPointer"] = pointer,
        ["roleBindings"] = new JsonObject { ["proof-root"] = targetRole }
    };

    [Fact]
    public void Input_binding_uses_rfc6901_escaping_and_array_segments_after_schema_validation()
    {
        var root = JsonNode.Parse(QueryJson())!.AsObject();
        root["roleBindings"]!["selected"]!["pointer"] = "/targets/0/a~1b~0c";
        root["inputSchema"] = JsonNode.Parse("""
            {"type":"object","additionalProperties":false,"required":["targets"],"properties":{
              "targets":{"type":"array","minItems":1,"maxItems":1,"items":{
                "type":"object","additionalProperties":false,"required":["a/b~c"],
                "properties":{"a/b~c":{"type":"string","minLength":1,"maxLength":200}}
              }}
            }}
            """);
        var contract = ApplicationQueryContract.Parse(root.ToJsonString(), App);
        var roles = new ApplicationQueryRoleBindingResolver(new BoundedJsonSchemaValidator()).Resolve(
            contract, """{"targets":[{"a/b~c":"entity.target"}]}""",
            new("entity.route", new Dictionary<string, string>
            {
                ["session.owner"] = "entity.owner"
            }));

        Assert.Equal("entity.target", roles["selected"]);
    }

    [Fact]
    public void Array_pointer_indices_are_canonical_while_numeric_object_keys_remain_exact()
    {
        var resolver = new ApplicationQueryRoleBindingResolver(new BoundedJsonSchemaValidator());
        foreach (var token in new[] { "01", "+0", " 0" })
        {
            var root = JsonNode.Parse(QueryJson())!.AsObject();
            root["roleBindings"]!["selected"]!["pointer"] = $"/targets/{token}";
            root["inputSchema"] = JsonNode.Parse("""
                {"type":"object","additionalProperties":false,"required":["targets"],"properties":{
                  "targets":{"type":"array","minItems":1,"maxItems":1,
                    "items":{"type":"string","minLength":1,"maxLength":200}}
                }}
                """);
            var contract = ApplicationQueryContract.Parse(root.ToJsonString(), App);
            var failure = Assert.Throws<ApplicationReadModelException>(() => resolver.Resolve(
                contract, """{"targets":["entity.target"]}""",
                new("entity.route", new Dictionary<string, string>
                {
                    ["session.owner"] = "entity.owner"
                })));
            Assert.Equal("READ_MODEL_ROLES_UNAVAILABLE", failure.Code);
        }

        var objectRoot = JsonNode.Parse(QueryJson())!.AsObject();
        objectRoot["roleBindings"]!["selected"]!["pointer"] = "/targets/01";
        objectRoot["inputSchema"] = JsonNode.Parse("""
            {"type":"object","additionalProperties":false,"required":["targets"],"properties":{
              "targets":{"type":"object","additionalProperties":false,"required":["01"],
                "properties":{"01":{"type":"string","minLength":1,"maxLength":200}}}
            }}
            """);
        var objectContract = ApplicationQueryContract.Parse(objectRoot.ToJsonString(), App);
        var roles = resolver.Resolve(objectContract, """{"targets":{"01":"entity.target"}}""",
            new("entity.route", new Dictionary<string, string>
            {
                ["session.owner"] = "entity.owner"
            }));
        Assert.Equal("entity.target", roles["selected"]);
    }

    private static string QueryJson() => """
        {
          "id":"sample-app.query.parameterized-object",
          "category":"sample.read",
          "name":"Parameterized object",
          "description":"Reads one registered object with opaque roles.",
          "matches":["parameterized object"],
          "roles":{"anchor":"Route anchor.","selected":"Selected entity.","trusted":"Host-authorized entity."},
          "roleBindings":{
            "anchor":{"source":"route-entity"},
            "selected":{"source":"input","pointer":"/targetId"},
            "trusted":{"source":"authorized-context","key":"session.owner"}
          },
          "executor":"object-projection",
          "object":{"qualifiedId":"sample-app.object.fixture","version":1,"contentFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"},
          "collection":"items",
          "outputSchema":{"type":"object"},
          "inputSchema":{"type":"object","additionalProperties":false,"required":["targetId"],"properties":{"targetId":{"type":"string","minLength":1,"maxLength":200}}},
          "exposure":"model-visible",
          "status":"active"
        }
        """;
}
