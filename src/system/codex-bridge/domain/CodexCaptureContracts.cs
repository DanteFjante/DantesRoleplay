using System.Text.Json;

namespace DantesRoleplay.CodexBridge;

/// <summary>Version and bounds for the separately deployed, read-only capture adapter.</summary>
public static class CodexCaptureVersions
{
    public const string SupportedCliVersion = "0.153.4";
}

public sealed record CodexCaptureOptions(
    string ExecutablePath,
    string RepositoryRoot,
    string PinnedVersion = CodexCaptureVersions.SupportedCliVersion,
    int MaximumLineBytes = 256 * 1024,
    TimeSpan? InitializationTimeout = null)
{
    public TimeSpan EffectiveInitializationTimeout => InitializationTimeout ?? TimeSpan.FromSeconds(10);
}

/// <summary>
/// A capture binding is deliberately local to one checked-out repository, project and linked
/// gameplay session. It is not a general Codex transcript subscription.
/// </summary>
public sealed record CodexGameplayCaptureBinding(
    string RepositoryRoot,
    string ProjectId,
    string GameplaySessionContextId,
    string ExternalCodexThreadId)
{
    public bool Matches(string codexSessionId, string cwd) =>
        !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(GameplaySessionContextId) &&
        !string.IsNullOrWhiteSpace(ExternalCodexThreadId) &&
        SamePath(RepositoryRoot, cwd) &&
        string.Equals(ExternalCodexThreadId, codexSessionId, StringComparison.Ordinal);

    private static bool SamePath(string left, string right)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), comparison);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

/// <summary>Untrusted hook input. transcript_path is retained only as correlation metadata and is never opened.</summary>
public sealed record CodexCaptureHookInput(
    string EventName,
    string SessionId,
    string Cwd,
    string TranscriptPath,
    string TurnId,
    string Prompt);

public sealed record CodexCaptureCorrelation(
    string SessionId,
    string Cwd,
    string TurnId,
    string Prompt,
    DateTimeOffset ReceivedAtUtc);

public sealed record CodexCaptureSpoolCheckpoint(
    IReadOnlyList<CodexCaptureCorrelation> Pending,
    IReadOnlyList<string> CompletedTurnIds,
    bool WatchInitialized = false);

/// <summary>
/// A synchronous bounded queue used by hooks. It never waits for Codex or reads transcript files;
/// callers can persist Snapshot() with their normal session checkpoint and Restore() after restart.
/// </summary>
public sealed class CodexCaptureCorrelationSpool
{
    private readonly int maximumPending;
    private readonly int maximumCompleted;
    private readonly Queue<CodexCaptureCorrelation> pending = new();
    private readonly Dictionary<string, CodexCaptureCorrelation> leased = new(StringComparer.Ordinal);
    private readonly HashSet<string> pendingIds = new(StringComparer.Ordinal);
    private readonly Queue<string> completedOrder = new();
    private readonly HashSet<string> completedIds = new(StringComparer.Ordinal);
    private bool watchInitialized;

    public CodexCaptureCorrelationSpool(int maximumPending = 64, int maximumCompleted = 256)
    {
        if (maximumPending is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(maximumPending));
        if (maximumCompleted is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(maximumCompleted));
        this.maximumPending = maximumPending;
        this.maximumCompleted = maximumCompleted;
    }

    public int Count => pending.Count + leased.Count;
    public bool WatchInitialized => watchInitialized;

    public bool TryEnqueue(CodexCaptureCorrelation correlation)
    {
        if (!ValidTurn(correlation.TurnId) || Count >= maximumPending ||
            pendingIds.Contains(correlation.TurnId) || leased.ContainsKey(correlation.TurnId)
            || completedIds.Contains(correlation.TurnId)) return false;
        pending.Enqueue(correlation);
        pendingIds.Add(correlation.TurnId);
        return true;
    }

    public bool TryDequeue(out CodexCaptureCorrelation? correlation)
    {
        if (pending.Count == 0) { correlation = null; return false; }
        correlation = pending.Dequeue();
        pendingIds.Remove(correlation.TurnId);
        return true;
    }

    public bool TryLease(out CodexCaptureCorrelation? correlation)
    {
        if (!TryDequeue(out correlation) || correlation is null) return false;
        leased.Add(correlation.TurnId, correlation);
        return true;
    }

    public void Requeue(CodexCaptureCorrelation correlation)
    {
        if (Count >= maximumPending || pendingIds.Contains(correlation.TurnId)
            || leased.ContainsKey(correlation.TurnId) || completedIds.Contains(correlation.TurnId)) return;
        pending.Enqueue(correlation);
        pendingIds.Add(correlation.TurnId);
    }

    public void RequeueLease(string turnId)
    {
        if (leased.Remove(turnId, out var correlation)) Requeue(correlation);
    }

