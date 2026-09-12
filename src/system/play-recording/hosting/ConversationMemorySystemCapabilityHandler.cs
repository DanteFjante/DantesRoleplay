using System.Text;
using System.Text.Json;
using DantesRoleplay.Play;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.DataAccess.Composition;

/// <summary>Exact revision-bound read access to the private outer-message journal.</summary>
public sealed class ConversationMemorySystemCapabilityHandler(IConversationMemoryStore memory)
    : ISystemReadCapabilityHandler
{
    private const int MaximumOutputBytes = 32_768;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public SystemCapabilityRegistration Registration { get; } = new(
        SystemCapabilityIds.ConversationMemory, 1, "play-recording",
        "Read an exact source-revision and message selection from the current private gameplay-session conversation journal.",
        SystemCapabilityMode.Read,
        """
        {"type":"object","additionalProperties":false,"required":["sessionContextId","sourceRevision","sourceMessageIds"],"properties":{"sessionContextId":{"type":"string","minLength":1,"maxLength":200},"sourceRevision":{"type":"integer","minimum":1},"sourceMessageIds":{"type":"array","minItems":1,"maxItems":64,"uniqueItems":true,"items":{"type":"string","minLength":1,"maxLength":200}}}}
        """,
        """
        {"type":"object","additionalProperties":false,"required":["sourceRevision","messages"],"properties":{"sourceRevision":{"type":"integer","minimum":1},"messages":{"type":"array","maxItems":64,"items":{"type":"object","additionalProperties":false,"required":["id","ordinal","sourceTurnId","sourceMessageId","role","sourceKind","text","textFingerprint","sourceAtUtc"],"properties":{"id":{"type":"string"},"ordinal":{"type":"integer","minimum":1},"sourceTurnId":{"type":"string"},"sourceMessageId":{"type":"string"},"role":{"enum":["user","assistant"]},"sourceKind":{"enum":["user-prompt","assistant-commentary","assistant-final"]},"text":{"type":"string"},"textFingerprint":{"type":"string","pattern":"^[0-9A-F]{64}$"},"sourceAtUtc":{"type":"string"}}}}}}
        """,
        ["procedure.system.conversation-dream"],
        DantesRoleplay.Authorization.PrivateOperatorCapability.Read,
        SystemCapabilitySensitivity.Secret, false, false);

    public Task<SystemCapabilityHandlerResult> ReadAsync(JsonElement input,
        CancellationToken cancellationToken = default) => ReadAsync(input,
        new(DantesRoleplay.Authorization.TrustedPrincipalContext.Unauthenticated("CONTEXT_REQUIRED"),
            "conversation-memory", "conversation-memory"), cancellationToken);

    public Task<SystemCapabilityHandlerResult> ReadAsync(JsonElement input,
        SystemCapabilityInvocationContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Principal.Verified || context.ApplicationId is null || context.StateSpaceId is null)
            return Task.FromResult(SystemCapabilityHandlerResult.Failure(
                "CONVERSATION_MEMORY_SCOPE_DENIED", "A verified exact application state scope is required.",
                "Retry through the authorized focused-worker host."));
        try
        {
            var revision = input.GetProperty("sourceRevision").GetInt32();
            var messages = memory.GetSourceMessages(new(context.Principal.PrincipalId,
                    context.ApplicationId.Value, context.StateSpaceId,
                    input.GetProperty("sessionContextId").GetString()!),
                revision, input.GetProperty("sourceMessageIds").EnumerateArray()
                    .Select(value => value.GetString()!).ToArray());
            var output = JsonSerializer.SerializeToElement(new
            {
                sourceRevision = revision,
                messages = messages.Select(value => new
                {
                    value.Id, value.Ordinal, value.SourceTurnId, value.SourceMessageId,
                    value.Role, value.SourceKind, text = value.Text!, value.TextFingerprint,
                    value.SourceAtUtc
                }).ToArray()
            }, Json);
            if (Encoding.UTF8.GetByteCount(output.GetRawText()) > MaximumOutputBytes)
                return Task.FromResult(SystemCapabilityHandlerResult.Failure(
                    "CONVERSATION_MEMORY_SELECTION_TOO_LARGE",
                    "The selected conversation memory exceeds the read-tool byte bound.",
                    "Select fewer exact source message IDs."));
            return Task.FromResult(SystemCapabilityHandlerResult.Success(output));
        }
        catch (ConversationMemoryException error)
        {
            return Task.FromResult(SystemCapabilityHandlerResult.Failure(error.Code, error.Message,
                "Refresh the journal state and retry with its exact current revision and message IDs."));
        }
    }
}
