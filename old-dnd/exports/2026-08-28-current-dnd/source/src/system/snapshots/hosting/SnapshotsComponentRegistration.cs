using DantesRoleplay.Snapshots;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class SnapshotsComponentRegistration
{
    internal static IServiceCollection AddSnapshotsComponent(this IServiceCollection services)
    {
        services.AddScoped<ISnapshotPackageStore, SnapshotPackageStore>();
        return services;
    }
}
