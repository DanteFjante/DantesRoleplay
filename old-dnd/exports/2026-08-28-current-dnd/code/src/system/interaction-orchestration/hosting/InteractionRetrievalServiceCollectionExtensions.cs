using DantesRoleplay.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DantesRoleplay.DataAccess;

/// <summary>Opt-in host configuration for the disposable feature-retrieval vector cache.</summary>
public static class InteractionRetrievalServiceCollectionExtensions
{
    public static IServiceCollection AddInteractionRetrievalDerivedIndex(
        this IServiceCollection services,
        string derivedDataDirectory,
        string databaseFileName = "interaction-retrieval.sqlite")
    {
        ArgumentNullException.ThrowIfNull(services);
        var location = InteractionDerivedIndexLocation.Create(derivedDataDirectory, databaseFileName);
        services.TryAddSingleton<IInteractionDerivedVectorIndex>(new SqliteInteractionDerivedVectorIndex(location));
        return services;
    }
}
