using System.Text.Json;
using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer;
using DantesRoleplay.MCPServer.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace DantesRoleplay.Tests;

public sealed class SystemAudienceContextToolsTests
{
    [Fact]
    public async Task Returns_only_the_verified_configured_actor_context()
    {
        var bindings = new Bindings(Binding());
        var participation = new Participation(ParticipationState.Active);

        var result = await SystemAudienceContextTools.ResolveAsync(
            new Seats(Seat()), new Audience(true), bindings, participation, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("actor.fixture", result.Subject);
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
        Assert.Equal("dnd2024", data.RootElement.GetProperty("applicationId").GetString());
        Assert.Equal("state-space.fixture", data.RootElement.GetProperty("stateSpaceId").GetString());
        Assert.Equal("campaign.fixture", data.RootElement.GetProperty("campaignId").GetString());
        Assert.Equal("actor.fixture", data.RootElement.GetProperty("actorId").GetString());
        Assert.Equal("bound", data.RootElement.GetProperty("status").GetString());
        Assert.Equal("actor.fixture", data.RootElement.GetProperty("roleHints")
            .GetProperty("actor").GetString());
        Assert.Equal(1, bindings.Calls);
        Assert.Equal(1, participation.Calls);
    }

    [Fact]
    public async Task Local_web_context_returns_the_same_verified_binding_without_request_input()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Response.Body = new MemoryStream();
        context.RequestServices = JsonResultServices();

        var result = await AudienceContextWebEndpoint.CurrentAsync(
            context, new Seats(Seat()), new Audience(true), new Bindings(Binding()),
            new Participation(ParticipationState.Active), CancellationToken.None);

        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("private, no-store", context.Response.Headers.CacheControl);
        Assert.Equal("dnd2024", response.RootElement.GetProperty("applicationId").GetString());
        Assert.Equal("campaign.fixture", response.RootElement.GetProperty("campaignId").GetString());
        Assert.Equal("actor.fixture", response.RootElement.GetProperty("actorId").GetString());
        Assert.False(response.RootElement.TryGetProperty("requestedCampaignId", out _));
    }

    [Fact]
    public async Task Local_web_context_does_not_expose_a_binding_when_validation_fails()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Response.Body = new MemoryStream();
        context.RequestServices = JsonResultServices();

