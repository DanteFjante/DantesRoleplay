using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Events;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.Retrieval;
using DantesRoleplay.Snapshots;
using DantesRoleplay.SystemFeedback;
using DantesRoleplay.World;
using DantesRoleplay.Information;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Projections;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.StateSpaceAdministration;
using DantesRoleplay.ComponentTypeAdministration;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.LegacyStateAdoption;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.TriggerScheduling;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemConversations;
using DantesRoleplay.SystemTasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Which database engine backs the kernel.
///
/// SQLite is the default and the right choice while the schema is moving: one file you can copy
/// to snapshot and delete to reset. Postgres exists as an option because the entity-component
/// model stores everything as JSON, and JSONB indexes that far better than SQLite's json1 —
/// so the day this stops being a single-user prototype, the switch is a connection string.
/// See ARCHITECTURE.md §8.3.
/// </summary>
public enum DatabaseProvider
{
    Sqlite,
    Postgres
}

public static class DataAccessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the kernel. A host wires everything up with one call and needs to know nothing
    /// else about the internals.
    /// </summary>
    /// <param name="connectionString">
    /// For SQLite this may be a bare file path — the directory is created and it is turned into
    /// a proper connection string.
    /// </param>
    public static IServiceCollection AddDantesRoleplayDataAccess(
        this IServiceCollection services,
        string connectionString,
        DatabaseProvider provider = DatabaseProvider.Sqlite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<DantesRoleplayDbContext>(options =>
        {
            switch (provider)
            {
                case DatabaseProvider.Sqlite:
                    options.UseSqlite(NormaliseSqlite(connectionString));
                    break;

                case DatabaseProvider.Postgres:
                    // Requires the Npgsql.EntityFrameworkCore.PostgreSQL package. Left as a throw
                    // rather than a silent fallback so switching provider fails loudly and early.
                    throw new NotSupportedException(
                        "Postgres support needs the Npgsql.EntityFrameworkCore.PostgreSQL package. " +
                        "Add it to DantesRoleplay.DataAccess and replace this branch with " +
                        "options.UseNpgsql(connectionString).");

                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
            }
        });

        services
            .AddOperationsAndAuditComponent()
            .AddHostSettingsComponent()
            .AddAssistantConversationsComponent()
            .AddPlayRecordingComponent()
            .AddStateComponent()
            .AddProceduresComponent()
            .AddMechanicsComponent()
            .AddEventsAndNotificationsComponent()
            .AddEffectsAndTransactionsComponent()
            .AddActionsComponent()
            .AddSnapshotsComponent()
            .AddFeedbackComponent()
            .AddInformationComponent()
            .AddCatalogComponent()
            .AddApplicationRegistryComponent()
            .AddSourceRegistryComponent()
            .AddRegistryAdministrationComponent()
            .AddApplicationPreviewComponent()
            .AddSchemaValidationComponent()
            .AddEcsComponent()
            .AddStateSpaceEdgesComponent()
            .AddComponentTypeAdministrationComponent()
            .AddEcsEffectsComponent()
            .AddProjectionMaterializationComponent()
            .AddApplicationActivationComponent()
            .AddCatalogNavigationComponent()
            .AddInteractionOrchestrationComponent()
            .AddStateSpaceAdministrationComponent()
            .AddSystemCapabilitiesComponent()
            .AddSystemConversationsComponent()
            .AddSystemTaskOrchestrationComponent()
            .AddLegacyStateAdoptionComponent()
            .AddTriggerSchedulingComponent()
            .AddApplicationExecutionComponent();

        return services;
    }

    /// <summary>
    /// Applies pending migrations, brings content fingerprints up to date, then seeds bootstrap
    /// contracts from the embedded markdown files. Called once by the host at startup.
    ///
    /// Migrate rather than EnsureCreated: the world schema is fixed, but the kernel still gains
    /// tables when a subsystem lands (mechanics, events), and EnsureCreated cannot evolve a
    /// database that already holds contracts you wrote.
    /// </summary>
    public static async Task InitialiseDantesRoleplayAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        _ = scope.ServiceProvider.GetRequiredService<ISystemCapabilityCatalog>();

        // BEFORE the seeders, not after. Both of them decide whether to write by comparing the
        // stored fingerprint against the file's, so running them against stale fingerprints would
        // append a pointless new version of every bootstrap record on the first start after this
        // landed — and then the fingerprints would agree, hiding the fact that it happened.
        var backfill = scope.ServiceProvider.GetRequiredService<ContentHashBackfill>();
        await backfill.RunAsync(cancellationToken);

        var seeder = scope.ServiceProvider.GetRequiredService<ProcedureSeeder>();
        await seeder.SeedAsync(cancellationToken);

        // The nine world.* structural event types, before any rule. Every accepted world change
        // records an event against one of them, so a database without them cannot change the world
        // at all — they are kernel contracts, not content, and a fresh install has to have them
        // without anyone remembering to import a catalog first.
        var eventTypes = scope.ServiceProvider.GetRequiredService<EventTypeSeeder>();
        await eventTypes.SeedAsync(cancellationToken);

        // The bootstrap rules, after the contracts, so that a fresh database has both the manual
        // and two worked examples of what the manual is describing.
        var rules = scope.ServiceProvider.GetRequiredService<MechanicSeeder>();
        await rules.SeedAsync(cancellationToken);
    }

    private static string NormaliseSqlite(string connectionStringOrPath)
    {
        if (connectionStringOrPath.Contains('=', StringComparison.Ordinal))
        {
            return connectionStringOrPath;
        }

        var full = Path.GetFullPath(connectionStringOrPath);
        var directory = Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return $"Data Source={full}";
    }
}
