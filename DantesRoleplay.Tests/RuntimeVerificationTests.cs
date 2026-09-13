using System.Net;
using System.Text.Json;
using DantesRoleplay.MCPServer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DantesRoleplay.Tests;

public sealed class RuntimeVerificationTests
{
    [Fact]
    public void Verification_removes_application_workers_without_constructing_them_and_normal_mode_is_unchanged()
    {
        CountingWorker.Reset();
        var normal = ServicesWithSixWorkers();
        var disabled = RuntimeVerification.PrepareServices(normal, Configuration(enabled: false));
        Assert.False(disabled.Enabled);
        Assert.Equal(6, normal.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)));

        var verification = ServicesWithSixWorkers();
        var enabled = RuntimeVerification.PrepareServices(verification, Configuration(enabled: true));
        Assert.True(enabled.Enabled);
        Assert.DoesNotContain(verification, descriptor => descriptor.ServiceType == typeof(IHostedService));
        using var provider = verification.BuildServiceProvider();
        Assert.Empty(provider.GetServices<IHostedService>());
        Assert.Equal(0, CountingWorker.Constructions);
        Assert.Equal(0, CountingWorker.Starts);
    }

    [Theory]
    [InlineData("GET", "/", true)]
    [InlineData("HEAD", "/", true)]
    [InlineData("GET", "/api/audience-context", true)]
    [InlineData("GET", "/api/readiness/applications/dnd2024", true)]
    [InlineData("GET", "/ui/dnd2024-play", false)]
    [InlineData("GET", "/components/system-theme.js", false)]
    [InlineData("GET", "/mcp", false)]
    [InlineData("POST", "/mcp", false)]
    [InlineData("GET", "/api/changes", false)]
    [InlineData("GET", "/api/applications/dnd2024/state-spaces/main/entities/world", false)]
    [InlineData("POST", "/api/readiness/applications/dnd2024", false)]
    [InlineData("PUT", "/ui/dnd2024-play", false)]
    public async Task Verification_allows_only_loopback_immutable_probe_routes(
        string method,
        string path,
        bool allowed)
    {
        var context = Context(method, path, IPAddress.Loopback);
        var invoked = false;
        await RuntimeVerification.InvokeAsync(context, _ =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, new(true, "dnd2024"));

        Assert.Equal(allowed, invoked);
        if (!allowed)
        {
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
            context.Response.Body.Position = 0;
            using var body = await JsonDocument.ParseAsync(context.Response.Body);
            Assert.Equal("RUNTIME_VERIFICATION_ONLY", body.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Verification_rejects_remote_reads_and_disabled_mode_preserves_dispatch()
    {
        var remote = Context(HttpMethods.Get, "/", IPAddress.Parse("192.0.2.7"));
        var remoteInvoked = false;
        await RuntimeVerification.InvokeAsync(remote, _ =>
        {
            remoteInvoked = true;
            return Task.CompletedTask;
        }, new(true, "dnd2024"));
        Assert.False(remoteInvoked);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, remote.Response.StatusCode);

        var forgedHost = Context(HttpMethods.Get, "/", IPAddress.Loopback);
        forgedHost.Request.Host = new HostString("example.test", 16217);
        var forgedInvoked = false;
        await RuntimeVerification.InvokeAsync(forgedHost, _ =>
        {
            forgedInvoked = true;
            return Task.CompletedTask;
        }, new(true, "dnd2024"));
        Assert.False(forgedInvoked);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, forgedHost.Response.StatusCode);

        var ordinary = Context(HttpMethods.Post, "/mcp", IPAddress.Parse("192.0.2.7"));
        var ordinaryInvoked = false;
        await RuntimeVerification.InvokeAsync(ordinary, _ =>
        {
            ordinaryInvoked = true;
            return Task.CompletedTask;
        }, RuntimeVerification.State.Disabled);
        Assert.True(ordinaryInvoked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://0.0.0.0:16217")]
    [InlineData("http://127.0.0.1:16217;http://192.0.2.7:16217")]
    public void Verification_requires_an_explicit_loopback_listener(string? urls)
    {
        var values = new Dictionary<string, string?>
        {
            [RuntimeVerification.ConfigurationKey] = "true",
            ["Knowledge:LocalPlayer:ApplicationId"] = "dnd2024",
            ["urls"] = urls
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.Throws<InvalidOperationException>(() =>
            RuntimeVerification.PrepareServices(new ServiceCollection(), configuration));
    }

    private static IServiceCollection ServicesWithSixWorkers()
    {
        var services = new ServiceCollection();
        for (var index = 0; index < 6; index++)
            services.AddSingleton<IHostedService>(_ => new CountingWorker());
        return services;
    }

    private static IConfiguration Configuration(bool enabled)
    {
        var values = new Dictionary<string, string?>
        {
            [RuntimeVerification.ConfigurationKey] = enabled.ToString(),
            ["Knowledge:LocalPlayer:ApplicationId"] = "dnd2024",
            ["urls"] = "http://127.0.0.1:16217"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static DefaultHttpContext Context(string method, string path, IPAddress remote)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Host = new HostString("127.0.0.1", 16217);
        context.Connection.RemoteIpAddress = remote;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class CountingWorker : IHostedService
    {
        internal static int Constructions;
        internal static int Starts;

        internal CountingWorker() => Interlocked.Increment(ref Constructions);
        internal static void Reset()
        {
            Constructions = 0;
            Starts = 0;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Starts);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
