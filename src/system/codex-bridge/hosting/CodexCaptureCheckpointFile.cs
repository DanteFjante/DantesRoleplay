using System.Text.Json;
using DantesRoleplay.CodexBridge;

namespace DantesRoleplay.DataAccess;

/// <summary>Bounded crash-safe persistence for the local capture delivery queue.</summary>
public static class CodexCaptureCheckpointFile
{
    private const int MaximumBytes = 512 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<FileStream> LockAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var lockPath = RequiredPath(path) + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                if (DateTime.UtcNow >= deadline)
                    throw new CodexBridgeException("CODEX_CAPTURE_CHECKPOINT_BUSY",
                        "The capture checkpoint is busy with another hook delivery.");
                await Task.Delay(25, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                    throw new CodexBridgeException("CODEX_CAPTURE_CHECKPOINT_BUSY",
                        "The capture checkpoint is busy with another hook delivery.");
                await Task.Delay(25, cancellationToken);
            }
        }
    }

    public static async Task<CodexCaptureSpoolCheckpoint> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = RequiredPath(path);
        if (!File.Exists(fullPath)) return new([], []);
        var info = new FileInfo(fullPath);
        if (info.Length > MaximumBytes) throw Failure("The capture checkpoint exceeds its byte bound.");
        try
        {
            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<CodexCaptureSpoolCheckpoint>(stream, JsonOptions,
                       cancellationToken)
                   ?? throw Failure("The capture checkpoint is empty.");
        }
        catch (JsonException)
        {
            throw Failure("The capture checkpoint is malformed.");
        }
    }

    public static async Task SaveAsync(
        string path,
        CodexCaptureSpoolCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var fullPath = RequiredPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(checkpoint, JsonOptions);
        if (bytes.Length > MaximumBytes) throw Failure("The capture checkpoint exceeds its byte bound.");
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string RequiredPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (Path.GetDirectoryName(fullPath) is null)
            throw Failure("The capture checkpoint path has no parent directory.");
        return fullPath;
    }

    private static CodexBridgeException Failure(string message) =>
        new("CODEX_CAPTURE_CHECKPOINT_INVALID", message);
}
