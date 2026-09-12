using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Procedures;
using DantesRoleplay.Retrieval;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Tests;

/// <summary>Grant/ownership fixtures test consumer admission, not production issuance or resolution.</summary>
public sealed class InteractionManualContextServiceTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _derivedRoot = Path.Combine(Path.GetTempPath(), "manual-sections-" + Guid.NewGuid().ToString("N"));
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("sample-app");
    private static readonly string HashA = Hash("activation");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_derivedRoot)) Directory.Delete(_derivedRoot, recursive: true);
    }

    [Fact]
    public async Task Authored_inner_worker_submit_contract_is_available_through_the_system_manual()
    {
        await using var db = _fixture.CreateContext();
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        var path = Path.Combine(repository!.FullName, "catalog", "procedures", "procedure",
            "system", "inner-worker", "submit.md");
        var authored = ProcedureFile.Parse(await File.ReadAllTextAsync(path), path);
        var procedures = new ProcedureStore(db);
        await procedures.WriteAsync(new()
        {
            Id = authored.Id,
            Category = authored.Category,
            Name = authored.Name,
            Description = authored.Description,
            Governs = authored.Governs,
            Matches = authored.Matches,
            Instructions = authored.Instructions,
            Constraints = authored.Constraints,
            Status = authored.Status,
            CreatedBy = authored.CreatedBy,
            ChangeNote = authored.ChangeNote
        });
        var result = await Service(procedures, new Changes(App, HashA), "system")
            .DiscoverAsync(Host(), "dependency handles Execute reconnect worker");

        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        using var packet = JsonDocument.Parse(result.DataJson!);
        Assert.Contains("procedure.system.inner-worker.submit",
            packet.RootElement.GetProperty("manualSections").GetRawText(),
            StringComparison.Ordinal);
        Assert.Contains("dependencyHandles", authored.Description, StringComparison.Ordinal);
        Assert.Contains("system.inner-worker.read", authored.Instructions, StringComparison.Ordinal);
        Assert.Contains("Execute", authored.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Global_categories_filter_before_disclosure_and_preserve_inactive_history()
    {
        await using var db = _fixture.CreateContext();
        var procedures = new ProcedureStore(db);
        await procedures.WriteAsync(Request("procedure.system.allowed", "system", "inspect scope", "## Inspect\nRead state."));
        await procedures.WriteAsync(Request("procedure.system.nested", "system.private", "inspect scope", "nested private instructions"));
        await procedures.WriteAsync(Request("procedure.secret.hidden", "secret", "inspect scope", "secret instructions"));
        await procedures.WriteAsync(Request("procedure.system.retired", "system", "old scope", "old instructions"));
        await procedures.WriteAsync(Request("procedure.system.retired", "system", "old scope", "retired instructions",
            status: ProcedureStatus.Archived));
        var service = Service(procedures, new Changes(App, HashA), "system");

        IInteractionManualContextService shared = service;
        var result = await shared.DiscoverAsync(new(Host(), "inspect scope"));
        using var packet = JsonDocument.Parse(result.DataJson!);
        var manual = packet.RootElement.GetProperty("manualSections").GetRawText();

        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Contains("procedure.system.allowed", manual, StringComparison.Ordinal);
        Assert.DoesNotContain("secret instructions", result.DataJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("nested private instructions", result.DataJson!, StringComparison.Ordinal);
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
    public async Task System_manual_semantic_sections_are_filtered_cached_invalidated_and_restartable()
    {
        await using var db = _fixture.CreateContext();
        var procedures = new ProcedureStore(db);
        await procedures.WriteAsync(Request("procedure.system.recovery", "system", "Operational guidance", """
            ## Routine
            Inspect the ordinary status.
            ## Recovery
            Resume persisted campaign from checkpoint.
            """));
        await procedures.WriteAsync(Request("procedure.secret.recovery", "secret", "DENIED_SECRET", """
            ## Hidden
            Resume persisted campaign from checkpoint. DENIED_SECRET
            """));
        var embeddings = new SectionEmbeddings();
        var location = InteractionDerivedIndexLocation.Create(_derivedRoot);
        InteractionManualContextService CreateService() => new(procedures,
            new InteractionFeatureRetriever(new Snapshots()), new FixtureGrants(), new FixtureTargets(),
            new Changes(App, HashA), ["system"], sectionRetriever: new(embeddings,
                new SqliteInteractionDerivedVectorIndex(location), new InteractionRetrievalRefreshCoordinator()));

        var first = await CreateService().DiscoverAsync(Host(), "restore lost progress");
        Assert.Equal(InteractionInvocationResultTag.Completed, first.Tag);
        Assert.Contains("Resume persisted campaign from checkpoint.", first.DataJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("DENIED_SECRET", first.DataJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(embeddings.Inputs.SelectMany(value => value),
            value => value.Contains("DENIED_SECRET", StringComparison.Ordinal));
        Assert.Equal(1, embeddings.DocumentBatches);

        _ = await CreateService().DiscoverAsync(Host("command.2"), "restore lost progress");
        // A new service and index instance reuse the retained generation without re-embedding sections.
        _ = await CreateService().DiscoverAsync(Host("command.3"), "restore lost progress");
        Assert.Equal(1, embeddings.DocumentBatches);

        await procedures.WriteAsync(Request("procedure.system.recovery", "system", "Operational guidance", """
            ## Routine
            Inspect the ordinary status.
            ## Recovery
            Continue the saved session from its durable checkpoint.
            """));
        var edited = await CreateService().DiscoverAsync(Host("command.4"), "restore lost progress");
        Assert.Contains("Continue the saved session", edited.DataJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("Resume persisted campaign", edited.DataJson!, StringComparison.Ordinal);
        Assert.Equal(2, embeddings.DocumentBatches);

        embeddings.Revision = "2";
        _ = await CreateService().DiscoverAsync(Host("command.5"), "restore lost progress");
        Assert.Equal(3, embeddings.DocumentBatches);
    }

    [Fact]
    public async Task System_manual_keeps_current_lexical_sections_when_embedding_provider_is_unavailable()
    {
        await using var db = _fixture.CreateContext();
        var procedures = new ProcedureStore(db);
        await procedures.WriteAsync(Request("procedure.system.recovery", "system", "Recovery", """
            ## Recovery
            Restore lost progress safely.
            """));
        var location = InteractionDerivedIndexLocation.Create(_derivedRoot);
        var sections = new ProcedureManualSectionRetriever(new UnavailableSectionEmbeddings(),
            new SqliteInteractionDerivedVectorIndex(location), new InteractionRetrievalRefreshCoordinator());
        var service = new InteractionManualContextService(procedures, new InteractionFeatureRetriever(new Snapshots()),
            new FixtureGrants(), new FixtureTargets(), new Changes(App, HashA), ["system"], sectionRetriever: sections);

        var result = await service.DiscoverAsync(Host(), "restore lost progress");

        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Contains("Restore lost progress safely.", result.DataJson!, StringComparison.Ordinal);
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
        params string[] categories) => new(procedures, new InteractionFeatureRetriever(new Snapshots()), new FixtureGrants(),
        new FixtureTargets(), changes, categories);

    [Fact]
    public async Task Invalid_grant_never_discloses_application_content_while_orientation_is_host_selected()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);
        await store.WriteAsync(Request("procedure.system.orientation", "system", "inspect", "Host selected orientation."));
        var record = AppRecord("sample-app.query.secret", "Secret definition details", "inspect");
        var service = new InteractionManualContextService(store, new InteractionFeatureRetriever(new Snapshots(AppSnapshot("one", record))),
            new FixtureGrants(), new FixtureTargets(), new Changes(App, HashA), ["system"]);
        var result = await service.DiscoverAsync(Host(grant: "revoked.grant"), "inspect");
        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.DoesNotContain("Secret definition", result.DataJson!);
        Assert.Contains("Host selected orientation", result.DataJson!);
        Assert.Empty(JsonNode.Parse(result.DataJson!)!["candidates"]!.AsArray());
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
    public async Task Completion_evidence_pins_each_budgeted_packet_while_resolution_and_input_remain_stable()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);
        await store.WriteAsync(Request("procedure.system.budgets", "system", "inspect",
            "## Inspect\n" + string.Concat(Enumerable.Repeat("Read the exact target before acting. ", 200)),
            matches: "inspect"));
        var service = Service(store, new Changes(App, HashA), "system");
        const string input = "{\"target\":\"preserve the entire supplied value\"}";
        var small = await service.DiscoverAsync(Host(), "inspect", input, maximumCharacters: 4000);
        var large = await service.DiscoverAsync(Host(), "inspect", input, maximumCharacters: 24_000);
        Assert.Equal(InteractionInvocationResultTag.Completed, small.Tag);
        Assert.Equal(InteractionInvocationResultTag.Completed, large.Tag);
        var smallPacket = JsonNode.Parse(small.DataJson!)!;
        var largePacket = JsonNode.Parse(large.DataJson!)!;
        Assert.Equal(smallPacket["resolutionFingerprint"]!.GetValue<string>(),
            largePacket["resolutionFingerprint"]!.GetValue<string>());
        Assert.NotEqual(small.CompletionEvidenceReference, large.CompletionEvidenceReference);
        Assert.True(small.DataJson!.Length < large.DataJson!.Length);
        foreach (var result in new[] { small, large })
        {
            var packet = JsonNode.Parse(result.DataJson!)!;
            Assert.Equal(input, InteractionCanonicalJson.CanonicalizeObject(packet["knownInputs"]!.ToJsonString()));
            var resultHash = packet["resultFingerprint"]!.GetValue<string>();
            Assert.Equal("manual.context." + resultHash.ToLowerInvariant(), result.CompletionEvidenceReference);
            packet["resultFingerprint"] = new string('0', 64);
            Assert.Equal(resultHash, Hash(InteractionCanonicalJson.CanonicalizeObject(packet.ToJsonString())));
        }
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
            new InteractionFeatureRetriever(new Snapshots(snapshot)), new FixtureGrants(), new FixtureTargets(), new Changes(App, HashA), []);
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

    [Fact]
    public async Task Denied_content_and_whole_catalog_changes_do_not_change_visible_context_or_hashes()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);
        var allowed = AppRecord("sample-app.query.allowed", "Visible instructions", "inspect");
        var grants = new FixtureGrants { AllowedIds = [allowed.QualifiedId] };
        async Task<InteractionInvocationResult> Discover(string generation, string secret)
        {
            var hidden = AppRecord("sample-app.query.secret", secret, "inspect");
            var service = new InteractionManualContextService(store,
                new InteractionFeatureRetriever(new Snapshots(AppSnapshot(generation, allowed, hidden))),
                grants, new FixtureTargets(), new Changes(App, Hash(generation)), []);
            return await service.DiscoverAsync(Host(revision: new(App, 1, Hash(generation), [])), "inspect");
        }
        var before = await Discover("generation.one", "Secret before");
        var after = await Discover("generation.two", "Secret after");
        Assert.Equal(InteractionInvocationResultTag.Completed, before.Tag);
        Assert.Equal(InteractionInvocationResultTag.Completed, after.Tag);
        Assert.Equal(before.DataJson, after.DataJson);
        Assert.Equal(before.CompletionEvidenceReference, after.CompletionEvidenceReference);
        Assert.DoesNotContain("Secret", after.DataJson!);
        Assert.DoesNotContain(Hash("generation.two"), after.DataJson!);
        Assert.Equal("unresolved", JsonNode.Parse(after.DataJson!)!["resolution"]!.GetValue<string>());
        Assert.Single(JsonNode.Parse(after.DataJson!)!["candidates"]!.AsArray());
    }

    [Fact]
    public async Task Permissioned_manual_context_uses_semantic_ranking_only_for_authorized_current_targets()
    {
        await using var db = _fixture.CreateContext();
        var allowed = AppRecord("sample-app.query.allowed", "authorized-vector", "unrelated phrase");
        var denied = AppRecord("sample-app.query.denied", "denied-vector", "another phrase");
        var snapshot = AppSnapshot("semantic-generation", denied, allowed);
        var grants = new FixtureGrants { AllowedIds = [allowed.QualifiedId] };
        var vectors = new ManualVectors(denied.QualifiedId, allowed.QualifiedId);
        var retriever = new InteractionFeatureRetriever(new Snapshots(snapshot), new ManualEmbeddings(), vectors);
        var service = new InteractionManualContextService(new ProcedureStore(db), retriever, grants,
            new FixtureTargets(), new Changes(App, HashA), []);

        var result = await service.DiscoverAsync(Host(), "semantic request");
        var packet = JsonNode.Parse(result.DataJson!)!;

        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Equal("Hybrid", packet["retrievalMode"]!.GetValue<string>());
        Assert.Equal(allowed.QualifiedId,
            Assert.Single(packet["candidates"]!.AsArray())!["reference"]!["qualifiedId"]!.GetValue<string>());
        Assert.DoesNotContain(denied.QualifiedId, result.DataJson!, StringComparison.Ordinal);
        Assert.Equal([allowed.QualifiedId], vectors.AuthorizedIds);
    }

    [Fact]
    public async Task Discovery_requires_application_read_and_consumes_one_host_operation()
    {
        await using var db = _fixture.CreateContext();
        var record = AppRecord("sample-app.query.allowed", "Visible instructions", "inspect");
        var grants = new FixtureGrants();
        var service = new InteractionManualContextService(new ProcedureStore(db),
            new InteractionFeatureRetriever(new Snapshots(AppSnapshot("one", record))), grants,
            new FixtureTargets(), new Changes(App, HashA), []);
        var budget = new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1));
        var result = await service.DiscoverAsync(Host(budget: budget), "inspect");
        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Single(JsonNode.Parse(result.DataJson!)!["candidates"]!.AsArray());
        Assert.Equal(0, budget.RemainingOperations);
        Assert.Null(result.Receipt);
        Assert.True(grants.Requirements.Count >= 2);
        Assert.All(grants.Requirements, requirement =>
        {
            Assert.Equal(StandingGrantCapability.Read, requirement.Capability);
            Assert.Equal(StandingGrantScope.Application, requirement.Scope);
            Assert.Empty(requirement.EffectKinds);
            Assert.Null(requirement.Task);
        });
        grants.Scope = StandingGrantScope.StateSpace;
        var wrongScope = await service.DiscoverAsync(Host(), "inspect");
        Assert.Empty(JsonNode.Parse(wrongScope.DataJson!)!["candidates"]!.AsArray());
    }

    [Fact]
    public async Task Missing_ownership_and_revocation_never_emit_definition_data()
    {
        await using var db = _fixture.CreateContext();
        var record = AppRecord("sample-app.query.allowed", "Visible instructions", "inspect");
        var grants = new FixtureGrants();
        var targets = new FixtureTargets { Unavailable = true };
        var service = new InteractionManualContextService(new ProcedureStore(db),
            new InteractionFeatureRetriever(new Snapshots(AppSnapshot("one", record))), grants,
            targets, new Changes(App, HashA), []);
        var unavailable = await service.DiscoverAsync(Host(), "inspect");
        Assert.Empty(JsonNode.Parse(unavailable.DataJson!)!["candidates"]!.AsArray());
        Assert.Empty(grants.Requirements);
        targets.Unavailable = false;
        grants.RevokeAfterCalls = 1;
        var revoked = await service.DiscoverAsync(Host(), "inspect");
        Assert.Equal("MANUAL_AUTHORITY_CHANGED", revoked.Code);
        Assert.Null(revoked.DataJson);
    }

    [Fact]
    public async Task Recipe_with_one_denied_step_is_hidden_before_selection_and_digesting()
    {
        await using var db = _fixture.CreateContext();
        var allowed = AppRecord("sample-app.query.inspect", "Visible instructions", "inspect");
        var denied = AppRecord("sample-app.query.secret", "Secret instructions", "inspect");
        var allowedRecipe = SeedRecipe(allowed);
        db.InteractionRecipes.Add(allowedRecipe);
        await db.SaveChangesAsync();
        var grants = new FixtureGrants { AllowedIds = [allowed.QualifiedId] };
        var service = new InteractionManualContextService(new ProcedureStore(db),
            new InteractionFeatureRetriever(new Snapshots(AppSnapshot("one", allowed, denied))), grants,
            new FixtureTargets(), new Changes(App, HashA), [], new InteractionRecipeStore(db));
        var before = await service.DiscoverAsync(Host(), "inspect");
        var deniedRecipe = SeedRecipe(allowed, denied);
        db.InteractionRecipes.Add(deniedRecipe);
        await db.SaveChangesAsync();
        var after = await service.DiscoverAsync(Host(), "inspect");
        Assert.Equal(InteractionInvocationResultTag.Completed, before.Tag);
        Assert.Equal(InteractionInvocationResultTag.Completed, after.Tag);
        Assert.Equal(before.DataJson, after.DataJson);
        Assert.Single(JsonNode.Parse(after.DataJson!)!["reusableTasks"]!.AsArray());
        Assert.DoesNotContain(deniedRecipe.Id, after.DataJson!);
        Assert.DoesNotContain(deniedRecipe.TemplateFingerprint, after.DataJson!);
        Assert.Contains(grants.Requirements, requirement => requirement.Definitions.Any(target => target.DefinitionId == denied.QualifiedId));

        // Seed retained verified rows directly: this checks discovery, not publication or provenance acceptance.
        static InteractionRecipe SeedRecipe(params CatalogRecordDefinition[] records)
        {
            var template = InteractionRecipeTemplate.FromProposal(App, new InteractionPlannerProposalCommand(
                records.Select((record, index) => new InteractionPlannerDraftStep("step." + index,
                    InteractionPlanStepKind.Query, record.QualifiedId, record.Version, record.ContentFingerprint,
                    [], new Dictionary<string, string>(), "{}")).ToArray()));
            var id = InteractionRecipeIds.Create(App, template.Fingerprint);
            var row = new InteractionRecipe { Id = id, ApplicationId = App.Value, TemplateFingerprint = template.Fingerprint,
                TemplateJson = template.CanonicalJson, CreatedAtUtc = DateTime.UnixEpoch };
            row.Revisions.Add(new InteractionRecipeRevision { RecipeId = id, Version = 1, Status = "verified",
                ApplicationRevision = 1, ApplicationFingerprint = Hash("application"), EffectiveSetFingerprint = HashA,
                RequestToken = "fixture." + template.Fingerprint, RequestFingerprint = template.Fingerprint,
                CreatedAtUtc = DateTime.UnixEpoch });
            return row;
        }
    }

    private static InteractionInvocationHost Host(string command = "command.1", InteractionInvocationBudget? budget = null, bool consume = false, string grant = "grant.1", ApplicationRevision? revision = null)
    {
        budget ??= new(2, DateTime.UtcNow.AddMinutes(1));
        if (consume) Assert.True(budget.TryConsumeOperation());
        return new(TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"), revision ?? Revision(),
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
    private static CatalogRecordDefinition AppRecord(string id, string description, string phrase)
    {
        var content = JsonSerializer.Serialize(new { description, inputSchema = new { type = "object", required = new[] { "target" } } });
        return new("sample", "query", id, "Inspect", description, [], [phrase], "", "active", 1, content, Hash(content), "fixture", "queries/inspect.json");
    }
    private static ActiveCatalogFeatureSnapshot AppSnapshot(string generation, params CatalogRecordDefinition[] records) =>
        new(CatalogNavigationManifest.Create(App, Hash(generation), "fixture", [new("sample", "Sample", "Fixture")],
            [new("sample", "", "Sample", "Fixture", CatalogDescriptionStatus.Authored)], records),
            records.Select(record => new ActiveCatalogFeatureDocument(record, SourceTrust.Trusted)).ToArray()) { EffectiveSetFingerprint = HashA };

    private sealed class FixtureGrants : IStandingGrantPolicy
    {
        internal HashSet<string>? AllowedIds;
        internal bool Revoked;
        internal int RevokeAfterCalls = int.MaxValue;
        internal StandingGrantScope Scope = StandingGrantScope.Application;
        internal readonly List<StandingGrantRequirement> Requirements = [];
        public Task<StandingGrantDecision> EvaluateAsync(InteractionInvocationHost host, StandingGrantRequirement requirement,
            CancellationToken cancellationToken = default)
        {
            Requirements.Add(requirement);
            Revoked |= Requirements.Count > RevokeAfterCalls;
            var allowed = host.GrantReference == "grant.1" && !Revoked
                && (AllowedIds is null || requirement.Definitions.All(target => AllowedIds.Contains(target.DefinitionId)));
            var grant = new StandingGrantRevision(host.GrantReference, "fixture.grant", 1, Hash("grant"), host.Principal.PrincipalId,
                App, Scope, Scope == StandingGrantScope.Application ? null : host.StateSpaceId, [StandingGrantCapability.Read],
                new(StandingGrantDefinitionMode.ExactIds, requirement.Definitions.Select(value => value.DefinitionId).ToArray(), []),
                [], 16, DateTime.UtcNow.AddHours(1), Revoked, "fixture.issuance");
            return Task.FromResult(new StandingGrantDecision(allowed, allowed ? "FIXTURE_ALLOWED" : "FIXTURE_DENIED", grant,
                new(host.Principal.PrincipalId, "fixture", "read", "application", host.CommandId, allowed, "fixture")));
        }
    }
    private sealed class FixtureTargets : IStandingGrantTargetResolver
    {
        internal bool Unavailable;
        public Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host, StandingGrantDefinitionReference selection,
            CancellationToken cancellationToken = default) => Task.FromResult(Unavailable
            ? new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Unavailable, "FIXTURE_UNAVAILABLE", null)
            : new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Available, "FIXTURE_RESOLVED",
                new(selection.DefinitionId, selection.Kind, App, "sample-app.query", "fixture.owner", selection.Revision, selection.ContentFingerprint)));
        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host, ApplicationCandidateSnapshot candidate,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ManualEmbeddings : ITextEmbeddingProvider
    {
        private static readonly EmbeddingProviderIdentity Identity = new("fixture", "manual", "1", 2);
        public Task<EmbeddingProviderStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmbeddingProviderStatus(true, Identity));
        public Task<EmbeddingBatchResult> EmbedAsync(IReadOnlyList<string> inputs,
            CancellationToken cancellationToken = default) => Task.FromResult(new EmbeddingBatchResult(Identity,
                inputs.Select(_ => new[] { 0f, 1f }).ToArray()));
    }

    private sealed class ManualVectors(params string[] rankedIds) : IInteractionDerivedVectorIndex
    {
        internal IReadOnlyList<string> AuthorizedIds { get; private set; } = [];
        public Task ReplaceAsync(InteractionRetrievalGeneration generation,
            IReadOnlyList<InteractionVectorDocument> documents, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The permissioned request must not rebuild the full catalog.");
        public Task<IReadOnlyList<InteractionVectorCandidate>> SearchAsync(InteractionRetrievalGeneration generation,
            float[] query, int limit, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The unrestricted vector route must not be used.");
        public Task<IReadOnlyList<InteractionVectorCandidate>> SearchAuthorizedAsync(InteractionRetrievalGeneration generation,
            float[] query, IReadOnlyCollection<string> authorizedQualifiedIds, int limit,
            CancellationToken cancellationToken = default)
        {
            AuthorizedIds = authorizedQualifiedIds.Order(StringComparer.Ordinal).ToArray();
            var allowed = authorizedQualifiedIds.ToHashSet(StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyList<InteractionVectorCandidate>>(rankedIds
                .Where(allowed.Contains).Take(limit).Select((id, index) => new InteractionVectorCandidate(id, index)).ToArray());
        }
    }

    private sealed class SectionEmbeddings : ITextEmbeddingProvider
    {
        internal string Revision { get; set; } = "1";
        internal int DocumentBatches { get; private set; }
        internal List<string[]> Inputs { get; } = [];
        public Task<EmbeddingProviderStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmbeddingProviderStatus(true, Identity()));
        public Task<EmbeddingBatchResult> EmbedAsync(IReadOnlyList<string> inputs,
            CancellationToken cancellationToken = default)
        {
            Inputs.Add(inputs.ToArray());
            if (inputs.Count > 1) DocumentBatches++;
            return Task.FromResult(new EmbeddingBatchResult(Identity(), inputs.Select(value =>
                value.Contains("restore lost progress", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Resume persisted campaign", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Continue the saved session", StringComparison.OrdinalIgnoreCase)
                    ? new[] { 0f, 1f }
                    : new[] { 1f, 0f }).ToArray()));
        }
        private EmbeddingProviderIdentity Identity() => new("fixture", "manual-section", Revision, 2);
    }

    private sealed class UnavailableSectionEmbeddings : ITextEmbeddingProvider
    {
        public Task<EmbeddingProviderStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(EmbeddingProviderStatus.Unavailable("EMBEDDING_OFFLINE", "Fixture unavailable."));
        public Task<EmbeddingBatchResult> EmbedAsync(IReadOnlyList<string> inputs,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class Changes(ApplicationIdentifier app, string fingerprint) : IApplicationDefinitionChangeReader
    { public ApplicationDefinitionChange? CurrentChange(ApplicationIdentifier id) => id == app ? new(app, 1, fingerprint, "operation.1", DateTime.UnixEpoch, new([], []), new(Hash("dependencies"), "fixture", true), new("rebuildable", false, false)) : null; public ApplicationDefinitionChange? RevisionChange(ApplicationIdentifier id, int revision) => CurrentChange(id); public IReadOnlyList<ApplicationDefinitionChange> ChangesAfter(ApplicationIdentifier id, int after, int limit) => []; }
}
