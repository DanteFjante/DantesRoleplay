using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class CaldrisStartingAreaContentPackageTests
{
    private static readonly IReadOnlyDictionary<string, (int Revision, string Sha256)> PreviousMaps =
        new Dictionary<string, (int, string)>(StringComparer.Ordinal)
        {
            ["location.caldris.atlas"] = (4, "3ef332cb5963a0081e3afcb9f045d4582e7f95507cdc6552e25e619dfc9102f1"),
            ["location.caldris.eredane"] = (4, "96f6a778bfe921a6637b5bd4cd3f084b343bdaac807854f583101710995307f4"),
            ["location.caldris.alderwick"] = (5, "f118c3091f212e9f2a8c2c26bf981e4d2b99009af0415d02e1e396195bc008bd"),
            ["location.caldris.atlas.bramble-country"] = (2, "fb9f3ccf2de4f999e871e11fb0ea46ae3bb7c298390167144224c5035815cb9c"),
            ["location.caldris.bramblebridge"] = (4, "dd77019c73bf9b5cd8360db5d3b1c9331126242f4e15f7367c5f278bb8580fda"),
            ["location.caldris.gilded-kettle"] = (1, "1b01a8f3334237ed69dfd48f6ac7327efc6234c087b3b9c4601e9bd795769c00")
        };

    [Fact]
    public void Retained_assets_have_exact_content_addresses_dimensions_and_recovery_pins()
    {
        var directory = PackageDirectory();
        using var manifest = Read(Path.Combine(directory, "asset-import-manifest.json"));
        var root = manifest.RootElement;
        Assert.Equal("dnd2024.retained-media-import/1", root.GetProperty("format").GetString());
        Assert.False(root.GetProperty("authority").GetProperty("liveMutationPerformed").GetBoolean());
        Assert.Equal(0, root.GetProperty("authority").GetProperty("playerKnowledgeRecordCount").GetInt32());

        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        Assert.Equal(11, assets.Length);
        Assert.Equal(8, assets.Count(value => value.GetProperty("kind").GetString() == "map"));
        Assert.Equal(3, assets.Count(value => value.GetProperty("kind").GetString() == "scene"));
        Assert.Equal(11, assets.Select(value => value.GetProperty("logicalAssetKey").GetString()).Distinct().Count());

        var atlas = Assert.Single(assets, value => value.GetProperty("ownerLocationId").GetString()
            == "location.caldris.atlas");
        Assert.Equal("caldris.map.atlas.clean.v2", atlas.GetProperty("logicalAssetKey").GetString());
        Assert.Equal("142d6050d58e17590d986f7f2cde1a4d83fad0e89e6c1b795cad5d87f9220be4",
            atlas.GetProperty("sha256").GetString());
        var supersededAtlas = atlas.GetProperty("supersedesPackageAsset");
        var supersededAtlasPath = Path.Combine(directory, supersededAtlas.GetProperty("relativePath").GetString()!);
        Assert.Equal("f2a9aeb6b3d38b494c507e5373a659837f5c633e278824d4e2d67d05fd5a5b0d",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(supersededAtlasPath))).ToLowerInvariant());

        foreach (var asset in assets)
        {
            var bytes = File.ReadAllBytes(Path.Combine(directory, asset.GetProperty("relativePath").GetString()!));
            Assert.Equal(asset.GetProperty("length").GetInt64(), bytes.LongLength);
            Assert.Equal(asset.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
            Assert.Equal(asset.GetProperty("width").GetInt32(), BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
            Assert.Equal(asset.GetProperty("height").GetInt32(), BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
            Assert.Equal("image/png", asset.GetProperty("mimeType").GetString());
        }

        foreach (var expected in PreviousMaps)
        {
            var map = Assert.Single(assets, value => value.GetProperty("kind").GetString() == "map"
                && value.GetProperty("ownerLocationId").GetString() == expected.Key);
            var previous = map.GetProperty("previousActiveMap");
            Assert.Equal(expected.Value.Revision, previous.GetProperty("componentRevision").GetInt32());
            Assert.Equal(expected.Value.Sha256, previous.GetProperty("playerSha256").GetString());
            Assert.Equal(expected.Value.Sha256, previous.GetProperty("dmSha256").GetString());
            Assert.Equal(expected.Value.Sha256, previous.GetProperty("localPreservationSha256").GetString());
        }

        var nearby = new[]
        {
            "caldris.map.gilded-kettle.clean.v1",
            "caldris.map.weir-bridge.v1",
            "caldris.map.mallow-abbey.v1"
        };
        Assert.All(nearby, key => Assert.Contains(assets,
            value => value.GetProperty("logicalAssetKey").GetString() == key));
        Assert.All(assets.Where(value => value.GetProperty("kind").GetString() == "scene"),
            value => Assert.Equal("guarded-location-gallery-append",
                value.GetProperty("activationDisposition").GetString()));
    }

    [Fact]
    public void Map_zoom_chain_records_exact_parent_anchors_and_identifiable_geography()
    {
        using var manifest = Read(Path.Combine(PackageDirectory(), "asset-import-manifest.json"));
        var review = manifest.RootElement.GetProperty("geographicContinuityReview");
        Assert.Contains("independent north-up detail frames", review.GetProperty("scopeBoundary").GetString());
        var chain = review.GetProperty("anchorChain").EnumerateArray().ToArray();
        Assert.Equal(7, chain.Length);

        var mapAssets = manifest.RootElement.GetProperty("assets").EnumerateArray()
            .Where(value => value.GetProperty("kind").GetString() == "map")
            .ToDictionary(value => value.GetProperty("ownerLocationId").GetString()!, StringComparer.Ordinal);
        foreach (var link in chain)
        {
            var child = link.GetProperty("child").GetString()!;
            var asset = mapAssets[child];
            Assert.Equal(link.GetProperty("parent").GetString(), asset.GetProperty("parentLocationId").GetString());
            Assert.Equal(link.GetProperty("x").GetInt32(), asset.GetProperty("mapAnchor").GetProperty("x").GetInt32());
            Assert.Equal(link.GetProperty("y").GetInt32(), asset.GetProperty("mapAnchor").GetProperty("y").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(link.GetProperty("evidence").GetString()));
        }

        var atlas = mapAssets["location.caldris.atlas"];
        var continuity = atlas.GetProperty("geographicContinuity");
        Assert.Equal("caldris.map.eredane.clean.v1", continuity.GetProperty("referenceAssetKey").GetString());
        Assert.Equal(4, continuity.GetProperty("reviewedCorrespondences").GetArrayLength());
    }

    [Fact]
    public void Overlay_payloads_use_direct_parent_frames_and_create_only_new_child_locations()
    {
        using var manifest = Read(Path.Combine(PackageDirectory(), "asset-import-manifest.json"));
        var contract = manifest.RootElement.GetProperty("coordinateContract");
        Assert.Equal("top-left", contract.GetProperty("origin").GetString());
        Assert.Equal("north-up", contract.GetProperty("orientation").GetString());

        var locations = manifest.RootElement.GetProperty("locationCreates").EnumerateArray().ToArray();
        Assert.Equal(14, locations.Length);
        Assert.Equal(14, locations.Select(value => value.GetProperty("id").GetString()).Distinct().Count());
        Assert.DoesNotContain(locations, value => PreviousMaps.ContainsKey(value.GetProperty("id").GetString()!));

        var coordinates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var location in locations)
        {
            var parent = location.GetProperty("container").GetProperty("id").GetString()!;
            Assert.Equal("location", location.GetProperty("container").GetProperty("slot").GetString());
            var components = location.GetProperty("components");
            var definition = components.GetProperty("game.core.world.location");
            Assert.Equal("site", definition.GetProperty("kind").GetString());
            Assert.Equal("active", definition.GetProperty("status").GetString());
            Assert.Equal("public", definition.GetProperty("visibility").GetString());
            var anchor = components.GetProperty("game.core.world.map.anchor");
            var x = anchor.GetProperty("x").GetInt32();
            var y = anchor.GetProperty("y").GetInt32();
            Assert.InRange(x, 0, 1000);
            Assert.InRange(y, 0, 1000);
            Assert.True(coordinates.Add($"{parent}:{x}:{y}"));
        }

        Assert.Equal(5, locations.Count(value => value.GetProperty("container").GetProperty("id").GetString()
            == "location.caldris.gilded-kettle"));
        Assert.Equal(4, locations.Count(value => value.GetProperty("container").GetProperty("id").GetString()
            == "location.caldris.atlas.weir-bridge"));
        Assert.Equal(5, locations.Count(value => value.GetProperty("container").GetProperty("id").GetString()
            == "location.caldris.bramblebridge.mallow-abbey"));
    }

    [Fact]
    public void Prepared_situation_is_unplayed_and_keeps_truth_clues_and_player_knowledge_separate()
    {
        var directory = PackageDirectory();
        using var situation = Read(Path.Combine(directory, "prepared-situation.json"));
        var root = situation.RootElement;
        Assert.Equal("future-ready-not-played", root.GetProperty("status").GetString());
        Assert.False(root.GetProperty("opening").GetProperty("hasOccurred").GetBoolean());
        var truths = root.GetProperty("dmTruthReferences").EnumerateArray()
            .Select(value => value.GetString()!).ToArray();
        Assert.Equal(["secret.caldris.quest.q01.the-thirteenth-bell"], truths);

        var reused = root.GetProperty("reusedClueReferences").EnumerateArray().ToArray();
        Assert.Equal(3, reused.Length);
        Assert.Equal(new[]
        {
            "clue.caldris.q01.barge-timing",
            "clue.caldris.q01.dry-flour",
            "clue.caldris.q01.theatre-fibres"
        }, reused.Select(value => value.GetProperty("id").GetString()!).Order(StringComparer.Ordinal).ToArray());
        Assert.All(reused, value =>
        {
            Assert.Equal("reuse-existing-record-and-relationships",
                value.GetProperty("disposition").GetString());
            Assert.Equal("party-knowledge-dm-page-0.json", value.GetProperty("sourceEvidenceFile").GetString());
            Assert.Equal("717BE58BCC02FE2A2F2727D3227E43C45086E2AFE93577F4B1A4695F9CB9A45B",
                value.GetProperty("sourceStateSpaceFingerprint").GetString());
            Assert.Equal("BC07473896F1395ACD7F9BC2C5B9A5A3E23BB78BE1A90AE359F11D9950E7FAF5",
                value.GetProperty("projectionDocumentRevision").GetString());
            Assert.Equal("location.caldris.bramblebridge", value.GetProperty("subjectId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(value.GetProperty("title").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(value.GetProperty("summary").GetString()));
        });

        var clues = root.GetProperty("clueCreates").EnumerateArray().ToArray();
        Assert.Equal(2, clues.Length);
        Assert.Equal(new[]
        {
            "clue.caldris.thirteenth-bell.closed-alley-map",
            "clue.caldris.thirteenth-bell.different-driver-whistle"
        }, clues.Select(value => value.GetProperty("id").GetString()!).Order(StringComparer.Ordinal).ToArray());
        foreach (var clue in clues)
        {
            Assert.Equal("location.caldris.atlas", clue.GetProperty("container").GetProperty("id").GetString());
            Assert.Equal("opening-clues", clue.GetProperty("container").GetProperty("slot").GetString());
            var value = clue.GetProperty("components").GetProperty("game.core.world.clue");
            Assert.Equal("unrevealed", value.GetProperty("status").GetString());
            Assert.Equal("gm", value.GetProperty("visibility").GetString());
            Assert.Equal("confidential", clue.GetProperty("components")
                .GetProperty("game.core.world.knowledge.classification").GetProperty("sensitivity").GetString());
        }

        var relationships = root.GetProperty("relationshipCreates").EnumerateArray().ToArray();
        Assert.Equal(8, relationships.Length);
        Assert.All(relationships, relationship =>
        {
            Assert.DoesNotContain("campaign.caldris.", relationship.GetProperty("from").GetString()!);
            Assert.DoesNotContain("campaign.caldris.", relationship.GetProperty("to").GetString()!);
        });
        Assert.All(clues, clue =>
        {
            var support = Assert.Single(relationships,
                relationship => relationship.GetProperty("from").GetString() == clue.GetProperty("id").GetString()
                    && relationship.GetProperty("kind").GetString() == "game.core.world.clue.supports");
            Assert.Equal("secret.caldris.quest.q01.the-thirteenth-bell",
                support.GetProperty("to").GetString());
        });
        Assert.All(reused, reference => Assert.DoesNotContain(relationships, relationship =>
            relationship.GetProperty("from").GetString() == reference.GetProperty("id").GetString()
                || relationship.GetProperty("to").GetString() == reference.GetProperty("id").GetString()));
        var serialized = root.GetRawText();
        Assert.DoesNotContain("secret.caldris.mystery-tax-wagon-solution", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.caldris.mystery-sealed-alcove-solution", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("clue.caldris.thirteenth-bell.dry-flour", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("clue.caldris.thirteenth-bell.pulley-fibers", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("clue.caldris.thirteenth-bell.barge-signal-timing", serialized, StringComparison.Ordinal);
        Assert.All(root.GetProperty("scenes").EnumerateArray(),
            scene => Assert.Equal("prepared-unused", scene.GetProperty("status").GetString()));
        Assert.All(root.GetProperty("playerOrientationCandidates").EnumerateArray(), candidate =>
        {
            Assert.True(candidate.GetProperty("requiresExplicitAdmission").GetBoolean());
            Assert.False(candidate.GetProperty("currentlyAdmitted").GetBoolean());
            Assert.Equal("actor.caldris.ganji", candidate.GetProperty("admitToActorId").GetString());
        });

        var prohibited = root.GetProperty("prohibitedMutations").EnumerateArray()
            .Select(value => value.GetString()!).ToArray();
        Assert.Contains(prohibited, value => value.Contains("session", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(prohibited, value => value.Contains("reveal", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(prohibited, value => value.Contains("move Ganji", StringComparison.Ordinal));

        using var prompts = Read(Path.Combine(directory, "image-prompts.json"));
        var promptAssets = prompts.RootElement.GetProperty("assets").EnumerateArray().ToDictionary(
            value => value.GetProperty("logicalAssetKey").GetString()!, StringComparer.Ordinal);
        using var manifest = Read(Path.Combine(directory, "asset-import-manifest.json"));
        foreach (var asset in manifest.RootElement.GetProperty("assets").EnumerateArray())
        {
            var prompt = promptAssets[asset.GetProperty("logicalAssetKey").GetString()!];
            Assert.Equal(asset.GetProperty("sha256").GetString(), prompt.GetProperty("selectedSha256").GetString());
            Assert.Equal("OpenAI image_gen", prompt.GetProperty("tool").GetString());
        }
    }

    [Fact]
    public void Every_new_entity_has_an_explicit_reviewed_path_to_the_world_root()
    {
        var directory = PackageDirectory();
        using var manifest = Read(Path.Combine(directory, "asset-import-manifest.json"));
        using var situation = Read(Path.Combine(directory, "prepared-situation.json"));
        var parents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["location.caldris.atlas"] = "world.caldris"
        };

        foreach (var asset in manifest.RootElement.GetProperty("assets").EnumerateArray()
                     .Where(value => value.GetProperty("kind").GetString() == "map"
                         && value.GetProperty("parentLocationId").ValueKind == JsonValueKind.String))
            parents.Add(asset.GetProperty("ownerLocationId").GetString()!,
                asset.GetProperty("parentLocationId").GetString()!);

        var newEntityIds = new List<string>();
        foreach (var location in manifest.RootElement.GetProperty("locationCreates").EnumerateArray())
        {
            var id = location.GetProperty("id").GetString()!;
            parents.Add(id, location.GetProperty("container").GetProperty("id").GetString()!);
            newEntityIds.Add(id);
        }
        foreach (var clue in situation.RootElement.GetProperty("clueCreates").EnumerateArray())
        {
            var id = clue.GetProperty("id").GetString()!;
            parents.Add(id, clue.GetProperty("container").GetProperty("id").GetString()!);
            newEntityIds.Add(id);
        }

        var secret = situation.RootElement.GetProperty("canonicalKnowledgeRecord").GetProperty("entityCreate");
        Assert.Equal("knowledge", secret.GetProperty("container").GetProperty("slot").GetString());
        var secretId = secret.GetProperty("id").GetString()!;
        parents.Add(secretId, secret.GetProperty("container").GetProperty("id").GetString()!);
        newEntityIds.Add(secretId);

        Assert.Equal(17, newEntityIds.Count);
        foreach (var entityId in newEntityIds)
        {
            var current = entityId;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (var depth = 0; current != "world.caldris" && depth < 12; depth++)
            {
                Assert.True(visited.Add(current), $"Containment cycle from {entityId} at {current}.");
                Assert.True(parents.TryGetValue(current, out var parent),
                    $"No reviewed containment path from {entityId}; missing parent for {current}.");
                current = parent;
            }
            Assert.Equal("world.caldris", current);
        }
    }

    [Fact]
    public void Import_components_relationship_data_and_prepared_document_validate_against_closed_schemas()
    {
        var directory = PackageDirectory();
        var repository = RepositoryRoot();
        var validator = new BoundedJsonSchemaValidator();
        using var manifest = Read(Path.Combine(directory, "asset-import-manifest.json"));
        using var situation = Read(Path.Combine(directory, "prepared-situation.json"));

        Validates(validator, Path.Combine(directory, "prepared-situation.schema.json"), situation.RootElement);
        foreach (var location in manifest.RootElement.GetProperty("locationCreates").EnumerateArray())
        {
            var components = location.GetProperty("components");
            Validates(validator, Schema(repository, "world", "location.schema.json"),
                components.GetProperty("game.core.world.location"));
            Validates(validator, Schema(repository, "world", "map", "anchor.schema.json"),
                components.GetProperty("game.core.world.map.anchor"));
        }

        foreach (var asset in manifest.RootElement.GetProperty("assets").EnumerateArray()
                     .Where(value => value.GetProperty("kind").GetString() == "scene"))
        {
            Assert.Equal("guarded-location-gallery-append", asset.GetProperty("activationDisposition").GetString());
            var binding = asset.GetProperty("bindingPlan");
            Assert.True(binding.GetProperty("preflightReadRequired").GetBoolean());
            Assert.True(binding.GetProperty("preservedBaseline").GetProperty("componentObservedAbsent").GetBoolean());
            Assert.Equal(0, binding.GetProperty("preservedBaseline").GetProperty("expectedComponentRevision").GetInt32());
            Validates(validator, Schema(repository, "media", "visual.schema.json"),
                binding.GetProperty("valueWhenAbsent"));
        }

        foreach (var clue in situation.RootElement.GetProperty("clueCreates").EnumerateArray())
        {
            var components = clue.GetProperty("components");
            Validates(validator, Schema(repository, "world", "clue.schema.json"),
                components.GetProperty("game.core.world.clue"));
            Validates(validator, Schema(repository, "world", "knowledge", "classification.schema.json"),
                components.GetProperty("game.core.world.knowledge.classification"));
        }

        var knowledge = situation.RootElement.GetProperty("canonicalKnowledgeRecord");
        var knowledgeEntity = knowledge.GetProperty("entityCreate");
        var knowledgeComponents = knowledgeEntity.GetProperty("components");
        Assert.StartsWith("secret.caldris.", knowledgeEntity.GetProperty("id").GetString(), StringComparison.Ordinal);
        Validates(validator, Schema(repository, "world", "secret.schema.json"),
            knowledgeComponents.GetProperty("game.core.world.secret"));
        Validates(validator, Schema(repository, "world", "knowledge", "classification.schema.json"),
            knowledgeComponents.GetProperty("game.core.world.knowledge.classification"));

        const string emptyRelationshipDataSchema = "{\"type\":\"object\",\"additionalProperties\":false}";
        const string campaignReferenceDataSchema = "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"role\",\"audience\"],\"properties\":{\"role\":{\"const\":\"knowledge\"},\"audience\":{\"const\":\"gm\"}}}";
        foreach (var relationship in situation.RootElement.GetProperty("relationshipCreates").EnumerateArray())
        {
            var kind = relationship.GetProperty("kind").GetString();
            var schema = kind == "game.core.campaign.references"
                ? campaignReferenceDataSchema
                : emptyRelationshipDataSchema;
            var value = relationship.GetProperty("data");
            var validation = validator.Validate(schema, value.GetRawText());
            Assert.True(validation.Status == SchemaValueStatus.Valid,
                $"Relationship {kind} data failed schema validation: {JsonSerializer.Serialize(validation.Diagnostics)}");
            Assert.Contains(kind, new[]
            {
                "game.core.campaign.references",
                "game.core.world.knowledge.about",
                "game.core.world.knowledge.in-world",
                "game.core.world.clue.supports"
            });
        }
    }

    private static JsonDocument Read(string path) => JsonDocument.Parse(File.ReadAllText(path));

    private static string PackageDirectory() => Path.Combine(RepositoryRoot(),
        "catalog", "applications", "dnd2024", "assets", "caldris", "measure-of-mercy");

    private static string Schema(string repository, params string[] parts) =>
        Path.Combine([repository, "catalog", "components", "game", "core", .. parts]);

    private static void Validates(BoundedJsonSchemaValidator validator, string schemaPath, JsonElement value)
    {
        var compilation = validator.Compile(File.ReadAllText(schemaPath));
        Assert.True(compilation.IsAccepted, JsonSerializer.Serialize(compilation.Diagnostics));
        var validation = validator.Validate(compilation.ProfileId, compilation.NormalizedSchema, value.GetRawText());
        Assert.True(validation.Status == SchemaValueStatus.Valid,
            $"{schemaPath}: {JsonSerializer.Serialize(validation.Diagnostics)}");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