        var result = await AudienceContextWebEndpoint.CurrentAsync(
            context, new Seats(Seat()), new Audience(false), new Bindings(Binding()),
            new Participation(ParticipationState.Active), CancellationToken.None);

        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("denied", response.RootElement.GetProperty("status").GetString());
        Assert.Equal("AUDIENCE_CONTEXT_DENIED", response.RootElement.GetProperty("error").GetString());
        Assert.False(response.RootElement.TryGetProperty("campaignId", out _));
        Assert.False(response.RootElement.TryGetProperty("actorId", out _));
    }

    [Fact]
    public async Task Policy_denial_stops_before_binding_or_participation_reads()
    {
        var bindings = new Bindings(Binding());
        var participation = new Participation(ParticipationState.Active);

        var result = await SystemAudienceContextTools.ResolveAsync(
            new Seats(Seat()), new Audience(false), bindings, participation, CancellationToken.None);

        Assert.Equal("AUDIENCE_CONTEXT_DENIED", result.Error?.Code);
        Assert.Equal(0, bindings.Calls);
        Assert.Equal(0, participation.Calls);
    }

    [Fact]
    public async Task Missing_actor_returns_reserved_character_creation_context()
    {
        var result = await SystemAudienceContextTools.ResolveAsync(
            new Seats(Seat()), new Audience(true), new Bindings(Binding()), new Participation(ParticipationState.Missing),
            CancellationToken.None);

        Assert.Null(result.Error);
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
        Assert.Equal("character-creation-required", data.RootElement.GetProperty("status").GetString());
        Assert.Equal("actor.fixture", data.RootElement.GetProperty("characterCreation")
            .GetProperty("characterId").GetString());
        Assert.Empty(data.RootElement.GetProperty("roleHints").EnumerateObject());
    }

    [Fact]
    public async Task Existing_actor_without_active_participation_remains_denied()
    {
        var result = await SystemAudienceContextTools.ResolveAsync(
            new Seats(Seat()), new Audience(true), new Bindings(Binding()), new Participation(ParticipationState.Inactive),
            CancellationToken.None);

        Assert.Equal("AUDIENCE_CONTEXT_DENIED", result.Error?.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Returns_verified_game_master_context_without_actor_or_participation_read()
    {
        var bindings = new Bindings(Binding());
        var participation = new Participation(ParticipationState.Active);

        var result = await SystemAudienceContextTools.ResolveAsync(
            new Seats(GameMasterSeat()), new Audience(true, KnowledgeAudienceRole.GameMaster), bindings,
            participation, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("campaign.fixture", result.Subject);
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
        Assert.Equal("bound", data.RootElement.GetProperty("status").GetString());
        Assert.Equal("game-master", data.RootElement.GetProperty("role").GetString());
        Assert.False(data.RootElement.TryGetProperty("actorId", out _));
        Assert.Equal(1, bindings.Calls);
        Assert.Equal(0, participation.Calls);
    }

    [Fact]
    public void Dnd_chat_contract_uses_the_server_bound_creation_identity()
    {
        var path = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024", "procedures",
            "play", "dnd2024.procedure.play.mini-game.md");
        var contract = File.ReadAllText(path);

        Assert.Contains("system.audience-context", contract, StringComparison.Ordinal);
        Assert.Contains("character-creation-required", contract, StringComparison.Ordinal);
        Assert.Contains("characterCreation.characterId", contract, StringComparison.Ordinal);
        Assert.Contains("Never use a", contract, StringComparison.Ordinal);
    }

    private static LocalKnowledgeSeatSnapshot Seat() => new(
        true, "principal.fixture", "dnd2024", "campaign.fixture", "actor.fixture",
        SourceIds: ["dnd2024-core"]);

    private static LocalKnowledgeSeatSnapshot GameMasterSeat() => new(
        true, "principal.fixture", "dnd2024", "campaign.fixture", null,
        KnowledgeAudienceRole.GameMaster, ["dnd2024-core"]);

    private static KnowledgeApplicationBinding Binding()
    {
        var path = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024", "metadata",
            "authorized-knowledge.json");
        Assert.True(KnowledgeApplicationBindingDocument.TryParse(File.ReadAllText(path), "dnd2024", out var document));
        return document.Bind("dnd2024", "state-space.fixture", "campaign.fixture", "binding.fixture");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static IServiceProvider JsonResultServices() => new ServiceCollection()
        .AddLogging()
        .AddOptions()
        .Configure<JsonOptions>(_ => { })
        .BuildServiceProvider();

    private sealed class Seats(LocalKnowledgeSeatSnapshot value) : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => value;
    }

    private sealed class Audience(bool allowed, KnowledgeAudienceRole role = KnowledgeAudienceRole.Actor) : IAuthorizedKnowledgeAudiencePolicy
    {
        public Task<KnowledgeAudienceResolution> ResolveAsync(string campaignId, CancellationToken cancellationToken = default) =>
            Task.FromResult(allowed
                ? new KnowledgeAudienceResolution(new("principal.fixture", campaignId,
                    role, role == KnowledgeAudienceRole.Actor ? "actor.fixture" : null, "policy.fixture"))
                : KnowledgeAudienceResolution.Denied());
    }

    private sealed class Bindings(KnowledgeApplicationBinding value) : IKnowledgeApplicationBindingResolver
    {
        public int Calls { get; private set; }
        public Task<KnowledgeApplicationBinding?> ResolveAsync(string campaignId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<KnowledgeApplicationBinding?>(value);
        }
    }

    private enum ParticipationState { Active, Missing, Inactive }

    private sealed class Participation(ParticipationState state) : IKnowledgeActorParticipationVerifier
    {
        public int Calls { get; private set; }
        public Task<KnowledgeParticipationResolution> ResolveAsync(
            KnowledgeApplicationBinding binding, string actorId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(state switch
            {
                ParticipationState.Active => new KnowledgeParticipationResolution(true, "participation.fixture"),
                ParticipationState.Missing => KnowledgeParticipationResolution.MissingActor(),
                _ => KnowledgeParticipationResolution.Denied()
            });
        }
    }
}
