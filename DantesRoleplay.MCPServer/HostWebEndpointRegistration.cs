using DantesRoleplay.Web.Security;
using Microsoft.AspNetCore.Routing;

namespace DantesRoleplay.MCPServer;

internal static class HostWebEndpointRegistration
{
    internal static IEndpointRouteBuilder MapDantesRoleplayHostWebAdapters(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/blob-uploads/{uploadId}", BlobTransferWebEndpoints.UploadAsync)
            .RequireDantesRoleplayUploadAccess();
        endpoints.MapPost("/api/applications/{applicationId}/visual-drafts", VisualDraftUploadWebEndpoint.UploadAsync)
            .RequireDantesRoleplayUploadAccess();
        endpoints.MapGet("/api/blobs/sha256/{sha256}", BlobTransferWebEndpoints.DownloadAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/audience-context", AudienceContextWebEndpoint.CurrentAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/readiness/applications/{applicationId}", ApplicationReadinessWebEndpoint.ReadAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/media",
                EntityMediaWebEndpoints.DiscoverAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapPost(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/media-batch",
                EntityMediaWebEndpoints.DiscoverBatchAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/media/{mediaId}/content",
                EntityMediaWebEndpoints.ReadAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet("/api/read-model-media/{token}/content", ReadModelMediaWebEndpoint.ReadAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapGet(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/read-models/{qualifiedQueryId}",
                ApplicationReadModelWebEndpoint.ReadAsync)
            .RequireDantesRoleplayReadAccess();
        endpoints.MapPatch(
                "/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/read-models/{qualifiedQueryId}",
                ApplicationReadModelWebEndpoint.WriteAsync)
            .RequireDantesRoleplayUploadAccess();
        return endpoints;
    }
}
