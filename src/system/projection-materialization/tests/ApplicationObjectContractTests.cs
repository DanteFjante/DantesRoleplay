using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using DantesRoleplay.CatalogNavigation;
using System.Text.Json;

namespace DantesRoleplay.Projections.Tests;

public sealed class ApplicationObjectContractTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public void Catalog_document_registers_one_versioned_object_and_persists_generated_reverse_mappings()
    {
        var setup = Setup("object-contract");
        var name = setup.Types.Define(new(setup.Application, "object-contract.name",
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"name\":{\"type\":\"string\"}}}"));
        var request = ValidRequest(setup, Ref(name));

        var registered = setup.Registry.Define(request);
        var replay = setup.Registry.Define(request);
        var read = setup.Registry.Get(registered.QualifiedId, registered.Version)!;

        Assert.Equal(1, registered.Version);
        Assert.Equal(registered.Reference, replay.Reference);
        Assert.Equal(registered.Reference, read.Reference);
        Assert.Equal(RegisteredApplicationObjectContract.ContractProfileId, read.ObjectContract!.ProfileId);
        Assert.Equal(["member", "subject"], read.EntityRoles);
        Assert.Equal("members", Assert.Single(read.ObjectContract.Collections).CollectionId);
        Assert.Equal("source-revision-bound", Assert.Single(read.ObjectContract.Collections).Cursor);
        Assert.Equal(["clear", "set"], read.ObjectContract.Writes!.Capabilities);
        Assert.Equal(2, read.ObjectContract.GeneratedWriteMappings.Count);
        Assert.All(read.ObjectContract.GeneratedWriteMappings, mapping =>
        {
            Assert.Equal("/name", mapping.ObjectPointer);
            Assert.Equal("identity", mapping.InputId);
            Assert.Equal("/name", mapping.SourcePointer);
        });

        var second = setup.Registry.Define(request with
        {
            DeclaredVersion = 2,
            ObjectContract = request.ObjectContract! with
            {
                Limits = request.ObjectContract!.Limits with { ItemCount = 999 }
            }
        });
        Assert.Equal(2, second.Version);
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(request with
        {
            DeclaredVersion = 4,
            ObjectContract = request.ObjectContract! with
            {
                Limits = request.ObjectContract!.Limits with { ItemCount = 998 }
            }
        }));
    }

    [Fact]
    public void Strict_catalog_parser_accepts_the_closed_object_vocabulary()
    {
        var setup = Setup("parsed-object");
        var type = setup.Types.Define(new(setup.Application, "parsed-object.name",
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}"));
        var json = """
        {
          "id":"parsed-object.summary","version":1,
          "schema":{"type":"object","additionalProperties":false,"properties":{"name":{"type":"string"}}},
          "roles":{"subject":{"required":true}},
          "sources":[{"id":"identity","role":"subject","component":{"qualifiedId":"TYPE_ID","version":TYPE_VERSION,"schemaHash":"TYPE_HASH"},"required":true}],
          "relationships":[],"references":[],
          "mappings":[{"inputId":"identity","sourcePointer":"/name","targetPointer":"/name"}],
          "collections":[],
          "limits":{"traversalDepth":1,"itemCount":10,"outputBytes":4096,"sqlQueries":1},
          "access":{"read":["player","dm"],"write":[]}
        }
        """.Replace("TYPE_ID", type.QualifiedId, StringComparison.Ordinal)
            .Replace("TYPE_VERSION", type.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("TYPE_HASH", type.SchemaHash, StringComparison.Ordinal);

        var parsed = ApplicationObjectDocument.Parse(json, setup.Application);
        var registered = setup.Registry.Define(parsed);

        Assert.Equal("parsed-object.summary", registered.QualifiedId);
        Assert.NotNull(registered.ObjectContract);
        Assert.Throws<ArgumentException>(() => ApplicationObjectDocument.Parse(
            json.Replace("\"access\":", "\"unknown\":true,\"access\":"), setup.Application));
    }

    [Fact]
    public void Catalog_objects_keep_their_reviewed_exact_version_fingerprints()
    {
        var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var game = ApplicationIdentifier.Parse("game");
        applications.Register(new(game, "Game", "", []));
        var application = ApplicationIdentifier.Parse("dnd2024");
        applications.Register(new(application, "D&D", "", [game]));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        types.Define(new(game, "game.core.campaign.root",
            File.ReadAllText(Path.Combine(Catalog(), "components", "game", "core", "campaign", "root.schema.json"))));
        types.Define(new(game, "game.core.campaign.character-participation",
            File.ReadAllText(Path.Combine(Catalog(), "components", "game", "core", "campaign", "character-participation.schema.json"))));
        types.Define(new(game, "game.core.world.faction",
            File.ReadAllText(Path.Combine(Catalog(), "components", "game", "core", "world", "faction.schema.json"))));
        types.Define(new(game, "game.core.world.root",
            File.ReadAllText(Path.Combine(Catalog(), "components", "game", "core", "world", "root.schema.json"))));
        var clockType = types.Define(new(game, "game.core.world.clock",
            File.ReadAllText(Path.Combine(Catalog(), "components", "game", "core", "world", "clock.schema.json"))));
        var hpType = types.Define(new(application, "dnd2024.creature.hit-points",
            File.ReadAllText(Path.Combine(Catalog(), "applications", "dnd2024", "components", "dnd2024.creature.hit-points.schema.json"))));
        var episodeType = types.Define(new(application, "dnd2024.rest-episode",
            File.ReadAllText(Path.Combine(Catalog(), "applications", "dnd2024", "components", "dnd2024.rest-episode.schema.json"))));
        var policyType = types.Define(new(application, "dnd2024.rest-policy",
            File.ReadAllText(Path.Combine(Catalog(), "applications", "dnd2024", "components", "dnd2024.rest-policy.schema.json"))));
        var registry = new SqliteProjectionDefinitionRegistry(db, types, schemas, applications);
        var objects = Path.Combine(Catalog(), "applications", "dnd2024", "objects");
        var campaignV1 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "campaign", "dnd2024.object.campaign-summary.v1.json")), application));
        var campaign = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "campaign", "dnd2024.object.campaign-summary.json")), application));
        var factions = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "world", "dnd2024.object.faction-directory-page.json")), application));
        var restCreature = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "rest", "dnd2024.object.rest-begin-creature.json")), application));
        var restWorld = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "rest", "dnd2024.object.rest-begin-world.json")), application));
        var restPolicy = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "rest", "dnd2024.object.rest-begin-policy.json")), application));

        Assert.Equal("3AE6FD831B4319BA96E15A1501896549030C80FDBFA49D5503D0568DB9B61DEB", campaignV1.ContentHash);
        Assert.Equal(2, campaign.Version);
        Assert.Equal("2C0836E9FF114C4F672D793012F2D0CD258D0A99B9DCFB5A892D63F5146011BF", campaign.ContentHash);
        Assert.Equal(["relationship.add", "relationship.remove", "set"],
            campaign.ObjectContract!.Writes!.Capabilities);
        Assert.Contains(campaign.ObjectContract.GeneratedWriteMappings, value =>
            value.ObjectPointer == "/premise" && value.Operation == "set" &&
            value.InputId == "campaign" && value.SourcePointer == "/premise");
        Assert.Contains(campaign.ObjectContract.GeneratedWriteMappings, value =>
            value.ObjectPointer == "/party" && value.Operation == "relationship.add" &&
            value.RelationshipId == "party");
        Assert.Contains(campaign.ObjectContract.GeneratedWriteMappings, value =>
            value.ObjectPointer == "/party" && value.Operation == "relationship.remove" &&
            value.RelationshipId == "party");
        Assert.Equal("867C1B1567F1801F34528A3AC7DD8DA2DB72BF69F24A086A3F5FC7F99AD31B3C", factions.ContentHash);
        Assert.Equal("858D347ED0CB9ADD8F937647703CD89F08F3AE313379037CFDD71B641F670F0C", restCreature.ContentHash);
        Assert.Equal("6D22CE1103C1E66FC163C8AEF2DF8FAB0C106D21C3EDA0D6A95D0984210B83A9", restWorld.ContentHash);
        Assert.Equal("4B1B67101E0CE06088AD997A3B8DAFED335EFEB3040851226E08D593A3EE19AD", restPolicy.ContentHash);
    }

    [Fact]
    public void Slice_four_pins_the_complete_exported_caldris_faction_directory()
    {
        string[] expected =
        [
            "faction.caldris.alderwick-crown-council", "faction.caldris.aur-river-judges",
            "faction.caldris.bellafont-water-courts", "faction.caldris.bramblebridge-watch",
            "faction.caldris.brotherhood-of-saint-orro", "faction.caldris.carrowmere-shipwright-fraternities",
            "faction.caldris.company-of-weirs", "faction.caldris.free-axle-companies",
            "faction.caldris.glassharbor-furnace-guilds", "faction.caldris.grey-mantle-houses",
            "faction.caldris.honest-weights-guild", "faction.caldris.houses-of-rescue",
            "faction.caldris.keepers-of-the-mountain-tithe", "faction.caldris.kethrian-canal-councils",
            "faction.caldris.lantern-sea-couriers", "faction.caldris.league-chairs-secretariat",
            "faction.caldris.long-bench-union", "faction.caldris.lorn-bell-houses",
            "faction.caldris.low-lantern-dry-sock-fund", "faction.caldris.namar-water-captains",
            "faction.caldris.nine-lamps-hospitallers", "faction.caldris.nine-march-moot",
            "faction.caldris.oath-rope-lodges", "faction.caldris.open-roof-society",
            "faction.caldris.quiet-bell-chapter", "faction.caldris.roadmenders-brotherhood",
            "faction.caldris.seed-mothers-compact", "faction.caldris.selucian-dock-assemblies",
            "faction.caldris.society-of-the-silver-pear", "faction.caldris.swallow-almanac-society",
            "faction.caldris.tensor-sect", "faction.caldris.tessa-sky-college",
            "faction.caldris.the-seventh-ledger", "faction.caldris.vessa-open-archive-league",
            "faction.caldris.wayward-company"
        ];
        var directory = Path.Combine(Catalog(), "world", "entities", "faction", "caldris");
        var actual = Directory.GetFiles(directory, "*.json").Select(path =>
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(document.RootElement.GetProperty("components").TryGetProperty("game.core.world.faction", out _));
            return document.RootElement.GetProperty("id").GetString()!;
        }).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);

        using var objectDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(Catalog(), "applications", "dnd2024",
            "objects", "world", "dnd2024.object.faction-directory-page.json")));
        var relationship = objectDocument.RootElement.GetProperty("relationships")[0];
        Assert.Equal("game.core.world.faction.in-world", relationship.GetProperty("qualifiedKind").GetString());
        Assert.Equal("incoming", relationship.GetProperty("direction").GetString());
    }

    [Fact]
    public void Registration_rejects_computed_ambiguous_cross_owner_and_unbounded_contracts()
    {
        var setup = Setup("invalid-object");
        var local = setup.Types.Define(new(setup.Application, "invalid-object.name",
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}"));
        var valid = ValidRequest(setup, Ref(local));

        Assert.Throws<ArgumentException>(() => setup.Registry.Define(valid with
        {
            QualifiedId = "invalid-object.unbounded",
            ObjectContract = valid.ObjectContract! with
            {
                Limits = valid.ObjectContract!.Limits with { SqlQueries = 65 }
            }
        }));

        var duplicateSource = valid with
        {
            QualifiedId = "invalid-object.ambiguous",
            ComponentInputs = [.. valid.ComponentInputs, new("other", "subject", Ref(local))],
            Mappings = [.. valid.Mappings, new("other", "/name", "/name")],
            ObjectContract = valid.ObjectContract! with
            {
                Sources = [.. valid.ObjectContract!.Sources, new("other", true)]
            }
        };
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(duplicateSource));

        var dependency = setup.Registry.Define(new(setup.Application, "invalid-object.computed-source",
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}",
            [new("identity", "subject", Ref(local))], [], [new("identity", "/name", "/name")]));
        var computed = valid with
        {
            QualifiedId = "invalid-object.computed-write",
            ComponentInputs = [],
            DependencyInputs = [new("derived", dependency.Reference,
                new Dictionary<string, string> { ["subject"] = "subject" })],
            Mappings = [new("derived", "/name", "/name")],
            ObjectContract = valid.ObjectContract! with
            {
                Sources = [],
                References = [new("derived", true)]
            }
        };
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(computed));

        var other = ApplicationIdentifier.Parse("other-object");
        new SqliteApplicationRegistry(setup.Db).Register(new(other, "Other", "", []));
        var foreign = setup.Types.Define(new(other, "other-object.endpoint",
            "{\"type\":\"object\",\"properties\":{}}"));
        var relationship = Assert.Single(valid.ObjectContract!.Relationships);
        var crossOwner = valid with
        {
            QualifiedId = "invalid-object.cross-owner",
            ObjectContract = valid.ObjectContract! with
            {
                Relationships = [relationship with
                {
                    RequiredEndpointComponents = [new("to", Ref(foreign))]
                }]
            }
        };
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(crossOwner));

        Assert.Null(setup.Registry.Get("invalid-object.unbounded", 1));
        Assert.Null(setup.Registry.Get("invalid-object.ambiguous", 1));
        Assert.Null(setup.Registry.Get("invalid-object.computed-write", 1));
        Assert.Null(setup.Registry.Get("invalid-object.cross-owner", 1));
    }

    [Fact]
    public void Object_query_round_trips_through_existing_discovery_contract()
    {
        var app = ApplicationIdentifier.Parse("query-object");
        var json = """
        {"id":"query-object.query.members","category":"world.members","name":"Members","description":"Lists members.","matches":["list members"],"roles":{"subject":"The owning entity.","member":"A listed entity."},"executor":"object-projection","object":{"qualifiedId":"query-object.summary","version":1,"contentFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"},"collection":"members","outputSchema":{"type":"object","additionalProperties":false,"properties":{"members":{"type":"array","items":{"type":"string"}}}},"exposure":"model-visible","status":"active"}
        """;

        var parsed = ApplicationQueryContract.Parse(json, app);
        var canonical = ApplicationCatalogRecordContent.QueryJson(parsed);
        var read = ApplicationQueryContract.Parse(canonical, app);

        Assert.True(read.IsObjectProjection);
        Assert.Equal("query-object.summary", read.ProjectionQualifiedId);
        Assert.Equal("members", read.ObjectCollectionId);
        Assert.Equal(new string('A', 64), read.ProjectionContentHash);
        Assert.DoesNotContain("outputSchemaHash", canonical, StringComparison.Ordinal);
        Assert.Contains("contentFingerprint", canonical, StringComparison.Ordinal);
    }

    private static ProjectionDefinitionRequest ValidRequest(SetupContext setup, EcsComponentReference type)
    {
        const string schema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"name\":{\"type\":\"string\"},\"members\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}}";
        const string editSchema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"name\":{\"type\":\"string\"}}}";
        return new(setup.Application, setup.Application.Value + ".summary", schema,
            [new("identity", "subject", type)], [], [new("identity", "/name", "/name")],
            new(
                [new("subject", true), new("member", false)],
                [new("identity", true)],
                [new("members", setup.Application.Value + ".relationship.member", "subject", "member", "many", "/members",
                    [new("from", type)], [])],
                [],
                [new("members", "members", 25, 100, [new("", "asc")], "source-revision-bound")],
                new(4, 500, 65_536, 8),
                new(["player", "dm"], ["dm"]),
                new(editSchema, ["set", "clear"], [new("/name", ["set", "clear"])])),
            1);
    }

    private SetupContext Setup(string id)
    {
        var db = _fixture.CreateContext();
        var application = ApplicationIdentifier.Parse(id);
        new SqliteApplicationRegistry(db).Register(new(application, id, "", []));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        return new(db, application, types, new SqliteProjectionDefinitionRegistry(db, types, schemas));
    }

    private static EcsComponentReference Ref(RegisteredComponentTypeVersion type) =>
        new(type.QualifiedId, type.Version, type.SchemaHash);

    private static string Catalog()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return Path.Combine(directory.FullName, "catalog");
        throw new DirectoryNotFoundException();
    }

    private sealed record SetupContext(DantesRoleplayDbContext Db, ApplicationIdentifier Application,
        SqliteComponentTypeRegistry Types, SqliteProjectionDefinitionRegistry Registry);

    public void Dispose() => _fixture.Dispose();
}