    public void AcknowledgeLease(string turnId)
    {
        if (leased.Remove(turnId)) Complete(turnId);
    }

    public void Complete(string turnId)
    {
        if (!ValidTurn(turnId) || pendingIds.Contains(turnId) || leased.ContainsKey(turnId)
            || !completedIds.Add(turnId)) return;
        completedOrder.Enqueue(turnId);
        while (completedOrder.Count > maximumCompleted) completedIds.Remove(completedOrder.Dequeue());
    }

    public void InitializeWatch(IEnumerable<string> existingTurnIds)
    {
        ArgumentNullException.ThrowIfNull(existingTurnIds);
        foreach (var turnId in existingTurnIds) Complete(turnId);
        watchInitialized = true;
    }

    public CodexCaptureSpoolCheckpoint Snapshot() =>
        new(pending.Concat(leased.Values).ToArray(), completedOrder.ToArray(), watchInitialized);

    public static CodexCaptureCorrelationSpool Restore(CodexCaptureSpoolCheckpoint checkpoint,
        int maximumPending = 64, int maximumCompleted = 256)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.Pending is null || checkpoint.CompletedTurnIds is null
            || checkpoint.Pending.Count > maximumPending || checkpoint.CompletedTurnIds.Count > maximumCompleted
            || checkpoint.Pending.Any(value => value is null || !ValidTurn(value.TurnId))
            || checkpoint.Pending.Select(value => value.TurnId).Distinct(StringComparer.Ordinal).Count() != checkpoint.Pending.Count
            || checkpoint.CompletedTurnIds.Any(value => !ValidTurn(value))
            || checkpoint.CompletedTurnIds.Distinct(StringComparer.Ordinal).Count() != checkpoint.CompletedTurnIds.Count
            || checkpoint.Pending.Any(value => checkpoint.CompletedTurnIds.Contains(value.TurnId, StringComparer.Ordinal)))
            throw new CodexBridgeException("CODEX_CAPTURE_CHECKPOINT_INVALID",
                "The capture checkpoint is malformed or exceeds its configured bounds.");
        var spool = new CodexCaptureCorrelationSpool(maximumPending, maximumCompleted);
        spool.watchInitialized = checkpoint.WatchInitialized;
        foreach (var turnId in checkpoint.CompletedTurnIds) spool.Complete(turnId);
        foreach (var correlation in checkpoint.Pending)
            if (!spool.TryEnqueue(correlation))
                throw new CodexBridgeException("CODEX_CAPTURE_CHECKPOINT_INVALID",
                    "The capture checkpoint cannot be restored without losing a turn.");
        return spool;
    }

    private static bool ValidTurn(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200;
}

/// <summary>One real visible item from a Codex turn; content is never joined with another item.</summary>
public sealed record CodexCapturedMessage(
    string ExternalMessageId,
    string Role,
    string Content,
    string Classification,
    int Ordinal);
public sealed record CodexCapturedTurn(string ThreadId, string TurnId, IReadOnlyList<CodexCapturedMessage> Messages);
public sealed record CodexCaptureDelivery(CodexCaptureCorrelation Correlation, CodexCapturedTurn Turn);

public interface ICodexThreadReadClient
{
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);
    Task<JsonElement> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default);
    Task<JsonElement> ReadTurnAsync(string threadId, string turnId, CancellationToken cancellationToken = default) =>
        ReadThreadAsync(threadId, includeTurns: true, cancellationToken);
}

public interface ICodexThreadTurnDiscoveryClient
{
    Task<IReadOnlyList<string>> ListCompletedTurnIdsAsync(
        string threadId,
        string? afterTurnId,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads one exact, completed linked turn and accepts only real user and visible assistant text.</summary>
public sealed class CodexCaptureAdapter(
    ICodexThreadReadClient client,
    CodexGameplayCaptureBinding binding,
    CodexCaptureCorrelationSpool spool)
{
    public bool TryAcceptHook(CodexCaptureHookInput input, DateTimeOffset receivedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.EventName is not ("UserPromptSubmit" or "Stop") ||
            !binding.Matches(input.SessionId, input.Cwd) || string.IsNullOrWhiteSpace(input.TurnId)) return false;
        return spool.TryEnqueue(new(input.SessionId, input.Cwd, input.TurnId,
            input.EventName == "UserPromptSubmit" ? Bound(input.Prompt ?? string.Empty, 32_000) : string.Empty, receivedAtUtc));
    }

    public async Task<CodexCaptureDelivery?> CaptureNextAsync(CancellationToken cancellationToken = default)
    {
        if (!spool.TryLease(out var correlation) || correlation is null) return null;
        try
        {
            var version = await client.GetVersionAsync(cancellationToken);
            if (!string.Equals(version, CodexCaptureVersions.SupportedCliVersion, StringComparison.Ordinal))
                throw new CodexBridgeException("CODEX_CAPTURE_VERSION_UNSUPPORTED",
                    $"Codex {version} is installed; capture requires {CodexCaptureVersions.SupportedCliVersion}.");
            var response = await client.ReadTurnAsync(binding.ExternalCodexThreadId, correlation.TurnId, cancellationToken);
            var captured = CodexCaptureThreadParser.Parse(response, binding.ExternalCodexThreadId, correlation.TurnId);
            return new(correlation, captured);
        }
        catch
        {
            spool.RequeueLease(correlation.TurnId);
            throw;
        }
    }

    public void Acknowledge(CodexCaptureDelivery delivery) => spool.AcknowledgeLease(delivery.Correlation.TurnId);
    public void Requeue(CodexCaptureDelivery delivery) => spool.RequeueLease(delivery.Correlation.TurnId);

    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
}

public static class CodexCaptureThreadParser
{
    public const int MaximumMessagesPerTurn = 32;
    public const int MaximumMessageCharacters = 32_000;

