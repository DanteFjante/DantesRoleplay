using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class ItemViewContractTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"x\":1,\"x\":2}")]
    [InlineData("{\"x\":{\"a\":1,\"A\":2}}")]
    [InlineData("{\"x\":1,}")]
    public void Input_rejects_malformed_or_ambiguous_values(string input) =>
        Assert.Equal("READ_MODEL_INPUT_INVALID", Assert.Throws<ApplicationReadModelException>(() => ApplicationReadModelInput.Normalize(input)).Code);

    [Fact]
    public void Input_enforces_UTF8_bytes_and_depth_before_projection()
    {
        Assert.Throws<ApplicationReadModelException>(() => ApplicationReadModelInput.Normalize("{\"x\":\"" + new string('é', 600) + "\"}"));
        Assert.Throws<ApplicationReadModelException>(() => ApplicationReadModelInput.Normalize(string.Concat(Enumerable.Repeat("{\"x\":", 9)) + "0" + new string('}', 9)));
        Assert.Equal("{}", ApplicationReadModelInput.Normalize(" { } "));
    }

    [Theory]
    [InlineData("details")]
    [InlineData("recipes")]
    [InlineData("uses")]
    public void Approved_drafts_compile_in_the_real_host_and_enforce_closed_input(string kind)
    {
        var query = JsonNode.Parse(Read(kind + ".query.draft.json"))!["query"]!;
        var validator = new BoundedJsonSchemaValidator();
        var input = validator.Compile(query["inputSchema"]!.ToJsonString());
        var output = validator.Compile(query["outputSchema"]!.ToJsonString());
        Assert.True(input.IsAccepted, JsonSerializer.Serialize(input.Diagnostics));
        Assert.True(output.IsAccepted, JsonSerializer.Serialize(output.Diagnostics));
        var example = JsonNode.Parse(Read("examples.json"))![kind]!;
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(output.NormalizedSchema, example.ToJsonString()).Status);
        query["projection"]!["contentHash"] = new string('A', 64);
        query["projection"]!["outputSchemaHash"] = output.SchemaHash;
        var parsed = ApplicationQueryContract.Parse(query.ToJsonString(), ApplicationIdentifier.Parse("dnd2024"));
        Assert.NotNull(parsed.InputSchemaJson);
        var request = new JsonObject { ["itemId"] = "fixture.item" };
        if (kind != "details") request["expectedSourceRevision"] = null;
        if (kind == "recipes") { request["makesOffset"] = 0; request["usesOffset"] = 0; }
        if (kind == "uses") request["offset"] = 0;
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(input.NormalizedSchema, request.ToJsonString()).Status);
        request["observerId"] = "fixture.other";
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(input.NormalizedSchema, request.ToJsonString()).Status);
        var draft = JsonNode.Parse(Read(kind + ".requirements.draft.json"))!;
        var requirements = draft["existingRequirementFields"]!.DeepClone();
        requirements["authorizedContext"] = draft["proposedAuthorizedContext"]!.DeepClone();
        Assert.NotNull(MechanicRequirements.Parse(requirements.ToJsonString()).AuthorizedContext);
        requirements["authorizedContext"]!["trustCaller"] = true;
        Assert.Throws<JsonException>(() => MechanicRequirements.Parse(requirements.ToJsonString()));
    }

    [Fact]
    public void Invalid_nullable_declarations_are_rejected_as_contract_errors()
    {
        var draft = JsonNode.Parse(Read("details.requirements.draft.json"))!;
        var req = draft["existingRequirementFields"]!.DeepClone();
        req["authorizedContext"] = draft["proposedAuthorizedContext"]!.DeepClone();
        req["authorizedContext"]!["sourceSets"] = null;
        Assert.Throws<JsonException>(() => MechanicRequirements.Parse(req.ToJsonString()));
    }

    private static string Read(string filename)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root.FullName, "docs", "current", "item-view-contracts", filename));
    }
}
