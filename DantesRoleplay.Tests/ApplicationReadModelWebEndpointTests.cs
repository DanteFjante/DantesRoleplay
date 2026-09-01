using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class ApplicationReadModelWebEndpointTests
{
    [Fact]
    public async Task Actor_may_read_only_their_own_registered_read_model()
    {
        var service = new ReadModels();

        var own = await ReadAsync(new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service);
        var other = await ReadAsync(new(true, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.other", service);

        Assert.Equal(StatusCodes.Status200OK, own.StatusCode);
        Assert.Equal("actor.aric", own.Body.GetProperty("data").GetProperty("subject")
            .GetProperty("id").GetString());
        Assert.Equal(StatusCodes.Status403Forbidden, other.StatusCode);
        Assert.Equal("READ_MODEL_AUDIENCE_DENIED", other.Body.GetProperty("code").GetString());
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task Game_master_may_read_an_application_character()
    {
        var service = new ReadModels();

        var response = await ReadAsync(new(true, "gm", "dnd2024", "campaign.1", null,
            KnowledgeAudienceRole.GameMaster), "actor.aric", service);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("resolution-fingerprint", response.Body
            .GetProperty("resolutionFingerprint").GetString());
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task Cross_application_and_disabled_seats_fail_before_projection()
    {
        var service = new ReadModels();

        var wrongApplication = await ReadAsync(new(true, "player", "other", "campaign.1", "actor.aric"),
            "actor.aric", service);
        var disabled = await ReadAsync(new(false, "player", "dnd2024", "campaign.1", "actor.aric"),
            "actor.aric", service);

        Assert.Equal(StatusCodes.Status403Forbidden, wrongApplication.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, disabled.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    private static async Task<(int StatusCode, JsonElement Body)> ReadAsync(
        LocalKnowledgeSeatSnapshot seat,
        string entityId,
        ReadModels service)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddOptions<JsonOptions>()
            .Configure(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
            .Services
            .AddLogging()
            .BuildServiceProvider();
        var result = await ApplicationReadModelWebEndpoint.ReadAsync(
            "dnd2024", "dnd2024-main", entityId, "dnd2024.query.character-sheet",
            context, new Seats(seat), service, CancellationToken.None);
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }

    private sealed class Seats(LocalKnowledgeSeatSnapshot seat) : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => seat;
    }

    private sealed class ReadModels : IApplicationReadModelService
    {
        public int Calls { get; private set; }

        public Task<ApplicationReadModelResult> ReadAsync(
            ApplicationReadModelRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ApplicationReadModelResult(
                request.ApplicationId.Value,
                request.StateSpaceId,
                request.QualifiedQueryId,
                "state-fingerprint",
                "resolution-fingerprint",
                new string('A', 64),
                new string('B', 64),
                new string('C', 64),
                JsonSerializer.Serialize(new { subject = new { id = request.RoleBindings["subject"] } })));
        }
    }
}
