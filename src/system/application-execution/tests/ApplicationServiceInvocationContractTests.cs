using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.ApplicationExecution.Tests;

public sealed class ApplicationServiceInvocationContractTests
{
    private const string Schema = """{"type":"object","additionalProperties":false,"properties":{}}""";
    private readonly BoundedJsonSchemaValidator _schemas = new();

    [Fact]
    public void Exact_retained_declaration_round_trips_with_existing_profile_schema_hashes()
    {
        var definition = Definition();
        var (selected, record) = Retained(definition.ToJson());
        var parsed = new ApplicationReadOnlyServiceDefinitionReader(_schemas).ReadRetained(selected, record);
        Assert.Equal(definition.ToJson(), parsed.ToJson());
        Assert.Equal(definition.ToJson(), JsonSerializer.Serialize(parsed));
        Assert.Equal(_schemas.Compile(Schema).SchemaHash, parsed.InputSchemaHash);
        var read = Assert.Single(parsed.Reads);
        Assert.Equal("fixture.query.inspect", read.QualifiedQueryId);
        Assert.Equal(2, read.Contract.ProjectionVersion);
        Assert.Equal("viewer", read.RoleMappings["subject"]);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("read")]
    [InlineData("contract")]
    public void Unknown_declaration_fields_cannot_manufacture_capabilities(string location)
    {
        var value = JsonNode.Parse(Definition().ToJson())!;
        var target = location == "root" ? value : location == "read" ? value["reads"]![0]! : value["reads"]![0]!["contract"]!;
        target["principal"] = "invented-authority";
        var (selected, record) = Retained(value.ToJsonString());
        Assert.Equal("INVALID_SERVICE_DECLARATION", Assert.Throws<InteractionContractException>(() =>
            new ApplicationReadOnlyServiceDefinitionReader(_schemas).ReadRetained(selected, record)).Code);
    }

    [Fact]
    public void Duplicate_and_missing_properties_are_rejected()
    {
        var json = Definition().ToJson();
        var (selected, record) = Retained("{\"reads\":[]," + json[1..]);
        Assert.Equal("DUPLICATE_JSON_PROPERTY", Assert.Throws<InteractionContractException>(() =>
            new ApplicationReadOnlyServiceDefinitionReader(_schemas).ReadRetained(selected, record)).Code);
        var missing = JsonNode.Parse(json)!;
        missing.AsObject().Remove("reads");
        (selected, record) = Retained(missing.ToJsonString());
        Assert.Throws<InteractionContractException>(() =>
            new ApplicationReadOnlyServiceDefinitionReader(_schemas).ReadRetained(selected, record));
    }

    [Fact]
    public void Schema_mismatch_is_rejected_in_constructors_and_retained_reader()
    {
        Assert.Equal("SERVICE_SCHEMA_MISMATCH", Assert.Throws<InteractionContractException>(() =>
            new ApplicationReadOnlyServiceDefinition(Hash("wrong"), Schema, _schemas.Compile(Schema).SchemaHash,
                Schema, [], _schemas)).Code);
        var value = JsonNode.Parse(Definition().ToJson())!;
        value["reads"]![0]!["contract"]!["outputSchemaHash"] = Hash("wrong");
        var (selected, record) = Retained(value.ToJsonString());
        Assert.Equal("SERVICE_SCHEMA_MISMATCH", Assert.Throws<InteractionContractException>(() =>
            new ApplicationReadOnlyServiceDefinitionReader(_schemas).ReadRetained(selected, record)).Code);
    }

    [Fact]
    public void Definition_identity_version_and_bytes_are_pinned()
    {
        var (selected, record) = Retained(Definition().ToJson());
        var reader = new ApplicationReadOnlyServiceDefinitionReader(_schemas);
        foreach (var changed in new[]
        {
            record with { ContentJson = record.ContentJson + " " },
            record with { Summary = record.Summary with { Version = 4 } },
            record with { Summary = record.Summary with { QualifiedId = "fixture.other" } },
            record with { Summary = record.Summary with { Kind = "query" } }
        })
            Assert.Equal("SERVICE_DEFINITION_STALE", Assert.Throws<InteractionContractException>(() =>
                reader.ReadRetained(selected, changed)).Code);
    }

