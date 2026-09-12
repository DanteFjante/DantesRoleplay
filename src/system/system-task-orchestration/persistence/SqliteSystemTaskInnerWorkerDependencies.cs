using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskDurableService
{
    internal async Task<IReadOnlyList<SystemInnerWorkerResolvedDependency>> ResolveInnerWorkerDependenciesAsync(
        InteractionInvocationHost host,
        IReadOnlyList<SystemTaskDurableHandle> handles,
        IReadOnlyList<SystemInnerWorkerDependencyInput> mappings,
        CancellationToken cancellationToken = default)
    {
        if (handles.Count == 0) return [];
        var requested = mappings.Count == 0
            ? handles.Select(value => new SystemInnerWorkerDependencyInput(
                value.TaskId, value, "")).ToArray()
            : mappings.ToArray();
        var results = new List<SystemInnerWorkerResolvedDependency>(requested.Length);
        foreach (var mapping in requested)
        {
            var result = await GetAsync(host, mapping.Handle, cancellationToken);
            if (result.Tag != InteractionInvocationResultTag.Completed || result.DataJson is null
                || string.IsNullOrWhiteSpace(result.CompletionEvidenceReference))
                throw new InteractionContractException(
                    result.Code is "SYSTEM_TASK_NOT_AUTHORIZED" or "STANDING_GRANT_DENIED"
                        ? "INNER_WORKER_DEPENDENCY_NOT_AUTHORIZED"
                        : "INNER_WORKER_DEPENDENCY_UNAVAILABLE",
                    "A required dependency does not have a freshly authorized successful terminal result.");
            try
            {
                var canonical = InteractionCanonicalJson.CanonicalizeObject(result.DataJson);
                using var document = JsonDocument.Parse(canonical);
                var root = document.RootElement;
                if (!root.TryGetProperty("task", out var task) || task.ValueKind != JsonValueKind.Object
                    || !task.TryGetProperty("taskId", out var taskId) || taskId.GetString() != mapping.Handle.TaskId
                    || !task.TryGetProperty("commandId", out var commandId) || commandId.GetString() != mapping.Handle.CommandId
                    || !root.TryGetProperty("data", out var data)
                    || !root.TryGetProperty("outputContractFingerprint", out var contract)
                    || contract.ValueKind != JsonValueKind.String || contract.GetString() is not { Length: 64 } schemaHash)
                    throw new JsonException();
                if (schemaHash.Any(value => !(value is >= '0' and <= '9' or >= 'A' and <= 'F')))
                    throw new JsonException();
                var outputJson = InteractionCanonicalJson.Canonicalize(data.GetRawText());
                using var output = JsonDocument.Parse(outputJson);
                var selected = SelectPointer(output.RootElement, mapping.JsonPointer);
                results.Add(new(mapping.Name, mapping.Handle, mapping.JsonPointer,
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(outputJson))),
                    selected.Clone()));
            }
            catch (Exception error) when (error is JsonException or InteractionContractException
                or InvalidOperationException or FormatException)
            {
                throw new InteractionContractException("INNER_WORKER_DEPENDENCY_RESULT_INVALID",
                    "A required dependency result or selected JSON Pointer is invalid.");
            }
        }
        return results.AsReadOnly();
    }

    private static JsonElement SelectPointer(JsonElement root, string pointer)
    {
        if (pointer.Length == 0) return root;
        var current = root;
        foreach (var encoded in pointer[1..].Split('/'))
        {
            var token = DecodePointerToken(encoded);
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(token, out current)) throw new JsonException();
            }
            else if (current.ValueKind == JsonValueKind.Array
                && int.TryParse(token, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var index)
                && index >= 0 && index < current.GetArrayLength())
                current = current[index];
            else throw new JsonException();
        }
        return current;
    }

    private static string DecodePointerToken(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '~') { builder.Append(value[index]); continue; }
            if (++index >= value.Length || value[index] is not ('0' or '1')) throw new JsonException();
            builder.Append(value[index] == '0' ? '~' : '/');
        }
        return builder.ToString();
    }
}
