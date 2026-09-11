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
        Assert.Null(read.ObjectContract.FieldProvenance);
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
        types.Define(new(game, "game.core.campaign.location-visit",
            File.ReadAllText(Path.Combine(Catalog(), "components", "game", "core", "campaign", "location-visit.schema.json"))));
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
        var campaignV2 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "campaign", "dnd2024.object.campaign-summary.v2.json")), application));
        var campaignV3 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "campaign", "dnd2024.object.campaign-summary.v3.json")), application));
        var campaignV4 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "campaign", "dnd2024.object.campaign-summary.json")), application));
        var factionsV1 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "world", "dnd2024.object.faction-directory-page.v1.json")), application));
        var factionsV2 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "world", "dnd2024.object.faction-directory-page.json")), application));
        var restCreatureV1 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "rest", "dnd2024.object.rest-begin-creature.v1.json")), application));
        var restCreatureV2 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "rest", "dnd2024.object.rest-begin-creature.json")), application));
        var restWorldV1 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "rest", "dnd2024.object.rest-begin-world.v1.json")), application));
        var restWorldV2 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "rest", "dnd2024.object.rest-begin-world.json")), application));
        var restPolicyV1 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "rest", "dnd2024.object.rest-begin-policy.v1.json")), application));
        var restPolicyV2 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "rest", "dnd2024.object.rest-begin-policy.json")), application));
        var campaignLocationVisitsV1 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "campaign", "dnd2024.object.campaign-location-visits.v1.json")), application));
        var campaignLocationVisitsV2 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "campaign", "dnd2024.object.campaign-location-visits.json")), application));
        var worldCampaignDirectoryV1 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "campaign", "dnd2024.object.world-campaign-directory.v1.json")), application));
        var worldCampaignDirectoryV2 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(
            objects, "campaign", "dnd2024.object.world-campaign-directory.json")), application));

        Assert.Equal("3AE6FD831B4319BA96E15A1501896549030C80FDBFA49D5503D0568DB9B61DEB", campaignV1.ContentHash);
        Assert.Equal(2, campaignV2.Version);
        Assert.Equal("2C0836E9FF114C4F672D793012F2D0CD258D0A99B9DCFB5A892D63F5146011BF", campaignV2.ContentHash);
        Assert.Equal(3, campaignV3.Version);
        Assert.Equal("E3979EB454E4A0D1AEC65446D6E2849D8E2ED29D0C8C43E7B8E5CF3E0783EB42", campaignV3.ContentHash);
        Assert.Equal(4, campaignV4.Version);
        Assert.Equal("0836CB401676AEDC9DAD5A21DEB7E5A4C333DEB7F8EDD7545432EF7430A4367C", campaignV4.ContentHash);
        using var queryDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(Catalog(), "applications", "dnd2024",
            "queries", "campaign", "dnd2024.query.campaign-summary.json")));
        Assert.True(queryDocument.RootElement.GetProperty("object").GetProperty("contentFingerprint").GetString() == campaignV4.ContentHash,
            $"Campaign query must pin v4 fingerprint {campaignV4.ContentHash}");
        Assert.Equal(["relationship.add", "relationship.remove", "set"],
            campaignV2.ObjectContract!.Writes!.Capabilities);
        Assert.Contains(campaignV2.ObjectContract.GeneratedWriteMappings, value =>
            value.ObjectPointer == "/premise" && value.Operation == "set" &&
            value.InputId == "campaign" && value.SourcePointer == "/premise");
        Assert.Contains(campaignV2.ObjectContract.GeneratedWriteMappings, value =>
            value.ObjectPointer == "/party" && value.Operation == "relationship.add" &&
            value.RelationshipId == "party");
        Assert.Contains(campaignV2.ObjectContract.GeneratedWriteMappings, value =>
            value.ObjectPointer == "/party" && value.Operation == "relationship.remove" &&
            value.RelationshipId == "party");
        Assert.Equal("867C1B1567F1801F34528A3AC7DD8DA2DB72BF69F24A086A3F5FC7F99AD31B3C", factionsV1.ContentHash);
        Assert.Equal("5CA3155732D7E39009C7E75F4693E6D507A712D6B671781CB13FBA1647141A75", factionsV2.ContentHash);
        Assert.Equal("858D347ED0CB9ADD8F937647703CD89F08F3AE313379037CFDD71B641F670F0C", restCreatureV1.ContentHash);
        Assert.Equal("AB0EC68C477CA95FB635A4E14A758F2922B16B08393C999C122A4E17E4CA4327", restCreatureV2.ContentHash);
        Assert.Equal("6D22CE1103C1E66FC163C8AEF2DF8FAB0C106D21C3EDA0D6A95D0984210B83A9", restWorldV1.ContentHash);
        Assert.Equal("F96FA03D1F68002295444132534946C91A60DB1F1CD182D4972D5A744AA88F7E", restWorldV2.ContentHash);
        Assert.Equal("4B1B67101E0CE06088AD997A3B8DAFED335EFEB3040851226E08D593A3EE19AD", restPolicyV1.ContentHash);
        Assert.Equal("0939F59817A4CED231A9F82E6F84986FFCB2FA20D7B780B23AE3DE2DFCBE0162", restPolicyV2.ContentHash);
        Assert.Equal("1097CC80B1D7866604305C830BB568D4E972B083C25EDCEDF8FFE06E183B32F9", campaignLocationVisitsV1.ContentHash);
        Assert.Equal("A59863150495454921D56301F9ADA44A6F9E8A2740A59A0754F28FF70390F555", campaignLocationVisitsV2.ContentHash);
        Assert.Equal("9417995A10D17B3EA16BBF0155C56BD2ACE9FC49FBF91B7B63384019EEA93215", worldCampaignDirectoryV1.ContentHash);
        Assert.Equal("6F43766F6B5EA5ACA21518EFAA61CC09D21589DD0E02725FB07E03C8CF5DD93C", worldCampaignDirectoryV2.ContentHash);
        foreach (var current in new[] { campaignV4, factionsV2, campaignLocationVisitsV2, worldCampaignDirectoryV2 })
        {
            Assert.True(current.ObjectContract!.IsFieldBased);
            Assert.Equal(RegisteredApplicationObjectContract.FieldBasedContractProfileId,
                current.ObjectContract.ProfileId);
            var metadata = Assert.Single(current.ObjectContract.Collections).Metadata!;
            Assert.Equal("/totalCount", metadata.TotalCount);
            Assert.Equal("/complete", metadata.Complete);
            Assert.Equal("/nextCursor", metadata.NextCursor);
            Assert.NotNull(current.ObjectContract.FieldProvenance);
        }
        foreach (var current in new[] { restCreatureV2, restWorldV2, restPolicyV2 })
        {
            Assert.Equal(2, current.Version);
            Assert.True(current.ObjectContract!.IsFieldBased);
            Assert.Equal(RegisteredApplicationObjectContract.FieldBasedContractProfileId,
                current.ObjectContract.ProfileId);
            Assert.NotNull(current.ObjectContract.FieldProvenance);
        }
        var restMetadata = Assert.Single(restWorldV2.ObjectContract!.Collections).Metadata!;
        Assert.Equal("/totalCount", restMetadata.TotalCount);
        Assert.Equal("/complete", restMetadata.Complete);
        Assert.Equal("/nextCursor", restMetadata.NextCursor);
        var restDiscovery = registry.Discover(restWorldV2.Reference)!;
        var restFields = restDiscovery.Fields.Where(field =>
            field.ObjectPointer.StartsWith("/rests/*/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(12, restFields.Length);
        Assert.Single(restFields, field => field.ObjectPointer == "/rests/*/policyEntityId");
        Assert.Contains(restFields, field => field.ObjectPointer == "/rests/*/sleepMinutes" && field.Required);
        Assert.Contains(restFields, field => field.ObjectPointer == "/rests/*/interruptionCount" && field.Required);
        Assert.DoesNotContain(restFields, field => field.ObjectPointer is "/rests/*/id" or "/rests/*/name");
        var factionDiscovery = registry.Discover(factionsV2.Reference)!;
        Assert.Contains(factionDiscovery.Sources, source =>
            source.Component.QualifiedTypeId == "game.core.world.faction");
        Assert.Contains(factionDiscovery.Fields, field =>
            field.ObjectPointer == "/items/*/summary"
            && field.Component.QualifiedTypeId == "game.core.world.faction"
            && field.ComponentPointer == "/summary"
            && field.Required);
        Assert.DoesNotContain(factionDiscovery.Fields, field =>
            field.ObjectPointer is "/items/*/id" or "/items/*/name" or "/items/*/members");
        var partyDiscovery = registry.Discover(campaignV4.Reference)!;
        Assert.Contains(partyDiscovery.Fields, field =>
            field.ObjectPointer == "/party/*/status"
            && field.Component.QualifiedTypeId == "game.core.campaign.character-participation"
            && !field.Required);
        foreach (var (file, expected) in new[]
        {
            ("dnd2024.query.campaign-location-visits.json", campaignLocationVisitsV2.ContentHash),
            ("dnd2024.query.world-campaign-directory.json", worldCampaignDirectoryV2.ContentHash)
        })
        {
            using var query = JsonDocument.Parse(File.ReadAllText(Path.Combine(Catalog(), "applications", "dnd2024",
                "queries", "campaign", file)));
            Assert.Equal(expected, query.RootElement.GetProperty("object").GetProperty("contentFingerprint").GetString());
        }
    }

    [Fact]
    public void Website_slice_ten_carrying_capacity_object_is_small_exact_and_read_only()
    {
        var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var application = ApplicationIdentifier.Parse("dnd2024");
        applications.Register(new(application, "D&D", "", []));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var abilities = types.Define(new(application, "dnd2024.creature.ability-scores",
            ComponentSchema("dnd2024.creature.ability-scores")));
        var body = types.Define(new(application, "dnd2024.creature.body",
            ComponentSchema("dnd2024.creature.body")));
        var retainedRequest = ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(Catalog(),
            "applications", "dnd2024", "objects", "character",
            "dnd2024.object.carrying-capacity-creature.v1.json")), application);
        var currentRequest = ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(Catalog(),
            "applications", "dnd2024", "objects", "character",
            "dnd2024.object.carrying-capacity-creature.json")), application);
        var registry = new SqliteProjectionDefinitionRegistry(db, types, schemas, applications);
        var retained = registry.Define(retainedRequest);
        var definition = registry.Define(currentRequest);

        Assert.Equal("234CF49317F99A4468828E77E06A954DCEED4640BEF4307A397CB8BFEE7529AF",
            retained.ContentHash);
        Assert.Equal(1, retained.Version);
        Assert.False(retained.ObjectContract!.IsFieldBased);
        Assert.Equal("828BC5D694102E129C870CA49291424ABFB79F8BDE17A169037CA26B9D5A3DC1", definition.ContentHash);
        Assert.Equal("dnd2024.object.carrying-capacity-creature", definition.QualifiedId);
        Assert.Equal(2, definition.Version);
        Assert.True(definition.ObjectContract!.IsFieldBased);
        Assert.Equal(RegisteredApplicationObjectContract.FieldBasedContractProfileId,
            definition.ObjectContract.ProfileId);
        Assert.NotNull(definition.ObjectContract.FieldProvenance);
        Assert.Equal([abilities.SchemaHash, body.SchemaHash],
            definition.ComponentInputs.Select(value => value.Type.SchemaHash).ToArray());
        Assert.Equal(["abilityScores", "body"],
            definition.ObjectContract!.Sources.Select(value => value.InputId).ToArray());
        Assert.All(definition.ObjectContract.Sources, value => Assert.True(value.Required));
        Assert.Empty(definition.ObjectContract.Relationships);
        Assert.Empty(definition.ObjectContract.References);
        Assert.Empty(definition.ObjectContract.Collections);
        Assert.Equal(["dm", "player"], definition.ObjectContract.Access.ReadPerspectives);
        Assert.Empty(definition.ObjectContract.Access.WritePerspectives);
        Assert.Null(definition.ObjectContract.Writes);
        Assert.Equal(4096, definition.ObjectContract.Limits.OutputBytes);
    }

    [Fact]
    public void Slice_ten_character_dossier_records_pin_the_imported_component_versions()
    {
        var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var application = ApplicationIdentifier.Parse("dnd2024");
        applications.Register(new(application, "D&D", "", []));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        RegisterPriorVersions(types, application, "dnd2024.character-creation-record", 2);
        var creation = types.Define(new(application, "dnd2024.character-creation-record", ComponentSchema("dnd2024.character-creation-record")));
        types.Define(new(application, "dnd2024.character.origin-selections", ComponentSchema("dnd2024.character.origin-selections")));
        RegisterPriorVersions(types, application, "dnd2024.character.feature-entitlements", 1);
        var features = types.Define(new(application, "dnd2024.character.feature-entitlements", ComponentSchema("dnd2024.character.feature-entitlements")));
        RegisterPriorVersions(types, application, "dnd2024.item.quantity", 1);
        var itemTypes = new[] { "dnd2024.item-definition", "dnd2024.activity.membership", "dnd2024.item-activity",
                     "dnd2024.core.definition-link", "dnd2024.item.quantity", "dnd2024.item.equipment" }
            .ToDictionary(componentId => componentId, componentId => types.Define(new(application, componentId,
                ComponentSchema(componentId))));
        var recordPaths = new[] { "dnd2024.object.inventory-item-activity-record.json",
            "dnd2024.object.inventory-item-recipe-record.json" };
        var recordRequests = recordPaths.Select(path => ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(Catalog(),
            "applications", "dnd2024", "objects", "item", path)), application)).ToArray();
        foreach (var type in recordRequests.SelectMany(value => value.ComponentInputs).Select(value => value.Type)
                     .DistinctBy(value => (value.QualifiedTypeId, value.TypeVersion)))
        {
            RegisterPriorVersions(types, application, type.QualifiedTypeId, type.TypeVersion - 1);
            Assert.Equal(type.SchemaHash, types.Define(new(application, type.QualifiedTypeId,
                ComponentSchema(type.QualifiedTypeId))).SchemaHash);
        }
        var registry = new SqliteProjectionDefinitionRegistry(db, types, schemas, applications);
        var retainedDossier = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(Catalog(),
            "applications", "dnd2024", "objects", "character", "dnd2024.object.character-dossier-records.v1.json")), application));
        var definition = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(Catalog(),
            "applications", "dnd2024", "objects", "character", "dnd2024.object.character-dossier-records.json")), application));
        Assert.Equal("115D425F1C8260EDCDE0C5FB8D12F798D82CA435120D1E77A9BA647A88ED9030", retainedDossier.ContentHash);
        Assert.Equal(1, retainedDossier.Version);
        Assert.False(retainedDossier.ObjectContract!.IsFieldBased);
        Assert.Equal("35A8A12BD202792DA4E7025788FE1D80CD983583EE5E5D919F71F7C9B3A8F2EB", definition.ContentHash);
        Assert.Equal(2, definition.Version);
        Assert.True(definition.ObjectContract!.IsFieldBased);
        Assert.NotNull(definition.ObjectContract.FieldProvenance);
        Assert.Equal(3, creation.Version);
        Assert.Equal(2, features.Version);
        Assert.Equal(3, definition.ComponentInputs.Single(value => value.InputId == "creation").Type.TypeVersion);
        Assert.Equal(2, definition.ComponentInputs.Single(value => value.InputId == "features").Type.TypeVersion);
        Assert.Empty(definition.ObjectContract!.Writes?.Capabilities ?? []);
        var retainedDefinition = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(Catalog(),
            "applications", "dnd2024", "objects", "item", "dnd2024.object.inventory-item-definition-records.v1.json")), application));
        var retainedInstance = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(Catalog(),
            "applications", "dnd2024", "objects", "item", "dnd2024.object.inventory-item-instance-records.v1.json")), application));
        Assert.Equal("5974D9AC98344F28D58F05E2065EA1DB5274893AA1D69D492F24DA0453E64A11", retainedDefinition.ContentHash);
        Assert.Equal("8005B25A8AEA43C76C10186369302CAED2B787AF1C38160C94186180253BB442", retainedInstance.ContentHash);
        var retainedDefinitionV2 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(Catalog(),
            "applications", "dnd2024", "objects", "item", "dnd2024.object.inventory-item-definition-records.v2.json")), application));
        var retainedInstanceV2 = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(Catalog(),
            "applications", "dnd2024", "objects", "item", "dnd2024.object.inventory-item-instance-records.v2.json")), application));
        Assert.Equal("4A8578B1A7010EB60AC64ABA7D64DD016106F19FF30A4D023A00CA35BF412476", retainedDefinitionV2.ContentHash);
        Assert.Equal("8F52017FA6BA33FB9699FB6755A1577B422B414AC55BF19401C27A803F4F15CC", retainedInstanceV2.ContentHash);
        Assert.False(retainedDefinitionV2.ObjectContract!.IsFieldBased);
        Assert.False(retainedInstanceV2.ObjectContract!.IsFieldBased);
        var itemDefinition = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(Catalog(),
            "applications", "dnd2024", "objects", "item", "dnd2024.object.inventory-item-definition-records.json")), application));
        Assert.Equal("9065B2C7FF9B616996FE782B8EACC170326D47779A33C1943F5DAC71E612BEC1", itemDefinition.ContentHash);
        Assert.Equal(3, itemDefinition.Version);
        Assert.True(itemDefinition.ObjectContract!.IsFieldBased);
        var itemRequest = ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(Catalog(),
            "applications", "dnd2024", "objects", "item", "dnd2024.object.inventory-item-instance-records.json")), application);
        foreach (var input in itemRequest.ComponentInputs)
            Assert.Equal(itemTypes[input.Type.QualifiedTypeId].SchemaHash, input.Type.SchemaHash);
        var itemInstance = registry.Define(itemRequest);
        Assert.Equal("74F012F1136D31415673D52666E85D7400C8E570A2A1B64AA0BA83ED5D8D348D", itemInstance.ContentHash);
        Assert.Equal(3, itemInstance.Version);
        Assert.True(itemInstance.ObjectContract!.IsFieldBased);
        Assert.Equal("definition", Assert.Single(itemInstance.ObjectContract.References).InputId);
        var reference = Assert.Single(itemInstance.DependencyInputs).Projection;
        Assert.Equal(3, reference.Version);
        Assert.Equal(itemDefinition.ContentHash, reference.ContentHash);
        var retainedActivity = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(Catalog(),
            "applications", "dnd2024", "objects", "item", "dnd2024.object.inventory-item-activity-record.v1.json")), application));
        var activity = registry.Define(recordRequests.Single(value => value.QualifiedId ==
            "dnd2024.object.inventory-item-activity-record"));
        var retainedRecipe = registry.Define(ApplicationObjectDocument.Parse(File.ReadAllText(Path.Combine(Catalog(),
            "applications", "dnd2024", "objects", "item", "dnd2024.object.inventory-item-recipe-record.v1.json")), application));
        var recipe = registry.Define(recordRequests.Single(value => value.QualifiedId ==
            "dnd2024.object.inventory-item-recipe-record"));
        Assert.Equal("523F276015DE95BB16508C202685101DDC3D9F5F11B99C0EC0A48588B6669D19", retainedActivity.ContentHash);
        Assert.Equal("534018C4A8C2D1A06F9307C89C8F32BCBE08B0FCA7B0CC9809EB5BA51B39DC4F", activity.ContentHash);
        Assert.Equal("530A2622F6C88B5E283F92ED2CB431CC7C6C96AB76E63E6AF895797DB8415127", retainedRecipe.ContentHash);
        Assert.Equal("D7F2A4003A65C9E122C1A90EB06E7C97D415D3C060E8C011FF3C4B2A092B3C50", recipe.ContentHash);
        Assert.False(retainedActivity.ObjectContract!.IsFieldBased);
        Assert.True(activity.ObjectContract!.IsFieldBased);
        Assert.False(retainedRecipe.ObjectContract!.IsFieldBased);
        Assert.True(recipe.ObjectContract!.IsFieldBased);
    }

    private static void RegisterPriorVersions(
        SqliteComponentTypeRegistry types,
        ApplicationIdentifier application,
        string componentId,
        int count)
    {
        for (var version = 1; version <= count; version++)
            types.Define(new(application, componentId,
                $$"""{"type":"object","title":"retained-prior-v{{version}}"}"""));
    }

    private static string ComponentSchema(string id) => File.ReadAllText(Path.Combine(
        Catalog(), "applications", "dnd2024", "components", id + ".schema.json"));

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
    public void Field_based_parser_uses_the_host_transport_and_requires_an_explicit_closed_profile()
    {
        var setup = Setup("field-contract");
        var type = setup.Types.Define(new(setup.Application, "field-contract.name",
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}"));
        var json = """
        {
          "id":"field-contract.summary","version":1,"profile":"application-object/v2",
          "roles":{"subject":{"required":true}},
          "sources":[{"id":"identity","role":"subject","component":{"qualifiedId":"TYPE_ID","version":TYPE_VERSION,"schemaHash":"TYPE_HASH"},"required":false}],
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
        var read = setup.Registry.Get(registered.QualifiedId, registered.Version)!;

        Assert.Equal(RegisteredApplicationObjectContract.TransportSchemaJson, parsed.OutputSchemaJson);
        Assert.Equal(RegisteredApplicationObjectContract.TransportSchemaJson, read.OutputSchemaJson);
        Assert.Equal(RegisteredApplicationObjectContract.TransportSchemaHash, read.OutputSchemaHash);
        Assert.True(read.ObjectContract!.IsFieldBased);
        Assert.Equal(RegisteredApplicationObjectContract.FieldBasedContractProfileId,
            read.ObjectContract.ProfileId);
        Assert.Throws<ArgumentException>(() => ApplicationObjectDocument.Parse(
            json.Replace("\"profile\":\"application-object/v2\",", "", StringComparison.Ordinal), setup.Application));
        Assert.Throws<ArgumentException>(() => ApplicationObjectDocument.Parse(
            json.Replace("\"roles\":", "\"schema\":{\"type\":\"object\"},\"roles\":", StringComparison.Ordinal),
            setup.Application));
        Assert.Throws<ArgumentException>(() => ApplicationObjectDocument.Parse(
            json.Replace("application-object/v2", "application-object/v3", StringComparison.Ordinal), setup.Application));
    }

    [Fact]
    public void Field_based_registration_requires_disjoint_explicit_metadata_and_object_root_mappings()
    {
        var setup = Setup("field-safety");
        var type = setup.Types.Define(new(setup.Application, "field-safety.name",
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}"));
        var legacy = ValidRequest(setup, Ref(type));
        var collection = Assert.Single(legacy.ObjectContract!.Collections) with
        {
            Order = [new("/name", "asc")],
            Metadata = new("/page/totalCount", "/page/complete", "/page/nextCursor")
        };
        var fieldBased = legacy with
        {
            OutputSchemaJson = RegisteredApplicationObjectContract.TransportSchemaJson,
            ObjectContract = legacy.ObjectContract with
            {
                ProfileId = RegisteredApplicationObjectContract.FieldBasedContractProfileId,
                Collections = [collection]
            }
        };

        var registered = setup.Registry.Define(fieldBased);
        Assert.True(registered.ObjectContract!.IsFieldBased);
        Assert.Equal("/page/complete", Assert.Single(registered.ObjectContract.Collections).Metadata!.Complete);
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(fieldBased with
        {
            QualifiedId = "field-safety.missing-metadata",
            ObjectContract = fieldBased.ObjectContract! with
            {
                Collections = [collection with { Metadata = null }]
            }
        }));
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(fieldBased with
        {
            QualifiedId = "field-safety.overlapping-metadata",
            ObjectContract = fieldBased.ObjectContract! with
            {
                Collections = [collection with
                {
                    Metadata = new("/name", "/page/complete", "/page/nextCursor")
                }]
            }
        }));
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(fieldBased with
        {
            QualifiedId = "field-safety.unsafe-target",
            Mappings = [new("identity", "/name", "/__proto__")]
        }));
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(fieldBased with
        {
            QualifiedId = "field-safety.unproven-order",
            ObjectContract = fieldBased.ObjectContract! with
            {
                Collections = [collection with { Order = [new("/missing", "asc")] }]
            }
        }));

        var secondVersion = setup.Types.Define(new(setup.Application, type.QualifiedId,
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"note\":{\"type\":\"string\"}}}"));
        var parent = Assert.Single(fieldBased.ObjectContract!.Relationships);
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(fieldBased with
        {
            QualifiedId = "field-safety.duplicate-endpoint-type",
            ObjectContract = fieldBased.ObjectContract! with
            {
                Relationships = [parent with
                {
                    RequiredEndpointComponents = [new("to", Ref(type)), new("to", Ref(secondVersion))]
                }]
            }
        }));

        var scalarRoot = new ProjectionDefinitionRequest(setup.Application, "field-safety.scalar-root",
            RegisteredApplicationObjectContract.TransportSchemaJson, [new("identity", "subject", Ref(type))], [],
            [new("identity", "/name", "")],
            new ApplicationObjectContractRequest([new("subject", true)], [new("identity", true)], [], [], [],
                new(1, 10, 4096, 1), new(["player"], []), null)
            { ProfileId = RegisteredApplicationObjectContract.FieldBasedContractProfileId }, 1);
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(scalarRoot));
    }

    [Fact]
    public void Field_based_collection_provenance_accepts_only_bounded_closed_object_alternatives()
    {
        var setup = Setup("field-alternatives");
        var root = setup.Types.Define(new(setup.Application, "field-alternatives.root",
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"name\":{\"type\":\"string\"}}}"));
        var oneOf = setup.Types.Define(new(setup.Application, "field-alternatives.one-of",
            "{\"oneOf\":[{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"shared\":{\"type\":\"string\"},\"shortOnly\":{\"type\":\"integer\"}}},{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"shared\":{\"type\":\"string\"},\"longOnly\":{\"type\":\"integer\"}}}]}"));
        var anyOf = setup.Types.Define(new(setup.Application, "field-alternatives.any-of",
            "{\"anyOf\":[{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"left\":{\"type\":\"string\"}}},{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"right\":{\"type\":\"string\"}}}]}"));

        var first = setup.Registry.Define(CollectionRequest("field-alternatives.one", oneOf));
        var firstFields = setup.Registry.Discover(first.Reference)!.Fields
            .Where(field => field.ObjectPointer.StartsWith("/members/*/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, firstFields.Length);
        Assert.Single(firstFields, field => field.ObjectPointer == "/members/*/shared");
        // Required describes the endpoint component's presence, not whether every union branch requires the field.
        Assert.Contains(firstFields, field => field.ObjectPointer == "/members/*/shortOnly" && field.Required);
        Assert.Contains(firstFields, field => field.ObjectPointer == "/members/*/longOnly" && field.Required);

        var second = setup.Registry.Define(CollectionRequest("field-alternatives.any", anyOf));
        var secondFields = setup.Registry.Discover(second.Reference)!.Fields;
        Assert.Contains(secondFields, field => field.ObjectPointer == "/members/*/left");
        Assert.Contains(secondFields, field => field.ObjectPointer == "/members/*/right");

        var open = setup.Types.Define(new(setup.Application, "field-alternatives.open",
            "{\"oneOf\":[{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"closed\":{\"type\":\"string\"}}},{\"type\":\"object\",\"additionalProperties\":true,\"properties\":{\"open\":{\"type\":\"string\"}}}]}"));
        var nonObject = setup.Types.Define(new(setup.Application, "field-alternatives.scalar",
            "{\"oneOf\":[{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"value\":{\"type\":\"string\"}}},{\"type\":\"string\"}]}"));
        var composed = setup.Types.Define(new(setup.Application, "field-alternatives.composed",
            "{\"oneOf\":[{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"value\":{\"type\":\"string\"}},\"allOf\":[{\"type\":\"object\"}]}]}"));
        Assert.Throws<ArgumentException>(() => setup.Types.Define(new(setup.Application,
            "field-alternatives.recursive",
            "{\"$defs\":{\"loop\":{\"$ref\":\"#/$defs/loop\"}},\"oneOf\":[{\"$ref\":\"#/$defs/loop\"}]}")));
        // The bounded component-schema profile rejects patternProperties before provenance registration.
        Assert.Throws<ArgumentException>(() => setup.Types.Define(new(setup.Application,
            "field-alternatives.patterned",
            "{\"oneOf\":[{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"known\":{\"type\":\"string\"}},\"patternProperties\":{\"^x-\":{\"type\":\"string\"}}}]}")));
        foreach (var invalid in new[] { open, nonObject, composed })
            Assert.Throws<ArgumentException>(() => setup.Registry.Define(
                CollectionRequest("field-alternatives.invalid-" + invalid.QualifiedId.Split('.').Last(), invalid)));

        var colliding = setup.Types.Define(new(setup.Application, "field-alternatives.colliding",
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"shared\":{\"type\":\"string\"}}}"));
        var collision = CollectionRequest("field-alternatives.collision", oneOf);
        var relationship = Assert.Single(collision.ObjectContract!.Relationships);
        Assert.Throws<ArgumentException>(() => setup.Registry.Define(collision with
        {
            ObjectContract = collision.ObjectContract with
            {
                Relationships = [relationship with
                {
                    RequiredEndpointComponents = [.. relationship.RequiredEndpointComponents,
                        new("to", Ref(colliding))]
                }]
            }
        }));

        ProjectionDefinitionRequest CollectionRequest(string id, RegisteredComponentTypeVersion endpoint)
        {
            var legacy = ValidRequest(setup, Ref(root));
            var relationship = Assert.Single(legacy.ObjectContract!.Relationships);
            var collection = Assert.Single(legacy.ObjectContract.Collections) with
            {
                Order = [new("/id", "asc")],
                Metadata = new("/totalCount", "/complete", "/nextCursor")
            };
            return legacy with
            {
                QualifiedId = id,
                OutputSchemaJson = RegisteredApplicationObjectContract.TransportSchemaJson,
                ObjectContract = legacy.ObjectContract with
                {
                    ProfileId = RegisteredApplicationObjectContract.FieldBasedContractProfileId,
                    Relationships = [relationship with
                    {
                        RequiredEndpointComponents = [.. relationship.RequiredEndpointComponents,
                            new("to", Ref(endpoint))]
                    }],
                    Collections = [collection]
                }
            };
        }
    }

    [Fact]
    public void Field_based_dependency_paths_resolve_bounded_leaf_component_provenance()
    {
        var setup = Setup("field-provenance");
        var type = setup.Types.Define(new(setup.Application, "field-provenance.identity",
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"profile\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"name\":{\"type\":\"string\"}}}}}"));
        var child = setup.Registry.Define(new ProjectionDefinitionRequest(setup.Application,
            "field-provenance.child", RegisteredApplicationObjectContract.TransportSchemaJson,
            [new("identity", "subject", Ref(type))], [],
            [new("identity", "/profile", "/profile")],
            FieldContract([new("subject", true)], [new("identity", true)], []), 1));
        var parent = setup.Registry.Define(new ProjectionDefinitionRequest(setup.Application,
            "field-provenance.parent", RegisteredApplicationObjectContract.TransportSchemaJson, [],
            [new("details", child.Reference, new Dictionary<string, string> { ["subject"] = "actor" })],
            [new("details", "/profile/name", "/displayName")],
            FieldContract([new("actor", true)], [], [new("details", true)]), 1));

        var field = Assert.Single(parent.ObjectContract!.FieldProvenance!);
        Assert.Equal("/displayName", field.ObjectPointer);
        Assert.Equal(["details", "identity"], field.InputPath);
        Assert.Equal("actor", field.EntityRole);
        Assert.Equal(Ref(type), field.Component);
        Assert.Equal("/profile/name", field.ComponentPointer);
        Assert.True(field.Required);

        var discovery = setup.Registry.Discover(parent.Reference)!;
        var source = Assert.Single(discovery.Sources);
        Assert.Equal("field-provenance.identity", source.Component.QualifiedTypeId);
        Assert.Equal(type.SchemaJson, source.SchemaJson);
        Assert.Equal("source-1", source.SourceId);
        Assert.Null(setup.Registry.Discover(parent.Reference with { ContentHash = new string('A', 64) }));

        Assert.Throws<ArgumentException>(() => setup.Registry.Define(new ProjectionDefinitionRequest(
            setup.Application, "field-provenance.invalid", RegisteredApplicationObjectContract.TransportSchemaJson,
            [], [new("details", child.Reference, new Dictionary<string, string> { ["subject"] = "actor" })],
            [new("details", "/profile/missing", "/displayName")],
            FieldContract([new("actor", true)], [], [new("details", true)]), 1)));

        var objectRoot = setup.Registry.Define(new ProjectionDefinitionRequest(setup.Application,
            "field-provenance.object-root", RegisteredApplicationObjectContract.TransportSchemaJson, [],
            [new("details", child.Reference, new Dictionary<string, string> { ["subject"] = "actor" })],
            [new("details", "/profile", "")],
            FieldContract([new("actor", true)], [], [new("details", true)]), 1));
        var rootField = Assert.Single(objectRoot.ObjectContract!.FieldProvenance!);
        Assert.Equal("", rootField.ObjectPointer);
        Assert.Equal("/profile", rootField.ComponentPointer);
    }

    [Fact]
    public void Authorized_read_discovery_reports_actual_schema_without_entity_identity_or_declared_fallback()
    {
        var setup = Setup("actual-provenance");
        var declared = setup.Types.Define(new(setup.Application, "actual-provenance.identity",
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}"));
        var actual = setup.Types.Define(new(setup.Application, declared.QualifiedId,
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"note\":{\"type\":\"string\"}}}"));
        var definition = setup.Registry.Define(new ProjectionDefinitionRequest(setup.Application,
            "actual-provenance.object", RegisteredApplicationObjectContract.TransportSchemaJson,
            [new("identity", "subject", Ref(declared))], [], [new("identity", "/name", "/name")],
            FieldContract([new("subject", true)], [new("identity", true)], []), 1));
        var observation = new ProjectionObservedSource(["identity"], "subject", true,
            Ref(declared), Ref(actual), "available");

        var evidence = setup.Registry.DiscoverRead(definition.Reference, [observation],
            [new(definition.Reference, "identity", "/name", "/name", "value")],
            "{\"name\":\"Visible\"}")!;

        Assert.Equal("available", evidence.Availability);
        var source = Assert.Single(evidence.Sources);
        Assert.Equal(Ref(declared), source.DeclaredComponent);
        Assert.Equal(Ref(actual), Assert.Single(source.ActualSources).Component);
        Assert.Equal(actual.SchemaJson, source.ActualSources[0].SchemaJson);
        Assert.Equal(["value"], evidence.Fields.Single(value => value.ObjectPointer == "/name").Availability);
        using (var literal = JsonDocument.Parse("{\"*\":\"Literal\"}"))
            Assert.Equal(["value"], SqliteProjectionDefinitionRegistry.ReadAvailability(literal.RootElement, "/*"));
        Assert.DoesNotContain("EntityId", JsonSerializer.Serialize(evidence), StringComparison.Ordinal);
        Assert.Equal("budget-exceeded", Assert.IsType<ApplicationObjectReadEvidence>(
            setup.Registry.DiscoverRead(definition.Reference,
                Enumerable.Repeat(observation, 513).ToArray(), [], "{}")).Availability);
        Assert.Throws<InvalidOperationException>(() => setup.Registry.DiscoverRead(definition.Reference,
            [observation with { ActualComponent = new("other.identity", 1, actual.SchemaHash) }], [], "{}"));
    }

    [Fact]
    public void Collection_discovery_does_not_attribute_an_escaped_relationship_owned_field_to_a_component()
    {
        var setup = Setup("escaped-provenance");
        var root = setup.Types.Define(new(setup.Application, "escaped-provenance.root",
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"title\":{\"type\":\"string\"}}}"));
        var item = setup.Types.Define(new(setup.Application, "escaped-provenance.item",
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"a/b\":{\"type\":\"string\"},\"shown\":{\"type\":\"string\"}}}"));
        var parent = new ApplicationObjectRelationship("items", "escaped-provenance.relationship.items",
            "root", "item", "many", "/items", [new("to", Ref(item))], []);
        var nested = new ApplicationObjectRelationship("nested", "escaped-provenance.relationship.nested",
            "item", "root", "many", "/items/*/a~1b", [], []);
        var collection = new ApplicationObjectCollection("items", "items", 10, 10,
            [new("/id", "asc")], "source-revision-bound")
        { Metadata = new("/totalCount", "/complete", "/nextCursor") };
        var request = new ProjectionDefinitionRequest(setup.Application, "escaped-provenance.object",
            RegisteredApplicationObjectContract.TransportSchemaJson,
            [new("root", "root", Ref(root))], [], [new("root", "/title", "/title")],
            new ApplicationObjectContractRequest([new("root", true), new("item", false)],
                [new("root", true)], [parent, nested], [], [collection],
                new(2, 100, 65_536, 8), new(["dm"], []), null)
            { ProfileId = RegisteredApplicationObjectContract.FieldBasedContractProfileId }, 1);

        var registered = setup.Registry.Define(request);
        var discovery = setup.Registry.Discover(registered.Reference)!;

        Assert.DoesNotContain(discovery.Fields, field => field.ObjectPointer == "/items/*/a~1b");
        Assert.Contains(discovery.Sources, source => source.Component.QualifiedTypeId == item.QualifiedId);
        var read = setup.Registry.DiscoverRead(registered.Reference, [], [],
            "{\"title\":\"Empty\",\"items\":[],\"totalCount\":0,\"complete\":true,\"nextCursor\":null}")!;
        var collectionSource = read.Sources.Single(source => source.InputPath.Count > 0
            && source.InputPath[0] == "collection");
        Assert.Equal("unobserved", collectionSource.Availability);
        Assert.Empty(collectionSource.ActualSources);
        Assert.Equal(["unobserved"], read.Fields.Single(field => field.ObjectPointer == "/items/*/shown").Availability);
    }

    [Fact]
    public void Object_query_round_trips_through_existing_discovery_contract()
    {
        var app = ApplicationIdentifier.Parse("query-object");
        var json = """
        {"id":"query-object.query.members","category":"world.members","name":"Members","description":"Lists members.","matches":["list members"],"roles":{"campaign":"The owning campaign.","member":"A listed entity."},"executor":"object-projection","campaignSelection":{"queryId":"query-object.query.selection","entityIdField":"campaignId"},"object":{"qualifiedId":"query-object.summary","version":1,"contentFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"},"collection":"members","outputSchema":{"type":"object","additionalProperties":false,"properties":{"members":{"type":"array","items":{"type":"string"}}}},"exposure":"model-visible","status":"active"}
        """;

        var parsed = ApplicationQueryContract.Parse(json, app);
        var canonical = ApplicationCatalogRecordContent.QueryJson(parsed);
        var read = ApplicationQueryContract.Parse(canonical, app);

        Assert.True(read.IsObjectProjection);
        Assert.Equal("query-object.summary", read.ProjectionQualifiedId);
        Assert.Equal("members", read.ObjectCollectionId);
        Assert.Equal(new string('A', 64), read.ProjectionContentHash);
        Assert.Equal("query-object.query.selection", read.CampaignSelection!.QueryId);
        Assert.Equal("campaignId", read.CampaignSelection.EntityIdField);
        Assert.DoesNotContain("outputSchemaHash", canonical, StringComparison.Ordinal);
        Assert.Contains("contentFingerprint", canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void Field_based_object_query_omits_authored_schema_and_round_trips_the_transport_contract()
    {
        var app = ApplicationIdentifier.Parse("query-field");
        var json = """
        {"id":"query-field.query.members","category":"world.members","name":"Members","description":"Lists members.","matches":["list members"],"roles":{"campaign":"The owning campaign.","member":"A listed entity."},"executor":"object-projection","profile":"application-object/v2","object":{"qualifiedId":"query-field.summary","version":1,"contentFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"},"collection":"members","exposure":"model-visible","status":"active"}
        """;

        var parsed = ApplicationQueryContract.Parse(json, app);
        var canonical = ApplicationCatalogRecordContent.QueryJson(parsed);
        var read = ApplicationQueryContract.Parse(canonical, app);

        Assert.True(read.IsFieldBasedObject);
        Assert.Equal(RegisteredApplicationObjectContract.TransportSchemaJson, read.OutputSchemaJson);
        Assert.Equal(RegisteredApplicationObjectContract.TransportSchemaHash, read.OutputSchemaHash);
        Assert.Contains("\"profile\":\"application-object/v2\"", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("outputSchema", canonical, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ApplicationQueryContract.Parse(
            json.Replace("\"profile\":\"application-object/v2\",", "", StringComparison.Ordinal), app));
        Assert.Throws<ArgumentException>(() => ApplicationQueryContract.Parse(
            json.Replace("\"object\":", "\"outputSchema\":{\"type\":\"object\"},\"object\":", StringComparison.Ordinal), app));
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

    private static ApplicationObjectContractRequest FieldContract(
        IReadOnlyList<ApplicationObjectRole> roles,
        IReadOnlyList<ApplicationObjectSource> sources,
        IReadOnlyList<ApplicationObjectReference> references) =>
        new(roles, sources, [], references, [], new(4, 100, 65_536, 8),
            new(["player", "dm"], []), null)
        { ProfileId = RegisteredApplicationObjectContract.FieldBasedContractProfileId };

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
