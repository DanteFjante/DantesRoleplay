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
    private const string Provenance = "codex-app-server/turn-items@0.153.4";
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
            var result = memory.AppendTurn(new(
                Binding,
                delivery.Turn.TurnId,
                messages,
                Provenance,
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

    public static string DeliveryToken(string threadId, string turnId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        return "codex-turn." + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(threadId + "\n" + turnId)));
    }
}
