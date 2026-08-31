using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Blobs;

public static class BlobStorageServiceCollectionExtensions
{
    public static IServiceCollection AddBlobStorageComponent(this IServiceCollection services, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        services.AddSingleton(new BlobStorageOptions(Path.GetFullPath(rootPath)));
        services.AddScoped<IBlobTransferService, FileBlobTransferService>();
        return services;
    }
}
