using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DantesRoleplay.CodexBridge;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Minimal app-server client for capture. Its only operational request is thread/read; it never
/// lists threads, starts/resumes a thread, starts a turn, or opens a hook transcript path.
/// </summary>
public sealed class CodexCaptureAppServerClient(CodexCaptureOptions options) : ICodexThreadReadClient
{
    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        using var process = new Process { StartInfo = StartInfo("--version", false) };
        if (!process.Start()) throw Failure("CODEX_PROCESS_UNAVAILABLE", "The Codex executable did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.EffectiveInitializationTimeout);
        var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw Failure("CODEX_PROCESS_TIMEOUT", "The Codex version probe timed out.");
        }
        if (process.ExitCode != 0) throw Failure("CODEX_PROCESS_UNAVAILABLE", Bound(await standardError, 500));
        var output = (await standardOutput).Trim();
        const string prefix = "codex-cli ";
        return output.StartsWith(prefix, StringComparison.Ordinal) ? output[prefix.Length..].Trim() : output;
    }

    public async Task<JsonElement> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(threadId) || threadId.Length > 200)
            throw Failure("CODEX_CAPTURE_THREAD_INVALID", "A capture thread identifier is required.");
        if (!includeTurns) throw Failure("CODEX_CAPTURE_PROTOCOL_INVALID", "Capture always requires turns.");
        using var process = new Process { StartInfo = StartInfo("app-server", true), EnableRaisingEvents = true };
        if (!process.Start()) throw Failure("CODEX_PROCESS_UNAVAILABLE", "The Codex app-server process did not start.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.EffectiveInitializationTimeout);
            var input = process.StandardInput;
            input.AutoFlush = true;
            var output = process.StandardOutput;
            await SendAsync(input, 1, "initialize", new
            {
                clientInfo = new { name = "dantes-roleplay-capture", version = "capture-v1" },
                capabilities = new { experimentalApi = true }
            }, timeout.Token);
            await ReadResponseAsync(output, 1, timeout.Token);
            await input.WriteLineAsync(JsonSerializer.Serialize(new { method = "initialized" }).AsMemory(), timeout.Token);
            await SendAsync(input, 2, "thread/read", new { threadId, includeTurns = true }, timeout.Token);
            return await ReadResponseAsync(output, 2, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure("CODEX_PROCESS_TIMEOUT", "Codex thread/read timed out.");
        }
        finally
        {
            try { process.StandardInput.Close(); } catch (IOException) { }
            if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
        }
    }

    private ProcessStartInfo StartInfo(string firstArgument, bool appServer)
    {
        var info = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            WorkingDirectory = options.RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardInput = appServer,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add(firstArgument);
        if (appServer) info.ArgumentList.Add("--stdio");
        return info;
    }

    private async Task SendAsync(StreamWriter input, long id, string method, object parameters, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(new { id, method, @params = parameters });
        if (Encoding.UTF8.GetByteCount(json) > options.MaximumLineBytes)
            throw Failure("CODEX_PROTOCOL_OVERSIZE", "A Codex request exceeded the configured line bound.");
        await input.WriteLineAsync(json.AsMemory(), cancellationToken);
    }

    private async Task<JsonElement> ReadResponseAsync(StreamReader output, long expectedId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await output.ReadLineAsync(cancellationToken);
            if (line is null) throw Failure("CODEX_PROCESS_EXITED", "The Codex app-server process exited before responding.");
            if (Encoding.UTF8.GetByteCount(line) > options.MaximumLineBytes)
                throw Failure("CODEX_PROTOCOL_OVERSIZE", "A Codex response exceeded the configured line bound.");
            JsonDocument document;
            try { document = JsonDocument.Parse(line); }
            catch (JsonException) { throw Failure("CODEX_PROTOCOL_INVALID", "Codex returned invalid JSON."); }
            using (document)
            {
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var value) || value != expectedId)
                    continue; // Notifications cannot influence capture.
                if (root.TryGetProperty("error", out var error)) throw Failure("CODEX_PROTOCOL_ERROR", Bound(error.GetRawText(), 500));
                if (!root.TryGetProperty("result", out var result)) throw Failure("CODEX_PROTOCOL_INVALID", "Codex returned no result.");
                return result.Clone();
            }
        }
    }

    private static string Bound(string value, int maximum) => string.IsNullOrWhiteSpace(value) ? "Codex process failed." : value.Length <= maximum ? value : value[..maximum];
    private static CodexBridgeException Failure(string code, string message) => new(code, message);
}
