using System.Net;
using DantesRoleplay.Web.Hosting;
using DantesRoleplay.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DantesRoleplay.Tests;

public sealed class WebRateLimitTests
{
    [Fact]
    public async Task Api_read_exhaustion_does_not_block_pages_assets_or_writes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddDantesRoleplayWeb("Data Source=:memory:", new ConfigurationBuilder().Build());
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UseRateLimiter();
        app.MapGet("/api/read/{id}", () => Results.Ok())
            .RequireRateLimiting(WebInterfaceSecurity.ReadRateLimitPolicy);
        app.MapGet("/", () => Results.Ok())
            .RequireRateLimiting(WebInterfaceSecurity.ReadRateLimitPolicy);
        app.MapGet("/components/client.js", () => Results.Ok())
            .RequireRateLimiting(WebInterfaceSecurity.ReadRateLimitPolicy);
        app.MapGet("/api/applications/sample/entities/place/media/map/content", () => Results.Ok())
            .RequireRateLimiting(WebInterfaceSecurity.ReadRateLimitPolicy);
        app.MapPost("/api/write", () => Results.Ok())
            .RequireRateLimiting(WebInterfaceSecurity.UploadRateLimitPolicy);
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        for (var index = 0; index < WebInterfaceSecurity.ReadRequestsPerMinute; index++)
        {
            using var response = await client.GetAsync($"/api/read/{index}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var rejected = await client.GetAsync("/api/read/another?fresh=true");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Contains("WEB_RATE_LIMITED", await rejected.Content.ReadAsStringAsync());
        Assert.InRange(rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);

        using var home = await client.GetAsync("/");
        using var asset = await client.GetAsync("/components/client.js");
        using var write = await client.PostAsync("/api/write", null);
        using var map = await client.GetAsync("/api/applications/sample/entities/place/media/map/content");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        Assert.Equal(HttpStatusCode.OK, map.StatusCode);

        // Pages and assets still share a finite allowance, independent of the API budget.
        for (var index = 3; index < WebInterfaceSecurity.ReadRequestsPerMinute; index++)
        {
            using var response = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var contentRejected = await client.GetAsync("/components/client.js");
        Assert.Equal(HttpStatusCode.TooManyRequests, contentRejected.StatusCode);
    }
}
