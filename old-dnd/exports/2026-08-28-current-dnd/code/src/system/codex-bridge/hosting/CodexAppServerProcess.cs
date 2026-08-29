using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Assistants;
using DantesRoleplay.CodexBridge;

namespace DantesRoleplay.DataAccess;

public sealed class CodexAppServerProcessFactory(CodexBridgeOptions options) : ICodexAppServerFactory
{
    private readonly SemaphoreSlim probeGate = new(1, 1);
    private CodexBridgeStatus? cachedStatus;
    private DateTime cachedAtUtc;

    public async Task<CodexBridgeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (cachedStatus is not null && DateTime.UtcNow - cachedAtUtc < TimeSpan.FromSeconds(15))
            return cachedStatus;
        await probeGate.WaitAsync(cancellationToken);
        try
        {
            if (cachedStatus is not null && DateTime.UtcNow - cachedAtUtc < TimeSpan.FromSeconds(15))
                return cachedStatus;
            cachedStatus = await ProbeAsync(cancellationToken);
            cachedAtUtc = DateTime.UtcNow;
            return cachedStatus;
        }
        finally { probeGate.Release(); }
    }

    public async Task<ICodexAppServerSession> CreateAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.Ready) throw new CodexBridgeException(status.ErrorCode, status.ErrorMessage);
        return await CodexAppServerProcessSession.StartAsync(options, cancellationToken);
    }

    private async Task<CodexBridgeStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process { StartInfo = StartInfo("--version") };
            if (!process.Start()) return Status(false, "", "CODEX_PROCESS_UNAVAILABLE", "The Codex executable did not start.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.EffectiveInitializationTimeout);
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                return Status(false, "", "CODEX_PROCESS_TIMEOUT",
                    "The Codex executable did not answer the version probe in time.");
            }
            var output = Bound((await outputTask).Trim(), 200);
            var error = Bound((await errorTask).Trim(), 500);
            if (process.ExitCode != 0)
                return Status(false, "", "CODEX_PROCESS_UNAVAILABLE",
                    string.IsNullOrWhiteSpace(error) ? "The Codex version probe failed." : error);
            const string prefix = "codex-cli ";
            var observed = output.StartsWith(prefix, StringComparison.Ordinal) ? output[prefix.Length..].Trim() : output;
            if (!string.Equals(observed, options.PinnedVersion, StringComparison.Ordinal))
                return Status(false, observed, "CODEX_VERSION_UNSUPPORTED",
                    $"Codex {observed} is installed; this bridge is pinned to {options.PinnedVersion}.");
            return Status(true, observed, "", "");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return Status(false, "", "CODEX_PROCESS_UNAVAILABLE",
                "The configured Codex executable cannot be started. Install an accessible Codex CLI or set Codex:ExecutablePath.");
        }
    }

    private CodexBridgeStatus Status(bool ready, string observed, string code, string message) => new(
        ready, "codex", options.PinnedVersion, observed, options.RepositoryRoot,
        "read-only-approval-gated", false, code, message, options.Model);

    private ProcessStartInfo StartInfo(string argument)
    {
        var info = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            WorkingDirectory = options.RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add(argument);
        return info;
    }

    private static string Bound(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];
}

internal sealed class CodexAppServerProcessSession : ICodexAppServerSession
{
    private readonly CodexBridgeOptions options;
    private readonly Process process;
    private readonly StreamWriter input;
    private readonly BoundedLineReader output;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly Queue<CodexProtocolEvent> pending = new();
    private readonly ConcurrentDictionary<string, PendingApproval> pendingApprovals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> startedFileChanges = new(StringComparer.Ordinal);
    private readonly Task<string> stderr;
    private long nextRequestId;
    private long diagnosticId;
    private string? threadId;
    private string? turnId;
    private bool disposed;

    private CodexAppServerProcessSession(CodexBridgeOptions options, Process process)
    {
        this.options = options;
        this.process = process;
        input = process.StandardInput;
        input.AutoFlush = true;
        output = new(process.StandardOutput.BaseStream, options.MaximumLineBytes);
        stderr = ReadBoundedAsync(process.StandardError, 4_000);
    }

