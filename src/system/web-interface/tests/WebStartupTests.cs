using DantesRoleplay.DataAccess;
using DantesRoleplay.MCPServer;
using DantesRoleplay.SqliteInfrastructure;
using DantesRoleplay.Authorization;
using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Web.Hosting;
using DantesRoleplay.Web.Pages;
using DantesRoleplay.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DantesRoleplay.Tests;

public sealed class WebStartupTests
{
    [Fact]
    public async Task Startup_registers_the_resource_target_owner_and_reader_around_the_retained_catalog_resolver()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDantesRoleplayDataAccess($"Data Source=web-resource-registration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        services.AddDantesRoleplayWeb("Data Source=:memory:", new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;

        var targets = scoped.GetRequiredService<IStandingGrantTargetResolver>();
        Assert.IsType<ResourceStandingGrantTargetResolver>(targets);
        Assert.IsType<SqliteStandingGrantTargetResolver>(scoped.GetRequiredService<SqliteStandingGrantTargetResolver>());
        Assert.IsType<SqliteStandingGrantReadCandidateReader>(
            scoped.GetRequiredService<IStandingGrantReadCandidateReader>());
        Assert.IsType<WebPageStandingGrantResourceTargetOwner>(
            Assert.Single(scoped.GetServices<IStandingGrantResourceTargetOwner>()));
        Assert.IsType<WebPagePermissionedReader>(scoped.GetRequiredService<WebPagePermissionedReader>());

        var application = ApplicationIdentifier.Parse("demo");
        var hash = new string('A', 64);
        var typed = await targets.ResolveCandidateReferenceAsync(new(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            new ApplicationRevision(application, 1, hash, []), "state", "grant@1", "command", "state@1",
            InteractionExecutionProfile.Atomic, new InteractionInvocationBudget(4, DateTime.UtcNow.AddMinutes(1))),
            new ApplicationCandidateReference(application, "not-a-candidate-id", 1, hash),
            new("demo.procedure", "procedure", 1, hash));
        Assert.Equal("STANDING_GRANT_CANDIDATE_SCOPE", typed.Code);
    }

    [Fact]
    public async Task Startup_migrates_web_schema_and_installs_recovery_without_reading_legacy_page_history()
    {
        var connectionString = $"Data Source=web-startup-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDantesRoleplayDataAccess(connectionString);
        services.AddDantesRoleplayWeb(connectionString, new ConfigurationBuilder().Build());
        services.AddScoped<IWebPageIdentityMigration>(_ =>
            throw new InvalidOperationException("Startup must not resolve the legacy page-history audit."));
        await using var provider = services.BuildServiceProvider();
        await provider.InitialiseDantesRoleplayAsync();
        await provider.InitialiseDantesRoleplayWebAsync();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebContentDbContext>();
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.PageMigrationReports.ToListAsync());
        var stamp = await SqliteChangeRecovery.ReadAsync(keeper, null);
        Assert.NotNull(stamp);
        await provider.InitialiseDantesRoleplayWebAsync();
        Assert.Equal(stamp, await SqliteChangeRecovery.ReadAsync(keeper, null));
    }

    [Theory]
    [InlineData("http://127.0.0.1:6217", "http://127.0.0.1:6217/")]
    [InlineData("http://0.0.0.0:6217", "http://localhost:6217/")]
    [InlineData("http://[::]:6217", "http://localhost:6217/")]
    [InlineData("http://*:6217", "http://localhost:6217/")]
    [InlineData("https://+:5144", "https://localhost:5144/")]
    [InlineData("http://192.168.1.2:6217", "http://192.168.1.2:6217/")]
    public void Ready_message_includes_actual_listener_and_openable_website_and_mcp_urls(string address, string website)
    {
        var logger = new MessageLogger();
        ServerStartupDiagnostics.LogReady(logger, [address], TimeSpan.FromSeconds(2));
        Assert.Contains(logger.Messages, message => message.Contains($"Website: {website}", StringComparison.Ordinal)
            && message.Contains($"listening on {address}", StringComparison.Ordinal));
        Assert.Contains($"MCP: {website}mcp", logger.Messages);
        Assert.Contains(logger.Messages, message => message.StartsWith("Server ready in ", StringComparison.Ordinal));
    }

    private sealed class MessageLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
