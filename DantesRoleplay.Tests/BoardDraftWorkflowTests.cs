using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer;
using DantesRoleplay.Knowledge;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Media;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.Tests;

public sealed class BoardDraftWorkflowTests
{
    private static readonly JintMechanicEngine Engine = new();

    [Fact]
    public void Nullable_capability_examples_remain_json_null()
    {
        const string schema = """
            {"type":"object","additionalProperties":false,"required":["background"],"properties":{"background":{"anyOf":[{"type":"null"},{"type":"string"}]}}}
            """;
        var example = DantesRoleplay.Capabilities.CapabilityContractBuilder.MinimalExample(schema);
        Assert.Equal(SchemaValueStatus.Valid, new BoundedJsonSchemaValidator().Validate(schema, example).Status);
        Assert.Equal("{\"background\":null}", example);
    }

    [Fact]
    public async Task Draft_is_deterministic_closed_and_inert_and_player_cannot_read_it()
    {
        var context = Context();
        var before = JsonSerializer.Serialize(context);
        var first = await Run("draft", context);
        var second = await Run("draft", context);
        Assert.True(first.Ok, first.Error);
        Assert.Equal(first.Output.Data, second.Output.Data);
        Assert.Empty(first.Output.Effects); Assert.Empty(first.Output.Events); Assert.Empty(first.Output.Notifications);
        Assert.Equal(before, JsonSerializer.Serialize(context));
        var query = ApplicationQueryContract.Parse(File.ReadAllText(Path.Combine(Root(), "catalog", "applications", "dnd2024", "queries", "combat", "dnd2024.query.encounter-board-draft.json")), ApplicationIdentifier.Parse("dnd2024"));
        var validation = new BoundedJsonSchemaValidator().Validate(query.OutputSchemaJson, first.Output.Data);
        Assert.Equal(SchemaValueStatus.Valid, validation.Status);
        context = context with { Audience = MechanicAudienceContext.Player };
        var player = await Run("draft", context);
        Assert.False(player.Ok);
        Assert.Empty(player.Output.Effects);
    }

    [Theory]
    [InlineData("columns", "3")]
    [InlineData("rows", "65")]
    [InlineData("obstacleCount", "33")]
    [InlineData("seed", "-1")]
    [InlineData("setting", "\"private-secret\"")]
    public async Task Invalid_generation_never_proposes_game_state_changes(string field, string json)
    {
        var context = Context();
        var input = JsonNode.Parse(context.Input)!; input[field] = JsonNode.Parse(json); context = context with { Input = input.ToJsonString() };
        var result = await Run("draft", context);
        Assert.False(result.Ok); Assert.Empty(result.Output.Effects);
    }

    [Fact]
    public async Task Accepted_layout_reloads_exactly_and_does_not_change_initiative_turns_or_positions()
    {
        var context = Context();
        var generated = await Run("draft", context);
        Assert.True(generated.Ok, generated.Error);
        var draft = JsonNode.Parse(generated.Output.Data)!;
        context = context with { Input = new JsonObject { ["expectedBoardRevision"] = null, ["expectedLocationId"] = "location.1",
            ["board"] = draft["board"]!.DeepClone(), ["background"] = null }.ToJsonString() };
        var accepted = await Run("accept", context);
        Assert.True(accepted.Ok, accepted.Error);
        Assert.Equal(2, accepted.Output.Effects.Count);
        Assert.All(accepted.Output.Effects, effect => Assert.True(effect.DefinitionId is "dnd2024.encounter.board" or "dnd2024.encounter.board-visual"));
        var encounter = context.Roles["encounter"];
        var components = encounter.Components.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var effect in accepted.Output.Effects) components[effect.DefinitionId!] = effect.Data!;
        context.Roles["encounter"] = encounter with { Components = components };
        context = context with { Input = "{}" };
        var projected = await Run("project", context);
        Assert.True(projected.Ok, projected.Error);
        var readback = JsonNode.Parse(projected.Output.Data)!;
        Assert.Equal(draft["board"]!["obstacles"]!.ToJsonString(), readback["obstacles"]!.ToJsonString());
        Assert.Empty(projected.Output.Effects);
        Assert.Equal(1, readback["board"]!["revision"]!.GetValue<int>());
        context = context with { Input = new JsonObject { ["expectedBoardRevision"] = null, ["expectedLocationId"] = "location.1",
            ["board"] = draft["board"]!.DeepClone(), ["background"] = null }.ToJsonString() };
        var stale = await Run("accept", context);
        Assert.False(stale.Ok); Assert.Empty(stale.Output.Effects);
        context = context with { Audience = MechanicAudienceContext.Player };
        var denied = await Run("accept", context);
        Assert.False(denied.Ok); Assert.Empty(denied.Output.Effects);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Acceptance_rejects_invalid_geometry_and_changed_scene_without_effects(bool geometry)
    {
        var context = Context(); var generated = await Run("draft", context);
        var draft = JsonNode.Parse(generated.Output.Data)!;
        if (geometry) draft["board"]!["obstacles"]![0]!["area"]!["x"] = 64;
        context = context with { Input = new JsonObject { ["expectedBoardRevision"] = null, ["expectedLocationId"] = geometry ? "location.1" : "location.old",
            ["board"] = draft["board"]!.DeepClone(), ["background"] = null }.ToJsonString() };
        var result = await Run("accept", context);
        Assert.False(result.Ok); Assert.Empty(result.Output.Effects);
    }

