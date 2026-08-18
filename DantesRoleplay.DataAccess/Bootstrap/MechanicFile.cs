using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.DataAccess.Bootstrap;

/// <summary>
/// One bootstrap game rule, parsed from markdown.
///
/// Same format as <see cref="ProcedureFile"/> — flat front matter, then <c>## </c> sections — for
/// the same reason the stores mirror each other: one format to learn. The sections here are
/// Description, Matches, Requirements and Source, and the last two may be fenced so the file reads
/// properly in a markdown viewer.
///
/// These live under <c>Rules/</c> rather than <c>Bootstrap/</c> so that the two seeders cannot see
/// each other's files. A single folder with two formats in it would have each seeder failing to
/// parse the other's, which is a confusing way to discover a layout decision.
/// </summary>
public sealed record MechanicFile(
    string Id,
    string Category,
    string Name,
    string Description,
    string Matches,
    string Requirements,
    string Source,
    string Scope,
    MechanicStatus Status)
{
    /// <summary>
    /// Content fingerprint. EVERY authored field appears here, in constructor order — a field left
    /// out cannot be edited at all, because the hash would not move and the seeder would see no
    /// change. That exact bug happened once with a procedure's Governs field; a test now asserts
    /// this cannot happen again.
    /// </summary>
    public string ContentHash => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{Category}{Name}{Description}{Matches}{Requirements}{Source}{Scope}{Status}")));

    public static MechanicFile Parse(string text, string sourceName)
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

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in normalised[3..end].Split('\n'))
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

        var sections = ParseSections(normalised[(end + 4)..]);

        var status = MechanicStatus.Active;

        if (fields.TryGetValue("status", out var statusText)
            && !Enum.TryParse(statusText, ignoreCase: true, out status))
        {
            throw new InvalidOperationException($"{sourceName}: '{statusText}' is not a valid status.");
        }

        if (!sections.TryGetValue("Source", out var source) || source.Length == 0)
        {
            throw new InvalidOperationException($"{sourceName}: a Source section is required.");
        }

        sections.TryGetValue("Description", out var description);
        sections.TryGetValue("Matches", out var matches);
        sections.TryGetValue("Requirements", out var requirements);
        fields.TryGetValue("scope", out var scope);

        return new MechanicFile(
            Required(fields, "id", sourceName),
            Required(fields, "category", sourceName),
            Required(fields, "name", sourceName),
            description ?? string.Empty,
            matches ?? string.Empty,
            Unfence(requirements ?? "{}"),
            Unfence(source),
            scope ?? string.Empty,
            status);
    }

    /// <summary>
    /// Strips a surrounding markdown code fence. The fence is there so these files read as
    /// documents rather than as a wall of text, and it must not reach the parser or the engine.
    /// </summary>
    private static string Unfence(string value)
    {
        var trimmed = value.Trim();

        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstBreak = trimmed.IndexOf('\n');

        if (firstBreak < 0)
        {
            return trimmed;
        }

        var body = trimmed[(firstBreak + 1)..];
        var lastFence = body.LastIndexOf("```", StringComparison.Ordinal);

        return (lastFence < 0 ? body : body[..lastFence]).Trim();
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
