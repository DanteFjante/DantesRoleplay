using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.DataAccess.Composition;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed record SystemInnerWorkerRetainedAdmission(string ResultSchemaJson);

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
            return hash == fingerprint.GetString() ? new(schema) : null;
        }
        catch (Exception error) when (error is JsonException or InteractionContractException or InvalidOperationException)
        {
            return null;
        }
    }
}