    [Fact]
    public void Authored_json_never_creates_service_host_authority()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApplicationReadOnlyServiceInvocationRequest>(
            """{"host":{"principal":"invented"},"inputJson":"{}"}"""));
        var request = Request();
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(request));
        Assert.Equal(16, request.Host.Budget.RemainingOperations); // Execution, not construction, consumes it.
    }

    [Theory]
    [InlineData(InteractionExecutionProfile.Atomic)]
    [InlineData(InteractionExecutionProfile.Workflow)]
    public void Unsupported_profiles_cannot_enter_read_only_services(InteractionExecutionProfile profile)
    {
        Assert.Equal("SERVICE_PROFILE_UNAVAILABLE", Assert.Throws<InteractionContractException>(() => Request(profile)).Code);
    }

    [Fact]
    public void Host_bindings_are_immutable_and_progress_bounds_apply_to_escaped_wire_bytes()
    {
        var bindings = new Dictionary<string, string> { ["viewer"] = "authorized-entity" };
        var request = Request(bindings: bindings);
        bindings["viewer"] = "substituted-entity";
        Assert.Equal("authorized-entity", request.HostRoleBindings["viewer"]);
        var progress = new ApplicationServiceProgressFrame(1, "{\"phase\":\"reading\"}");
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(progress));
        Assert.Equal(["sequence", "dataJson"], wire.RootElement.EnumerateObject().Select(value => value.Name));
        var quoted = JsonSerializer.Serialize(new { text = new string('"', 300) });
        Assert.True(Encoding.UTF8.GetByteCount(quoted) < 2048);
        Assert.Equal("SERVICE_PROGRESS_TOO_LARGE", Assert.Throws<InteractionContractException>(() =>
            new ApplicationServiceProgressFrame(2, quoted)).Code);
    }

    private ApplicationReadOnlyServiceDefinition Definition()
    {
        var schema = _schemas.Compile(Schema);
        var read = new ApplicationServiceReadDeclaration("inspect", "fixture.query.inspect",
            new(ApplicationQueryContract.MechanicProjectionExecutor, "fixture.mechanic.inspect", 2,
                Hash("projection"), schema.SchemaHash, schema.NormalizedSchema, ApplicationQueryExposure.ModelVisible, ["subject"]),
            new Dictionary<string, string> { ["subject"] = "viewer" }, _schemas, schema.NormalizedSchema);
        return new(schema.SchemaHash, schema.NormalizedSchema, schema.SchemaHash, schema.NormalizedSchema, [read], _schemas);
    }

    [Fact]
    public async Task Progress_channel_is_bounded_single_use_and_closed_attempts_still_count()
    {
        var channel = new ApplicationServiceProgressChannel();
        Assert.True(channel.TryBind());
        Assert.False(channel.TryBind());
        for (var i = 0; i < 8; i++)
            Assert.Equal(ApplicationServiceProgressDisposition.Accepted, channel.TryWrite("{}"));
        Assert.Equal(ApplicationServiceProgressDisposition.Backpressured, channel.TryWrite("{}"));
        Assert.Equal(1, (await channel.Reader.ReadAsync()).Sequence);
        Assert.Equal(ApplicationServiceProgressDisposition.Accepted, channel.TryWrite("{}"));
        var frames = new List<ApplicationServiceProgressFrame>();
        while (channel.Reader.TryRead(out var frame)) frames.Add(frame);
        Assert.Equal(Enumerable.Range(2, 8), frames.Select(frame => frame.Sequence));
        channel.Complete();
        await channel.Reader.Completion;
        for (var i = 10; i < 32; i++)
            Assert.Equal(ApplicationServiceProgressDisposition.Closed, channel.TryWrite("{}"));
        Assert.Equal("SERVICE_PROGRESS_LIMIT", Assert.Throws<InteractionContractException>(() => channel.TryWrite("{}")).Code);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApplicationServiceProgressChannel>("{}"));
    }

    [Fact]
    public void Backpressured_frames_charge_the_aggregate_serialized_byte_allowance()
    {
        var channel = new ApplicationServiceProgressChannel();
        Assert.True(channel.TryBind());
        var payload = JsonSerializer.Serialize(new { text = new string('a', 1500) });
        for (var i = 0; i < 8; i++)
            Assert.Equal(ApplicationServiceProgressDisposition.Accepted, channel.TryWrite(payload));
        Assert.Equal(ApplicationServiceProgressDisposition.Backpressured, channel.TryWrite(payload));
        Assert.Equal(ApplicationServiceProgressDisposition.Backpressured, channel.TryWrite(payload));
        Assert.Equal("SERVICE_PROGRESS_LIMIT", Assert.Throws<InteractionContractException>(() => channel.TryWrite(payload)).Code);
    }

    private ApplicationReadOnlyServiceInvocationRequest Request(
        InteractionExecutionProfile profile = InteractionExecutionProfile.ReadOnly,
        IReadOnlyDictionary<string, string>? bindings = null)
    {
        var definition = Definition();
        var (selected, _) = Retained(definition.ToJson());
        var host = new InteractionInvocationHost(TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            new ApplicationRevision(ApplicationIdentifier.Parse("fixture"), 1, Hash("application"), []),
            "space", "grant", "command", "state-revision", profile, new(16, DateTime.UtcNow.AddMinutes(1)));
        return new(host, selected, definition, bindings ?? new Dictionary<string, string> { ["viewer"] = "entity" },
            "{}", ExecutionLimits.ReadModel);
    }

    private static (SystemTaskSelectedDefinition Selected, CatalogRecordView Record) Retained(string declaration)
    {
        var content = JsonSerializer.Serialize(new { requirements = "{\"service\":" + declaration + "}", source = "return {data:{}};" });
        var hash = Hash(content);
        return (new("fixture.mechanic.service", 3, hash), new(new("fixture", "mechanic", "fixture.mechanic.service",
            "Service", "Fixture", "mechanics", "active", 3, hash, "fixture", "mechanics/service.md"), content));
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
