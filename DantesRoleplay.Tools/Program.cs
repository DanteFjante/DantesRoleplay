using DantesRoleplay.Tools;
using DantesRoleplay.Tools.Commands;

// Every tool that exists. Adding one is this line plus a file; there is no reflection scan,
// because a tool that appears in the list only if a naming convention was followed is a tool
// somebody will spend an afternoon failing to invoke.
ITool[] tools =
[
    new ExportTool(),
    new ImportTool(),
    new ValidateTool(),
    new VerifyTool(),
    new HashesTool(),
    new BackfillHashesTool()
];

var parsed = CommandLine.Parse(args);

if (parsed.ToolName is null or "help" or "--help" or "-h")
{
    return Help(tools, parsed.Arguments.FirstOrDefault());
}

var tool = tools.FirstOrDefault(t =>
    string.Equals(t.Name, parsed.ToolName, StringComparison.OrdinalIgnoreCase));

if (tool is null)
{
    Console.Error.WriteLine($"Unknown tool '{parsed.ToolName}'.");
    Console.Error.WriteLine();
    Help(tools, null, Console.Error);
    return 2;
}

string databasePath;

try
{
    databasePath = tool.RequiresDatabase
        ? DatabaseLocator.Resolve(parsed.Options.GetValueOrDefault("database"))
        : string.Empty;
}
catch (FileNotFoundException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

var context = new ToolContext(
    parsed.Arguments,
    parsed.Options,
    databasePath,
    Console.Out,
    Console.Error);

try
{
    return await tool.RunAsync(context, CancellationToken.None);
}
catch (Exception ex)
{
    // Tools are run by a person at a prompt, so an unhandled exception should read as a problem
    // rather than as a crash. The stack is still available behind --stack for the case where the
    // message is not enough.
    Console.Error.WriteLine($"{tool.Name} failed: {ex.Message}");

    if (parsed.Options.ContainsKey("stack"))
    {
        Console.Error.WriteLine(ex);
    }

    return 1;
}

static int Help(IReadOnlyList<ITool> tools, string? wanted, TextWriter? writer = null)
{
    var output = writer ?? Console.Out;

    if (wanted is not null)
    {
        var one = tools.FirstOrDefault(t => string.Equals(t.Name, wanted, StringComparison.OrdinalIgnoreCase));

        if (one is not null)
        {
            output.WriteLine(one.Usage);
            return 0;
        }
    }

    output.WriteLine("roleplay — developer tools for the DantesRoleplay catalog.");
    output.WriteLine();
    output.WriteLine("Usage: roleplay <tool> [options]");
    output.WriteLine();
    output.WriteLine("Tools:");

    foreach (var t in tools.OrderBy(t => t.Name, StringComparer.Ordinal))
    {
        output.WriteLine($"  {t.Name,-18} {t.Summary}");
    }

    output.WriteLine();
    output.WriteLine("Common options:");
    output.WriteLine("  --database <path>  SQLite file to work against. Defaults to the MCP server's");
    output.WriteLine("                     data/dantesroleplay.db, found by walking up from the");
    output.WriteLine("                     current directory. DANTESROLEPLAY_DB overrides that.");
    output.WriteLine("  --stack            Print the stack trace if a tool throws.");
    output.WriteLine();
    output.WriteLine("  roleplay help <tool>   Usage for one tool.");

    return 0;
}