    public static CodexCapturedTurn Parse(JsonElement response, string expectedThreadId, string expectedTurnId)
    {
        var thread = Object(response, "thread");
        if (!string.Equals(String(thread, "id"), expectedThreadId, StringComparison.Ordinal))
            throw Failure("CODEX_CAPTURE_THREAD_MISMATCH", "Codex returned a different thread.");
        var turns = Array(thread, "turns");
        var matches = turns.EnumerateArray().Where(turn => turn.ValueKind == JsonValueKind.Object &&
            string.Equals(OptionalString(turn, "id"), expectedTurnId, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw Failure("CODEX_CAPTURE_TURN_MISMATCH", "Codex did not return the requested turn exactly once.");
        var turn = matches[0];
        if (!string.Equals(OptionalString(turn, "status"), "completed", StringComparison.Ordinal))
            throw Failure("CODEX_CAPTURE_TURN_INCOMPLETE", "Codex returned a turn that is not completed.");
        var messages = new List<CodexCapturedMessage>();
        var ordinal = 0;
        foreach (var item in Array(turn, "items").EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var type = OptionalString(item, "type");
            var role = type == "userMessage" ? "user" : type == "agentMessage" ? "assistant" : string.Empty;
            if (string.IsNullOrEmpty(role) || !Visible(item)) continue;
            var content = role == "user" ? UserText(item) : OptionalString(item, "text");
            if (string.IsNullOrWhiteSpace(content) || content.Length > MaximumMessageCharacters)
                throw Failure("CODEX_CAPTURE_PROTOCOL_INVALID", "A visible message has invalid text.");
            var id = OptionalString(item, "id");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 200)
                throw Failure("CODEX_CAPTURE_PROTOCOL_INVALID", "A visible message has no valid identity.");
            if (messages.Count >= MaximumMessagesPerTurn)
                throw Failure("CODEX_CAPTURE_BATCH_OVERSIZE", "A completed turn has too many visible messages to capture atomically.");
            messages.Add(new(id, role, content, Classification(item), ordinal++));
        }
        if (messages.Count == 0) throw Failure("CODEX_CAPTURE_PROTOCOL_INVALID", "The completed turn has no visible messages.");
        return new(expectedThreadId, expectedTurnId, messages);
    }

    private static bool Visible(JsonElement item) =>
        !item.TryGetProperty("visibility", out var visibility) || visibility.ValueKind == JsonValueKind.String &&
        string.Equals(visibility.GetString(), "visible", StringComparison.Ordinal);
    private static string Classification(JsonElement item)
    {
        var value = OptionalString(item, "phase");
        if (value.Length == 0) value = OptionalString(item, "channel");
        return value switch { "final_answer" => "final", "commentary" => "commentary", _ => string.Empty };
    }
    private static string UserText(JsonElement item)
    {
        if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            throw Failure("CODEX_CAPTURE_PROTOCOL_INVALID", "A user message has no content array.");
        var text = content.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object &&
            string.Equals(OptionalString(value, "type"), "text", StringComparison.Ordinal))
            .Select(value => OptionalString(value, "text")).ToArray();
        if (text.Length != 1) throw Failure("CODEX_CAPTURE_PROTOCOL_INVALID", "A user message must contain exactly one text item.");
        return text[0];
    }
    private static JsonElement Object(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : throw Failure("CODEX_CAPTURE_PROTOCOL_INVALID", $"Codex omitted '{name}'.");
    private static JsonElement Array(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value : throw Failure("CODEX_CAPTURE_PROTOCOL_INVALID", $"Codex omitted '{name}'.");
    private static string String(JsonElement parent, string name) => OptionalString(parent, name) is { Length: > 0 } value ? value : throw Failure("CODEX_CAPTURE_PROTOCOL_INVALID", $"Codex omitted '{name}'.");
    private static string OptionalString(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static CodexBridgeException Failure(string code, string message) => new(code, message);
}
