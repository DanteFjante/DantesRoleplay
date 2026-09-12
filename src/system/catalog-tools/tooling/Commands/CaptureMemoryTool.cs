using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using DantesRoleplay.CodexBridge;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Play;

namespace DantesRoleplay.Tools.Commands;

/// <summary>One-shot hook target for an explicitly linked Codex/gameplay session.</summary>
public sealed class CaptureMemoryTool : ITool
{
    private readonly Func<CodexCaptureOptions, ICodexThreadReadClient> clientFactory;
    private readonly Func<TextReader> input;

    public CaptureMemoryTool() : this(
        options => new CodexCaptureAppServerClient(options),
        () => Console.In)
    {
    }

    internal CaptureMemoryTool(
        Func<CodexCaptureOptions, ICodexThreadReadClient> clientFactory,
        Func<TextReader> input)
    {
        this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        this.input = input ?? throw new ArgumentNullException(nameof(input));
    }

    public string Name => "capture-memory";
    public string Summary => "Capture one linked Codex turn into the private conversation journal.";
    public string Usage => """
        Codex hook JSON is read from stdin. Configure every scope value in the hook command itself;
        hook JSON cannot select another owner, application, gameplay session, repository, or thread.

        roleplay capture-memory --principal <id> --application <id> --state-space <id>
          --gameplay-session <id> --project <id> --repository <absolute-path> --thread <codex-thread-id>
          --checkpoint <absolute-path> [--connect|--retry] [--wait-for-completion-ms <0..25000>]
          [--watch --watch-ms <100..3600000> --poll-ms <100..5000>]
          [--codex <executable>] [--database <path>]

        Run once with --connect and empty stdin to create or explicitly reconnect the binding.
        Run with --retry and empty stdin after the journal retry operation to deliver one saved item.
        Run --watch as a background process after connecting. It baselines the pinned thread, then
        discovers only later completed turn IDs until its fixed deadline and delivers them through
        the same durable checkpoint and journal service.
        Normal hook invocations omit --connect. The accepted Codex hook object uses hook_event_name,
        session_id, cwd, transcript_path, and turn_id, plus prompt for UserPromptSubmit.
        event_name is a compatibility alias and conflicts are rejected. transcript_path is correlation
        metadata only and is never opened. UserPromptSubmit
        checkpoints the turn; an asynchronous Stop hook can use a bounded completion wait before
        reading that exact completed turn through Codex app-server,
        commits the full visible message batch, then acknowledges the local queue item. Successful
        automatic hooks keep stdout empty; explicit --retry prints the retained receipt summary.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Count != 0) return Invalid(context, "capture-memory has no positional arguments.");
        var principal = Required(context, "principal");
        var application = Required(context, "application");
        var stateSpace = Required(context, "state-space");
        var gameplaySession = Required(context, "gameplay-session");
        var project = Required(context, "project");
        var repository = Required(context, "repository");
        var thread = Required(context, "thread");
        var checkpointPath = Required(context, "checkpoint");
        if (new[] { principal, application, stateSpace, gameplaySession, project, repository, thread, checkpointPath }
            .Any(value => value is null)) return 2;
        var connectRequested = context.HasFlag("connect");
        var retryRequested = context.HasFlag("retry");
        var watchRequested = context.HasFlag("watch");
        if ((connectRequested ? 1 : 0) + (retryRequested ? 1 : 0) + (watchRequested ? 1 : 0) > 1)
            return Invalid(context, "capture-memory accepts only one of --connect, --retry, and --watch.");
        var completionWaitMilliseconds = 0;
        if (context.Option("wait-for-completion-ms") is { } rawCompletionWait
            && (!int.TryParse(rawCompletionWait, NumberStyles.None, CultureInfo.InvariantCulture,
                    out completionWaitMilliseconds)
                || completionWaitMilliseconds is < 0 or > 25_000))
            return Invalid(context, "--wait-for-completion-ms must be an integer from 0 through 25000.");
        if (watchRequested)
        {
            if (!TryBoundedInteger(context, "watch-ms", 100, 3_600_000, out var watchMilliseconds)
                || !TryBoundedInteger(context, "poll-ms", 100, 5_000, out var pollMilliseconds, 1_000))
                return 2;
            return await RunWatchAsync(context, principal!, application!, stateSpace!, gameplaySession!,
                project!, repository!, thread!, checkpointPath!, watchMilliseconds, pollMilliseconds,
                cancellationToken);
        }

        await using var checkpointLock = await CodexCaptureCheckpointFile.LockAsync(checkpointPath!, cancellationToken);
        var checkpoint = await CodexCaptureCheckpointFile.LoadAsync(checkpointPath!, cancellationToken);
        var spool = CodexCaptureCorrelationSpool.Restore(checkpoint);
        var captureBinding = new CodexGameplayCaptureBinding(
            Path.GetFullPath(repository!), project!, gameplaySession!, thread!);
        var client = clientFactory(new(context.Option("codex") ?? "codex", captureBinding.RepositoryRoot));
        if (completionWaitMilliseconds > 0)
            client = new CompletionWaitingCodexThreadReadClient(
                client, TimeSpan.FromMilliseconds(completionWaitMilliseconds));
        var adapter = new CodexCaptureAdapter(
            client,
            captureBinding,
            spool);
        var memoryBinding = new ConversationMemoryBinding(
            new(principal!, application!, stateSpace!, gameplaySession!),
            "codex", project!, captureBinding.RepositoryRoot, thread!);
        await using var db = context.OpenDatabase();
        var memory = new ApplicationConversationMemoryStore(db);
        var service = new ConversationMemoryCodexCaptureService(
            memory, memoryBinding, adapter, captureBinding);
        var hookJson = await input().ReadToEndAsync(cancellationToken);
        if (connectRequested)
        {
            service.Connect();
            if (string.IsNullOrWhiteSpace(hookJson)) return 0;
        }
        if (retryRequested && !string.IsNullOrWhiteSpace(hookJson))
            return Invalid(context, "capture-memory --retry requires empty stdin.");

        CodexCaptureHookInput? hook = null;
        if (!retryRequested)
        {
            try { hook = CodexCaptureHookInputParser.Parse(hookJson); }
            catch (Exception error) when (error is JsonException or InvalidOperationException)
            {
                context.Error.WriteLine($"CODEX_CAPTURE_HOOK_INVALID: {error.Message}");
                return 2;
            }
        }

        var journal = memory.GetState(service.Binding.Scope, includeArchived: true);
        if (retryRequested && journal?.Status == ConversationMemoryStatuses.RetryPending)
            journal = memory.Retry(service.Binding.Scope);
        if (journal?.Status != ConversationMemoryStatuses.Connected)
        {
            context.Error.WriteLine(
                "CONVERSATION_MEMORY_NOT_CONNECTED: Connect the exact journal binding before accepting hooks.");
            return 3;
        }

        var prior = spool.Snapshot();
        if (hook is not null)
        {
            var known = prior.Pending.Any(value => value.TurnId == hook.TurnId)
                || prior.CompletedTurnIds.Contains(hook.TurnId, StringComparer.Ordinal);
            if (!service.TryAcceptHook(hook) && !known)
            {
                context.Error.WriteLine(
                    "CODEX_CAPTURE_SCOPE_DENIED: The hook did not match the configured repository and thread.");
                return 3;
            }
        }
        await CodexCaptureCheckpointFile.SaveAsync(checkpointPath!, spool.Snapshot(), cancellationToken);
        if (!retryRequested && hook!.EventName != "Stop") return 0;

        try
        {
            var captured = await service.CaptureNextAsync(cancellationToken);
            if (captured is not null && retryRequested)
                context.Out.WriteLine($"{captured.ReceiptId}\t{captured.Messages.Count}\t{captured.Journal.Revision}");
            return 0;
        }
        finally
        {
            await CodexCaptureCheckpointFile.SaveAsync(checkpointPath!, spool.Snapshot(), CancellationToken.None);
        }
    }

    private async Task<int> RunWatchAsync(
        ToolContext context,
        string principal,
        string application,
        string stateSpace,
        string gameplaySession,
        string project,
        string repository,
        string thread,
        string checkpointPath,
        int watchMilliseconds,
        int pollMilliseconds,
        CancellationToken cancellationToken)
    {
        var captureBinding = new CodexGameplayCaptureBinding(
            Path.GetFullPath(repository), project, gameplaySession, thread);
        var client = clientFactory(new(context.Option("codex") ?? "codex", captureBinding.RepositoryRoot));
        if (client is not ICodexThreadTurnDiscoveryClient discovery)
            return Invalid(context, "capture-memory --watch requires a client with bounded turn discovery.");
        var memoryBinding = new ConversationMemoryBinding(
            new(principal, application, stateSpace, gameplaySession),
            "codex", project, captureBinding.RepositoryRoot, thread);
        var elapsed = Stopwatch.StartNew();
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using (var checkpointLock = await CodexCaptureCheckpointFile.LockAsync(
                             checkpointPath, cancellationToken))
            {
                var checkpoint = await CodexCaptureCheckpointFile.LoadAsync(checkpointPath, cancellationToken);
                var spool = CodexCaptureCorrelationSpool.Restore(checkpoint);
                var anchor = spool.WatchInitialized ? checkpoint.CompletedTurnIds.LastOrDefault() : null;
                var completedTurnIds = await discovery.ListCompletedTurnIdsAsync(
                    thread, anchor, initializeBaseline: !spool.WatchInitialized, cancellationToken);
                await using var db = context.OpenDatabase();
                var memory = new ApplicationConversationMemoryStore(db);
                var journal = memory.GetState(memoryBinding.Scope, includeArchived: true);
                if (journal?.Status == ConversationMemoryStatuses.Disconnected) return 0;
                if (journal?.Status != ConversationMemoryStatuses.Connected)
                {
                    context.Error.WriteLine(
                        "CONVERSATION_MEMORY_NOT_CONNECTED: Connect the exact journal binding before watching.");
                    return 3;
                }
                if (!spool.WatchInitialized)
                {
                    spool.InitializeWatch(completedTurnIds);
                    await CodexCaptureCheckpointFile.SaveAsync(checkpointPath, spool.Snapshot(), cancellationToken);
                }
                else
                {
                    foreach (var turnId in completedTurnIds)
                    {
                        var before = spool.Snapshot();
                        var known = before.Pending.Any(value => value.TurnId == turnId)
                            || before.CompletedTurnIds.Contains(turnId, StringComparer.Ordinal);
                        if (!spool.TryEnqueue(new(thread, captureBinding.RepositoryRoot, turnId, string.Empty,
                                DateTimeOffset.UtcNow)) && !known)
                            throw new CodexBridgeException("CODEX_CAPTURE_QUEUE_FULL",
                                "The completed-turn watcher cannot enqueue another turn without exceeding its bound.");
                    }
                    await CodexCaptureCheckpointFile.SaveAsync(checkpointPath, spool.Snapshot(), cancellationToken);
                    var adapter = new CodexCaptureAdapter(client, captureBinding, spool);
                    var service = new ConversationMemoryCodexCaptureService(
                        memory, memoryBinding, adapter, captureBinding);
                    try
                    {
                        while (spool.Count > 0)
                            await service.CaptureNextAsync(cancellationToken);
                    }
                    finally
                    {
                        await CodexCaptureCheckpointFile.SaveAsync(
                            checkpointPath, spool.Snapshot(), CancellationToken.None);
                    }
                }
            }
            var remaining = TimeSpan.FromMilliseconds(watchMilliseconds) - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            var delay = TimeSpan.FromMilliseconds(pollMilliseconds);
            await Task.Delay(remaining < delay ? remaining : delay, cancellationToken);
        } while (elapsed.ElapsedMilliseconds < watchMilliseconds);
        return 0;
    }

    private static bool TryBoundedInteger(
        ToolContext context,
        string name,
        int minimum,
        int maximum,
        out int value,
        int? defaultValue = null)
    {
        if (context.Option(name) is not { } raw)
        {
            if (defaultValue is { } fallback) { value = fallback; return true; }
            value = 0;
            context.Error.WriteLine($"capture-memory --watch needs --{name}.");
            return false;
        }
        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value >= minimum && value <= maximum) return true;
        context.Error.WriteLine($"--{name} must be an integer from {minimum} through {maximum}.");
        return false;
    }

    private static string? Required(ToolContext context, string name)
    {
        var value = context.Option(name);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        context.Error.WriteLine($"capture-memory needs --{name}.");
        return null;
    }

    private static int Invalid(ToolContext context, string message)
    {
        context.Error.WriteLine(message);
        context.Error.WriteLine("Run `roleplay help capture-memory` for usage.");
        return 2;
    }
}

internal sealed class CompletionWaitingCodexThreadReadClient(
    ICodexThreadReadClient inner,
    TimeSpan maximumWait) : ICodexThreadReadClient
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(250);

    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) =>
        inner.GetVersionAsync(cancellationToken);

    public Task<JsonElement> ReadThreadAsync(string threadId, bool includeTurns,
        CancellationToken cancellationToken = default) =>
        inner.ReadThreadAsync(threadId, includeTurns, cancellationToken);

    public async Task<JsonElement> ReadTurnAsync(string threadId, string turnId,
        CancellationToken cancellationToken = default)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var response = await inner.ReadTurnAsync(threadId, turnId, cancellationToken);
            try
            {
                _ = CodexCaptureThreadParser.Parse(response, threadId, turnId);
                return response;
            }
            catch (CodexBridgeException error) when (
                error.Code == "CODEX_CAPTURE_TURN_INCOMPLETE" && elapsed.Elapsed < maximumWait)
            {
                var remaining = maximumWait - elapsed.Elapsed;
                await Task.Delay(remaining < RetryInterval ? remaining : RetryInterval, cancellationToken);
            }
        }
    }
}

internal static class CodexCaptureHookInputParser
{
    internal static CodexCaptureHookInput Parse(string json)
    {
        if (json.Length > 0 && json[0] == '\uFEFF') json = json[1..];
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Hook input must be an object.");
        return new(
            EventName(root),
            RequiredString(root, "session_id"),
            RequiredString(root, "cwd"),
            OptionalString(root, "transcript_path"),
            RequiredString(root, "turn_id"),
            OptionalString(root, "prompt"));
    }

    private static string EventName(JsonElement root)
    {
        var current = OptionalString(root, "hook_event_name");
        var compatibility = OptionalString(root, "event_name");
        if (current.Length > 0 && compatibility.Length > 0
            && current != compatibility)
            throw new InvalidOperationException(
                "hook_event_name conflicts with the event_name compatibility alias.");
        return current.Length > 0 ? current
            : compatibility.Length > 0 ? compatibility
            : throw new InvalidOperationException("hook_event_name is required.");
    }

    private static string RequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException($"{name} is required.");

    private static string OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;
}
