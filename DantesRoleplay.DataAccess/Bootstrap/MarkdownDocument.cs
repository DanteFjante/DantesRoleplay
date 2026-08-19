using System.Text;

namespace DantesRoleplay.DataAccess.Bootstrap;

/// <summary>
/// Writes the markdown that <see cref="MechanicFile"/> and <see cref="ProcedureFile"/> read.
///
/// It lives beside those parsers rather than in the exporter on purpose: a format is two halves,
/// and separating them is how the reader and the writer drift until a file that was just written
/// no longer parses. Anything added to one half belongs in the other in the same change.
///
/// Everything is written with LF and no trailing spaces, whatever platform it is written on, so a
/// catalog exported on Windows and one exported on Linux are byte-identical. That is the same
/// reason <see cref="DantesRoleplay.Content.ContentHash"/> normalises line endings, and it matters
/// more here: these files go into git, and a diff that is entirely invisible line endings is a
/// diff nobody reviews.
/// </summary>
internal static class MarkdownDocument
{
    /// <summary>
    /// One front-matter line.
    ///
    /// The parser splits on the first colon and takes the rest of the LINE, so a value containing
    /// a newline would silently lose everything after it and a value containing a leading '#'
    /// would be skipped as a comment. Both are rejected here rather than written and discovered
    /// later, because the loss happens on the way back in, not on the way out.
    /// </summary>
    public static void Field(StringBuilder builder, string name, string? value)
    {
        var text = Content.ContentHash.Normalise(value);

        if (text.Length == 0)
        {
            return;
        }

        if (text.Contains('\n', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Front matter field '{name}' contains a line break. Front matter is one line per "
                + "field — move multi-line content into a '## ' section.");
        }

        if (text.StartsWith('#'))
        {
            throw new InvalidOperationException(
                $"Front matter field '{name}' starts with '#', which the parser treats as a "
                + "comment and skips.");
        }

        builder.Append(name).Append(": ").Append(text).Append('\n');
    }

    /// <summary>
    /// One <c>## </c> section, optionally fenced.
    ///
    /// A body line beginning with "## " would be read back as the start of the next section,
    /// silently truncating this one and inventing another. A fenced body containing a fence would
    /// close early. Neither is recoverable once written, so both throw.
    /// </summary>
    public static void Section(StringBuilder builder, string heading, string? body, string? fenceLanguage = null)
    {
        var text = Content.ContentHash.Normalise(body);

        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The '{heading}' section contains a line starting with '## ', which would be "
                    + "read back as the start of a new section. Indent it, or drop one '#'.");
            }
        }

        if (fenceLanguage is not null && text.Contains("```", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The '{heading}' section contains a code fence and is itself fenced, so it would "
                + "be read back truncated at the inner fence.");
        }

        builder.Append("## ").Append(heading).Append('\n');

        if (fenceLanguage is not null)
        {
            builder.Append("```").Append(fenceLanguage).Append('\n');
        }

        if (text.Length > 0)
        {
            builder.Append(text).Append('\n');
        }

        if (fenceLanguage is not null)
        {
            builder.Append("```\n");
        }

        builder.Append('\n');
    }

    /// <summary>Opens the front matter block.</summary>
    public static void OpenFrontMatter(StringBuilder builder) => builder.Append("---\n");

    /// <summary>Closes the front matter block and leaves a blank line before the first section.</summary>
    public static void CloseFrontMatter(StringBuilder builder) => builder.Append("---\n\n");
}
