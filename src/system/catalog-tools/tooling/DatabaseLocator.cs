using System.Text.Json;

namespace DantesRoleplay.Tools;

/// <summary>
/// Finds the database, so that running a tool from anywhere in the checkout works.
///
/// Order: an explicit --database, then the DANTESROLEPLAY_DB environment variable, then a walk up
/// from the current directory looking for the saved runtime selection, then the legacy data file. The walk makes this
/// usable — the alternative is every invocation carrying a relative path that is wrong from half
/// the directories in the repository.
///
/// A missing file is an error rather than a silently created empty one. These tools read and
/// correct existing catalogs; creating a database as a side effect of a typo'd path would produce
/// a clean, empty, entirely wrong report.
/// </summary>
public static class DatabaseLocator
{
    private static readonly string[] KnownRelativePaths =
    [
        Path.Combine("DantesRoleplay.MCPServer", "data", "dantesroleplay.db"),
        Path.Combine("data", "dantesroleplay.db")
    ];

    public static string Resolve(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var full = Path.GetFullPath(explicitPath);

            return File.Exists(full)
                ? full
                : throw new FileNotFoundException($"No database at '{full}'.", full);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("DANTESROLEPLAY_DB");

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            var full = Path.GetFullPath(fromEnvironment);

            return File.Exists(full)
                ? full
                : throw new FileNotFoundException(
                    $"DANTESROLEPLAY_DB points at '{full}', which does not exist.", full);
        }

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            var selected = ResolveSavedDatabase(directory.FullName);
            if (selected is not null) return selected;
            foreach (var relative in KnownRelativePaths)
            {
                var candidate = Path.Combine(directory.FullName, relative);

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not find dantesroleplay.db by walking up from "
            + $"'{Directory.GetCurrentDirectory()}'. Pass --database <path>, or set "
            + "DANTESROLEPLAY_DB.");
    }

    /// <summary>
    /// Resolves where a new database should be created. Unlike <see cref="Resolve"/>, the file is
    /// allowed not to exist; an explicit option and DANTESROLEPLAY_DB still take precedence.
    /// Without either, the repository root is found by its solution file and the normal MCP data
    /// path is returned.
    /// </summary>
    public static string ResolveTarget(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("DANTESROLEPLAY_DB");

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            var selected = ResolveSavedDatabase(directory.FullName);
            if (selected is not null) return selected;
            var existing = Path.Combine(directory.FullName, KnownRelativePaths[0]);

            if (File.Exists(existing) || File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
            {
                return existing;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find the DantesRoleplay repository root by walking up from "
            + $"'{Directory.GetCurrentDirectory()}'. Pass --database <path>, or set "
            + "DANTESROLEPLAY_DB.");
    }

    // Tools follow the same authoritative database selection as the launcher. A damaged
    // selection must not silently redirect a backup/import to an unrelated legacy database.
    internal static string? ResolveSavedDatabase(string directory)
    {
        foreach (var relative in new[]
        {
            Path.Combine("DantesRoleplay.MCPServer", "data", "runtime-launch.json"),
            Path.Combine("data", "runtime-launch.json")
        })
        {
            var profile = Path.Combine(directory, relative);
            if (!File.Exists(profile)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(profile));
                var root = document.RootElement;
                var database = root.GetProperty("database").GetString();
                if (root.GetProperty("schemaVersion").GetInt32() != 1
                    || string.IsNullOrWhiteSpace(database) || !Path.IsPathFullyQualified(database)
                    || !File.Exists(database))
                    throw new InvalidOperationException("The saved database path is missing or invalid.");
                return Path.GetFullPath(database);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException
                or KeyNotFoundException or FormatException or ArgumentException)
            {
                throw new InvalidOperationException(
                    $"Cannot resolve the selected database from '{profile}'. Restore that profile or supply --database explicitly.", exception);
            }
        }
        return null;
    }
}
