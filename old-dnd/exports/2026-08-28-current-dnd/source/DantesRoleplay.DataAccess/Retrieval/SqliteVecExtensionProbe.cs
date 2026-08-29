using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace DantesRoleplay.DataAccess.Retrieval;

public sealed class SqliteVecOptions
{
    public bool Enabled { get; init; }
    public string? ExtensionPath { get; init; }
    public string RequiredVersion { get; init; } = "v0.1.9";
    public string RequiredSha256 { get; init; } =
        "FCF98662A7AD9DCE394B96A88F91032047823831B951C76636787C312A6476E6";
}

public sealed record SqliteVecStatus(
    bool Ready,
    string Version = "",
    string ErrorCode = "",
    string ErrorMessage = "");

/// <summary>
/// Proves that the configured native sqlite-vec artifact can load into the same Microsoft.Data.Sqlite
/// provider used by the kernel. It creates no persistent table and changes no world state.
/// </summary>
public sealed class SqliteVecExtensionProbe(SqliteVecOptions options)
{
    public async Task<SqliteVecStatus> CheckAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
            return new(false, ErrorCode: "VECTOR_INDEX_DISABLED",
                ErrorMessage: "The sqlite-vec provider is disabled.");
        if (string.IsNullOrWhiteSpace(options.ExtensionPath))
            return new(false, ErrorCode: "VECTOR_EXTENSION_PATH_MISSING",
                ErrorMessage: "No sqlite-vec extension path is configured.");

        var fullPath = Path.GetFullPath(options.ExtensionPath);
        if (!File.Exists(fullPath))
            return new(false, ErrorCode: "VECTOR_EXTENSION_MISSING",
                ErrorMessage: $"The configured sqlite-vec extension does not exist: {fullPath}");

        if (string.IsNullOrWhiteSpace(options.RequiredVersion) ||
            string.IsNullOrWhiteSpace(options.RequiredSha256) ||
            options.RequiredSha256.Length != 64)
            return new(false, ErrorCode: "VECTOR_PIN_INVALID",
                ErrorMessage: "The sqlite-vec version and SHA-256 pin must both be configured.");

        var openedHere = connection.State != System.Data.ConnectionState.Open;
        try
        {
            await using (var stream = File.OpenRead(fullPath))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!string.Equals(hash, options.RequiredSha256, StringComparison.OrdinalIgnoreCase))
                    return new(false, ErrorCode: "VECTOR_EXTENSION_HASH_MISMATCH",
                        ErrorMessage: "The configured sqlite-vec extension does not match the pinned SHA-256.");
            }

            if (openedHere) await connection.OpenAsync(cancellationToken);
            connection.EnableExtensions(true);
            try
            {
                connection.LoadExtension(fullPath);
            }
            finally
            {
                connection.EnableExtensions(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT vec_version();";
            var version = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
            if (string.IsNullOrWhiteSpace(version))
                return new(false, ErrorCode: "VECTOR_EXTENSION_INVALID",
                    ErrorMessage: "sqlite-vec loaded but returned no version.");
            if (!string.Equals(version, options.RequiredVersion, StringComparison.Ordinal))
                return new(false, Version: version, ErrorCode: "VECTOR_EXTENSION_VERSION_MISMATCH",
                    ErrorMessage: $"sqlite-vec {version} loaded; required version is {options.RequiredVersion}.");
            return new(true, version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(false, ErrorCode: "VECTOR_EXTENSION_CANCELLED",
                ErrorMessage: "The sqlite-vec readiness check was cancelled.");
        }
        catch (Exception exception)
        {
            return new(false, ErrorCode: "VECTOR_EXTENSION_LOAD_FAILED",
                ErrorMessage: exception.Message.Length <= 500 ? exception.Message : exception.Message[..500]);
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}
