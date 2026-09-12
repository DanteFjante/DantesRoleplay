using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using DantesRoleplay.Play;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Retains only a freshly authorized, durably completed conversation-dream worker result. The
/// caller cannot supply provider output, schema identity, or completion evidence.
/// </summary>
public sealed class ConversationMemoryDreamRecorder(
    DantesRoleplayDbContext db,
    IConversationMemoryStore memory,
    IApplicationCandidateCapabilityGateway workers,
    TimeProvider clock) : IConversationMemoryDreamRecorder
{
    public async Task<ConversationMemoryDerivedCandidateDocument> RetainCompletedAsync(
        TrustedPrincipalContext principal,
        ConversationMemoryDreamRecordRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!principal.Verified || principal.PrincipalId != request.Scope.PrincipalId)
            throw Failure("CONVERSATION_MEMORY_SCOPE_DENIED", "A verified journal owner is required.");
        var application = ApplicationIdentifier.Parse(request.Scope.ApplicationId);
        var read = await workers.InvokeAsync(principal, application, SystemCapabilityIds.InnerWorkerRead,
            JsonSerializer.Serialize(new
            {
                stateSpaceId = request.Scope.StateSpaceId,
                taskId = request.Task.TaskId,
                commandId = request.Task.CommandId
            }), null, correlationId, cancellationToken);
        if (!read.Ok || read.Data is null)
            throw Failure(read.Error?.Code ?? "CONVERSATION_MEMORY_DREAM_UNAVAILABLE",
                read.Error?.Message ?? "The focused worker result is unavailable.");
        var result = read.Data.Value;
        if (String(result, "tag") != "completed"
            || NullableString(result, "dataJson") is not { } dataJson
            || NullableString(result, "completionEvidenceReference") is not { } evidence)
            throw Failure("CONVERSATION_MEMORY_DREAM_INCOMPLETE",
                "The focused conversation dream has no durable completed result.");

        var task = await db.Set<SystemTaskLifecycleRecord>().AsNoTracking().SingleOrDefaultAsync(value =>
            value.TaskId == request.Task.TaskId && value.CommandId == request.Task.CommandId,
            cancellationToken);
        if (task is null || task.State != "completed" || task.Purpose != "procedure-workflow"
            || task.PrincipalReference != principal.PrincipalId
            || task.ApplicationId != request.Scope.ApplicationId
            || task.StateSpaceId != request.Scope.StateSpaceId
            || task.DefinitionId != SystemInnerWorkerProcedureIdentity.ConversationDream(application)
            || task.ResultJson != dataJson
            || task.CompletionEvidenceReference != evidence
            || string.IsNullOrWhiteSpace(task.AdmissionPayloadJson))
            throw Failure("CONVERSATION_MEMORY_DREAM_SCOPE_MISMATCH",
                "The completed task does not belong to this exact conversation-dream scope.");
        var outputFingerprint = OutputFingerprint(task.AdmissionPayloadJson);
        ValidateCandidateSources(dataJson, request.SourceRevision, request.SourceMessageIds);
        return memory.AppendDerivedCandidate(new(
            request.Scope, request.Task, request.SourceRevision, request.SourceMessageIds,
            dataJson, outputFingerprint, evidence, clock.GetUtcNow().UtcDateTime));
    }

    private static string OutputFingerprint(string admission)
    {
        try
        {
            using var document = JsonDocument.Parse(admission);
            var value = document.RootElement.GetProperty("innerWorker").GetProperty("OutputSchemaFingerprint");
            var fingerprint = value.GetString();
            if (fingerprint is null || fingerprint.Length != 64
                || fingerprint.Any(character => !Uri.IsHexDigit(character))) throw new JsonException();
            return fingerprint.ToUpperInvariant();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw Failure("CONVERSATION_MEMORY_DREAM_EVIDENCE_INVALID",
                "The completed task has no valid retained output contract evidence.");
        }
    }

    private static void ValidateCandidateSources(
        string candidateJson,
        int sourceRevision,
        IReadOnlyList<string> sourceMessageIds)
    {
        try
        {
            using var document = JsonDocument.Parse(candidateJson);
            var root = document.RootElement;
            var ids = root.GetProperty("sourceMessageIds").EnumerateArray()
                .Select(value => value.GetString()).ToArray();
            if (root.ValueKind != JsonValueKind.Object
                || root.GetProperty("sourceRevision").GetInt32() != sourceRevision
                || ids.Any(value => value is null)
                || !ids.SequenceEqual(sourceMessageIds, StringComparer.Ordinal))
                throw new JsonException();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw Failure("CONVERSATION_MEMORY_DREAM_SOURCE_MISMATCH",
                "The completed dream result does not cite the requested revision and message IDs exactly.");
        }
    }

    private static string String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;
    private static string? NullableString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
    private static ConversationMemoryException Failure(string code, string message) => new(code, message);
}