    public static async Task<ICodexAppServerSession> StartAsync(
        CodexBridgeOptions options, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            WorkingDirectory = options.RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add("app-server");
        info.ArgumentList.Add("--stdio");
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw Failure("CODEX_PROCESS_UNAVAILABLE", "The Codex app-server process did not start.");
            var session = new CodexAppServerProcessSession(options, process);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.EffectiveInitializationTimeout);
            await session.CallAsync("initialize", new
            {
                clientInfo = new { name = "dantes-roleplay-web", title = "DantesRoleplay web control center", version = "slice-9" }
            }, timeout.Token);
            await session.NotifyAsync("initialized", timeout.Token);
            return session;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    public async Task<CodexTurnStartResult> StartTurnAsync(
        string? externalThreadId, string message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var threadResult = externalThreadId is null
            ? await CallAsync("thread/start", BuildThreadParameters(options, null), cancellationToken)
            : await CallAsync("thread/resume", BuildThreadParameters(options, externalThreadId), cancellationToken);

        var thread = RequiredObject(threadResult, "thread");
        threadId = RequiredString(thread, "id");
        if (externalThreadId is not null && threadId != externalThreadId)
            throw Failure("CODEX_THREAD_MISMATCH", "Codex resumed a different thread identifier.");
        var model = OptionalString(threadResult, "model");
        var modelProvider = OptionalString(threadResult, "modelProvider");

        var turnResult = await CallAsync(
            "turn/start", BuildTurnParameters(options, threadId, message), cancellationToken);
        var turn = RequiredObject(turnResult, "turn");
        turnId = RequiredString(turn, "id");
        var status = OptionalString(turn, "status");
        return new(threadId, turnId, model, modelProvider, string.IsNullOrWhiteSpace(status) ? "inProgress" : status);
    }

