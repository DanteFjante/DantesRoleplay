using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Effects;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;

namespace DantesRoleplay.Tests;

/// <summary>Feature 4 Slice 1 proves scoped trusted-GM knowledge as catalog-owned world state.</summary>
public sealed class CatalogWorldFeature4Tests : IDisposable
{
    private const string World = "world.feature-01.fixture";
    private const string Fact = "fact.feature-04.toll-ledger";
    private const string Rumour = "rumour.feature-04.observatory-signal";
    private const string Secret = "secret.feature-04.oren-correspondence";
    private static readonly string[] Clues = ["clue.feature-04.ledger-seal", "clue.feature-04.oren-letter", "clue.feature-04.observatory-lantern"];
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), $"world-feature-04-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true);
    }

    [Fact]
    public async Task Fresh_import_contains_scoped_fact_rumour_secret_and_three_clues_without_copying_truth()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        var contents = await CatalogReader.ReadAsync(_catalogCopy);
        AssertKnowledgeFixture(contents);

        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var imported = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world)
            .ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        Assert.NotNull(await new ProcedureStore(db).GetAsync("procedure.game.core.world.knowledge"));

        var definitions = await world.GetDefinitionsAsync();
        Assert.Contains(definitions, d => d.Id == "game.core.world.fact" && d.UsageCount >= 1);
        Assert.Contains(definitions, d => d.Id == "game.core.world.rumour" && d.UsageCount >= 1);
        Assert.Contains(definitions, d => d.Id == "game.core.world.secret" && d.UsageCount >= 1);
        Assert.Contains(definitions, d => d.Id == "game.core.world.clue" && d.UsageCount >= 3);

        var secret = Require(await world.GetEntityAsync(Secret));
        var secretData = Component(secret, "game.core.world.secret");
        AssertKnowledgeData(secretData, "secret");
        foreach (var id in new[] { Fact, Rumour }.Concat(Clues))
        {
            var entity = Require(await world.GetEntityAsync(id));
            var kind = id.StartsWith("fact.", StringComparison.Ordinal) ? "fact" : id.StartsWith("rumour.", StringComparison.Ordinal) ? "rumour" : "clue";
            var data = Component(entity, $"game.core.world.{kind}");
            AssertKnowledgeData(data, kind);
            Assert.DoesNotContain(secretData, data, StringComparison.Ordinal);
            var links = (await world.GetRelationshipsAsync(id, includeIncoming: false)).ToArray();
            Assert.Equal(kind == "clue" ? 3 : 2, links.Length);
            Assert.Contains(links, link => link.ToEntityId == World && link.Kind == "game.core.world.knowledge.in-world" && link.Data == "{}");
            Assert.Contains(links, link => link.Kind == "game.core.world.knowledge.about" && link.Data == "{}");
            if (kind == "clue") Assert.Contains(links, link => link.Kind == "game.core.world.clue.supports" && link.Data == "{}");
        }
    }

    [Fact]
    public void Closed_knowledge_data_and_directed_link_conventions_reject_invalid_fixture_authoring()
    {
        Assert.Throws<InvalidOperationException>(() => AssertKnowledgeData("""{"status":"active","summary":"x","provenance":"p","visibility":"gm","targetId":"bad"}""", "fact"));
        Assert.Throws<InvalidOperationException>(() => AssertKnowledgeData("""{"status":"unconfirmed","summary":"x","provenance":"p","visibility":"gm"}""", "fact"));
        Assert.Throws<InvalidOperationException>(() => AssertKnowledgeData("""{"status":"active","summary":"x","provenance":"p","visibility":"party"}""", "secret"));
        Assert.Throws<InvalidOperationException>(() => AssertKnowledgeData("""{"status":"unrevealed","summary":"x","provenance":"p","visibility":"party"}""", "clue"));
        Assert.Throws<InvalidOperationException>(() => AssertKnowledgeData("""{"status":"revealed","summary":"x","provenance":"p","visibility":"gm"}""", "clue"));
        Assert.Throws<InvalidOperationException>(() => AssertKnowledgeLinks([new(Fact, World, "game.core.world.clue.supports", "{}")], new[] { Fact }));
        Assert.Throws<InvalidOperationException>(() => AssertKnowledgeLinks([new(Fact, Fact, "game.core.world.knowledge.about", "{}")], new[] { Fact }));
        Assert.Throws<InvalidOperationException>(() => AssertKnowledgeLinks([new(Clues[0], Secret, "game.core.world.clue.supports", "{\"why\":1}")], Clues));
    }

    [Fact]
    public async Task Scoped_clue_reveal_and_rumour_confirmation_are_deterministic_and_preserve_the_secret()
    {
        CopyDirectory(RepositoryCatalog(), _catalogCopy);
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db); var mechanics = new MechanicStore(db);
        var imported = await new CatalogImporter(db, mechanics, new ProcedureStore(db), world, new EventTypeStore(db)).ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(imported.Aborted);
        var runner = new CatalogMechanicTestHarness(db, mechanics, new ProjectionResolver(db), new JintMechanicEngine(), new EffectApplier(db, world, null, new EventLedger(db)), new OperationLog(db), new MechanicComposer(mechanics, new ProjectionResolver(db), new JintMechanicEngine()));
        var secret = Component(Require(await world.GetEntityAsync(Secret)), "game.core.world.secret");
        var clueBefore = Component(Require(await world.GetEntityAsync(Clues[0])), "game.core.world.clue");
        var rumourBefore = Component(Require(await world.GetEntityAsync(Rumour)), "game.core.world.rumour");

        var reveal = await RunAsync(runner, "reveal a clue", "clue", Clues[0], "mechanic.game.core.world.clue.reveal");
        Assert.True(reveal.Ok, reveal.Error?.Why); Assert.Equal(EffectType.ComponentSet, Assert.Single(reveal.Output.Effects).Type);
        Assert.Equal(clueBefore.Replace("\"status\":\"unrevealed\"", "\"status\":\"revealed\"", StringComparison.Ordinal).Replace("\"visibility\":\"gm\"", "\"visibility\":\"party\"", StringComparison.Ordinal), Component(Require(await world.GetEntityAsync(Clues[0])), "game.core.world.clue"));
        Assert.Equal(secret, Component(Require(await world.GetEntityAsync(Secret)), "game.core.world.secret"));
        Assert.Equal("world.component.replaced", Assert.Single(await new EventLedger(db).FindAsync(rootOperationId: reveal.OperationId)).TypeId);

        var confirm = await RunAsync(runner, "confirm a rumour", "rumour", Rumour, "mechanic.game.core.world.rumour.confirm");
        Assert.True(confirm.Ok, confirm.Error?.Why);
        Assert.Equal(rumourBefore.Replace("\"status\":\"unconfirmed\"", "\"status\":\"confirmed\"", StringComparison.Ordinal), Component(Require(await world.GetEntityAsync(Rumour)), "game.core.world.rumour"));
        Assert.Equal(secret, Component(Require(await world.GetEntityAsync(Secret)), "game.core.world.secret"));
        Assert.Equal("world.component.replaced", Assert.Single(await new EventLedger(db).FindAsync(rootOperationId: confirm.OperationId)).TypeId);

        var stale = await RunAsync(runner, "reveal a clue", "clue", Clues[0], "mechanic.game.core.world.clue.reveal");
        var invalid = await runner.RunAsync(new ActionRequest { Intent = "confirm a rumour", RoleEntityIds = new Dictionary<string, string> { ["rumour"] = Rumour, ["world"] = World }, Input = "{\"status\":\"confirmed\"}", Seed = 404 });
        Assert.False(stale.Ok); Assert.False(invalid.Ok); Assert.Equal(0, stale.AppliedCount); Assert.Equal(0, invalid.AppliedCount);
        Assert.Equal(secret, Component(Require(await world.GetEntityAsync(Secret)), "game.core.world.secret"));
    }

    private static async Task<ActionRunResult> RunAsync(CatalogMechanicTestHarness runner, string intent, string role, string id, string mechanicId)
    {
        var result = await runner.RunAsync(new ActionRequest { Intent = intent, RoleEntityIds = new Dictionary<string, string> { [role] = id, ["world"] = World }, Input = "{}", Seed = 404 });
        Assert.Equal(mechanicId, result.Mechanic?.Id); return result;
    }

    private static void AssertKnowledgeFixture(CatalogContents contents)
    {
        foreach (var id in new[] { "fact", "rumour", "secret", "clue" })
            Assert.Contains(contents.Components, c => c.Id == $"game.core.world.{id}" && !string.IsNullOrWhiteSpace(c.Schema));
        var knowledge = new[] { Fact, Rumour, Secret }.Concat(Clues).ToArray();
        foreach (var id in knowledge)
        {
            var entity = contents.Entities.Single(e => e.Id == id);
            var kind = id.Split('.')[0];
            AssertKnowledgeData(entity.Components.Single(c => c.DefinitionId == $"game.core.world.{kind}").Data, kind);
        }
        AssertKnowledgeLinks(contents.Relationships!.Relationships
            .Where(r => knowledge.Contains(r.From, StringComparer.Ordinal))
            .Where(r => r.Kind is "game.core.world.knowledge.in-world" or "game.core.world.knowledge.about" or "game.core.world.clue.supports")
            .Select(r => new Link(r.From, r.To, r.Kind, r.Data)), knowledge);
    }

    private static void AssertKnowledgeData(string json, string kind)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 4 || new[] { "status", "summary", "provenance", "visibility" }.Any(key => !root.TryGetProperty(key, out _))) throw new InvalidOperationException("Knowledge data must be closed.");
        var status = Text(root, "status", 20);
        Text(root, "summary", 1000); Text(root, "provenance", 500);
        var visibility = Text(root, "visibility", 10);
        var valid = kind switch
        {
            "fact" => (status is "active" or "archived") && (visibility is "public" or "party" or "gm"),
            "rumour" => (status is "unconfirmed" or "confirmed" or "disproved" or "archived") && (visibility is "public" or "party" or "gm"),
            "secret" => (status is "active" or "archived") && visibility == "gm",
            "clue" => (status == "unrevealed" && visibility == "gm") || (status == "revealed" && visibility == "party"),
            _ => false
        };
        if (!valid) throw new InvalidOperationException("Knowledge state/visibility is invalid.");
    }

    private static void AssertKnowledgeLinks(IEnumerable<Link> candidates, IReadOnlyCollection<string> knowledgeIds)
    {
        foreach (var link in candidates)
        {
            if (link.Data != "{}" || !knowledgeIds.Contains(link.From, StringComparer.Ordinal) || link.From == link.To)
                throw new InvalidOperationException("Knowledge links require distinct knowledge sources and exact empty data.");
            if (link.Kind is not ("game.core.world.knowledge.in-world" or "game.core.world.knowledge.about" or "game.core.world.clue.supports"))
                throw new InvalidOperationException("Unknown knowledge link.");
            if (link.Kind == "game.core.world.clue.supports" && !link.From.StartsWith("clue.", StringComparison.Ordinal))
                throw new InvalidOperationException("Only clues may support evidence.");
        }
    }

    private static string Text(JsonElement root, string property, int maximum)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString() != value.GetString()!.Trim() || value.GetString()!.Length > maximum) throw new InvalidOperationException($"{property} is invalid.");
        return value.GetString()!;
    }
    private static string Component(EntitySnapshot entity, string id) => entity.Components.Single(c => c.DefinitionId == id).Data;
    private static EntitySnapshot Require(EntitySnapshot? entity) => Assert.IsType<EntitySnapshot>(entity);
    private static string RepositoryCatalog() => Path.Combine(RepositoryRoot(), "catalog");
    private static string RepositoryRoot() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "DantesRoleplay.slnx"))) return d.FullName; throw new DirectoryNotFoundException(); }
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, d))); foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(destination, Path.GetRelativePath(source, f))); }
    private sealed record Link(string From, string To, string Kind, string Data);
}
