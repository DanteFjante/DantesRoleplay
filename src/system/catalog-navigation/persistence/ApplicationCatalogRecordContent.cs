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
        if (query.IsFieldBasedObject)
        {
            var fieldBasedContent = JsonSerializer.Serialize(new
            {
                id = query.Id,
                category = query.Category,
                name = query.Name,
                description = query.Description,
                matches = query.Matches,
                roles = query.Roles,
                executor = query.Executor,
                profile = query.ObjectProfileId,
                @object = new
                {
                    qualifiedId = query.ProjectionQualifiedId,
                    version = query.ProjectionVersion,
                    contentFingerprint = query.ProjectionContentHash
                },
                collection = query.ObjectCollectionId,
                exposure = query.Exposure == ApplicationQueryExposure.ModelVisible
                    ? "model-visible" : "binding-only",
                status = query.Status
            });
            return WithOptionalQueryMetadata(WithoutNullCollection(fieldBasedContent, query), query);
        }
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
            return WithOptionalQueryMetadata(WithoutNullCollection(objectContent, query), query);
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
        if (query.InputSchemaJson is null && query.CampaignSelection is null
            && query.RoleBindings is null && query.Selection is null
            && query.MediaOwnerReference is null) return content;
        var document = JsonNode.Parse(content)!.AsObject();
        if (query.InputSchemaJson is not null)
            document["inputSchema"] = JsonNode.Parse(query.InputSchemaJson);
        if (query.CampaignSelection is not null)
            document["campaignSelection"] = JsonSerializer.SerializeToNode(new
            {
                queryId = query.CampaignSelection.QueryId,
                entityIdField = query.CampaignSelection.EntityIdField
            });
        if (query.Selection is not null)
            document["selection"] = JsonSerializer.SerializeToNode(new
            {
                queryId = query.Selection.QueryId,
                targetRole = query.Selection.TargetRole,
                resultPointer = query.Selection.ResultPointer,
                roleBindings = query.Selection.RoleBindings
            });
        if (query.RoleBindings is not null)
        {
            var bindings = new JsonObject();
            foreach (var value in query.RoleBindings.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var binding = new JsonObject { ["source"] = value.Value.Source };
                if (value.Value.Pointer is not null) binding["pointer"] = value.Value.Pointer;
                if (value.Value.Key is not null) binding["key"] = value.Value.Key;
                bindings[value.Key] = binding;
            }
            document["roleBindings"] = bindings;
        }
        if (query.MediaOwnerReference is not null)
            document["mediaOwnerReference"] = JsonSerializer.SerializeToNode(new
            {
                role = query.MediaOwnerReference.Role,
                resultPointer = query.MediaOwnerReference.ResultPointer,
                availabilityPointer = query.MediaOwnerReference.AvailabilityPointer,
                availabilityValue = query.MediaOwnerReference.AvailabilityValue
            });
        return document.ToJsonString();
    }

    private static string WithoutNullCollection(string content, ApplicationQueryContract query)
    {
        if (query.ObjectCollectionId is not null) return content;
        var document = JsonNode.Parse(content)!.AsObject();
        document.Remove("collection");
        return document.ToJsonString();
    }

    public static string Fingerprint(string contentJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contentJson)));
    }
}
