using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Events;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>Feature 19 owns dated World history without deriving it from clocks, events, or campaigns.</summary>
public sealed class CatalogWorldFeature19Tests : IDisposable
{
    private const string Root = "world.feature-01.fixture";
    private const string Calendar = "lantern-compact-epoch";
    private const string Component = "game.core.world.chronology";
    private const string InWorld = "game.core.world.chronology.in-world";
    private const string About = "game.core.world.chronology.about";
    private readonly SqliteFixture _fixture = new();
    private readonly string _copy = Path.Combine(Path.GetTempPath(), $"world-feature-19-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_copy)) Directory.Delete(_copy, true);
    }

    [Fact]
    public async Task Fresh_import_reads_active_chronology_in_stable_signed_date_order_without_world_writes()
    {
        Copy(Catalog(), _copy);
        var contents = await CatalogReader.ReadAsync(_copy);
        AssertCatalogContract(contents);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var procedures = new ProcedureStore(db);
        var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, procedures, world, new EventTypeStore(db), new SubscriptionStore(db))
            .ApplyAsync(_copy, new CatalogImportOptions());
        Assert.False(imported.Aborted);

        var chronologyProcedure = await procedures.GetAsync("procedure.game.core.world.chronology");
        Assert.NotNull(chronologyProcedure);
        Assert.Contains(InWorld, chronologyProcedure!.Instructions, StringComparison.Ordinal);
        Assert.Contains(About, chronologyProcedure.Instructions, StringComparison.Ordinal);
        var readProcedure = await procedures.GetAsync("procedure.game.core.world.read");
        Assert.Contains("World chronology", readProcedure!.Instructions, StringComparison.Ordinal);

        var before = await CountsAsync(db);
        var operationsBefore = await db.Operations.CountAsync();
        var projection = Projection(await GraphAsync(db, procedures, world, mechanics));
        var chronology = BuildChronology(projection);

        Assert.Equal([
            "chronology.feature-19.market-charter",
            "chronology.feature-19.gate-dedication",
            "chronology.feature-19.observatory-era"
        ], chronology.Select(entry => entry.Id));
        Assert.Equal([-525600L, 0L, 0L], chronology.Select(entry => entry.OccurredAtMinute));
        Assert.DoesNotContain(chronology, entry => entry.Id == "chronology.feature-19.archived-draft");
        Assert.Equal("Compact Epoch, minute zero", chronology[1].DateLabel);
        Assert.Equal("exact", chronology[1].Precision);
        Assert.Equal("era", chronology[2].Precision);
        Assert.Equal(8, projection.Edges.Count(edge => edge.Kind is InWorld or About));
        Assert.Null(projection.Truncated);

