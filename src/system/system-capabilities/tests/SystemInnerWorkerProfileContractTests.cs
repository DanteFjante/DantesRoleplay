using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.Tests;

public sealed class SystemInnerWorkerProfileContractTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Schema = """{"additionalProperties":false,"properties":{"answer":{"type":"string"}},"type":"object"}""";

    [Fact]
    public void Host_resolved_profile_preserves_worker_scope_and_copies_narrow_evidence()
    {
        var tools = new[] { Binding("read_value", "capability.read"), Binding("write_value", "capability.write") };
        var references = new[] { "context:z", "context:a" };
        var profile = Create(tools, references);
        tools[0] = Binding("other_value", "capability.other");
        references[0] = "mutated";

        Assert.Equal("principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", profile.Worker.InvocationHost.Principal.PrincipalId);
        Assert.Equal("fixture-app", profile.Worker.InvocationHost.ApplicationRevision.ApplicationId.Value);
        Assert.Equal("state.1", profile.Worker.InvocationHost.StateSpaceId);
        Assert.Equal("grant.1", profile.Worker.InvocationHost.GrantReference);
        Assert.Equal("command.1", profile.Worker.InvocationHost.CommandId);
        Assert.Equal(["context:a", "context:z"], profile.RequiredContextReferences);
        Assert.Equal(["read_value", "write_value"], profile.ToolBindings.Select(value => value.Definition.Name));
        Assert.Equal(HashOf(Schema), profile.OutputSchemaFingerprint);
        Assert.Equal("manual.packet.1", profile.ManualContext.Reference);
        Assert.Equal("authority.1", profile.AuthorityProvenance.Reference);
    }

    [Fact]
    public void Resolved_profile_is_host_only_and_cannot_be_deserialized_or_serialized()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SystemInnerWorkerResolvedProfile>("{}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(Create()));
    }

    [Theory]
    [InlineData("other.profile")]
    [InlineData("INVALID")]
    public void Profile_identity_and_existing_agent_limits_fail_closed(string profileId)
    {
        var error = Assert.Throws<InteractionContractException>(() => Create(profile: new(profileId, "Worker", "Host identity")));
        Assert.Equal(profileId == "other.profile" ? "WORKER_PROFILE_IDENTITY_CONFLICT" : "INVALID_WORKER_PROFILE", error.Code);
    }

    [Fact]
    public void Output_schema_fingerprint_must_pin_the_worker_schema()
    {
        var error = Assert.Throws<InteractionContractException>(() => Create(schemaFingerprint: Hash));
        Assert.Equal("WORKER_OUTPUT_SCHEMA_STALE", error.Code);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Duplicate_tool_or_context_references_are_rejected(bool duplicateTools, bool duplicateReferences)
    {
        var tools = duplicateTools
            ? new[] { Binding("read_value", "capability.read"), Binding("read_value", "capability.write") }
            : new[] { Binding("read_value", "capability.read") };
        var references = duplicateReferences ? new[] { "context:a", "context:a" } : new[] { "context:a" };

        var error = Assert.Throws<InteractionContractException>(() => Create(tools, references));
        Assert.Equal(duplicateTools ? "INVALID_WORKER_TOOL_BINDINGS" : "INVALID_WORKER_CONTEXT_REFERENCES", error.Code);
    }

    [Fact]
    public void Tool_aliases_may_share_one_exact_capability_but_not_conflicting_revisions()
    {
        var aliases = Create([Binding("read_value", "capability.read"), Binding("read_summary", "capability.read")]);
        Assert.Equal(2, aliases.ToolBindings.Count);

        var error = Assert.Throws<InteractionContractException>(() => Create([
            Binding("read_value", "capability.read", 1), Binding("read_summary", "capability.read", 2)]));
        Assert.Equal("INVALID_WORKER_TOOL_BINDINGS", error.Code);
    }

    [Fact]
    public void Empty_tool_allowlist_means_no_tools_and_provenance_is_not_an_authorization_decision()
    {
        var profile = Create([], []);
        Assert.Empty(profile.ToolBindings);
        Assert.Empty(profile.RequiredContextReferences);
        Assert.Equal("grant-revision.1", profile.AuthorityProvenance.GrantRevision);
    }

    [Fact]
    public void Tool_binding_requires_valid_bounded_canonical_definition()
    {
        var definition = new AiToolDefinition("read_value", "Fixture tool", """{"z":1,"a":2}""");
        var binding = new SystemInnerWorkerToolBinding(definition, new("capability.read", 1, Hash));

        Assert.Equal("""{"a":2,"z":1}""", binding.Definition.InputSchemaJson);
        Assert.Equal("INVALID_WORKER_TOOL", Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerToolBinding(new("bad.tool", "Fixture", "{}"), new("capability.read", 1, Hash))).Code);
        Assert.Equal("JSON_OBJECT_REQUIRED", Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerToolBinding(new("read_value", "Fixture", "[]"), new("capability.read", 1, Hash))).Code);
        Assert.Equal("INVALID_WORKER_TOOL", Assert.Throws<InteractionContractException>(() =>
            new SystemInnerWorkerToolBinding(new("read_value", new string('x', 1_001), "{}"), new("capability.read", 1, Hash))).Code);
    }

    [Fact]
    public void Required_evidence_dependencies_cannot_be_null()
    {
        Assert.Throws<ArgumentNullException>(() => new SystemInnerWorkerResolvedProfile(
            Worker(), new("fixture.worker", 1, Hash), new("fixture.worker", "Worker", "Identity"), HashOf(Schema), [], [], null!,
            new("authority.1", "grant-revision.1", Hash)));
    }

    [Fact]
    public void Ai_budget_uses_the_host_default_or_preserves_a_narrower_host_ceiling()
    {
        var defaultBudget = Create().AiBudget;
        var narrowed = Create(aiBudget: new SystemInnerWorkerAiBudget(1_024, 2)).AiBudget;

        Assert.Equal(SystemInnerWorkerAiBudget.DefaultProviderTokens, defaultBudget.ProviderTokens);
        Assert.Equal(SystemInnerWorkerAiBudget.DefaultToolCalls, defaultBudget.ToolCalls);
        Assert.Equal(1_024, narrowed.ProviderTokens);
        Assert.Equal(2, narrowed.ToolCalls);
    }

    private static SystemInnerWorkerResolvedProfile Create(
        IReadOnlyList<SystemInnerWorkerToolBinding>? tools = null,
        IReadOnlyList<string>? references = null,
        AiAgentProfile? profile = null,
        string? schemaFingerprint = null,
        SystemInnerWorkerAiBudget? aiBudget = null) => new(
        Worker(), new("fixture.worker", 1, Hash), profile ?? new("fixture.worker", "Worker", "Host identity", "Host resolved instructions."),
        schemaFingerprint ?? HashOf(Schema), tools ?? [Binding("read_value", "capability.read")], references ?? ["context:a"],
        new("manual.packet.1", Hash), new("authority.1", "grant-revision.1", Hash), aiBudget);

    private static SystemInnerWorkerToolBinding Binding(string name, string capability, int version = 1) =>
        new(new(name, "Fixture tool", "{\"type\":\"object\"}"), new(capability, version, Hash));

    private static SystemInnerWorkerRequest Worker() => new(Host(), new("procedure.fixture", 1, Hash), "{\"work\":true}", Schema);

    private static InteractionInvocationHost Host() => new(
        TrustedPrincipalContext.VerifiedPrincipal("principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "fixture"),
        new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []), "state.1", "grant.1", "command.1", "revision.1",
        InteractionExecutionProfile.Workflow, new InteractionInvocationBudget(2, DateTime.UtcNow.AddMinutes(1)));

    private static string HashOf(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
