using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tools;

/// <summary>
/// One developer tool.
///
/// This exists so the second tool costs a file rather than an argument-parsing rewrite. Catalog
/// export and import are the ones coming; the fingerprint tools below are here first because the
/// catalog cannot be reasoned about until every record has a fingerprint.
///
/// These are DEVELOPER tools, invoked from a checkout, and that boundary is the whole reason this
/// project exists rather than a fourth MCP verb. There are three tools on the MCP surface and there
/// will not be a fourth: import and export are operations a human performs against a database file
/// with a shell open, not moves an agent makes mid-session.
/// </summary>
public interface ITool
{
    /// <summary>What the user types, e.g. "hashes".</summary>
    string Name { get; }

    /// <summary>One line, shown in the tool list.</summary>
    string Summary { get; }

    /// <summary>Usage and options, shown by `roleplay help &lt;name&gt;`.</summary>
    string Usage { get; }

    /// <summary>
    /// Process exit code: 0 for success, non-zero for anything a script should stop on. Tools that
    /// report drift use this so CI can assert agreement without parsing the output.
    /// </summary>
    Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Everything a tool is given: the parsed command line, somewhere to write, and a way to open the
/// database. No service provider — these tools need a DbContext and nothing else, and building a
/// container for that would be more machinery than the thing it configures.
/// </summary>
/// <param name="Arguments">Positional arguments after the tool name.</param>
/// <param name="Options">Long options, e.g. --database path, with --flag stored as an empty value.</param>
/// <param name="DatabasePath">Resolved path to the SQLite file.</param>
/// <param name="Out">Where to write normal output.</param>
/// <param name="Error">Where to write problems.</param>
public sealed record ToolContext(
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Options,
    string DatabasePath,
    TextWriter Out,
    TextWriter Error)
{
    public bool HasFlag(string name) => Options.ContainsKey(name);

    public string? Option(string name) =>
        Options.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    /// <summary>
    /// Opens the database exactly as the application does, minus the initialisation. Nothing here
    /// migrates or seeds: a tool that silently upgraded the schema of the file it was pointed at
    /// would be a poor thing to run against a database you were trying to inspect.
    /// </summary>
    public DantesRoleplayDbContext OpenDatabase()
    {
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite($"Data Source={DatabasePath}")
            .Options;

        return new DantesRoleplayDbContext(options);
    }
}