        Assert.Equal(before, await CountsAsync(db));
        Assert.Equal(operationsBefore + 1, await db.Operations.CountAsync());
    }

    [Fact]
    public async Task Component_schema_rejects_extra_or_malformed_date_data()
    {
        var contents = await CatalogReader.ReadAsync(Catalog());
        var definition = Assert.Single(contents.Components, component => component.Id == Component);
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(definition.Schema);
        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
        const string valid = """{"status":"active","title":"Valid","summary":"Valid fixture.","calendarId":"lantern-compact-epoch","occurredAtMinute":-1,"precision":"exact","dateLabel":"Before the epoch","visibility":"public"}""";
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(compilation.ProfileId, compilation.NormalizedSchema, valid).Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(compilation.ProfileId, compilation.NormalizedSchema,
            valid.Replace("-1", "1.5", StringComparison.Ordinal)).Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(compilation.ProfileId, compilation.NormalizedSchema,
            valid.Replace("\"visibility\":\"public\"", "\"visibility\":\"public\",\"extra\":true", StringComparison.Ordinal)).Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(compilation.ProfileId, compilation.NormalizedSchema,
            valid.Replace("\"dateLabel\":\"Before the epoch\",", string.Empty, StringComparison.Ordinal)).Status);
    }

    [Fact]
    public void Closed_data_and_scope_subject_conventions_reject_invalid_authoring()
    {
        const string valid = """{"status":"active","title":"Title","summary":"Summary","calendarId":"lantern-compact-epoch","occurredAtMinute":-1,"precision":"approximate","dateLabel":"Before the epoch","visibility":"party"}""";
        Assert.Equal(-1L, Chronology(valid).OccurredAtMinute);
        Assert.Throws<InvalidOperationException>(() => Chronology("{}"));
        Assert.Throws<InvalidOperationException>(() => Chronology(valid.Replace("-1", "1.5", StringComparison.Ordinal)));
        Assert.Throws<InvalidOperationException>(() => Chronology(valid.Replace("\"approximate\"", "\"uncertain\"", StringComparison.Ordinal)));
        Assert.Throws<InvalidOperationException>(() => Chronology(valid.Replace("\"party\"", "\"secret\"", StringComparison.Ordinal)));
        Assert.Throws<InvalidOperationException>(() => Chronology(valid.Replace("\"visibility\":\"party\"", "\"visibility\":\"party\",\"extra\":true", StringComparison.Ordinal)));

        var validLinks = new[] {
            new Link("record", Root, InWorld, "{}"),
            new Link("record", "location.feature-01.market", About, "{}")
        };
        AssertLinks("record", Calendar, Calendar, Root, validLinks,
            new Dictionary<string, string> { ["location.feature-01.market"] = Root });
        Assert.Throws<InvalidOperationException>(() => AssertLinks("record", "other-calendar", Calendar, Root, validLinks,
            new Dictionary<string, string> { ["location.feature-01.market"] = Root }));
        Assert.Throws<InvalidOperationException>(() => AssertLinks("record", Calendar, Calendar, Root, [.. validLinks, validLinks[0]],
            new Dictionary<string, string> { ["location.feature-01.market"] = Root }));
        Assert.Throws<InvalidOperationException>(() => AssertLinks("record", Calendar, Calendar, Root,
            [new(Root, "record", InWorld, "{}")], new Dictionary<string, string>()));
        Assert.Throws<InvalidOperationException>(() => AssertLinks("record", Calendar, Calendar, Root,
            [new("record", Root, InWorld, "{\"unexpected\":true}")], new Dictionary<string, string>()));
        Assert.Throws<InvalidOperationException>(() => AssertLinks("record", Calendar, Calendar, Root, validLinks,
            new Dictionary<string, string> { ["location.feature-01.market"] = "world.other" }));

        var tooManySubjects = new List<Link> { new("record", Root, InWorld, "{}") };
        var subjectWorlds = new Dictionary<string, string>();
        for (var index = 0; index < 11; index++)
        {
            var subject = $"subject.{index}";
            tooManySubjects.Add(new("record", subject, About, "{}"));
            subjectWorlds[subject] = Root;
        }
        Assert.Throws<InvalidOperationException>(() => AssertLinks("record", Calendar, Calendar, Root, tooManySubjects, subjectWorlds));
    }

    [Fact]
    public async Task Clock_change_never_creates_reorders_or_rewrites_chronology()
    {
        Copy(Catalog(), _copy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        Assert.False((await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world, new EventTypeStore(db), new SubscriptionStore(db))
            .ApplyAsync(_copy, new CatalogImportOptions())).Aborted);

        var before = await ChronologyStateAsync(db);
        await world.SetComponentAsync(Root, "game.core.world.clock",
            """{"calendarId":"lantern-compact-epoch","currentMinute":1440,"revision":1}""");
        var after = await ChronologyStateAsync(db);

        Assert.Equal(before, after);
        Assert.Equal(before.Count, after.Count);
    }

    private static void AssertCatalogContract(CatalogContents contents)
    {
        var definition = Assert.Single(contents.Components, component => component.Id == Component);
        Assert.False(string.IsNullOrWhiteSpace(definition.Schema));
        var procedure = Assert.Single(contents.Procedures, candidate => candidate.Id == "procedure.game.core.world.chronology");
        Assert.Contains("signed minute", procedure.Instructions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(contents.Mechanics, mechanic => mechanic.Id.Contains("chronology", StringComparison.Ordinal));
        Assert.DoesNotContain(contents.EventTypes, eventType => eventType.Id.Contains("chronology", StringComparison.Ordinal));
        Assert.DoesNotContain(contents.Subscriptions, subscription => subscription.Id.Contains("chronology", StringComparison.Ordinal));

        var relationships = Assert.IsType<RelationshipsFile>(contents.Relationships).Relationships;
        var records = contents.Entities
            .Where(entity => entity.Components.Any(component => component.DefinitionId == Component))
            .Where(entity => relationships.Any(link => link.From == entity.Id && link.To == Root && link.Kind == InWorld))
            .OrderBy(entity => entity.Id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(4, records.Length);
        var root = contents.Entities.Single(entity => entity.Id == Root);
        var rootCalendar = ClockCalendar(root.Components.Single(component => component.DefinitionId == "game.core.world.clock").Data);
        foreach (var record in records)
        {
            var state = Chronology(record.Components.Single(component => component.DefinitionId == Component).Data);
            var links = relationships.Where(link => link.From == record.Id || link.To == record.Id)
                .Select(link => new Link(link.From, link.To, link.Kind, link.Data)).ToArray();
            var subjectWorlds = links.Where(link => link.From == record.Id && link.Kind == About)
                .ToDictionary(link => link.To, link => ContainingWorld(contents, link.To), StringComparer.Ordinal);
            AssertLinks(record.Id, state.CalendarId, rootCalendar, Root, links, subjectWorlds);
        }
    }

    private static IReadOnlyList<ChronologyEntry> BuildChronology(GraphProjection projection)
    {
        if (projection.Truncated is not null) throw new InvalidOperationException("Chronology input is truncated.");
        var root = projection.Nodes.SingleOrDefault(node => node.Id == projection.RootId)
            ?? throw new InvalidOperationException("Chronology root is missing.");
        var rootCalendar = ClockCalendar(root.Components.Single(component => component.DefinitionId == "game.core.world.clock").Data);
        var records = new List<ChronologyEntry>();
        foreach (var node in projection.Nodes.Where(node => node.Components.Any(component => component.DefinitionId == Component)))
        {
            var state = Chronology(node.Components.Single(component => component.DefinitionId == Component).Data);
            var links = projection.Edges.Where(edge => edge.FromEntityId == node.Id || edge.ToEntityId == node.Id)
                .Select(edge => new Link(edge.FromEntityId, edge.ToEntityId, edge.Kind, edge.Data)).ToArray();
            var subjects = links.Where(link => link.From == node.Id && link.Kind == About)
                .ToDictionary(link => link.To, _ => Root, StringComparer.Ordinal);
            AssertLinks(node.Id, state.CalendarId, rootCalendar, Root, links, subjects);
            if (state.Status == "active") records.Add(new(node.Id, state.OccurredAtMinute, state.DateLabel, state.Precision));
        }
        return records.OrderBy(record => record.OccurredAtMinute).ThenBy(record => record.Id, StringComparer.Ordinal).ToArray();
    }

    private static ChronologyState Chronology(string json)
    {
        using var document = JsonDocument.Parse(json);
        var value = document.RootElement;
        var names = value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray()
            : [];
        string[] expected = ["calendarId", "dateLabel", "occurredAtMinute", "precision", "status", "summary", "title", "visibility"];
        if (!names.SequenceEqual(expected, StringComparer.Ordinal)) throw new InvalidOperationException("Chronology data is not closed.");
        var status = Text(value, "status", 10);
        var title = Text(value, "title", 160);
        var summary = Text(value, "summary", 1000);
        var calendarId = Text(value, "calendarId", 100);
        var precision = Text(value, "precision", 20);
        var dateLabel = Text(value, "dateLabel", 100);
        var visibility = Text(value, "visibility", 10);
        if (status is not ("active" or "archived") || precision is not ("exact" or "approximate" or "era") ||
            visibility is not ("public" or "party" or "gm") ||
            !value.GetProperty("occurredAtMinute").TryGetInt64(out var minute) || minute is < -1_000_000_000 or > 1_000_000_000)
            throw new InvalidOperationException("Chronology data is invalid.");
        return new(status, title, summary, calendarId, minute, precision, dateLabel, visibility);
    }

    private static void AssertLinks(string recordId, string calendarId, string rootCalendar, string rootId,
        IReadOnlyCollection<Link> links, IReadOnlyDictionary<string, string> subjectWorlds)
    {
        var scopes = links.Where(link => link.From == recordId && link.Kind == InWorld).ToArray();
        var subjects = links.Where(link => link.From == recordId && link.Kind == About).ToArray();
        if (calendarId != rootCalendar || scopes.Length != 1 || scopes[0].To != rootId || scopes[0].Data != "{}" ||
            scopes[0].From == scopes[0].To || subjects.Length > 10 || subjects.Select(link => link.To).Distinct(StringComparer.Ordinal).Count() != subjects.Length ||
            subjects.Any(link => link.To == recordId || link.Data != "{}" || !subjectWorlds.TryGetValue(link.To, out var subjectWorld) || subjectWorld != rootId) ||
            links.Any(link => (link.Kind is InWorld or About) && link.From != recordId))
            throw new InvalidOperationException("Chronology scope or subjects are invalid.");
    }

    private static string ContainingWorld(CatalogContents contents, string entityId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = contents.Entities.Single(entity => entity.Id == entityId);
        while (seen.Add(current.Id))
        {
            if (current.Components.Any(component => component.DefinitionId == "game.core.world.root")) return current.Id;
            if (current.ContainerId is null) break;
            current = contents.Entities.Single(entity => entity.Id == current.ContainerId);
        }
        throw new InvalidOperationException("Chronology subject has no proven containing World.");
    }

    private static string ClockCalendar(string json)
    {
        using var document = JsonDocument.Parse(json);
        var value = document.RootElement;
        return Text(value, "calendarId", 100);
    }

    private static string Text(JsonElement value, string property, int maximum)
    {
        if (!value.TryGetProperty(property, out var item) || item.ValueKind != JsonValueKind.String) throw new InvalidOperationException($"Chronology {property} is missing.");
        var text = item.GetString()!;
        if (text.Length == 0 || text.Length > maximum || text != text.Trim()) throw new InvalidOperationException($"Chronology {property} is invalid.");
        return text;
    }

    private static async Task<ToolEnvelope> GraphAsync(DantesRoleplayDbContext db, IProcedureStore procedures, IWorldStore world, IMechanicStore mechanics) =>
        await new QueryMcpTool().QueryAsync(procedures, world, new GraphProjectionReader(world), mechanics,
            new EventTypeStore(db), new SubscriptionStore(db), new EventLedger(db), new OperationLog(db), new NotificationStore(db),
            "graph", id: Root, componentIds: [Component, "game.core.world.clock"], containmentDepth: 0,
            relationshipKinds: [InWorld, About], relationshipDepth: 2, maxNodes: 100, maxEdges: 200);

    private static GraphProjection Projection(ToolEnvelope envelope) => Assert.IsType<GraphProjection>(envelope.Data);

    private static async Task<IReadOnlyList<string>> ChronologyStateAsync(DantesRoleplayDbContext db) =>
        await db.Components.Where(component => component.DefinitionId == Component)
            .OrderBy(component => component.EntityId).Select(component => component.EntityId + "|" + component.Data).ToListAsync();

    private static async Task<WorldCounts> CountsAsync(DantesRoleplayDbContext db) => new(
        await db.Entities.CountAsync(), await db.Components.CountAsync(), await db.Containments.CountAsync(),
        await db.Relationships.CountAsync(), await db.Events.CountAsync(), await db.Notifications.CountAsync());

    private static string Catalog()
    {
        var workingCatalog = Path.Combine(Directory.GetCurrentDirectory(), "catalog");
        if (Directory.Exists(workingCatalog)) return workingCatalog;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));

        WorldFeatureFixture.RestoreRelationships(source, target);
    }

    private sealed record ChronologyState(string Status, string Title, string Summary, string CalendarId,
        long OccurredAtMinute, string Precision, string DateLabel, string Visibility);
    private sealed record ChronologyEntry(string Id, long OccurredAtMinute, string DateLabel, string Precision);
    private sealed record Link(string From, string To, string Kind, string Data);
    private sealed record WorldCounts(int Entities, int Components, int Containments, int Relationships, int Events, int Notifications);
}
