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
          --checkpoint <absolute-path> [--connect|--retry] [--codex <executable>] [--database <path>]

        Run once with --connect and empty stdin to create or explicitly reconnect the binding.
        Run with --retry and empty stdin after the journal retry operation to deliver one saved item.
        Normal hook invocations omit --connect. The accepted hook object uses event_name, session_id, cwd, transcript_path, turn_id, and
        prompt. transcript_path is correlation metadata only and is never opened. UserPromptSubmit
        checkpoints the turn; Stop reads that exact completed turn through Codex app-server,
        commits the full visible message batch, then acknowledges the local queue item.
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
        if (context.HasFlag("connect") && context.HasFlag("retry"))
            return Invalid(context, "capture-memory accepts only one of --connect and --retry.");

        await using var checkpointLock = await CodexCaptureCheckpointFile.LockAsync(checkpointPath!, cancellationToken);
        var checkpoint = await CodexCaptureCheckpointFile.LoadAsync(checkpointPath!, cancellationToken);
        var spool = CodexCaptureCorrelationSpool.Restore(checkpoint);
        var captureBinding = new CodexGameplayCaptureBinding(
            Path.GetFullPath(repository!), project!, gameplaySession!, thread!);
        var adapter = new CodexCaptureAdapter(
            clientFactory(new(context.Option("codex") ?? "codex", captureBinding.RepositoryRoot)),
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
        var connectRequested = context.HasFlag("connect");
        var retryRequested = context.HasFlag("retry");
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
            if (captured is not null)
                context.Out.WriteLine($"{captured.ReceiptId}\t{captured.Messages.Count}\t{captured.Journal.Revision}");
            return 0;
        }
        finally
        {
            await CodexCaptureCheckpointFile.SaveAsync(checkpointPath!, spool.Snapshot(), CancellationToken.None);
        }
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

internal static class CodexCaptureHookInputParser
{
    internal static CodexCaptureHookInput Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Hook input must be an object.");
        return new(
            RequiredString(root, "event_name"),
            RequiredString(root, "session_id"),
            RequiredString(root, "cwd"),
            OptionalString(root, "transcript_path"),
            RequiredString(root, "turn_id"),
            OptionalString(root, "prompt"));
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
