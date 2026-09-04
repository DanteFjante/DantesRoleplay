using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        return JsonSerializer.Serialize(new
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
    }

    public static string Fingerprint(string contentJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contentJson)));
    }
}