    public async IAsyncEnumerable<CodexProtocolEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (threadId is null || turnId is null) throw new InvalidOperationException("The Codex turn has not started.");
        while (true)
        {
            CodexProtocolEvent? next = pending.Count == 0 ? null : pending.Dequeue();
            if (next is null)
            {
                JsonElement message;
                if (TryNextApprovalExpiry(out var expiringRequestId, out var delay))
                {
                    using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    expiry.CancelAfter(delay);
                    try { message = await ReadMessageAsync(expiry.Token); }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        next = new("approval-expired", ExternalRequestId: expiringRequestId);
                        message = default;
                    }
                }
                else message = await ReadMessageAsync(cancellationToken);
                if (next is not null)
                {
                    yield return next;
                    continue;
                }
                if (message.TryGetProperty("method", out var methodProperty))
                {
                    var method = methodProperty.GetString() ?? string.Empty;
                    if (message.TryGetProperty("id", out var serverRequestId))
                    {
                        next = TryCreateApproval(serverRequestId, method,
                            message.TryGetProperty("params", out var requestParameters) ? requestParameters : default);
                        if (next is null)
                        {
                            await DenyServerRequestAsync(serverRequestId, method, cancellationToken);
                            next = new("activity", Activity: new(
                                $"server-request-{Interlocked.Increment(ref diagnosticId)}", "error", "denied",
                                Bound($"Denied unsupported app-server request {method}.", 500)));
                        }
                    }
                    else
                    {
                        if (method == "item/started" && message.TryGetProperty("params", out var startedParameters))
                            CaptureStartedItem(startedParameters);
                        next = NormalizeNotification(method, message.TryGetProperty("params", out var parameters)
                            ? parameters : default);
                        if (next?.Type == "approval-resolved") pendingApprovals.TryRemove(next.ExternalRequestId, out _);
                    }
                }
            }
            if (next is null) continue;
            yield return next;
            if (next.Type == "terminal") yield break;
        }
    }

    private bool TryNextApprovalExpiry(out string externalRequestId, out TimeSpan delay)
    {
        var next = pendingApprovals
            .Where(item => Volatile.Read(ref item.Value.Responded) == 0)
            .OrderBy(item => item.Value.ExpiresAtUtc)
            .FirstOrDefault();
        if (next.Value is null)
        {
            externalRequestId = string.Empty;
            delay = default;
            return false;
        }
        externalRequestId = next.Key;
        delay = next.Value.ExpiresAtUtc - DateTime.UtcNow;
        if (delay < TimeSpan.FromMilliseconds(1)) delay = TimeSpan.FromMilliseconds(1);
        return true;
    }

    public async Task RespondApprovalAsync(
        string externalRequestId, string decision, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (decision is not (CodexApprovalDecisions.Accept or CodexApprovalDecisions.Decline or
            CodexApprovalDecisions.Cancel))
            throw Failure("CODEX_APPROVAL_DECISION_INVALID", "The Codex approval decision is invalid.");
        if (!pendingApprovals.TryGetValue(externalRequestId, out var approval))
            throw Failure("CODEX_APPROVAL_SESSION_UNKNOWN", "The Codex approval is not pending in this process.");
        if (decision == CodexApprovalDecisions.Accept && !approval.CanAccept)
            throw Failure("CODEX_APPROVAL_NOT_ACCEPTABLE",
                "The Codex request cannot be accepted within the repository safety boundary.");
        if (Interlocked.Exchange(ref approval.Responded, 1) != 0)
            throw Failure("CODEX_APPROVAL_ALREADY_DISPATCHED", "The Codex approval response was already dispatched.");

        await WriteAsync(BuildApprovalResponse(
            approval.RequestId, approval.Kind, decision, approval.RequestedPermissions), cancellationToken);
        if (decision == CodexApprovalDecisions.Cancel && approval.Kind == CodexApprovalKinds.Permissions)
            await InterruptAsync(cancellationToken);
    }

    public Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        if (threadId is null || turnId is null || disposed) return Task.CompletedTask;
        return SendRequestWithoutWaitingAsync("turn/interrupt", new { threadId, turnId }, cancellationToken);
    }

    private async Task<JsonElement> CallAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref nextRequestId);
        await WriteAsync(BuildRequest(id, method, parameters), cancellationToken);
        while (true)
        {
            var message = await ReadMessageAsync(cancellationToken);
            if (message.TryGetProperty("method", out var methodProperty))
            {
                var incomingMethod = methodProperty.GetString() ?? string.Empty;
                if (message.TryGetProperty("id", out var serverRequestId))
                {
                    // Approval requests before turn/start has returned cannot be safely correlated
                    // to the durable local turn, so they remain fail-closed.
                    await DenyServerRequestAsync(serverRequestId, incomingMethod, cancellationToken);
                }
                else
                {
                    if (incomingMethod == "item/started" &&
                        message.TryGetProperty("params", out var startedParameters))
                        CaptureStartedItem(startedParameters);
                    var normalized = NormalizeNotification(incomingMethod,
                        message.TryGetProperty("params", out var notificationParams) ? notificationParams : default);
                    if (normalized is not null) pending.Enqueue(normalized);
                }
                continue;
            }
            if (!message.TryGetProperty("id", out var responseId) || !ResponseIdEquals(responseId, id))
                continue;
            if (message.TryGetProperty("error", out var error))
                throw Failure("CODEX_PROTOCOL_ERROR", Bound(JsonText(error), 500));
            if (!message.TryGetProperty("result", out var result))
                throw Failure("CODEX_PROTOCOL_INVALID", $"Codex returned no result for {method}.");
            return result.Clone();
        }
    }

    private Task SendRequestWithoutWaitingAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref nextRequestId);
        return WriteAsync(BuildRequest(id, method, parameters), cancellationToken);
    }

    private Task NotifyAsync(string method, CancellationToken cancellationToken) =>
        WriteAsync(BuildNotification(method), cancellationToken);

    private Task DenyServerRequestAsync(JsonElement id, string method, CancellationToken cancellationToken) =>
        WriteAsync(BuildDeniedResponse(id, method), cancellationToken);

    private async Task WriteAsync(string line, CancellationToken cancellationToken)
    {
        if (Encoding.UTF8.GetByteCount(line) > options.MaximumLineBytes)
            throw Failure("CODEX_PROTOCOL_OVERSIZE", "An outgoing Codex protocol message exceeded the line bound.");
        await writeGate.WaitAsync(cancellationToken);
        try { await input.WriteLineAsync(line.AsMemory(), cancellationToken); }
        catch (IOException exception) { throw Failure("CODEX_PROCESS_EXITED", Bound(exception.Message, 400)); }
        finally { writeGate.Release(); }
    }

    private async Task<JsonElement> ReadMessageAsync(CancellationToken cancellationToken)
    {
        string? line;
        try { line = await output.ReadLineAsync(cancellationToken); }
        catch (InvalidDataException exception) { throw Failure("CODEX_PROTOCOL_OVERSIZE", exception.Message); }
        if (line is null)
        {
            var diagnostic = process.HasExited ? await stderr : string.Empty;
            throw Failure("CODEX_PROCESS_EXITED", string.IsNullOrWhiteSpace(diagnostic)
                ? "The Codex app-server process exited before the turn completed."
                : Bound(diagnostic, 500));
        }
        return ParseProtocolLine(line, options.MaximumLineBytes);
    }

    private CodexProtocolEvent? TryCreateApproval(
        JsonElement requestId, string method, JsonElement parameters)
    {
        if (method is not ("item/commandExecution/requestApproval" or
            "item/fileChange/requestApproval" or "item/permissions/requestApproval") ||
            parameters.ValueKind != JsonValueKind.Object || threadId is null || turnId is null)
            return null;
        try
        {
            if (RequiredString(parameters, "threadId") != threadId ||
                RequiredString(parameters, "turnId") != turnId)
                return null;
            var externalRequestId = CanonicalRequestId(requestId);
            var itemId = Bound(RequiredString(parameters, "itemId"), 200);
            var externalApprovalId = Bound(OptionalString(parameters, "approvalId"), 200);
            var reason = DisplayText(OptionalString(parameters, "reason"), 500);
            CodexProtocolApprovalRequest normalized;
            JsonElement requestedPermissions = default;

            if (method == "item/commandExecution/requestApproval")
            {
                var command = DisplayText(OptionalString(parameters, "command"), 2_000);
                var cwd = OptionalString(parameters, "cwd");
                var relativeCwd = ".";
                var cwdSafe = string.IsNullOrWhiteSpace(cwd) || TryRepositoryRelative(cwd, out relativeCwd);
                var workingDirectory = string.IsNullOrWhiteSpace(cwd) ? "." :
                    cwdSafe ? relativeCwd : "outside repository";
                var network = parameters.TryGetProperty("networkApprovalContext", out var networkElement) &&
                    networkElement.ValueKind == JsonValueKind.Object;
                var host = network ? DisplayText(OptionalString(networkElement, "host"), 253) : string.Empty;
                var protocol = network ? DisplayText(OptionalString(networkElement, "protocol"), 20) : string.Empty;
                var networkValid = !network || !string.IsNullOrWhiteSpace(host) &&
                    protocol is "http" or "https" or "socks5Tcp" or "socks5Udp";
                var kind = network ? CodexApprovalKinds.Network : CodexApprovalKinds.Command;
                var canAccept = cwdSafe && networkValid && (network || !string.IsNullOrWhiteSpace(command));
                var summary = network
                    ? $"Allow one {protocol} connection to {host}."
                    : $"Run one command: {Bound(command, 300)}";
                var details = new CodexApprovalDetails(
                    reason, command, workingDirectory, [], host, protocol,
                    network ? [$"Network {protocol} to {host}"] : []);
                normalized = ApprovalRequest(
                    externalRequestId, itemId, externalApprovalId, kind, method, parameters,
                    summary, details, canAccept);
            }
            else if (method == "item/fileChange/requestApproval")
            {
                var paths = new List<string>();
                var canAccept = startedFileChanges.TryGetValue(itemId, out var proposed) && proposed.Length is > 0 and <= 20;
                if (proposed is not null)
                {
                    foreach (var path in proposed.Take(20))
                    {
                        if (TryRepositoryRelative(path, out var relative)) paths.Add(relative);
                        else { paths.Add("outside repository"); canAccept = false; }
                    }
                }
                var summary = paths.Count == 0
                    ? "Codex requested a file change without a bounded proposal."
                    : $"Change {paths.Count} repository file(s): {string.Join(", ", paths.Take(5))}.";
                normalized = ApprovalRequest(
                    externalRequestId, itemId, externalApprovalId, CodexApprovalKinds.FileChange,
                    method, parameters, summary,
                    new(reason, "", ".", paths, "", "", ["Write proposed repository files"]),
                    canAccept);
            }
            else
            {
                if (!parameters.TryGetProperty("permissions", out var permissionElement) ||
                    permissionElement.ValueKind != JsonValueKind.Object)
                    return null;
                requestedPermissions = permissionElement.Clone();
                var (permissions, canAccept) = NormalizePermissions(permissionElement);
                var summary = permissions.Count == 0
                    ? "Codex requested an unsupported or empty permission set."
                    : $"Grant for this turn: {string.Join(", ", permissions.Take(5))}.";
                normalized = ApprovalRequest(
                    externalRequestId, itemId, externalApprovalId, CodexApprovalKinds.Permissions,
                    method, parameters, summary,
                    new(reason, "", ".", [], "", "", permissions), canAccept);
            }

            var state = new PendingApproval(
                requestId.Clone(), normalized.Kind, normalized.CanAccept, requestedPermissions, normalized,
                DateTime.UtcNow.Add(options.EffectiveApprovalTimeout));
            if (!pendingApprovals.TryAdd(externalRequestId, state))
            {
                var existing = pendingApprovals[externalRequestId];
                if (existing.Normalized.RequestFingerprint != normalized.RequestFingerprint)
                    throw Failure("CODEX_APPROVAL_REQUEST_MISMATCH",
                        "Codex reused an approval request identity with different content.");
                normalized = existing.Normalized;
            }
            return new("approval", Approval: normalized);
        }
        catch (CodexBridgeException exception) when (exception.Code != "CODEX_APPROVAL_REQUEST_MISMATCH")
        {
            // Malformed supported requests are denied by the caller and never reach durable state.
            return null;
        }
        catch (CodexBridgeException) { throw; }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or JsonException)
        {
            return null;
        }
    }

    private CodexProtocolApprovalRequest ApprovalRequest(
        string externalRequestId,
        string itemId,
        string externalApprovalId,
        string kind,
        string method,
        JsonElement parameters,
        string summary,
        CodexApprovalDetails details,
        bool canAccept)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            method + "\0" + parameters.GetRawText())));
        return new(externalRequestId, itemId, externalApprovalId, kind, fingerprint,
            Bound(DisplayText(summary, 500), 500), details, canAccept);
    }

    private void CaptureStartedItem(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object ||
            OptionalString(item, "type") != "fileChange")
            return;
        var itemId = OptionalString(item, "id");
        if (string.IsNullOrWhiteSpace(itemId) || itemId.Length > 200 ||
            !item.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
            return;
        var paths = changes.EnumerateArray().Take(21)
            .Select(change => OptionalString(change, "path"))
            .Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (startedFileChanges.Count >= 64 && !startedFileChanges.ContainsKey(itemId)) return;
        startedFileChanges[itemId] = paths;
    }

    private (IReadOnlyList<string> Permissions, bool CanAccept) NormalizePermissions(JsonElement value)
    {
        var descriptions = new List<string>();
        var supported = true;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name == "network")
            {
                if (property.Value.ValueKind == JsonValueKind.Null) continue;
                if (property.Value.ValueKind != JsonValueKind.Object) { supported = false; continue; }
                foreach (var networkProperty in property.Value.EnumerateObject())
                {
                    if (networkProperty.Name != "enabled" || networkProperty.Value.ValueKind is not
                        (JsonValueKind.True or JsonValueKind.False)) { supported = false; continue; }
                    if (networkProperty.Value.GetBoolean()) descriptions.Add("network access");
                }
            }
            else if (property.Name == "fileSystem")
            {
                if (property.Value.ValueKind == JsonValueKind.Null) continue;
                if (property.Value.ValueKind != JsonValueKind.Object) { supported = false; continue; }
                foreach (var fileProperty in property.Value.EnumerateObject())
                {
                    if (fileProperty.Name == "entries" && fileProperty.Value.ValueKind == JsonValueKind.Array)
                    {
                        var entries = fileProperty.Value.EnumerateArray().Take(21).ToArray();
                        if (entries.Length > 20) supported = false;
                        foreach (var entry in entries.Take(20))
                        {
                            if (entry.ValueKind != JsonValueKind.Object ||
                                OptionalString(entry, "access") is not ("read" or "write") ||
                                !entry.TryGetProperty("path", out var pathObject) ||
                                pathObject.ValueKind != JsonValueKind.Object ||
                                OptionalString(pathObject, "type") != "path" ||
                                !pathObject.TryGetProperty("path", out var pathValue) ||
                                pathValue.ValueKind != JsonValueKind.String ||
                                !TryRepositoryRelative(pathValue.GetString() ?? string.Empty, out var relative))
                            { supported = false; continue; }
                            descriptions.Add($"{OptionalString(entry, "access")} {relative}");
                        }
                    }
                    else if (fileProperty.Name is "read" or "write")
                    {
                        if (fileProperty.Value.ValueKind == JsonValueKind.Array && fileProperty.Value.GetArrayLength() > 0)
                            supported = false;
                    }
                    else if (fileProperty.Name == "globScanMaxDepth")
                    {
                        if (fileProperty.Value.ValueKind != JsonValueKind.Null) supported = false;
                    }
                    else if (fileProperty.Value.ValueKind != JsonValueKind.Null) supported = false;
                }
            }
            else if (property.Value.ValueKind != JsonValueKind.Null) supported = false;
        }
        return (descriptions.Take(20).ToArray(), supported && descriptions.Count is > 0 and <= 20);
    }

    private bool TryRepositoryRelative(string path, out string relative)
    {
        relative = string.Empty;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RepositoryRoot));
            var candidate = Path.GetFullPath(path, root);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!candidate.Equals(root, comparison) &&
                !candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                return false;
            relative = Path.GetRelativePath(root, candidate).Replace('\\', '/');
            if (relative == ".") relative = ".";
            return relative.Length <= 500;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal static string CanonicalRequestId(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.String when !string.IsNullOrWhiteSpace(id.GetString()) && id.GetString()!.Length <= 190 =>
            "string:" + id.GetString(),
        JsonValueKind.Number when id.TryGetInt64(out var number) =>
            "number:" + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw Failure("CODEX_APPROVAL_REQUEST_INVALID", "Codex emitted an invalid approval request identity.")
    };

    private static string DisplayText(string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sanitized = new string(value.Select(character => char.IsControl(character) &&
            character is not ('\n' or '\t') ? ' ' : character).ToArray()).Trim();
        return Bound(sanitized, maximum);
    }

    internal static CodexProtocolEvent? NormalizeNotification(string method, JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object) return null;
        if (method == "serverRequest/resolved" && parameters.TryGetProperty("requestId", out var requestId))
            return new("approval-resolved", ExternalRequestId: CanonicalRequestId(requestId));
        if (method == "item/agentMessage/delta")
            return new("delta", Delta: Bound(OptionalString(parameters, "delta"), 8_000));
        if (method == "turn/completed")
        {
            var turn = RequiredObject(parameters, "turn");
            var status = OptionalString(turn, "status");
            var error = turn.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null
                ? Bound(JsonText(errorElement), 500) : string.Empty;
            return new("terminal", Status: status, ErrorCode: status == "failed" ? "CODEX_TURN_FAILED" : "",
                ErrorMessage: error);
        }
        if (method is "warning" or "error")
        {
            var message = OptionalString(parameters, "message");
            if (string.IsNullOrWhiteSpace(message)) message = Bound(JsonText(parameters), 500);
            return new("activity", Activity: new(
                $"{method}-{Guid.NewGuid():n}", method, method, Bound(message, 500)));
        }
        if (method != "item/completed" || !parameters.TryGetProperty("item", out var item) ||
            item.ValueKind != JsonValueKind.Object)
            return null;
        var type = OptionalString(item, "type");
        var id = OptionalString(item, "id");
        if (type == "agentMessage") return new("reply", Reply: Bound(OptionalString(item, "text"), 8_001));
        var statusValue = OptionalString(item, "status");
        return type switch
        {
            "commandExecution" => ActivityEvent(id, "command", statusValue,
                $"{OptionalString(item, "command")} ({statusValue})"),
            "fileChange" => ActivityEvent(id, "file-change", statusValue,
                SummarizeFileChanges(item, statusValue)),
            "mcpToolCall" => ActivityEvent(id, "mcp-tool", statusValue,
                $"{OptionalString(item, "server")}/{OptionalString(item, "tool")} ({statusValue})"),
            "dynamicToolCall" => ActivityEvent(id, "dynamic-tool", statusValue,
                $"{OptionalString(item, "tool")} ({statusValue})"),
            "webSearch" => ActivityEvent(id, "web-search", "completed",
                $"Web search: {OptionalString(item, "query")}"),
            _ => null // Includes reasoning and all other non-visible/internal items.
        };
    }

    internal static JsonElement BuildThreadParameters(CodexBridgeOptions options, string? externalThreadId)
    {
        var value = externalThreadId is null
            ? JsonSerializer.SerializeToElement(new
            {
                cwd = options.RepositoryRoot,
                approvalPolicy = "on-request",
                sandbox = "read-only",
                model = options.Model,
                serviceName = "dantes-roleplay-web"
            })
            : JsonSerializer.SerializeToElement(new
            {
                threadId = externalThreadId,
                cwd = options.RepositoryRoot,
                approvalPolicy = "on-request",
                sandbox = "read-only"
            });
        return value;
    }

    internal static JsonElement BuildTurnParameters(
        CodexBridgeOptions options, string externalThreadId, string message) =>
        JsonSerializer.SerializeToElement(new
        {
            threadId = externalThreadId,
            input = new[] { new { type = "text", text = message } },
            cwd = options.RepositoryRoot,
            approvalPolicy = "on-request",
            sandboxPolicy = new { type = "readOnly", networkAccess = false }
        });

    internal static string BuildRequest(long id, string method, object parameters) =>
        JsonSerializer.Serialize(new { id, method, @params = parameters });

    internal static string BuildNotification(string method) =>
        JsonSerializer.Serialize(new { method });

    internal static string BuildResultResponse(JsonElement id, object result) =>
        JsonSerializer.Serialize(new
        {
            id = JsonSerializer.Deserialize<object>(id.GetRawText()),
            result
        });

    internal static string BuildApprovalResponse(
        JsonElement id, string kind, string decision, JsonElement requestedPermissions)
    {
        object result = kind == CodexApprovalKinds.Permissions
            ? new
            {
                permissions = decision == CodexApprovalDecisions.Accept
                    ? JsonSerializer.Deserialize<object>(requestedPermissions.GetRawText())
                    : new { },
                scope = "turn"
            }
            : new { decision };
        return BuildResultResponse(id, result);
    }

    internal static string BuildDeniedResponse(JsonElement id, string method) =>
        JsonSerializer.Serialize(new
        {
            id = JsonSerializer.Deserialize<object>(id.GetRawText()),
            error = new { code = -32001, message = $"{method} is unsupported by the bounded web bridge." }
        });

    internal static JsonElement ParseProtocolLine(string line, int maximumBytes)
    {
        if (Encoding.UTF8.GetByteCount(line) > maximumBytes)
            throw Failure("CODEX_PROTOCOL_OVERSIZE", "A Codex protocol line exceeded 256 KiB.");
        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Failure("CODEX_PROTOCOL_INVALID", "Codex emitted an invalid JSON protocol line.");
        }
    }

    private static CodexProtocolEvent ActivityEvent(string id, string kind, string status, string summary) =>
        new("activity", Activity: new(
            string.IsNullOrWhiteSpace(id) ? $"{kind}-{Guid.NewGuid():n}" : Bound(id, 200),
            kind, string.IsNullOrWhiteSpace(status) ? "completed" : Bound(status, 30),
            Bound(string.IsNullOrWhiteSpace(summary) ? kind : summary, 500)));

    private static string SummarizeFileChanges(JsonElement item, string status)
    {
        if (!item.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
            return $"File changes ({status}).";
        var paths = changes.EnumerateArray().Select(change => OptionalString(change, "path"))
            .Where(path => !string.IsNullOrWhiteSpace(path)).Take(5).ToArray();
        return $"{changes.GetArrayLength()} file change(s){(paths.Length == 0 ? "" : ": " + string.Join(", ", paths))} ({status}).";
    }

    private static bool ResponseIdEquals(JsonElement id, long expected) =>
        id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number) && number == expected ||
        id.ValueKind == JsonValueKind.String && id.GetString() == expected.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static JsonElement RequiredObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value : throw Failure("CODEX_PROTOCOL_INVALID", $"Codex omitted required object '{name}'.");
    private static string RequiredString(JsonElement parent, string name)
    {
        var value = OptionalString(parent, name);
        return string.IsNullOrWhiteSpace(value)
            ? throw Failure("CODEX_PROTOCOL_INVALID", $"Codex omitted required string '{name}'.") : value;
    }
    private static string OptionalString(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static string JsonText(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty : value.GetRawText();
    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
    private static CodexBridgeException Failure(string code, string message) => new(code, message);

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try { input.Close(); } catch (IOException) { }
        if (!process.HasExited)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); } catch (Exception) { }
        process.Dispose();
        writeGate.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum)
    {
        var buffer = new char[512];
        var builder = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer);
            if (read == 0) break;
            var retained = Math.Min(read, maximum - builder.Length);
            if (retained > 0) builder.Append(buffer, 0, retained);
        }
        return builder.ToString().Trim();
    }

    private sealed class BoundedLineReader(Stream stream, int maximumBytes)
    {
        private readonly byte[] buffer = new byte[4096];
        private int position;
        private int length;

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            using var line = new MemoryStream();
            while (true)
            {
                if (position == length)
                {
                    length = await stream.ReadAsync(buffer, cancellationToken);
                    position = 0;
                    if (length == 0) return line.Length == 0 ? null : Decode(line);
                }
                var value = buffer[position++];
                if (value == (byte)'\n') return Decode(line);
                if (value != (byte)'\r') line.WriteByte(value);
                if (line.Length > maximumBytes)
                    throw new InvalidDataException("A Codex protocol line exceeded 256 KiB.");
            }
        }

        private static string Decode(MemoryStream line) => Encoding.UTF8.GetString(line.GetBuffer(), 0, checked((int)line.Length));
    }

    private sealed class PendingApproval(
        JsonElement requestId,
        string kind,
        bool canAccept,
        JsonElement requestedPermissions,
        CodexProtocolApprovalRequest normalized,
        DateTime expiresAtUtc)
    {
        public JsonElement RequestId { get; } = requestId;
        public string Kind { get; } = kind;
        public bool CanAccept { get; } = canAccept;
        public JsonElement RequestedPermissions { get; } = requestedPermissions;
        public CodexProtocolApprovalRequest Normalized { get; } = normalized;
        public DateTime ExpiresAtUtc { get; } = expiresAtUtc;
        public int Responded;
    }
}
