namespace DantesRoleplay.Tools;

/// <summary>
/// Finds the database, so that running a tool from anywhere in the checkout works.
///
/// Order: an explicit --database, then the DANTESROLEPLAY_DB environment variable, then a walk up
/// from the current directory looking for the MCP server's data file. The walk is what makes this
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
}
