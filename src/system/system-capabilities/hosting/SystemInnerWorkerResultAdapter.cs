using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using Json.Schema;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>
/// Pure readback projection for the lifecycle owner. The response, handle and stored result
/// reference describe the candidate terminal record. The separately supplied earlier commits and
/// recovery identity must belong to request.InvocationHost, independently of that candidate.
/// The lifecycle owner must verify their principal/scope/command ownership before asserting
/// currentInvocationEvidenceVerified, including when the candidate belongs to another command.
/// If ownership is unresolved, pass false; no ambiguous evidence will be disclosed.
/// This adapter neither records a result nor
/// checks a lease, submits work, creates evidence, or authorizes model-reported mutations.
/// It is deliberately internal and unregistered until that lifecycle integration exists.
/// </summary>
internal static class SystemInnerWorkerResultAdapter
{
    internal static InteractionInvocationResult MapStoredResult(
        SystemInnerWorkerRequest request,
        AiResponse response,
        SystemTaskDurableHandle handle,
        string storedResultEvidenceReference,
        bool currentInvocationEvidenceVerified,
        IReadOnlyList<InteractionInvocationCommitReceipt>? currentInvocationPreviousCommits = null,
        ApplicationEcsExecutionIdentity? currentInvocationRecoveryIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(handle);
        if (!currentInvocationEvidenceVerified)
            return InteractionInvocationResult.Failed("SYSTEM_INNER_WORKER_RECONCILIATION_REQUIRED",
                "Current-invocation evidence ownership is unresolved; reconcile it before readback or retry.");
        var previousCommits = currentInvocationPreviousCommits;
        var recoveryIdentity = currentInvocationRecoveryIdentity;
        if (handle.CommandId != request.InvocationHost.CommandId)
            return InteractionInvocationResult.Failed("SYSTEM_INNER_WORKER_RESULT_IDENTITY_MISMATCH",
                "The recorded worker result belongs to another command; reconcile the current invocation before retrying.",
                previousCommits, recoveryIdentity);
        if (recoveryIdentity is not null)
            return InteractionInvocationResult.Failed("SYSTEM_INNER_WORKER_RECONCILIATION_REQUIRED",
                "An operation has unresolved completion; reconcile its receipt before retrying.",
                previousCommits, recoveryIdentity);
        if (string.IsNullOrWhiteSpace(storedResultEvidenceReference))
        {
            if (previousCommits is { Count: > 0 })
                return InteractionInvocationResult.Failed("SYSTEM_INNER_WORKER_RESULT_UNRECORDED",
                    "Earlier operations committed, but no persisted terminal worker result is available.", previousCommits);
            return InteractionInvocationResult.Unavailable("SYSTEM_INNER_WORKER_RESULT_UNRECORDED",
                "No persisted worker result evidence is available.");
        }
        if (storedResultEvidenceReference.Length > InteractionContractLimits.Identifier)
        {
            return InteractionInvocationResult.Failed("SYSTEM_INNER_WORKER_RESULT_EVIDENCE_INVALID",
                "The persisted worker result reference is invalid.", previousCommits);
        }
        if (!response.Ok)
            return InteractionInvocationResult.Failed("SYSTEM_INNER_WORKER_RUN_FAILED",
                "The recorded AI run failed; inspect its bounded activity for details.", previousCommits);
        if (response.StructuredData is null)
            return InvalidOutput(previousCommits);

        try
        {
            var schema = JsonSchema.FromText(request.ResultSchemaJson,
                new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            // Canonicalization also rejects duplicate properties, excessive depth and large output.
            var dataJson = InteractionCanonicalJson.Canonicalize(response.StructuredData.Value.GetRawText());
            using var data = JsonDocument.Parse(dataJson);
            if (!schema.Evaluate(data.RootElement).IsValid)
                return InvalidOutput(previousCommits);
            var summary = "Worker computation completed.";
            if (data.RootElement.ValueKind == JsonValueKind.Object &&
                data.RootElement.TryGetProperty("summary", out var text) && text.ValueKind == JsonValueKind.String)
            {
                summary = text.GetString() ?? summary;
                if (summary.Length > 512) summary = summary[..512];
            }
            // The task handle is data in a computation result. Earlier authoritative receipts
            // remain in the common envelope rather than being copied into model-shaped data.
            // Never turn a model's 'committed' field, tool success, or conversation ID into a receipt.
            var compact = JsonSerializer.Serialize(new
            {
                task = handle,
                summary,
                data = data.RootElement,
                procedure = request.ProcedureVersion,
                outputContractFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.ResultSchemaJson)))
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return InteractionInvocationResult.CompletedComputation(compact, storedResultEvidenceReference, previousCommits);
        }
        catch (Exception exception) when (exception is JsonException or JsonSchemaException or InteractionContractException)
        {
            return InvalidOutput(previousCommits);
        }
    }

    private static InteractionInvocationResult InvalidOutput(IReadOnlyList<InteractionInvocationCommitReceipt>? commits) =>
        InteractionInvocationResult.Failed("SYSTEM_INNER_WORKER_OUTPUT_INVALID",
            "The recorded AI output does not satisfy the selected bounded output contract.", commits);
}
