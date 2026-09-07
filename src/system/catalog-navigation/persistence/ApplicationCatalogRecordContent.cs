using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.CatalogNavigation;

/// <summary>One canonical serialization boundary for application-owned catalog records.</summary>
public static class ApplicationCatalogRecordContent
{
    public static string MechanicJson(MechanicFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return JsonSerializer.Serialize(new
        {
            id = file.Id,
            category = file.Category,
            name = file.Name,
            description = file.Description,
            matches = file.Matches,
            requirements = file.Requirements,
            source = file.Source,
            scope = file.Scope,
            status = file.Status.ToString().ToLowerInvariant()
        });
    }

    public static string QueryJson(ApplicationQueryContract query)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var schema = JsonDocument.Parse(query.OutputSchemaJson);
        if (query.IsObjectProjection)
        {
            var objectContent = JsonSerializer.Serialize(new
            {
                id = query.Id,
                category = query.Category,
                name = query.Name,
                description = query.Description,
                matches = query.Matches,
                roles = query.Roles,
                executor = query.Executor,
                @object = new
                {
                    qualifiedId = query.ProjectionQualifiedId,
                    version = query.ProjectionVersion,
                    contentFingerprint = query.ProjectionContentHash
                },
                collection = query.ObjectCollectionId,
                outputSchema = schema.RootElement,
                exposure = query.Exposure == ApplicationQueryExposure.ModelVisible
                    ? "model-visible" : "binding-only",
                status = query.Status
            });
            return WithOptionalQueryMetadata(objectContent, query);
        }
        var content = JsonSerializer.Serialize(new
        {
            id = query.Id,
            category = query.Category,
            name = query.Name,
            description = query.Description,
            matches = query.Matches,
            roles = query.Roles,
            executor = query.Executor,
            projection = new
            {
                qualifiedId = query.ProjectionQualifiedId,
                version = query.ProjectionVersion,
                contentHash = query.ProjectionContentHash,
                outputSchemaHash = query.OutputSchemaHash
            },
            outputSchema = schema.RootElement,
            exposure = query.Exposure == ApplicationQueryExposure.ModelVisible
                ? "model-visible" : "binding-only",
            status = query.Status
        });
        return WithOptionalQueryMetadata(content, query);
    }

    private static string WithOptionalQueryMetadata(string content, ApplicationQueryContract query)
    {
        // Preserve legacy fingerprints byte-for-byte while retaining opt-in query metadata in
        // the canonical catalog used by discovery and the web authorization boundary.
        if (query.InputSchemaJson is null && query.CampaignSelection is null) return content;
        var document = JsonNode.Parse(content)!.AsObject();
        if (query.InputSchemaJson is not null)
            document["inputSchema"] = JsonNode.Parse(query.InputSchemaJson);
        if (query.CampaignSelection is not null)
            document["campaignSelection"] = JsonSerializer.SerializeToNode(new
            {
                queryId = query.CampaignSelection.QueryId,
                entityIdField = query.CampaignSelection.EntityIdField
            });
        return document.ToJsonString();
    }

    public static string Fingerprint(string contentJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contentJson)));
    }
}
