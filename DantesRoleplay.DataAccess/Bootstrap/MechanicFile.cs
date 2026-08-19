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
    ///
    /// The hash itself lives in <see cref="DantesRoleplay.Content.ContentHash"/> rather than here.
    /// The stores compute the same fingerprint as they write, and a second definition of it would
    /// disagree the moment either was touched — silently, in both directions. Fully qualified
    /// because this property shadows the class name.
    ///
    /// This used to concatenate the fields with no separator at all, so ("ab", "c") and
    /// ("a", "bc") fingerprinted identically. ProcedureFile had been fixed for that; this had not,
    /// and nothing tested it.
    /// </summary>
    public string ContentHash => DantesRoleplay.Content.ContentHash.ForMechanic(
        Category, Name, Description, Matches, Requirements, Source, Scope, Status);

    /// <summary>
    /// Renders the markdown half of this rule — everything except the JavaScript.
    ///
    /// <see cref="Source"/> is written to a sibling .js by the caller. That split is the point of
    /// the catalog: the source becomes a real JavaScript file that an editor highlights, a linter
    /// reads and git diffs line by line, instead of an escaped string inside a payload. The rules
    /// authored through the escaped path measurably decayed — one went from 87 commented lines to
    /// 24 lines averaging 233 characters, with no comments left.
    ///
    /// Deliberately no option to inline the source. The embedded bootstrap rules keep their
    /// '## Source' section and <see cref="Parse"/> still reads it, but nothing should be able to
    /// choose the inline form for a file it is writing fresh.
    /// </summary>
    public string ToMarkdown()
    {
        var builder = new StringBuilder();

        MarkdownDocument.OpenFrontMatter(builder);
        MarkdownDocument.Field(builder, "id", Id);
        MarkdownDocument.Field(builder, "category", Category);
        MarkdownDocument.Field(builder, "name", Name);
        MarkdownDocument.Field(builder, "scope", Scope);
        MarkdownDocument.Field(builder, "status", Status.ToString().ToLowerInvariant());
        MarkdownDocument.CloseFrontMatter(builder);

        MarkdownDocument.Section(builder, "Description", Description);
        MarkdownDocument.Section(builder, "Matches", Matches);

        // Written verbatim rather than reformatted. Pretty-printing it here would change the
        // fingerprint of a rule that nobody edited, and every such rule would then read as drifted.
        MarkdownDocument.Section(builder, "Requirements", Requirements, fenceLanguage: "json");

        return builder.ToString();
    }

    /// <param name="sidecarSource">
    /// The contents of the sibling .js, for a catalog file whose source lives outside the markdown.
    /// Null for the embedded bootstrap rules, which carry a '## Source' section instead.
    /// </param>
    public static MechanicFile Parse(string text, string sourceName, string? sidecarSource = null)
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

        var hasSection = sections.TryGetValue("Source", out var source) && source.Length > 0;
        var hasSidecar = !string.IsNullOrWhiteSpace(sidecarSource);

        // Both present is an error rather than a precedence rule. Two places holding the source of
        // one rule is the failure this whole feature exists to prevent, and quietly preferring one
        // would mean an edit to the other vanished with nothing to see.
        if (hasSection && hasSidecar)
        {
            throw new InvalidOperationException(
                $"{sourceName}: the source is in both a '## Source' section and a sibling .js file. "
                + "Keep one — a catalog file uses the .js, an embedded bootstrap rule uses the "
                + "section.");
        }

        if (!hasSection && !hasSidecar)
        {
            throw new InvalidOperationException(
                $"{sourceName}: a Source section is required, or a sibling .js file alongside it.");
        }

        source = hasSidecar ? sidecarSource! : source!;

        sections.TryGetValue("Description", out var description);
        sections.TryGetValue("Matches", out var matches);
        sections.TryGetValue("Requirements", out var requirements);
        fields.TryGetValue("scope", out var scope);

        // A Requirements section that is present but empty, or an empty fence, has to land on the
        // same value the store writes for it. The store normalises blank requirements to "{}", so
        // a file that parsed to "" would fingerprint differently from the row it produced, and the
        // seeder would rewrite that rule on every single start without ever converging.
        var declared = Unfence(requirements ?? "{}");

        if (string.IsNullOrWhiteSpace(declared))
        {
            declared = "{}";
        }

        return new MechanicFile(
            Required(fields, "id", sourceName),
            Required(fields, "category", sourceName),
            Required(fields, "name", sourceName),
            description ?? string.Empty,
            matches ?? string.Empty,
            declared,
            hasSidecar ? source.Trim() : Unfence(source),
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
