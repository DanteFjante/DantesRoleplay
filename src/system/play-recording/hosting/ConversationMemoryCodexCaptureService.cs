using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.CodexBridge;
using DantesRoleplay.Play;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Binds one Codex adapter to one host-authorized gameplay session and acknowledges a queued turn
/// only after the journal has returned its durable append receipt.
/// </summary>
public sealed class ConversationMemoryCodexCaptureService : IConversationMemoryHostBinding
{
    private readonly IConversationMemoryStore memory;
    private readonly CodexCaptureAdapter capture;
    private readonly TimeProvider clock;

    public ConversationMemoryCodexCaptureService(
        IConversationMemoryStore memory,
        ConversationMemoryBinding binding,
        CodexCaptureAdapter capture,
        CodexGameplayCaptureBinding captureBinding,
        TimeProvider? clock = null)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
        ArgumentNullException.ThrowIfNull(captureBinding);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (binding.SourceClient != "codex"
            || binding.SourceProjectId != captureBinding.ProjectId
            || binding.Scope.SessionContextId != captureBinding.GameplaySessionContextId
            || binding.SourceThreadId != captureBinding.ExternalCodexThreadId
            || !string.Equals(Path.GetFullPath(binding.RepositoryRoot),
                Path.GetFullPath(captureBinding.RepositoryRoot), pathComparison))
            throw new ArgumentException("The journal and Codex capture bindings must identify the same linked session.",
                nameof(captureBinding));
        this.clock = clock ?? TimeProvider.System;
    }

    public ConversationMemoryBinding Binding { get; }

    public ConversationMemoryJournalDocument Connect() => memory.Connect(Binding);

    public bool TryAcceptHook(CodexCaptureHookInput input) =>
        IsConnected() && capture.TryAcceptHook(input, clock.GetUtcNow());

    public async Task<ConversationMemoryAppendResult?> CaptureNextAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected())
            throw new ConversationMemoryException("CONVERSATION_MEMORY_NOT_CONNECTED",
                "The gameplay conversation capture is not connected.");
        CodexCaptureDelivery? delivery;
        try
        {
            delivery = await capture.CaptureNextAsync(cancellationToken);
        }
        catch (CodexBridgeException error)
        {
            memory.MarkRetryPending(Binding.Scope, error.Code);
            throw;
        }
        if (delivery is null) return null;

        try
        {
            var sourceAt = delivery.Correlation.ReceivedAtUtc.UtcDateTime;
            var messages = delivery.Turn.Messages.OrderBy(value => value.Ordinal).Select(value =>
                new ConversationMemoryCapturedMessage(
                    value.ExternalMessageId,
                    value.Role,
                    value.Role == ConversationMemoryRoles.User
                        ? ConversationMemoryMessageKinds.UserPrompt
                        : value.Classification == "commentary"
                            ? ConversationMemoryMessageKinds.AssistantCommentary
                            : ConversationMemoryMessageKinds.AssistantFinal,
                    value.Content,
                    sourceAt)).ToArray();
            var result = AppendWithReplayRecovery(new(
                Binding,
                delivery.Turn.TurnId,
                messages,
                "codex-app-server/turn-items@" + delivery.SourceVersion,
                clock.GetUtcNow().UtcDateTime,
                DeliveryToken(Binding.SourceThreadId, delivery.Turn.TurnId)));
            capture.Acknowledge(delivery);
            return result;
        }
        catch
        {
            capture.Requeue(delivery);
            throw;
        }
    }

    private bool IsConnected() =>
        memory.GetState(Binding.Scope, includeArchived: true)?.Status == ConversationMemoryStatuses.Connected;

    private ConversationMemoryAppendResult AppendWithReplayRecovery(ConversationMemoryTurnAppend append)
    {
        try { return memory.AppendTurn(append); }
        catch (ConversationMemoryException error) when (error.Code == "CONVERSATION_MEMORY_REPLAY_CONFLICT")
        {
            // The journal can commit before the local checkpoint is acknowledged. A later
            // supported client upgrade must replay that receipt with its original provenance.
            // The unchanged store still verifies the complete binding/token/payload fingerprint.
            var journal = memory.GetState(Binding.Scope);
            if (journal is null) throw;
            IReadOnlyList<ConversationMemoryMessageDocument> retained;
            try
            {
                retained = memory.GetSourceMessages(Binding.Scope, journal.Revision,
                    append.Messages.Select(message => message.SourceMessageId).ToArray());
            }
            catch (ConversationMemoryException readError) when (readError.Code == "CONVERSATION_MEMORY_SOURCE_INVALID")
            {
                retained = [];
            }
            var provenance = retained.FirstOrDefault()?.CaptureProvenance;
            const string prefix = "codex-app-server/turn-items@";
            if (provenance == append.CaptureProvenance
                || (provenance != prefix + CodexCaptureVersions.SupportedCliVersion
                    && provenance != prefix + CodexCaptureVersions.SupportedDesktopVersion)
                || retained.Any(message => message.SourceTurnId != append.SourceTurnId
                    || message.CaptureProvenance != provenance || message.Status != "captured")
                || !retained.Select(message => new ConversationMemoryCapturedMessage(
                    message.SourceMessageId, message.Role, message.SourceKind, message.Text!, message.SourceAtUtc))
                    .SequenceEqual(append.Messages))
                throw;
            return memory.AppendTurn(append with { CaptureProvenance = provenance! });
        }
    }

    public static string DeliveryToken(string threadId, string turnId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        return "codex-turn." + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(threadId + "\n" + turnId)));
    }
}
