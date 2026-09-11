using DantesRoleplay.MCPServer;
using DantesRoleplay.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace DantesRoleplay.Tests;

public sealed class HostWebEndpointRegistrationTests
{
    [Fact]
    public void Host_specific_adapters_preserve_their_routes_and_explicit_traffic_policies()
    {
        var builder = WebApplication.CreateBuilder();
        RegisterHandlerServices(builder.Services);
        var application = builder.Build();
        application.MapDantesRoleplayHostWebAdapters();

        var routes = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => (
                endpoint.RoutePattern.RawText,
                Method: endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single(),
                Policy: endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName))
            .ToArray();

        Assert.Equal([
            ("/api/blob-uploads/{uploadId}", HttpMethods.Put, WebInterfaceSecurity.UploadRateLimitPolicy),
            ("/api/applications/{applicationId}/visual-drafts", HttpMethods.Post, WebInterfaceSecurity.UploadRateLimitPolicy),
            ("/api/blobs/sha256/{sha256}", HttpMethods.Get, WebInterfaceSecurity.ReadRateLimitPolicy),
            ("/api/audience-context", HttpMethods.Get, WebInterfaceSecurity.ReadRateLimitPolicy),
            ("/api/readiness/applications/{applicationId}", HttpMethods.Get, WebInterfaceSecurity.ReadRateLimitPolicy),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/media", HttpMethods.Get, WebInterfaceSecurity.ReadRateLimitPolicy),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/media-batch", HttpMethods.Post, WebInterfaceSecurity.ReadRateLimitPolicy),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/media/{mediaId}/content", HttpMethods.Get, WebInterfaceSecurity.ReadRateLimitPolicy),
            ("/api/read-model-media/{token}/content", HttpMethods.Get, WebInterfaceSecurity.ReadRateLimitPolicy),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/read-models/{qualifiedQueryId}", HttpMethods.Get, WebInterfaceSecurity.ReadRateLimitPolicy),
            ("/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/read-models/{qualifiedQueryId}", HttpMethods.Patch, WebInterfaceSecurity.UploadRateLimitPolicy)
        ], routes);
    }

    private static void RegisterHandlerServices(IServiceCollection services)
    {
        Type[] adapters =
        [
            typeof(BlobTransferWebEndpoints),
            typeof(VisualDraftUploadWebEndpoint),
            typeof(AudienceContextWebEndpoint),
            typeof(ApplicationReadinessWebEndpoint),
            typeof(EntityMediaWebEndpoints),
            typeof(ReadModelMediaWebEndpoint),
            typeof(ApplicationReadModelWebEndpoint)
        ];
        var serviceTypes = adapters
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Where(type => type.IsInterface || type.Name.EndsWith("Service", StringComparison.Ordinal)
                || type.Name.EndsWith("Guard", StringComparison.Ordinal))
            .Distinct();
        foreach (var serviceType in serviceTypes)
            services.Add(ServiceDescriptor.Singleton(serviceType,
                _ => throw new InvalidOperationException("Route inventory must not resolve handler services.")));
    }
}
