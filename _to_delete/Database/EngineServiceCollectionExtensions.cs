using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Database;

/// <summary>
/// The engine's single registration point. A host wires the kernel up with one call and needs to
/// know nothing else about its internals.
/// </summary>
public static class EngineServiceCollectionExtensions
{
    public static IServiceCollection AddDantesRoleplayEngine(
        this IServiceCollection services,
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var full = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        services.AddDbContext<DantesRoleplayDbContext>(options =>
            options.UseSqlite($"Data Source={full}"));

        services.AddScoped<ProcedureStore>();
        services.AddScoped<OperationLog>();

        return services;
    }

    /// <summary>
    /// Applies pending migrations at startup. Called once by the host.
    ///
    /// Migrate rather than EnsureCreated on purpose: the schema will change often, and
    /// EnsureCreated cannot evolve a database that already holds contracts you wrote.
    /// </summary>
    public static async Task MigrateDantesRoleplayEngineAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