    [Fact]
    public async Task Player_upload_is_denied_before_reading_or_storing_bytes()
    {
        var result = await VisualDraftUploadWebEndpoint.UploadAsync("dnd2024", new DefaultHttpContext(),
            new Seats(), null!, null!, null!, CancellationToken.None);
        Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task Media_batch_is_bounded_private_and_cannot_upgrade_a_player()
    {
        var media = new Media();
        var http = new DefaultHttpContext();
        var result = await EntityMediaWebEndpoints.DiscoverBatchAsync("dnd2024", "dnd2024-main",
            new(["item.1", "definition.1"], "player"), http, new Seats(), media, CancellationToken.None);
        Assert.Equal("private, no-store", http.Response.Headers.CacheControl);
        Assert.Equal(2, media.Reads);
        Assert.IsAssignableFrom<IContentTypeHttpResult>(result);
        Assert.All(media.Audiences, audience => Assert.Equal(EntityMediaAudience.Player, audience));
        var denied = await EntityMediaWebEndpoints.DiscoverBatchAsync("dnd2024", "dnd2024-main",
            new(["item.1"], "dm"), http, new Seats(), media, CancellationToken.None);
        Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
        var oversized = await EntityMediaWebEndpoints.DiscoverBatchAsync("dnd2024", "dnd2024-main",
            new(Enumerable.Range(0, 257).Select(n => $"item.{n}").ToArray(), "player"), http, new Seats(), media, CancellationToken.None);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(oversized).StatusCode);
        Assert.Equal(2, media.Reads);
    }

    private sealed class Media : IEntityMediaService
    {
        public int Reads;
        public readonly List<EntityMediaAudience> Audiences = [];
        public Task<EntityMediaDiscoveryResult> DiscoverAsync(ApplicationIdentifier applicationId, string stateSpaceId,
            string entityId, EntityMediaAudience audience, bool diagnostics = false, CancellationToken cancellationToken = default)
        {
            Reads++; Audiences.Add(audience);
            return Task.FromResult(new EntityMediaDiscoveryResult(applicationId.Value, stateSpaceId, entityId, new string('A',64), [], []));
        }
        public Task<EntityMediaReadResult?> OpenReadAsync(ApplicationIdentifier applicationId, string stateSpaceId,
            string entityId, string mediaId, EntityMediaAudience audience, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public void Png_dimensions_are_bounded_and_read_from_the_verified_header()
    {
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jZV8AAAAASUVORK5CYII=");
        Assert.Equal((1, 1), VisualDraftUploadWebEndpoint.PngDimensions(bytes));
        Assert.Null(VisualDraftUploadWebEndpoint.PngDimensions(bytes.AsSpan(0, 20)));
        bytes[16] = 255;
        Assert.Null(VisualDraftUploadWebEndpoint.PngDimensions(bytes));
    }

    private static MechanicProjection Context() => new()
    {
        StateSpaceId = "dnd2024-main", Audience = MechanicAudienceContext.GameMaster,
        Input = "{\"columns\":12,\"rows\":12,\"obstacleCount\":5,\"seed\":42,\"setting\":\"ruin\",\"prompt\":\"PRIVATE_PROMPT_CANARY\"}",
        Roles = {
            ["campaign"] = new("campaign.1", "Campaign", new Dictionary<string,string> {
                ["game.core.campaign.root"] = "{\"status\":\"active\"}",
                ["game.core.campaign.current-scene"] = "{\"location\":{\"entityId\":\"location.1\"},\"encounter\":{\"entityId\":\"encounter.1\"}}" }, null, ""),
            ["encounter"] = new("encounter.1", "Encounter", new Dictionary<string,string> { ["dnd2024.encounter.definition"] = "{}" }, null, "")
        },
        References = { ["location.1"] = new("location.1", new Dictionary<string,string> { ["game.core.world.location"] = "{\"status\":\"active\"}" }, "Location") }
    };

    private static Task<MechanicRunResult> Run(string suffix, MechanicProjection context) => Engine.RunAsync(
        File.ReadAllText(Path.Combine(Root(), "catalog", "applications", "dnd2024", "mechanics", "combat", $"dnd2024.mechanic.encounter.board.{suffix}.js")), context, ExecutionLimits.Default);
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
    private sealed class Seats : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => new(true, "player", "dnd2024", "campaign.1", "actor.1");
    }
}
