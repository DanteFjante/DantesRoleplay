using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.DataAccess.Composition;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed record SystemInnerWorkerRetainedAdmission(string ResultSchemaJson,
    IReadOnlyList<SystemInnerWorkerDependencyInput> DependencyInputs);

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    internal async Task<SystemInnerWorkerRetainedAdmission?> ReadInnerWorkerAdmissionAsync(
        SystemTaskDurableHandle handle,
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSnapshotAsync(connection, transaction, handle, cancellationToken);
        if (snapshot is null || snapshot.Request.Purpose != SystemTaskPurpose.ProcedureWorkflow)
            return null;
        var payload = await ScalarStringAsync(connection, transaction,
            "SELECT admission_payload_json FROM system_task_lifecycle WHERE task_id=$task",
            cancellationToken, ("$task", handle.TaskId));
        if (payload is null || payload != InteractionCanonicalJson.CanonicalizeObject(payload))
            return null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("innerWorker", out var worker)
                || worker.ValueKind != JsonValueKind.Object
                || !worker.TryGetProperty("format", out var format)
                || format.GetString() != SystemInnerWorkerAssignmentV1.Format
                || !worker.TryGetProperty("resultSchemaJson", out var schemaElement)
                || schemaElement.ValueKind != JsonValueKind.String
                || !worker.TryGetProperty("OutputSchemaFingerprint", out var fingerprint)
                || fingerprint.ValueKind != JsonValueKind.String)
                return null;
            var schema = InteractionCanonicalJson.CanonicalizeObject(schemaElement.GetString()!);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema)));
            if (hash != fingerprint.GetString()) return null;
            var inputs = new List<SystemInnerWorkerDependencyInput>();
            if (worker.TryGetProperty("dependencyInputs", out var dependencyInputs))
            {
                if (dependencyInputs.ValueKind != JsonValueKind.Array
                    || dependencyInputs.GetArrayLength() > InteractionContractLimits.DependenciesPerStep)
                    return null;
                foreach (var input in dependencyInputs.EnumerateArray())
                {
                    if (input.ValueKind != JsonValueKind.Object
                        || !input.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                        || !input.TryGetProperty("handle", out var handleElement) || handleElement.ValueKind != JsonValueKind.Object
                        || !handleElement.TryGetProperty("taskId", out var taskId) || taskId.ValueKind != JsonValueKind.String
                        || !handleElement.TryGetProperty("commandId", out var commandId) || commandId.ValueKind != JsonValueKind.String
                        || !input.TryGetProperty("jsonPointer", out var pointer) || pointer.ValueKind != JsonValueKind.String)
                        return null;
                    inputs.Add(new(name.GetString()!, new(taskId.GetString()!, commandId.GetString()!), pointer.GetString()!));
                }
            }
            if (inputs.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != inputs.Count
                || inputs.Any(value => !snapshot.Request.Dependencies.Contains(value.Handle))) return null;
            return new(schema, inputs.AsReadOnly());
        }
        catch (Exception error) when (error is JsonException or InteractionContractException or InvalidOperationException)
        {
            return null;
        }
    }
}
