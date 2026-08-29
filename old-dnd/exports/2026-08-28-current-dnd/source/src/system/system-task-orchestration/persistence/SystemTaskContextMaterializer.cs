using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.SystemConversations;

namespace DantesRoleplay.SystemTasks;

public sealed class SystemTaskContextMaterializer(
    ISystemConversationContextMaterializer systemContext) : ISystemTaskContextMaterializer
{
    public const string Profile = "system-task-plan-v1";

    public async Task<SystemTaskContextSnapshot> MaterializeAsync(
        string query,
        SystemTaskRequestContext context,
        CancellationToken cancellationToken = default)
    {
        var source = await systemContext.MaterializeAsync(query,
            new(context.Principal, context.Scope, context.CorrelationId), cancellationToken);
        JsonObject root;
        try
        {
            root = JsonNode.Parse(source.Json)?.AsObject()
                ?? throw new JsonException("The system context is not an object.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new SystemTaskException(
                "SYSTEM_TASK_CONTEXT_UNAVAILABLE", "Authorized system planning context is unavailable.");
        }
        root["profile"] = Profile;
        root["limitations"] = new JsonArray(
            "Read steps may use only exact registered read capabilities.",
            "Write steps are inert until explicit server confirmation and current-authority execution.",
            "No application ECS actions, arbitrary tools, files, secrets, paths, SQL, URLs, or supplied authority.");
        var json = root.ToJsonString(new(JsonSerializerDefaults.Web));
        if (Encoding.UTF8.GetByteCount(json) > SystemConversationContextMaterializer.MaximumContextBytes)
            throw new SystemTaskException(
                "SYSTEM_TASK_CONTEXT_TOO_LARGE", "Authorized system planning context exceeds the safe limit.");
        return new(Profile, json,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
            source.SourceReferences);
    }
}
