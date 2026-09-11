using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class PartyKnowledgeProjectionTests
{
    private static readonly JintMechanicEngine Engine = new();
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private const string RevisionA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string RevisionB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string MechanicId = "dnd2024.mechanic.party-knowledge.project";

    [Fact]
    public void Registered_query_pins_the_canonical_mechanic_and_compiled_output_schema()
    {
        var mechanic = MechanicFile.Parse(File.ReadAllText(MechanicMarkdownPath()), MechanicId,
            File.ReadAllText(MechanicPath()));
        var mechanicHash = ApplicationCatalogRecordContent.Fingerprint(
            ApplicationCatalogRecordContent.MechanicJson(mechanic));
        var query = ApplicationQueryContract.Parse(File.ReadAllText(QueryPath()), ApplicationIdentifier.Parse("dnd2024"));
        var compilation = new BoundedJsonSchemaValidator().Compile(query.OutputSchemaJson);

        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics.Select(value => value.Code)));
        Assert.Equal(MechanicId, query.ProjectionQualifiedId);
        Assert.True(mechanicHash == query.ProjectionContentHash,
            $"Mechanic hash: {mechanicHash}; query pin: {query.ProjectionContentHash}");
        Assert.True(compilation.SchemaHash == query.OutputSchemaHash,
            $"Schema hash: {compilation.SchemaHash}; query pin: {query.OutputSchemaHash}");
    }

    [Fact]
    public async Task Player_projection_is_active_party_union_with_exact_admissions_and_familiar_redaction()
    {
        var projection = Projection(RevisionA,
        [
            Document("knowledge.mixed", "Mixed report", "location.market"),
            Document("knowledge.familiar", "Hidden familiar detail", "location.market"),
            Document("knowledge.world", "World baseline", "location.market"),
            Document("knowledge.faction", "Faction baseline", "location.market"),
            Document("knowledge.region", "Region baseline", "location.market"),
            Document("knowledge.archived", "Old report", "location.market", status: "archived"),
            Document("knowledge.future", "Future report", "location.market", validFrom: 101)
        ],
        [
            State("actor.one", "knowledge.mixed", "known"),
            State("actor.two", "knowledge.mixed", "doubted"),
            State("actor.two", "knowledge.familiar", "familiar"),
            State("actor.one", "knowledge.world", "unknown"),
            State("actor.withdrawn", "knowledge.familiar", "believed")
        ],
        [
            Baseline("world.one", "knowledge.world"),
            Baseline("faction.one", "knowledge.faction"),
            Baseline("region.one", "knowledge.region")
        ]);

        using var output = await Run(projection);

        Assert.True(output.RootElement.GetProperty("status").GetString() == "ready", output.RootElement.GetRawText());
        Assert.Equal("party", output.RootElement.GetProperty("audience").GetString());
        Assert.Equal("campaign.one", output.RootElement.GetProperty("campaignId").GetString());
        Assert.Equal("world.one", output.RootElement.GetProperty("worldId").GetString());
        Assert.Equal(5, output.RootElement.GetProperty("entries").GetArrayLength());

        var mixed = Entry(output, "knowledge.mixed");
        Assert.Equal("mixed", mixed.GetProperty("stance").GetString());
        Assert.Equal(2, mixed.GetProperty("admissions").GetArrayLength());

        var world = Entry(output, "knowledge.world");
        Assert.Equal("known", world.GetProperty("stance").GetString());
        var unknown = world.GetProperty("admissions").EnumerateArray()
            .Single(value => value.GetProperty("actorId").GetString() == "actor.one");
        Assert.Equal("unknown", unknown.GetProperty("stance").GetString());
        Assert.False(unknown.GetProperty("admits").GetBoolean());

        var faction = Entry(output, "knowledge.faction");
        Assert.Equal("faction.one", Assert.Single(faction.GetProperty("admissions").EnumerateArray())
            .GetProperty("scopeId").GetString());
        var region = Entry(output, "knowledge.region");
        Assert.Equal("actor.one", Assert.Single(region.GetProperty("admissions").EnumerateArray())
            .GetProperty("actorId").GetString());

        var familiar = output.RootElement.GetProperty("entries").EnumerateArray()
            .Single(value => value.TryGetProperty("recognitionKey", out _));
        Assert.False(familiar.TryGetProperty("knowledgeId", out _));
        Assert.False(familiar.TryGetProperty("documentRevision", out _));
        Assert.False(familiar.TryGetProperty("subject", out _));
        Assert.False(familiar.TryGetProperty("mediaOwnerId", out _));
        Assert.DoesNotContain("Hidden familiar detail", familiar.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(familiar.GetProperty("admissions").EnumerateArray(),
            value => value.GetProperty("actorId").GetString() == "actor.withdrawn");

        var query = ApplicationQueryContract.Parse(File.ReadAllText(QueryPath()), ApplicationIdentifier.Parse("dnd2024"));
        var compilation = new BoundedJsonSchemaValidator().Compile(query.OutputSchemaJson);
        Assert.True(compilation.IsAccepted,
            string.Join("; ", compilation.Diagnostics.Select(value => value.Code + ": " + value.Message)));
        Assert.Equal(query.OutputSchemaHash, compilation.SchemaHash);
        Assert.Equal(SchemaValueStatus.Valid,
            new BoundedJsonSchemaValidator().Validate(query.OutputSchemaJson, output.RootElement.GetRawText()).Status);
    }

    [Fact]
    public async Task Dm_projection_reads_current_documents_without_party_admissions()
    {
        var projection = Projection(RevisionA, [Document("knowledge.dm", "DM record", "subject.one")], [], [])
            with { Audience = MechanicAudienceContext.GameMaster };

        using var output = await Run(projection);

        Assert.True(output.RootElement.GetProperty("status").GetString() == "ready", output.RootElement.GetRawText());
        Assert.Equal("dm", output.RootElement.GetProperty("audience").GetString());
        var entry = Entry(output, "knowledge.dm");
        Assert.Equal("dm", entry.GetProperty("stance").GetString());
        Assert.Empty(entry.GetProperty("admissions").EnumerateArray());
    }

    [Fact]
    public async Task Caller_cannot_promote_a_player_projection_by_forging_input_audience()
    {
        var projection = Projection(RevisionA, [Document("knowledge.one", "Known", "subject.one")],
            [State("actor.one", "knowledge.one", "known")], []) with { Input = "{\"audience\":\"dm\"}" };

        using var output = await Run(projection);

        Assert.Equal("unavailable", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("party", output.RootElement.GetProperty("audience").GetString());
        Assert.Empty(output.RootElement.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task Incomplete_wrong_world_and_duplicate_graphs_fail_the_whole_projection()
    {
        var original = Projection(RevisionA, [Document("knowledge.one", "Known", "subject.one")],
            [State("actor.one", "knowledge.one", "known")], []);
        var graph = original.GraphSnapshots["partyKnowledge"];

        var incomplete = original with { GraphSnapshots = new Dictionary<string, MechanicGraphSnapshot>
        {
            ["partyKnowledge"] = graph with { Complete = false }
        }};
        Assert.Equal("unavailable", (await Run(incomplete)).RootElement.GetProperty("status").GetString());

        var wrongSteps = graph.Steps.ToDictionary();
        var worldStep = wrongSteps["knowledgeWorlds"];
        wrongSteps["knowledgeWorlds"] = worldStep with
        {
            Nodes = worldStep.Nodes.Append(Node("world.other", "Other world",
                ("game.core.world.root", new { status = "active", summary = "Other", visibility = "party" }))).ToArray(),
            Edges = worldStep.Edges.Append(Edge("knowledge.one", "world.other", "game.core.world.knowledge.in-world")).ToArray()
        };
        var wrong = original with { GraphSnapshots = new Dictionary<string, MechanicGraphSnapshot>
        {
            ["partyKnowledge"] = graph with { Steps = wrongSteps }
        }};
        Assert.Equal("unavailable", (await Run(wrong)).RootElement.GetProperty("status").GetString());

        var duplicateSteps = graph.Steps.ToDictionary();
        var knowledge = duplicateSteps["knowledge"];
        duplicateSteps["knowledge"] = knowledge with { Edges = knowledge.Edges.Append(knowledge.Edges[0]).ToArray() };
        var duplicate = original with { GraphSnapshots = new Dictionary<string, MechanicGraphSnapshot>
        {
            ["partyKnowledge"] = graph with { Steps = duplicateSteps }
        }};
        Assert.Equal("unavailable", (await Run(duplicate)).RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Source_revision_changes_every_disclosed_document_version()
    {
        var first = Projection(RevisionA, [Document("knowledge.one", "Known", "subject.one")],
            [State("actor.one", "knowledge.one", "known")], []);
        var second = first with { GraphSnapshots = new Dictionary<string, MechanicGraphSnapshot>
        {
            ["partyKnowledge"] = first.GraphSnapshots["partyKnowledge"] with { SourceRevisionFingerprint = RevisionB }
        }};

        using var left = await Run(first);
        using var right = await Run(second);

        Assert.True(left.RootElement.GetProperty("status").GetString() == "ready", left.RootElement.GetRawText());
        Assert.True(right.RootElement.GetProperty("status").GetString() == "ready", right.RootElement.GetRawText());
        Assert.Equal(RevisionA, Entry(left, "knowledge.one").GetProperty("documentRevision").GetString());
        Assert.Equal(RevisionB, Entry(right, "knowledge.one").GetProperty("documentRevision").GetString());
        Assert.NotEqual(left.RootElement.GetProperty("sourceRevision").GetString(),
            right.RootElement.GetProperty("sourceRevision").GetString());
    }

    [Fact]
    public async Task Source_fenced_pages_preserve_the_complete_party_union()
    {
        var documents = Enumerable.Range(0, 81)
            .Select(index => Document($"knowledge.{index:D3}", $"Record {index}", "subject.one")).ToArray();
        var firstDocuments = documents.Take(40).ToArray();
        var first = Projection(RevisionA, firstDocuments, [], firstDocuments.Select(value => Baseline("world.one", value.Id)).ToArray(),
            sourceTotal: 81, nextCursor: "40");

        using var firstOutput = await Run(first);
        Assert.Equal("ready", firstOutput.RootElement.GetProperty("status").GetString());
        Assert.Equal(40, firstOutput.RootElement.GetProperty("entries").GetArrayLength());
        Assert.Equal("40", firstOutput.RootElement.GetProperty("nextCursor").GetString());
        Assert.Equal("partial", firstOutput.RootElement.GetProperty("coverage").GetString());
        Assert.Equal("complete", firstOutput.RootElement.GetProperty("fieldCoverage").GetString());

        var secondDocuments = documents.Skip(40).Take(40).ToArray();
        var second = Projection(RevisionA, secondDocuments, [], secondDocuments.Select(value => Baseline("world.one", value.Id)).ToArray(),
            pageOffset: 40, sourceTotal: 81, nextCursor: "80") with { Input = JsonSerializer.Serialize(new
        {
            cursor = "40", expectedSourceRevision = RevisionA, expectedGraphSourceRevision = RevisionA
        }) };
        using var secondOutput = await Run(second);
        Assert.Equal(40, secondOutput.RootElement.GetProperty("entries").GetArrayLength());
        Assert.Equal("80", secondOutput.RootElement.GetProperty("nextCursor").GetString());

        var finalDocuments = documents.Skip(80).ToArray();
        var final = Projection(RevisionA, finalDocuments, [], finalDocuments.Select(value => Baseline("world.one", value.Id)).ToArray(),
            pageOffset: 80, sourceTotal: 81) with { Input = JsonSerializer.Serialize(new
        {
            cursor = "80", expectedSourceRevision = RevisionA, expectedGraphSourceRevision = RevisionA
        }) };
        using var finalOutput = await Run(final);
        Assert.Equal(1, finalOutput.RootElement.GetProperty("entries").GetArrayLength());
        Assert.False(finalOutput.RootElement.TryGetProperty("nextCursor", out _));
        Assert.Equal("complete", finalOutput.RootElement.GetProperty("coverage").GetString());

        var changed = second with { Input = JsonSerializer.Serialize(new
        {
            cursor = "40", expectedSourceRevision = RevisionA, expectedGraphSourceRevision = RevisionB
        }) };
        using var changedOutput = await Run(changed);
        Assert.Equal("unavailable", changedOutput.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Dm_first_page_handles_the_current_world_source_ceiling_without_disclosing_a_truncated_union()
    {
        var documents = Enumerable.Range(0, 40)
            .Select(index => Document($"knowledge.{index:D4}", $"Record {index:D4}", "subject.one")).ToArray();
        var projection = Projection(RevisionA, documents, [], [], sourceTotal: 1_237, nextCursor: "40") with
        { Audience = MechanicAudienceContext.GameMaster };

        var constant = await Engine.RunAsync("return { data: { status: 'probe' } };", projection, ExecutionLimits.ReadModel);
        Assert.True(constant.Ok, constant.Error);

        using var output = await Run(projection);

        Assert.Equal("ready", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("dm", output.RootElement.GetProperty("audience").GetString());
        Assert.Equal("40", output.RootElement.GetProperty("nextCursor").GetString());
        Assert.Equal("knowledge.0000", output.RootElement.GetProperty("entries")[0].GetProperty("knowledgeId").GetString());
        Assert.Equal("knowledge.0039", output.RootElement.GetProperty("entries")[39].GetProperty("knowledgeId").GetString());
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(output.RootElement.GetRawText()) <= 60_000,
            output.RootElement.GetRawText().Length.ToString());
    }

    [Fact]
    public async Task Every_source_page_is_materialized_by_the_real_mechanic_without_leaking_unadmitted_player_records()
    {
        var dmDocuments = Enumerable.Range(0, 1_237)
            .Select(index => Document($"knowledge.{index:D4}", $"DM record {index:D4}", "subject.one")).ToArray();
        var playerSecrets = Enumerable.Range(0, 1_237)
            .Select(index => SecretDocument($"secret.{index:D4}", $"DM-only secret {index:D4}", "subject.one")).ToArray();
        var dmIds = new List<string>();
        var playerOutput = new List<string>();

        for (var offset = 0; offset < dmDocuments.Length; offset += 40)
        {
            var next = offset + 40 < dmDocuments.Length ? (offset + 40).ToString() : null;
            var continuation = offset == 0 ? "{}" : JsonSerializer.Serialize(new
            {
                cursor = offset.ToString(), expectedSourceRevision = RevisionA, expectedGraphSourceRevision = RevisionA
            });
            var dm = Projection(RevisionA, dmDocuments.Skip(offset).Take(40).ToArray(), [], [], offset,
                dmDocuments.Length, next) with { Audience = MechanicAudienceContext.GameMaster, Input = continuation };
            using var dmOutput = await Run(dm);
            Assert.False(dmOutput.RootElement.TryGetProperty("pageEntryCount", out _));
            Assert.False(dmOutput.RootElement.TryGetProperty("totalCount", out _));
            dmIds.AddRange(dmOutput.RootElement.GetProperty("entries").EnumerateArray()
                .Select(value => value.GetProperty("knowledgeId").GetString()!));

            var player = Projection(RevisionA, playerSecrets.Skip(offset).Take(40).ToArray(), [], [], offset,
                playerSecrets.Length, next) with { Input = continuation };
            using var playerPage = await Run(player);
            Assert.Equal("empty", playerPage.RootElement.GetProperty("status").GetString());
            Assert.Empty(playerPage.RootElement.GetProperty("entries").EnumerateArray());
            Assert.False(playerPage.RootElement.TryGetProperty("pageEntryCount", out _));
            Assert.DoesNotContain("secret.", playerPage.RootElement.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("DM-only secret", playerPage.RootElement.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("game.core.world.secret", playerPage.RootElement.GetRawText(), StringComparison.Ordinal);
            playerOutput.Add(playerPage.RootElement.GetRawText());
        }

        Assert.Equal(1_237, dmIds.Count);
        Assert.Equal(dmIds.Count, dmIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("knowledge.0000", dmIds[0]);
        Assert.Equal("knowledge.1236", dmIds[^1]);
        Assert.DoesNotContain("1237", string.Concat(playerOutput), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Additive_fields_survive_while_one_malformed_document_is_omitted_with_partial_coverage()
    {
        var projection = Projection(RevisionA,
            [Document("knowledge.good", "Readable", "subject.one"), Document("knowledge.bad", "Malformed", "subject.one")],
            [State("actor.one", "knowledge.good", "known"), State("actor.one", "knowledge.bad", "known")], []);
        var graph = projection.GraphSnapshots["partyKnowledge"];
        var steps = graph.Steps.ToDictionary();
        var knowledge = steps["knowledge"];
        var changed = knowledge.Nodes.Select(node =>
        {
            var components = node.Components.ToDictionary();
            if (node.Id == "knowledge.good")
            {
                components["game.core.world.fact"] = JsonSerializer.SerializeToElement(new
                    { status = "active", summary = "Readable", provenance = "fixture", visibility = "party", additive = "preserved" });
                components["game.core.world.knowledge.classification"] = JsonSerializer.SerializeToElement(new
                    { subjectKind = "state", sensitivity = "open", additive = true });
            }
            if (node.Id == "knowledge.bad")
                components["game.core.world.knowledge.classification"] = JsonSerializer.SerializeToElement(new { sensitivity = "open" });
            return node with { Components = components };
        }).ToArray();
        steps["knowledge"] = knowledge with { Nodes = changed };
        projection = projection with { GraphSnapshots = new Dictionary<string, MechanicGraphSnapshot>
        {
            ["partyKnowledge"] = graph with { Steps = steps }
        }};

        using var output = await Run(projection);

        Assert.Equal("ready", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("partial", output.RootElement.GetProperty("coverage").GetString());
        Assert.Single(output.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Equal("knowledge.good", output.RootElement.GetProperty("entries")[0].GetProperty("knowledgeId").GetString());
    }

    [Fact]
    public async Task Ambiguous_subject_is_field_local_and_never_exposes_a_chosen_or_hidden_subject()
    {
        var projection = Projection(RevisionA,
        [
            Document("knowledge.good", "Readable", "subject.one"),
            Document("knowledge.multi", "A fact with two independently valid subjects", "subject.one"),
            Document("knowledge.neighbor", "Also readable", "subject.one")
        ],
        [
            State("actor.one", "knowledge.good", "known"),
            State("actor.one", "knowledge.multi", "known"),
            State("actor.one", "knowledge.neighbor", "known")
        ], []);
        var graph = projection.GraphSnapshots["partyKnowledge"];
        var steps = graph.Steps.ToDictionary();
        var subjects = steps["subjects"];
        steps["subjects"] = subjects with
        {
            Nodes = subjects.Nodes.Append(Node("subject.hidden", "Never disclosed as the chosen subject")).ToArray(),
            Edges = subjects.Edges.Append(Edge("knowledge.multi", "subject.hidden", "game.core.world.knowledge.about")).ToArray()
        };
        projection = projection with { GraphSnapshots = new Dictionary<string, MechanicGraphSnapshot>
        {
            ["partyKnowledge"] = graph with { Steps = steps }
        }};

        using var player = await Run(projection);
        Assert.Equal("ready", player.RootElement.GetProperty("status").GetString());
        Assert.Equal("partial", player.RootElement.GetProperty("fieldCoverage").GetString());
        Assert.Equal(3, player.RootElement.GetProperty("entries").GetArrayLength());
        var multi = Entry(player, "knowledge.multi");
        Assert.False(multi.TryGetProperty("subject", out _));
        Assert.DoesNotContain("subject.hidden", player.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Never disclosed", player.RootElement.GetRawText(), StringComparison.Ordinal);
        var query = ApplicationQueryContract.Parse(File.ReadAllText(QueryPath()), ApplicationIdentifier.Parse("dnd2024"));
        Assert.Equal(SchemaValueStatus.Valid,
            new BoundedJsonSchemaValidator().Validate(query.OutputSchemaJson, player.RootElement.GetRawText()).Status);

        using var dm = await Run(projection with { Audience = MechanicAudienceContext.GameMaster });
        Assert.Equal(3, dm.RootElement.GetProperty("entries").GetArrayLength());
        Assert.False(Entry(dm, "knowledge.multi").TryGetProperty("subject", out _));
    }

    [Fact]
    public async Task Byte_adaptive_source_prefix_pages_preserve_all_documents_without_skips_or_duplicates()
    {
        var documents = Enumerable.Range(0, 40)
            .Select(index => Document($"knowledge.{index:D2}", new string('s', 1_000), "location.market"))
            .ToArray();
        var seen = new List<string>();
        var offset = 0;
        for (var page = 0; page < 40; page++)
        {
            var source = documents.Skip(offset).Take(40).ToArray();
            var hostNext = offset + source.Length < documents.Length ? (offset + source.Length).ToString() : null;
            var projection = Projection(RevisionA, source, [], [], offset, documents.Length, hostNext) with
            {
                Audience = MechanicAudienceContext.GameMaster,
                Input = offset == 0 ? "{}" : JsonSerializer.Serialize(new
                {
                    cursor = offset.ToString(), expectedSourceRevision = RevisionA, expectedGraphSourceRevision = RevisionA
                })
            };
            using var output = await Run(projection);
            Assert.True(Encoding.UTF8.GetByteCount(output.RootElement.GetRawText()) <= 60_000,
                output.RootElement.GetRawText());
            seen.AddRange(output.RootElement.GetProperty("entries").EnumerateArray()
                .Select(value => value.GetProperty("knowledgeId").GetString()!));
            if (!output.RootElement.TryGetProperty("nextCursor", out var next)) break;
            var nextOffset = int.Parse(next.GetString()!);
            Assert.InRange(nextOffset, offset + 1, Math.Min(offset + 40, documents.Length));
            offset = nextOffset;
            Assert.True(page < 39, "The byte-adaptive page cursor did not complete within its bounded source.");
        }
        Assert.Equal(documents.Select(value => value.Id).Order(StringComparer.Ordinal), seen);
        Assert.Equal(seen.Count, seen.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Familiar_recognition_uses_the_absolute_raw_source_ordinal_across_pages()
    {
        var projection = Projection(RevisionA, [Document("knowledge.familiar", "Familiar", "location.market")],
            [State("actor.one", "knowledge.familiar", "familiar")], [], pageOffset: 40, sourceTotal: 41) with
        {
            Input = JsonSerializer.Serialize(new
            {
                cursor = "40", expectedSourceRevision = RevisionA, expectedGraphSourceRevision = RevisionA
            })
        };

        using var output = await Run(projection);
        var familiar = Assert.Single(output.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Equal(RevisionA + ".41", familiar.GetProperty("recognitionKey").GetString());
        Assert.DoesNotContain("knowledge.familiar", output.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Long_display_is_bounded_and_reports_partial_coverage()
    {
        var projection = Projection(RevisionA, [Document("knowledge.long", new string('s', 1000), "subject.one")],
            [State("actor.one", "knowledge.long", "known")], []);
        var graph = projection.GraphSnapshots["partyKnowledge"];
        var steps = graph.Steps.ToDictionary();
        steps["knowledge"] = steps["knowledge"] with
        {
            Nodes = steps["knowledge"].Nodes.Select(node => node.Id == "knowledge.long"
                ? node with { Name = new string('n', 400) } : node).ToArray()
        };
        steps["subjects"] = steps["subjects"] with
        {
            Nodes = steps["subjects"].Nodes.Select(node => node.Id == "subject.one"
                ? node with { Name = new string('q', 400) } : node).ToArray()
        };
        projection = projection with { GraphSnapshots = new Dictionary<string, MechanicGraphSnapshot>
        {
            ["partyKnowledge"] = graph with { Steps = steps }
        }};

        using var output = await Run(projection);

        Assert.Equal("partial", output.RootElement.GetProperty("coverage").GetString());
        Assert.Equal(1500, Entry(output, "knowledge.long").GetProperty("text").GetString()!.Length);
    }

    private static MechanicProjection Projection(string revision, IReadOnlyList<Doc> documents,
        IReadOnlyList<MechanicGraphEdge> explicitStates, IReadOnlyList<MechanicGraphEdge> baselines,
        int pageOffset = 0, int? sourceTotal = null, string? nextCursor = null)
    {
        var campaign = Node("campaign.one", "Campaign", ("game.core.campaign.root", new
        {
            status = "active", title = "Campaign", premise = "Premise", partyGoals = new[] { "Goal" },
            toneAndBoundaries = new[] { "Boundary" }, rulesetScope = "dnd2024", creationMethod = "manual",
            reviewFingerprint = new string('a', 64)
        }));
        var world = Node("world.one", "World",
            ("game.core.world.root", new { status = "active", summary = "World", visibility = "party" }),
            ("game.core.world.clock", new { calendarId = "calendar", currentMinute = 100, revision = 1 }));
        var participations = new[]
        {
            Node("participation.one", "One participates", ("game.core.campaign.character-participation", new { status = "active" })),
            Node("participation.two", "Two participates", ("game.core.campaign.character-participation", new { status = "active" })),
            Node("participation.withdrawn", "Withdrawn", ("game.core.campaign.character-participation", new { status = "withdrawn" }))
        };
        var actors = new[] { Node("actor.one", "Actor One"), Node("actor.two", "Actor Two"), Node("actor.withdrawn", "Withdrawn Actor") };
        var location = Node("location.market", "Market", ("game.core.world.location", new
            { kind = "settlement", status = "active", summary = "Market", visibility = "party" }));
        var subject = Node("subject.one", "Subject");
        var region = Node("region.one", "Region", ("game.core.world.location", new
            { kind = "region", status = "active", summary = "Region", visibility = "party" }));
        var faction = Node("faction.one", "Faction", ("game.core.world.faction", new
        {
            status = "active", summary = "Faction", visibility = "party", goals = new[] { "Goal" },
            methods = new[] { "Method" }, assets = Array.Empty<string>(), agenda = new { state = "ready", summary = "Agenda" }
        }));
        var knowledgeNodes = documents.Select(value => KnowledgeNode(value)).ToArray();
        var subjectNodes = documents.Select(value => value.SubjectId == location.Id ? location : subject)
            .DistinctBy(value => value.Id).ToArray();
        var knowledgeEdges = documents.Select(value => Edge(value.Id, world.Id, "game.core.world.knowledge.in-world")).ToArray();
        var subjectEdges = documents.Select(value => Edge(value.Id, value.SubjectId, "game.core.world.knowledge.about")).ToArray();
        var scopeIds = baselines.Select(value => value.FromEntityId).Distinct(StringComparer.Ordinal).ToArray();
        var scopeNodes = scopeIds.Select(id => id switch
        {
            "world.one" => world,
            "faction.one" => faction,
            "region.one" => region,
            _ => Node(id, id)
        }).ToArray();
        var steps = new Dictionary<string, MechanicGraphStepSnapshot>(StringComparer.Ordinal)
        {
            ["world"] = Step([world], [Edge(campaign.Id, world.Id, "game.core.campaign.in-world")]),
            ["participations"] = Step(participations, participations.Select(value =>
                Edge(campaign.Id, value.Id, "game.core.campaign.has-character-participation")).ToArray()),
            ["actors"] = Step(actors,
            [
                Edge("participation.one", "actor.one", "game.core.campaign.character-participation.for-actor"),
                Edge("participation.two", "actor.two", "game.core.campaign.character-participation.for-actor"),
                Edge("participation.withdrawn", "actor.withdrawn", "game.core.campaign.character-participation.for-actor")
            ]),
            ["actorAncestors"] = Step([region, world], []),
            ["knowledge"] = Step(knowledgeNodes, knowledgeEdges),
            ["knowledgeWorlds"] = Step([world], knowledgeEdges),
            ["subjects"] = Step(subjectNodes, subjectEdges),
            ["explicitStates"] = Step(knowledgeNodes.Where(node => explicitStates.Any(edge =>
                edge.ToEntityId == node.Id)).ToArray(), explicitStates),
            ["baselineScopes"] = Step(scopeNodes, baselines),
            ["scopeLinks"] = scopeIds.Contains(faction.Id, StringComparer.Ordinal)
                ? Step([world, actors[1]],
                [
                    Edge(faction.Id, world.Id, "game.core.world.faction.in-world"),
                    Edge(faction.Id, actors[1].Id, "game.core.world.faction.member")
                ])
                : Step([], []),
            ["scopeAncestors"] = Step([world], [])
        };
        var graph = new MechanicGraphSnapshot(campaign, steps,
        [
            new("region.one", "actor.one", "party", 1),
            new("world.one", "region.one", "regions", 1)
        ], true, new(steps.Count, steps.Values.Sum(value => value.NodeCount) + 1,
            steps.Values.Sum(value => value.EdgeCount), 1, 16, 1), revision,
            new MechanicGraphPage(pageOffset, sourceTotal ?? documents.Count, 40, nextCursor));
        return new()
        {
            StateSpaceId = "space.one",
            Audience = MechanicAudienceContext.Player,
            Input = "{}",
            Roles = { ["campaign"] = new(campaign.Id, campaign.Name,
                new Dictionary<string, string> { ["game.core.campaign.root"] = Json(campaign.Components["game.core.campaign.root"]) }, null, "") },
            GraphSnapshots = { ["partyKnowledge"] = graph }
        };
    }

    private static MechanicGraphNode KnowledgeNode(Doc value)
    {
        var components = new List<(string, object)>
        {
            (value.Secret ? "game.core.world.secret" : "game.core.world.fact", new
            {
                status = value.Status, summary = value.Summary, provenance = "fixture",
                visibility = value.Secret ? "gm" : "party"
            }),
            ("game.core.world.knowledge.classification", new { subjectKind = "state", sensitivity = "open" })
        };
        if (value.ValidFrom is not null)
            components.Add(("game.core.world.knowledge.validity", new { validFromMinute = value.ValidFrom.Value }));
        return Node(value.Id, value.Id + " title", components.ToArray());
    }

    private static MechanicGraphNode Node(string id, string name, params (string Id, object Value)[] components)
    {
        var values = components.ToDictionary(value => value.Id,
            value => JsonSerializer.SerializeToElement(value.Value), StringComparer.Ordinal);
        var revisions = components.ToDictionary(value => value.Id,
            _ => new MechanicGraphComponentRevision(1, RevisionA, 1), StringComparer.Ordinal);
        return new(id, name, values, revisions, 1);
    }

    private static MechanicGraphEdge Edge(string from, string to, string kind, object? data = null) =>
        new(from, to, kind, JsonSerializer.SerializeToElement(data ?? new { }), 1);

    private static MechanicGraphEdge State(string actor, string knowledge, string state) =>
        Edge(actor, knowledge, "game.core.world.knowledge.state", new { state });

    private static MechanicGraphEdge Baseline(string scope, string knowledge) =>
        Edge(scope, knowledge, "game.core.world.knowledge.baseline", new { inheritance = "current-scope" });

    private static MechanicGraphStepSnapshot Step(IReadOnlyList<MechanicGraphNode> nodes,
        IReadOnlyList<MechanicGraphEdge> edges) => new(nodes, edges, true, null, 1, nodes.Count, edges.Count);

    private static async Task<JsonDocument> Run(MechanicProjection projection)
    {
        var result = await Engine.RunAsync(File.ReadAllText(MechanicPath()), projection, ExecutionLimits.ReadModel);
        Assert.True(result.Ok, result.Error);
        Assert.Empty(result.Output.Effects);
        Assert.Empty(result.Output.Events);
        Assert.Empty(result.Output.Notifications);
        return JsonDocument.Parse(result.Output.Data);
    }

    private static JsonElement Entry(JsonDocument output, string id) => output.RootElement
        .GetProperty("entries").EnumerateArray().Single(value =>
            value.TryGetProperty("knowledgeId", out var knowledgeId) && knowledgeId.GetString() == id);

    private static string Json(JsonElement value) => value.GetRawText();
    private static Doc Document(string id, string summary, string subjectId, string status = "active", long? validFrom = null) =>
        new(id, summary, subjectId, status, validFrom);
    private static Doc SecretDocument(string id, string summary, string subjectId) =>
        new(id, summary, subjectId, "active", null, true);
    private static string MechanicPath() => Path.Combine(Root(), "catalog", "applications", "dnd2024", "mechanics",
        "campaign", "dnd2024.mechanic.party-knowledge.project.js");
    private static string MechanicMarkdownPath() => Path.Combine(Root(), "catalog", "applications", "dnd2024", "mechanics",
        "campaign", "dnd2024.mechanic.party-knowledge.project.md");
    private static string QueryPath() => Path.Combine(Root(), "catalog", "applications", "dnd2024", "queries",
        "campaign", "dnd2024.query.party-knowledge.json");
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }

    private sealed record Doc(string Id, string Summary, string SubjectId, string Status, long? ValidFrom,
        bool Secret = false);
}
