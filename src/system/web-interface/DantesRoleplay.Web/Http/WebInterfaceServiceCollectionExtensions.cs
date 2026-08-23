using DantesRoleplay.Web.Data;
using DantesRoleplay.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Web.Hosting;

public static class WebInterfaceServiceCollectionExtensions
{
    private const string MigrationHistoryTable = "__web_migrations_history";

    public static IServiceCollection AddDantesRoleplayWeb(
        this IServiceCollection services,
        string connectionStringOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringOrPath);
        var connectionString = NormaliseSqlite(connectionStringOrPath);

        services.AddDbContext<WebContentDbContext>(options =>
            options.UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsHistoryTable(MigrationHistoryTable)));
        services.AddScoped<IWebPageStore, WebPageStore>();
        services.AddScoped<DynamicDataReader>();

        return services;
    }

    public static async Task InitialiseDantesRoleplayWebAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WebContentDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    private static string NormaliseSqlite(string connectionStringOrPath)
    {
        if (connectionStringOrPath.Contains('=', StringComparison.Ordinal))
        {
            return connectionStringOrPath;
        }

        var fullPath = Path.GetFullPath(connectionStringOrPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return $"Data Source={fullPath}";
    }
}
