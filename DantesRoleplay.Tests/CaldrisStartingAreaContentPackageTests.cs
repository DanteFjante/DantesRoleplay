using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

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
            value => Assert.Equal("retained-inert-until-explicit-scene-use",
                value.GetProperty("activationDisposition").GetString()));
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
        Assert.Equal(3, root.GetProperty("dmTruthReferences").GetArrayLength());

        var clues = root.GetProperty("clueCreates").EnumerateArray().ToArray();
        Assert.Equal(5, clues.Length);
        Assert.Equal(5, clues.Select(value => value.GetProperty("id").GetString()).Distinct().Count());
        foreach (var clue in clues)
        {
            var value = clue.GetProperty("components").GetProperty("game.core.world.clue");
            Assert.Equal("unrevealed", value.GetProperty("status").GetString());
            Assert.Equal("gm", value.GetProperty("visibility").GetString());
            Assert.Equal("confidential", clue.GetProperty("components")
                .GetProperty("game.core.world.knowledge.classification").GetProperty("sensitivity").GetString());
        }

        Assert.Equal(15, root.GetProperty("relationshipCreates").GetArrayLength());
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

    private static JsonDocument Read(string path) => JsonDocument.Parse(File.ReadAllText(path));

    private static string PackageDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                return Path.Combine(directory.FullName,
                    "catalog", "applications", "dnd2024", "assets", "caldris", "measure-of-mercy");
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
