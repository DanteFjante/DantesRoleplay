using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.DataAccess.Bootstrap;

/// <summary>
/// One bootstrap contract, parsed from markdown.
///
/// Format — flat key/value front matter, then <c>## </c> sections:
///
/// <code>
/// ---
/// id: procedure.system.modify
/// category: system
/// name: Modify the application
/// ---
///
/// ## Description
/// One or two sentences.
///
/// ## Instructions
/// 1. Inspect the relevant subsystem.
///
/// ## Constraints
/// - Never bypass persistence APIs with arbitrary SQL.
/// </code>
///
/// Hand-parsed rather than pulling in a YAML dependency: the front matter is flat by design, and
/// ARCHITECTURE.md §3.11 puts a size budget on the kernel. Twenty lines beats a package here.
/// </summary>
public sealed record ProcedureFile(
    string Id,
    string Category,
    string Name,
    string Description,
    string Governs,
    string Instructions,
    string Constraints,
    ProcedureStatus Status)
{
    /// <summary>
    /// Content fingerprint. The seeder appends a new version only when this changes, so
    /// restarting the app fifty times does not produce fifty identical revisions.
    ///
    /// EVERY authored field must appear here, in constructor order. A field left out cannot be
    /// edited at all: changing it in the markdown yields the same hash, the seeder sees no
    /// change, and the edit is silently ignored forever. Governs was missing when it was first
    /// added, and was exactly that bug. A test now asserts this cannot happen again.
    /// </summary>
    public string ContentHash => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{Category}{Name}{Description}{Governs}{Instructions}{Constraints}{Status}")));

    public static ProcedureFile Parse(string text, string sourceName)
    {
        var normalised = text.Replace("\r\n", "\n").Trim();

        if (!normalised.StartsWith("---", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{sourceName}: expected front matter starting with ---.");
        }

        var end = normalised.IndexOf("\n---", 3, StringComparison.Ordinal);

        if (end < 0)
        {
            throw new InvalidOperationException($"{sourceName}: front matter is not closed with ---.");
        }

        var frontMatter = normalised[3..end];
        var body = normalised[(end + 4)..];

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in frontMatter.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf(':');

            if (separator <= 0)
            {
                throw new InvalidOperationException($"{sourceName}: cannot parse front matter line '{trimmed}'.");
            }

            fields[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }

        var sections = ParseSections(body);

        var id = Required(fields, "id", sourceName);
        var category = Required(fields, "category", sourceName);
        var name = Required(fields, "name", sourceName);

        var status = ProcedureStatus.Active;

        if (fields.TryGetValue("status", out var statusText)
            && !Enum.TryParse(statusText, ignoreCase: true, out status))
        {
            throw new InvalidOperationException($"{sourceName}: '{statusText}' is not a valid status.");
        }

        if (!sections.TryGetValue("Instructions", out var instructions) || instructions.Length == 0)
        {
            throw new InvalidOperationException($"{sourceName}: an Instructions section is required.");
        }

        sections.TryGetValue("Description", out var description);
        sections.TryGetValue("Constraints", out var constraints);
        fields.TryGetValue("governs", out var governs);

        return new ProcedureFile(
            id,
            category,
            name,
            description ?? string.Empty,
            governs ?? string.Empty,
            instructions,
            constraints ?? string.Empty,
            status);
    }

    private static Dictionary<string, string> ParseSections(string body)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? heading = null;
        var buffer = new StringBuilder();

        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush(sections, heading, buffer);
                heading = line[3..].Trim();
                buffer.Clear();
                continue;
            }

            buffer.AppendLine(line);
        }

        Flush(sections, heading, buffer);
        return sections;
    }

    private static void Flush(Dictionary<string, string> sections, string? heading, StringBuilder buffer)
    {
        if (heading is not null)
        {
            sections[heading] = buffer.ToString().Trim();
        }
    }

    private static string Required(Dictionary<string, string> fields, string key, string sourceName) =>
        fields.TryGetValue(key, out var value) && value.Length > 0
            ? value
            : throw new InvalidOperationException($"{sourceName}: front matter is missing '{key}'.");
}
