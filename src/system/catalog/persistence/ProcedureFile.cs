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
    ProcedureStatus Status,
    string CreatedBy = "",
    string ChangeNote = "",
    string Matches = "")
{
    /// <summary>
    /// Content fingerprint. The seeder appends a new version only when this changes, so
    /// restarting the app fifty times does not produce fifty identical revisions.
    ///
    /// EVERY authored field must appear here, in constructor order. A field left out cannot be
    /// edited at all: changing it in the markdown yields the same hash, the seeder sees no
    /// change, and the edit is silently ignored forever. Governs was missing when it was first
    /// added, and was exactly that bug. A test now asserts this cannot happen again.
    ///
    /// The hash itself lives in <see cref="DantesRoleplay.Content.ContentHash"/> rather than here.
    /// The stores compute the same fingerprint as they write, and a second definition of it would
    /// disagree the moment either was touched — silently, in both directions. Fully qualified
    /// because this property shadows the class name.
    /// </summary>
    /// <remarks>
    /// <c>Matches</c> is deliberately absent, and it is the one exception to the rule above.
    /// Phrases change how a contract is <em>found</em>, never what it says or governs, and their
    /// consumer — the catalog navigation record that feeds retrieval — reads them from the file at
    /// activation rather than from this fingerprint. So a phrase-only edit still takes effect on
    /// re-activation, which is where it matters.
    ///
    /// The cost of including them would be paid by every procedure at once: the field list is
    /// joined positionally, so adding one moves all 78 fingerprints, marks every record changed,
    /// and un-suppresses the near-duplicate review warning for each — 63 of them, measured. That
    /// baseline cannot currently be re-recorded, because `roleplay export` fails on unrelated
    /// stored state (`mechanic.lock.pick` sits in an unregistered namespace).
    ///
    /// The residue: the database copy of Matches refreshes only when some other field also
    /// changes. Retrieval is unaffected; a stale row is.
    /// </remarks>
    public string ContentHash => DantesRoleplay.Content.ContentHash.ForProcedure(
        Category, Name, Description, Governs, Instructions, Constraints, Status);

    /// <summary>
    /// Renders this contract back to the markdown <see cref="Parse"/> reads.
    ///
    /// One file, unlike a mechanic: a contract is entirely prose. Instructions are a numbered list
    /// and Constraints a bullet list, which is markdown already — encoding them as JSON would
    /// re-escape every line break and put the manual back behind the same wall the rules were
    /// stuck behind.
    /// </summary>
    public string ToMarkdown()
    {
        var builder = new StringBuilder();

        MarkdownDocument.OpenFrontMatter(builder);
        MarkdownDocument.Field(builder, "id", Id);
        MarkdownDocument.Field(builder, "category", Category);
        MarkdownDocument.Field(builder, "name", Name);
        MarkdownDocument.Field(builder, "governs", Governs);
        MarkdownDocument.Field(builder, "status", Status.ToString().ToLowerInvariant());
        MarkdownDocument.TextField(builder, "createdBy", CreatedBy);
        MarkdownDocument.TextField(builder, "changeNote", ChangeNote);
        MarkdownDocument.CloseFrontMatter(builder);

        MarkdownDocument.Section(builder, "Description", Description);
        MarkdownDocument.Section(builder, "Matches", Matches);
        MarkdownDocument.Section(builder, "Instructions", Instructions);
        MarkdownDocument.Section(builder, "Constraints", Constraints);

        return builder.ToString().TrimEnd() + "\n";
    }

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
        sections.TryGetValue("Matches", out var matches);
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
            status,
            MarkdownDocument.ReadTextField(fields, "createdBy"),
            MarkdownDocument.ReadTextField(fields, "changeNote"),
            matches ?? string.Empty);
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
