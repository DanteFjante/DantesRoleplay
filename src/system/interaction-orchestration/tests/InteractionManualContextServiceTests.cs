using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Procedures;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Tests;

/// <summary>Scope fixtures supply host authority, state, activation and catalog snapshots only.</summary>
public sealed class InteractionManualContextServiceTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("sample-app");
    private static readonly string HashA = Hash("activation");

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Global_categories_filter_before_disclosure_and_preserve_inactive_history()
    {
        await using var db = _fixture.CreateContext();
        var procedures = new ProcedureStore(db);
        await procedures.WriteAsync(Request("procedure.system.allowed", "system.operations", "inspect scope", "## Inspect\nRead state."));
        await procedures.WriteAsync(Request("procedure.secret.hidden", "secret", "inspect scope", "secret instructions"));
        await procedures.WriteAsync(Request("procedure.system.retired", "system.operations", "old scope", "old instructions"));
        await procedures.WriteAsync(Request("procedure.system.retired", "system.operations", "old scope", "retired instructions",
            status: ProcedureStatus.Archived));
        var service = Service(procedures, new Changes(App, HashA), "system");

        IInteractionManualContextService shared = service;
        var result = await shared.DiscoverAsync(new(Host(), "inspect scope"));
        using var packet = JsonDocument.Parse(result.DataJson!);
        var manual = packet.RootElement.GetProperty("manualSections").GetRawText();

        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Contains("procedure.system.allowed", manual, StringComparison.Ordinal);
        Assert.DoesNotContain("secret instructions", result.DataJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("retired instructions", result.DataJson!, StringComparison.Ordinal);
        Assert.Equal("old instructions", (await procedures.GetAsync("procedure.system.retired", 1))!.Instructions);
        Assert.Equal(2, (await procedures.GetVersionsAsync("procedure.system.retired")).Count);
    }

    [Fact]
    public async Task Phrase_revisions_refresh_evidence_and_ambiguous_candidates_remain_unselected()
    {
        await using var db = _fixture.CreateContext();
        var procedures = new ProcedureStore(db);
        await procedures.WriteAsync(Request("procedure.system.alpha", "system", "inspect scope", "## Alpha\nRead alpha.", matches: "inspect scope"));
        await procedures.WriteAsync(Request("procedure.system.beta", "system", "inspect scope", "## Beta\nRead beta.", matches: "inspect scope"));
        var service = Service(procedures, new Changes(App, HashA), "system");

        var first = await service.DiscoverAsync(Host(), "inspect scope");
        await procedures.WriteAsync(Request("procedure.system.alpha", "system", "inspect scope", "## Alpha\nRead alpha.", matches: "inspect area"));
        var second = await service.DiscoverAsync(Host("command.2"), "inspect scope");
        var refreshed = await service.DiscoverAsync(Host("command.3"), "inspect scope",
            expectedResolutionFingerprint: JsonDocument.Parse(first.DataJson!).RootElement
                .GetProperty("resolutionFingerprint").GetString());
        using var firstPacket = JsonDocument.Parse(first.DataJson!);
        using var secondPacket = JsonDocument.Parse(second.DataJson!);
        using var refreshedPacket = JsonDocument.Parse(refreshed.DataJson!);

        Assert.Equal("ambiguous", firstPacket.RootElement.GetProperty("resolution").GetString());
        Assert.Equal("unresolved", secondPacket.RootElement.GetProperty("resolution").GetString());
        Assert.NotEqual(firstPacket.RootElement.GetProperty("resolutionFingerprint").GetString(),
            secondPacket.RootElement.GetProperty("resolutionFingerprint").GetString());
        Assert.Equal("refresh-required", refreshedPacket.RootElement.GetProperty("resolution").GetString());
        Assert.Equal(JsonValueKind.Null, firstPacket.RootElement.GetProperty("selectedAction").ValueKind);
    }

    [Fact]
    public async Task Rejects_invalid_input_exhausted_budget_and_elapsed_deadline_without_writing()
    {
        await using var db = _fixture.CreateContext();
        var procedures = new ProcedureStore(db);
        await procedures.WriteAsync(Request("procedure.system.inspect", "system", "inspect", "Read."));
        var service = Service(procedures, new Changes(App, HashA), "system");

        var invalid = await service.DiscoverAsync(Host(), "inspect", "[]");
        var exhausted = await service.DiscoverAsync(Host(budget: new(1, DateTime.UtcNow.AddMinutes(1)), consume: true), "inspect");
        var elapsed = await service.DiscoverAsync(Host(budget: new(1, DateTime.UtcNow.AddMinutes(-1))), "inspect");

        Assert.Equal(InteractionInvocationResultTag.Failed, invalid.Tag);
        Assert.Equal("INVOCATION_BUDGET_EXHAUSTED", exhausted.Code);
        Assert.Equal("INVOCATION_DEADLINE_EXCEEDED", elapsed.Code);
        Assert.Single(await procedures.GetVersionsAsync("procedure.system.inspect"));
    }

    private static InteractionManualContextService Service(IProcedureStore procedures, IApplicationDefinitionChangeReader changes,
        params string[] categories) => new(procedures, new InteractionFeatureRetriever(new Snapshots()), new Allow(),
        new Spaces(State()), changes, categories);

    [Fact]
    public async Task Denied_grant_stops_before_even_reading_the_procedure_store()
    {
        var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);
        var service = Service(store, new Changes(App, HashA), "system");
        await db.DisposeAsync(); // Any attempted store read would fail with an unavailable dependency.
        var result = await service.DiscoverAsync(Host(grant: "revoked.grant"), "inspect");
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", result.Code);
        Assert.Null(result.DataJson);
    }

    [Fact]
    public async Task Escaped_context_is_bounded_and_carries_verifiable_result_and_separate_source_hashes()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);
        var written = await store.WriteAsync(Request("procedure.system.bounded", "system", "inspect",
            "## Inspect\n" + string.Concat(Enumerable.Repeat("\"å\" ", 2000)), matches: "inspect"));
        var service = Service(store, new Changes(App, HashA), "system");
        var result = await service.DiscoverAsync(Host(), "inspect", "{\"target\":\"untouched\"}", maximumCharacters: 4000);
        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.InRange(result.DataJson!.Length, 1, 4000);
        var packet = JsonNode.Parse(result.DataJson)!;
        Assert.True(packet["bounded"]!.GetValue<bool>());
        Assert.Equal("untouched", packet["knownInputs"]!["target"]!.GetValue<string>());
        var hash = packet["resultFingerprint"]!.GetValue<string>();
        packet["resultFingerprint"] = new string('0', 64);
        Assert.Equal(hash, Hash(InteractionCanonicalJson.CanonicalizeObject(packet.ToJsonString())));
        var larger = await service.DiscoverAsync(Host(), "inspect", maximumCharacters: 24_000);
        var first = JsonNode.Parse(larger.DataJson!)!["manualSections"]!.AsArray()[0]!;
        Assert.Equal(written.Procedure.SourceHash, first["storedSourceHash"]!.GetValue<string>());
        Assert.Equal(Hash("inspect"), first["associationFingerprint"]!.GetValue<string>());
    }

    [Fact]
    public async Task Two_authored_intents_find_one_exact_query_and_report_missing_inputs_without_invoking_it()
    {
        await using var db = _fixture.CreateContext();
        const string content = """{"inputSchema":{"type":"object","required":["target"]}}""";
        var record = new CatalogRecordDefinition("sample", ApplicationQueryContract.CatalogKind,
            "sample-app.query.inspect", "Inspect", "Read target", [], ["inspect target", "show target"], "", "active", 1,
            content, Hash(content), "fixture", "queries/inspect.json");
        var snapshot = new ActiveCatalogFeatureSnapshot(CatalogNavigationManifest.Create(App, Hash("query-manifest"), "fixture",
            [new("sample", "Sample", "Fixture")], [new("sample", "", "Sample", "Fixture", CatalogDescriptionStatus.Authored)], [record]),
            [new(record, SourceTrust.Trusted)]) { EffectiveSetFingerprint = HashA };
        var service = new InteractionManualContextService(new ProcedureStore(db),
            new InteractionFeatureRetriever(new Snapshots(snapshot)), new Allow(), new Spaces(State()), new Changes(App, HashA), []);
        var first = await service.DiscoverAsync(Host(), "inspect target");
        var second = await service.DiscoverAsync(Host(), "show target", "{\"target\":\"one\"}");
        var a = JsonNode.Parse(first.DataJson!)!;
        var b = JsonNode.Parse(second.DataJson!)!;
        Assert.Equal(a["candidates"]![0]!["reference"]!.ToJsonString(), b["candidates"]![0]!["reference"]!.ToJsonString());
        Assert.Equal("target", a["candidates"]![0]!["missingInputs"]![0]!.GetValue<string>());
        Assert.Empty(b["candidates"]![0]!["missingInputs"]!.AsArray());
        Assert.Null(a["selectedAction"]);
        Assert.Null(first.Receipt);
        Assert.Null(second.Receipt);
    }

    private static InteractionInvocationHost Host(string command = "command.1", InteractionInvocationBudget? budget = null, bool consume = false, string grant = "grant.1")
    {
        budget ??= new(2, DateTime.UtcNow.AddMinutes(1));
        if (consume) Assert.True(budget.TryConsumeOperation());
        return new(TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"), Revision(),
            "state.1", grant, command, InteractionStateRevision.From(State()), InteractionExecutionProfile.ReadOnly, budget);
    }

    private static StateSpaceView State() => new("state.1", Revision(), HashA, 1, DateTime.UtcNow, DateTime.UtcNow);
    private static ApplicationRevision Revision() => new(App, 1, Hash("application"), []);
    private static WriteProcedureRequest Request(string id, string category, string description, string instructions,
        string matches = "", ProcedureStatus? status = null) => new()
    {
        Id = id, Category = category, Name = id, Description = description, Instructions = instructions,
        Governs = "inspect", Matches = matches, Status = status, CreatedBy = "test"
    };
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class Snapshots(ActiveCatalogFeatureSnapshot? supplied = null) : IActiveCatalogFeatureSnapshotProvider
    {
        private readonly ActiveCatalogFeatureSnapshot _snapshot = new(CatalogNavigationManifest.Create(App, Hash("manifest"), "fixture",
            [new("sample", "Sample", "Fixture")], [new("sample", "", "Sample", "Fixture", CatalogDescriptionStatus.Authored)], []), [] )
        { EffectiveSetFingerprint = HashA };
        public bool TryGetSnapshot(ApplicationIdentifier applicationId, out ActiveCatalogFeatureSnapshot snapshot)
        { snapshot = supplied ?? _snapshot; return applicationId == App; }
    }
    private sealed class Allow : IInteractionAuthorizationPolicy
    { public InteractionAuthorizationDecision Evaluate(InteractionAuthorizationRequest request) => InteractionAuthorizationDecision.Allow(request, "grant.1"); }
    private sealed class Spaces(StateSpaceView state) : IStateSpaceRegistry
    { public StateSpaceView? Get(string id) => id == state.StateSpaceId ? state : null; public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException(); public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier app, string? after, int limit) => new([state], null); }
    private sealed class Changes(ApplicationIdentifier app, string fingerprint) : IApplicationDefinitionChangeReader
    { public ApplicationDefinitionChange? CurrentChange(ApplicationIdentifier id) => id == app ? new(app, 1, fingerprint, "operation.1", DateTime.UnixEpoch, new([], []), new(Hash("dependencies"), "fixture", true), new("rebuildable", false, false)) : null; public ApplicationDefinitionChange? RevisionChange(ApplicationIdentifier id, int revision) => CurrentChange(id); public IReadOnlyList<ApplicationDefinitionChange> ChangesAfter(ApplicationIdentifier id, int after, int limit) => []; }
}
