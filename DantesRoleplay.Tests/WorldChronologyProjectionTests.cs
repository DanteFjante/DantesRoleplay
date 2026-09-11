using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class WorldChronologyProjectionTests
{
    private static readonly JintMechanicEngine Engine = new();
    private const string Revision = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string MechanicId = "dnd2024.mechanic.world-chronology.project";
    private const string QueryId = "dnd2024.query.world-chronology";

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
    public async Task Player_redacts_canonical_subjects_while_dm_orders_and_discloses_them()
    {
        var projection = Projection(
        [
            Record("chronology.party", "Party charter", 10, "party", subjectIds: ["location.market"]),
            Record("chronology.public", "Public dedication", 10, "public", subjectIds: ["location.market"]),
            Record("chronology.gm", "GM canary", 1, "gm", subjectIds: ["location.market"]),
            Record("chronology.archived", "Archived", 2, "public", status: "archived")
        ]);

        using var player = await Run(projection);
        Assert.Equal("ready", player.RootElement.GetProperty("status").GetString());
        Assert.Equal("player", player.RootElement.GetProperty("perspective").GetString());
        var playerEntries = player.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(["chronology-1", "chronology-2"], playerEntries.Select(value => value.GetProperty("id").GetString()));
        Assert.Equal(["Party charter", "Public dedication"], playerEntries.Select(value => value.GetProperty("title").GetString()));
        Assert.All(playerEntries, value =>
        {
            Assert.False(value.TryGetProperty("canonicalId", out _));
            Assert.False(value.TryGetProperty("subjects", out _));
        });
        Assert.DoesNotContain("GM canary", player.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("chronology.party", player.RootElement.GetRawText(), StringComparison.Ordinal);

        using var dm = await Run(projection with { Audience = MechanicAudienceContext.GameMaster });
        Assert.Equal("dm", dm.RootElement.GetProperty("perspective").GetString());
        var dmEntries = dm.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(["GM canary", "Party charter", "Public dedication"], dmEntries.Select(value => value.GetProperty("title").GetString()));
        var party = dmEntries.Single(value => value.GetProperty("title").GetString() == "Party charter");
        var subject = Assert.Single(party.GetProperty("subjects").EnumerateArray());
        Assert.Equal("location.market", subject.GetProperty("id").GetString());
        Assert.Equal("Market", subject.GetProperty("name").GetString());

        var query = ApplicationQueryContract.Parse(File.ReadAllText(QueryPath()), ApplicationIdentifier.Parse("dnd2024"));
        var schemas = new BoundedJsonSchemaValidator();
        var compilation = schemas.Compile(query.OutputSchemaJson);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics.Select(value => value.Code)));
        Assert.Equal(query.OutputSchemaHash, compilation.SchemaHash);
        Assert.Equal(SchemaValueStatus.Valid, schemas.Validate(query.OutputSchemaJson, player.RootElement.GetRawText()).Status);
    }

    [Fact]
    public async Task Caller_cannot_promote_player_projection_by_forging_input_perspective()
    {
        using var output = await Run(Projection([Record("chronology.one", "Known", 1, "party")]) with
        {
            Input = "{\"perspective\":\"dm\"}"
        });

        Assert.Equal("unavailable", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("player", output.RootElement.GetProperty("perspective").GetString());
        Assert.Empty(output.RootElement.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task Additive_component_fields_are_ignored_without_rejecting_the_record()
    {
        using var output = await Run(Projection([Record("chronology.additive", "Kept", 1, "public", additive: true)]));

        Assert.Equal("ready", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("complete", output.RootElement.GetProperty("coverage").GetString());
        Assert.Equal("Kept", Assert.Single(output.RootElement.GetProperty("entries").EnumerateArray())
            .GetProperty("title").GetString());
    }

    [Fact]
    public async Task Malformed_display_field_is_localized_without_losing_its_summary_or_neighbor()
    {
        using var output = await Run(Projection(
        [
            Record("chronology.good", "Readable", 1, "public"),
            Record("chronology.bad-title", "", 2, "party", summary: "The useful summary remains.")
        ]));

        Assert.Equal("ready", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("partial", output.RootElement.GetProperty("coverage").GetString());
        var entries = output.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal("Readable", entries[0].GetProperty("title").GetString());
        Assert.False(entries[1].TryGetProperty("title", out _));
        Assert.Equal("The useful summary remains.", entries[1].GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Missing_minute_is_localized_partial_coverage_and_sorts_after_dated_records()
    {
        using var output = await Run(Projection(
        [
            Record("chronology.dated", "Dated", 4, "public"),
            Record("chronology.undated", "Undated", null, "party")
        ]));

        Assert.Equal("ready", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("partial", output.RootElement.GetProperty("coverage").GetString());
        var entries = output.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(["Dated", "Undated"], entries.Select(value => value.GetProperty("title").GetString()));
        Assert.False(entries[1].TryGetProperty("occurredAtMinute", out _));
    }

    [Fact]
    public async Task Calendar_mismatch_and_foreign_subject_fail_closed()
    {
        using var wrongCalendar = await Run(Projection([Record("chronology.calendar", "Wrong calendar", 1, "public",
            calendarId: "other-calendar")]));
        Assert.Equal("unavailable", wrongCalendar.RootElement.GetProperty("status").GetString());

        var foreignSubject = Projection([Record("chronology.subject", "Foreign subject", 1, "party",
            subjectIds: ["faction.foreign"])], subjectWorld: "world.other");
        using var wrongSubject = await Run(foreignSubject);
        Assert.Equal("unavailable", wrongSubject.RootElement.GetProperty("status").GetString());
        Assert.Empty(wrongSubject.RootElement.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task Incomplete_or_double_world_graph_fails_closed()
    {
        var original = Projection([Record("chronology.one", "Scoped", 1, "public")]);
        var graph = original.GraphSnapshots["worldChronology"];
        using var incomplete = await Run(original with { GraphSnapshots = new Dictionary<string, MechanicGraphSnapshot>
        {
            ["worldChronology"] = graph with { Complete = false }
        }});
        Assert.Equal("unavailable", incomplete.RootElement.GetProperty("status").GetString());

        var steps = graph.Steps.ToDictionary();
        var otherWorld = Node("world.other", "Other", ("game.core.world.root", new { status = "active" }));
        var recordWorlds = steps["recordWorlds"];
        var records = steps["records"];
        var extra = Edge("chronology.one", "world.other", "game.core.world.chronology.in-world");
        steps["records"] = records with { Edges = records.Edges.Append(extra).ToArray() };
        steps["recordWorlds"] = recordWorlds with
        {
            Nodes = recordWorlds.Nodes.Append(otherWorld).ToArray(),
            Edges = recordWorlds.Edges.Append(extra).ToArray()
        };
        using var doubleWorld = await Run(original with { GraphSnapshots = new Dictionary<string, MechanicGraphSnapshot>
        {
            ["worldChronology"] = graph with { Steps = steps }
        }});
        Assert.Equal("unavailable", doubleWorld.RootElement.GetProperty("status").GetString());
        Assert.Empty(doubleWorld.RootElement.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task More_than_500_visible_records_fails_closed_instead_of_truncating()
    {
        var records = Enumerable.Range(0, 501).Select(index =>
            Record($"chronology.{index:D3}", $"Record {index}", index, "public")).ToArray();

        using var output = await Run(Projection(records));

        Assert.Equal("unavailable", output.RootElement.GetProperty("status").GetString());
        Assert.Empty(output.RootElement.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task Exactly_500_visible_records_are_source_fenced_across_bounded_pages()
    {
        var records = Enumerable.Range(0, 500).Select(index =>
            Record($"chronology.{index:D3}", $"Record {index}", index, "public")).ToArray();
        var projection = Projection(records) with { Audience = MechanicAudienceContext.GameMaster };
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        for (var page = 0; page < 13; page++)
        {
            var input = cursor is null ? "{}" : JsonSerializer.Serialize(new
            {
                cursor,
                expectedSourceRevision = new string('B', 64),
                expectedGraphSourceRevision = Revision
            });
            using var output = await Run(projection with { Input = input });

            Assert.Equal("ready", output.RootElement.GetProperty("status").GetString());
            Assert.Equal("complete", output.RootElement.GetProperty("fieldCoverage").GetString());
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(output.RootElement.GetRawText()) <= 60_000);
            var entries = output.RootElement.GetProperty("entries").EnumerateArray().ToArray();
            Assert.InRange(entries.Length, 1, 40);
            Assert.All(entries, entry => Assert.True(ids.Add(entry.GetProperty("id").GetString()!)));
            cursor = output.RootElement.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
            Assert.Equal(cursor is null ? "complete" : "partial",
                output.RootElement.GetProperty("coverage").GetString());
        }
        Assert.Null(cursor);
        Assert.Equal(500, ids.Count);
    }

    [Fact]
    public async Task Unicode_and_escaped_text_pages_stay_below_the_exact_utf8_limit()
    {
        var summary = string.Concat(Enumerable.Repeat("漢\"\\\n", 249)) + "漢\"\\x";
        var records = Enumerable.Range(0, 41).Select(index =>
            Record($"chronology.unicode.{index:D2}", $"記録 \"{index}\"", index, "public", summary: summary)).ToArray();
        var projection = Projection(records) with { Audience = MechanicAudienceContext.GameMaster };

        using var first = await Run(projection);
        Assert.Equal("ready", first.RootElement.GetProperty("status").GetString());
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(first.RootElement.GetRawText()) <= 60_000);
        Assert.True(first.RootElement.GetProperty("entries").GetArrayLength() < 40,
            "The byte bound, rather than only the entry count, must split this page.");
        Assert.Equal("partial", first.RootElement.GetProperty("coverage").GetString());
        Assert.True(first.RootElement.TryGetProperty("nextCursor", out _));
    }

    [Theory]
    [InlineData("{\"cursor\":\"1\"}")]
    [InlineData("{\"expectedSourceRevision\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}")]
    [InlineData("{\"cursor\":\"01\",\"expectedSourceRevision\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"expectedGraphSourceRevision\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}")]
    [InlineData("{\"cursor\":\"1\",\"expectedSourceRevision\":\"BBBB\",\"expectedGraphSourceRevision\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}")]
    [InlineData("{\"cursor\":\"1\",\"expectedSourceRevision\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}")]
    public async Task Continuation_requires_a_canonical_cursor_and_both_source_fences(string input)
    {
        using var output = await Run(Projection([Record("chronology.one", "Known", 1, "public")]) with
        {
            Input = input
        });

        Assert.Equal("unavailable", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("CHRONOLOGY_CONTEXT_INVALID", output.RootElement.GetProperty("reason").GetString());
        Assert.Equal("partial", output.RootElement.GetProperty("coverage").GetString());
    }

    [Fact]
    public async Task Continuation_compares_the_declared_graph_fence_not_the_reserved_host_fence()
    {
        using var output = await Run(Projection([Record("chronology.one", "Known", 1, "public")]) with
        {
            Input = "{\"cursor\":\"1\",\"expectedSourceRevision\":\"CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC\",\"expectedGraphSourceRevision\":\"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\"}"
        });

        Assert.Equal("unavailable", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("CHRONOLOGY_SOURCE_CHANGED", output.RootElement.GetProperty("reason").GetString());
        Assert.Empty(output.RootElement.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task Hidden_gm_and_archived_records_do_not_consume_the_player_visible_entry_limit()
    {
        var records = new List<ChronologyRecord>
        {
            Record("chronology.player.one", "Player one", 1, "public"),
            Record("chronology.player.two", "Player two", 2, "party")
        };
        records.AddRange(Enumerable.Range(0, 300).Select(index => Record($"chronology.gm.{index:D3}",
            "GM hidden", index, "gm", calendarId: "foreign-calendar")));
        records.AddRange(Enumerable.Range(0, 300).Select(index => Record($"chronology.archived.{index:D3}",
            "Archived hidden", index, "public", status: "archived", calendarId: "foreign-calendar")));

        using var output = await Run(Projection(records));

        Assert.Equal("ready", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("complete", output.RootElement.GetProperty("coverage").GetString());
        Assert.Equal(["Player one", "Player two"], output.RootElement.GetProperty("entries").EnumerateArray()
            .Select(value => value.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task Large_graph_snapshot_remains_deeply_immutable_before_mechanic_execution()
    {
        var projection = Projection(Enumerable.Range(0, 500).Select(index =>
            Record($"chronology.{index:D3}", $"Record {index}", index, "public")).ToArray());
        const string source = """
            'use strict';
            var graph = ctx.graphSnapshots.worldChronology, rejected = 0;
            try { graph.root.name = 'changed'; } catch (error) { rejected++; }
            try { graph.steps.records.nodes[0].components['game.core.world.chronology'].title = 'changed'; } catch (error) { rejected++; }
            try { graph.steps.records.edges[0].data.changed = true; } catch (error) { rejected++; }
            return {data:{rejected:rejected,root:graph.root.name,
              title:graph.steps.records.nodes[0].components['game.core.world.chronology'].title,
              edgeKeys:Object.keys(graph.steps.records.edges[0].data).length}};
            """;

        var result = await Engine.RunAsync(source, projection, ExecutionLimits.ReadModel);

        Assert.True(result.Ok, result.Error);
        using var output = JsonDocument.Parse(result.Output.Data);
        Assert.Equal(3, output.RootElement.GetProperty("rejected").GetInt32());
        Assert.Equal("Campaign", output.RootElement.GetProperty("root").GetString());
        Assert.Equal("Record 0", output.RootElement.GetProperty("title").GetString());
        Assert.Equal(0, output.RootElement.GetProperty("edgeKeys").GetInt32());
    }

    private static MechanicProjection Projection(IReadOnlyList<ChronologyRecord> records, string subjectWorld = "world.one")
    {
        var campaign = Node("campaign.one", "Campaign", ("game.core.campaign.root", new
        {
            status = "active", title = "Campaign", rulesetScope = "dnd2024", additive = "retained"
        }));
        var world = Node("world.one", "World",
            ("game.core.world.root", new { status = "active", summary = "World" }),
            ("game.core.world.clock", new { calendarId = "calendar.one", currentMinute = 100, revision = 1 }));
        var recordNodes = records.Select(RecordNode).ToArray();
        var recordEdges = records.Select(record => Edge(record.Id, world.Id, "game.core.world.chronology.in-world")).ToArray();
        var subjectIds = records.SelectMany(record => record.SubjectIds).Distinct(StringComparer.Ordinal).ToArray();
        var subjects = subjectIds.Select(id => id == "location.market"
            ? Node(id, "Market", ("game.core.world.location", new { status = "active", summary = "Market" }))
            : Node(id, "Foreign faction", ("game.core.world.faction", new { status = "active", summary = "Foreign" }))).ToArray();
        var subjectEdges = records.SelectMany(record => record.SubjectIds.Select(subject =>
            Edge(record.Id, subject, "game.core.world.chronology.about"))).ToArray();
        var scopeWorld = subjectWorld == world.Id ? world : Node(subjectWorld, "Other world",
            ("game.core.world.root", new { status = "active", summary = "Other" }));
        var scopeEdges = subjectIds.Where(id => id != "location.market")
            .Select(id => Edge(id, scopeWorld.Id, "game.core.world.faction.in-world")).ToArray();
        var steps = new Dictionary<string, MechanicGraphStepSnapshot>(StringComparer.Ordinal)
        {
            ["world"] = Step([world], [Edge(campaign.Id, world.Id, "game.core.campaign.in-world")]),
            ["records"] = Step(recordNodes, recordEdges),
            ["recordWorlds"] = Step([world], recordEdges),
            ["subjects"] = Step(subjects, subjectEdges),
            ["subjectScopes"] = Step(scopeEdges.Length == 0 ? [] : [scopeWorld], scopeEdges),
            ["subjectAncestors"] = Step([world], [])
        };
        var containment = subjectIds.Contains("location.market", StringComparer.Ordinal)
            ? new MechanicGraphContainment[] { new(world.Id, "location.market", "locations", 1) }
            : [];
        var graph = new MechanicGraphSnapshot(campaign, steps, containment, true,
            new(steps.Count, recordNodes.Length + subjects.Length + 2, recordEdges.Length * 2 + subjectEdges.Length + scopeEdges.Length + 1,
                1, 16, 1), Revision);
        return new()
        {
            StateSpaceId = "space.one",
            Audience = MechanicAudienceContext.Player,
            Input = "{}",
            Roles = { ["campaign"] = new(campaign.Id, campaign.Name,
                new Dictionary<string, string> { ["game.core.campaign.root"] = Json(campaign.Components["game.core.campaign.root"]) }, null, "") },
            GraphSnapshots = { ["worldChronology"] = graph }
        };
    }

    private static MechanicGraphNode RecordNode(ChronologyRecord record)
    {
        var component = new Dictionary<string, object?>
        {
            ["status"] = record.Status,
            ["title"] = record.Title,
            ["summary"] = record.Summary,
            ["calendarId"] = record.CalendarId,
            ["occurredAtMinute"] = record.Minute,
            ["precision"] = record.Precision,
            ["dateLabel"] = record.DateLabel,
            ["visibility"] = record.Visibility
        };
        if (record.Additive) component["additive"] = true;
        return Node(record.Id, record.Id + " label", ("game.core.world.chronology", component));
    }

    private static ChronologyRecord Record(string id, string title, long? minute, string visibility,
        string status = "active", string? summary = null, string calendarId = "calendar.one", string precision = "exact",
        string? dateLabel = null, IReadOnlyList<string>? subjectIds = null, bool additive = false) => new(
            id, title, summary ?? title + " summary.", calendarId, minute, precision, dateLabel ?? "Date " + minute,
            visibility, status, subjectIds ?? [], additive);

    private static MechanicGraphNode Node(string id, string name, params (string Id, object Value)[] components)
    {
        var values = components.ToDictionary(value => value.Id,
            value => JsonSerializer.SerializeToElement(value.Value), StringComparer.Ordinal);
        var revisions = components.ToDictionary(value => value.Id,
            _ => new MechanicGraphComponentRevision(1, Revision, 1), StringComparer.Ordinal);
        return new(id, name, values, revisions, 1);
    }

    private static MechanicGraphEdge Edge(string from, string to, string kind) =>
        new(from, to, kind, JsonSerializer.SerializeToElement(new { }), 1);

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

    private static string Json(JsonElement value) => value.GetRawText();
    private static string MechanicPath() => Path.Combine(Root(), "catalog", "applications", "dnd2024", "mechanics",
        "world", "dnd2024.mechanic.world-chronology.project.js");
    private static string MechanicMarkdownPath() => Path.Combine(Root(), "catalog", "applications", "dnd2024", "mechanics",
        "world", "dnd2024.mechanic.world-chronology.project.md");
    private static string QueryPath() => Path.Combine(Root(), "catalog", "applications", "dnd2024", "queries",
        "world", "dnd2024.query.world-chronology.json");
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }

    private sealed record ChronologyRecord(string Id, string Title, string Summary, string CalendarId, long? Minute,
        string Precision, string DateLabel, string Visibility, string Status, IReadOnlyList<string> SubjectIds, bool Additive);
}
