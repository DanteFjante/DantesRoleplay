namespace DantesRoleplay.Tools;

/// <summary>
/// Argument parsing, kept to one small file on purpose.
///
/// The grammar is `roleplay &lt;tool&gt; [positional...] [--option value] [--flag]`. That is all
/// these tools need, and a parsing library would be the first PackageReference in a project whose
/// dependency list is currently the point.
/// </summary>
public static class CommandLine
{
    public static ParsedCommandLine Parse(string[] args)
    {
        string? toolName = null;
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var name = arg[2..];
                var inlineSeparator = name.IndexOf('=', StringComparison.Ordinal);

                if (inlineSeparator > 0)
                {
                    options[name[..inlineSeparator]] = name[(inlineSeparator + 1)..];
                    continue;
                }

                // A following token is this option's value unless it is itself an option, which is
                // what makes `--dry-run` and `--database path` both work without declaring which
                // options take values.
                var next = i + 1 < args.Length ? args[i + 1] : null;

                if (next is not null && !next.StartsWith("--", StringComparison.Ordinal))
                {
                    options[name] = next;
                    i++;
                }
                else
                {
                    options[name] = string.Empty;
                }

                continue;
            }

            if (toolName is null)
            {
                toolName = arg;
                continue;
            }

            positional.Add(arg);
        }

        return new ParsedCommandLine(toolName, positional, options);
    }
}

public sealed record ParsedCommandLine(
    string? ToolName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Options);
