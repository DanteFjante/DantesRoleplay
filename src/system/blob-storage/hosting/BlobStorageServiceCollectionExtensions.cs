using Microsoft.Extensions.DependencyInjection;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Media;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.Blobs;

public static class BlobStorageServiceCollectionExtensions
{
    public static IServiceCollection AddBlobStorageComponent(this IServiceCollection services, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        services.AddSingleton(new BlobStorageOptions(Path.GetFullPath(rootPath)));
        services.AddScoped<IBlobTransferService, FileBlobTransferService>();
        services.AddScoped<IEntityMediaService, EntityMediaService>();
        services.AddSingleton<IReadModelMediaLinkStore, ReadModelMediaLinkStore>();
        services.AddScoped<ISystemAiToolSource, EntityMediaAiToolSource>();
        return services;
    }
}
