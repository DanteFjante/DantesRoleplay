using System.Net;
using System.Text;
using System.Text.Json;
using DantesRoleplay.DataAccess.Composition;

namespace DantesRoleplay.Interactions.Tests;

public sealed class InteractionOuterProviderTests
{
    [Fact]
    public async Task Outer_turn_is_fixed_outer_profile_schema_only_and_has_no_tools()
    {
        HttpRequestMessage? observed = null;
        string? body = null;
        var handler = new Handler(async request =>
        {
            observed = request;
            body = await request.Content!.ReadAsStringAsync();
            return Response("{\"decision\":\"delegate\",\"intentText\":\"The actor attempts the declared action.\"}");
        });
        var provider = Provider(handler);

        var result = await provider.DecideAsync(new("I attack the driver."));

        Assert.True(result.Available);
        Assert.Equal(InteractionOuterDecision.Delegate, result.Decision);
        Assert.Equal(InteractionOuterProtocol.OuterTurnTask, observed!.Headers.GetValues("X-Dantes-Task-Class").Single());
        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.Equal(InteractionRoleProfile.Outer.Model, root.GetProperty("model").GetString());
        Assert.Equal(InteractionRoleProfile.Outer.ReasoningEffort, root.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal(InteractionOuterProtocol.OuterTurnSchemaName,
            root.GetProperty("text").GetProperty("format").GetProperty("name").GetString());
        Assert.Empty(root.GetProperty("tools").EnumerateArray());
        Assert.Equal("none", root.GetProperty("tool_choice").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
    }

    [Fact]
    public async Task Narration_uses_distinct_fixed_schema_and_rejects_tool_output()
    {
        var calls = 0;
        var provider = Provider(new Handler(request =>
        {
            calls++;
            Assert.Equal(InteractionOuterProtocol.NarrationTask,
                request.Headers.GetValues("X-Dantes-Task-Class").Single());
            return Task.FromResult(calls == 1
                ? Response("{\"narration\":\"The blow lands.\"}")
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"completed\",\"model\":\"gpt-5.6-luna\",\"output\":[{\"type\":\"function_call\"}]}")
                });
        }));

        var result = await provider.NarrateAsync(new("attack", "succeeded", "OK", ["hit"], ["receipt"]));
        var rejected = await provider.NarrateAsync(new("attack", "succeeded", "OK", ["hit"], ["receipt"]));

        Assert.True(result.Available);
        Assert.Equal("The blow lands.", result.Narration);
        Assert.False(rejected.Available);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"status\":1,\"model\":\"gpt-5.6-luna\",\"output\":[]}")]
    [InlineData("{\"status\":\"completed\",\"model\":\"gpt-5.6-luna\",\"output\":[1]}")]
    [InlineData("{\"status\":\"completed\",\"model\":\"gpt-5.6-luna\",\"output\":[{\"type\":\"message\",\"content\":{}}]}")]
    public async Task Malformed_remote_envelopes_fail_closed_without_escaping(string responseBody)
    {
        var provider = Provider(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody)
        })));

        var result = await provider.DecideAsync(new("Perform the declared fixture action."));

        Assert.False(result.Available);
        Assert.Equal("REMOTE_MODEL_RESPONSE_INVALID", result.Code);
    }

    private static OpenAiResponsesOuterInteractionProvider Provider(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new OpenAiInteractionPlanningOptions
        {
            Enabled = true,
            ApiKey = "test-key",
            Endpoint = new("https://api.openai.com/v1/responses"),
            Timeout = TimeSpan.FromSeconds(10)
        });

    private static HttpResponseMessage Response(string output) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            status = "completed",
            model = InteractionRoleProfile.Outer.Model,
            output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = output } } } }
        }), Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
