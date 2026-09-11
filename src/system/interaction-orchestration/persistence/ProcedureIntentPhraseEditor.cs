using System.Text;
using DantesRoleplay.DataAccess.Bootstrap;

namespace DantesRoleplay.Interactions;

internal static class ProcedureIntentPhraseEditor
{
    internal static string Edit(string markdown, IReadOnlyList<string> phrases)
    {
        ArgumentNullException.ThrowIfNull(markdown); ArgumentNullException.ThrowIfNull(phrases);
        var before = ProcedureFile.Parse(markdown, "retained.md");
        var normalized = Normalize(phrases);
        var matches = Find(markdown);
        if (matches.Count > 1) throw new ArgumentException("Repeated Matches sections are ambiguous.");
        var nl = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string edited;
        if (matches.Count == 1)
        {
            var span = matches[0];
            var replacement = normalized.Count == 0 ? "" : "## Matches" + nl + string.Join(nl, normalized) + nl;
            edited = markdown[..span.Start] + replacement + markdown[span.End..];
        }
        else if (normalized.Count == 0) edited = markdown;
        else
        {
            var at = FrontMatterEnd(markdown);
            var insert = (at > 0 && !markdown[..at].EndsWith(nl, StringComparison.Ordinal) ? nl : "")
                + "## Matches" + nl + string.Join(nl, normalized) + nl + nl;
            edited = markdown[..at] + insert + markdown[at..];
        }
        var after = ProcedureFile.Parse(edited, "retained.md");
        if (after.Matches.Replace("\r\n", "\n", StringComparison.Ordinal) != string.Join('\n', normalized)
            || (before with { Matches = "" }) != (after with { Matches = "" }))
            throw new ArgumentException("Procedure meaning could not be preserved.");
        return edited;
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> values)
    {
        if (values.Count > 32) throw new ArgumentException("Too many phrases.");
        var result = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null || value.Any(char.IsControl)) throw new ArgumentException("Phrase is invalid.");
            var text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (text.Length is 0 or > 200) throw new ArgumentException("Phrase is invalid.");
            if (seen.Add(text.ToLowerInvariant())) result.Add(text);
        }
        return result;
    }

    private static List<(int Start, int End)> Find(string text)
    {
        var found = new List<(int, int)>(); var starts = new List<(int Pos, bool Matches)>();
        char fence = '\0'; var fenceLength = 0;
        for (var pos = FrontMatterEnd(text); pos < text.Length;)
        {
            var end = text.IndexOf('\n', pos); if (end < 0) end = text.Length;
            var line = text[pos..end].TrimEnd('\r'); var trim = line.TrimStart();
            var isMatches = line.StartsWith("## ", StringComparison.Ordinal)
                && line[3..].Trim().Equals("Matches", StringComparison.OrdinalIgnoreCase);
            if (fence != '\0' && line.StartsWith("## ", StringComparison.Ordinal))
                throw new ArgumentException("A fenced section heading has ambiguous owner meaning.");
            if (trim.Length >= 3 && trim[0] is '`' or '~')
            {
                var length = 0;
                while (length < trim.Length && trim[length] == trim[0]) length++;
                if (length >= 3 && fence == '\0') { fence = trim[0]; fenceLength = length; }
                else if (trim[0] == fence && length >= fenceLength && string.IsNullOrWhiteSpace(trim[length..])) fence = '\0';
            }
            if (line.StartsWith("## ", StringComparison.Ordinal)) starts.Add((pos, isMatches));
            pos = end == text.Length ? end : end + 1;
        }
        foreach (var item in starts.Where(x => x.Matches))
        {
            var end = starts.Where(x => x.Pos > item.Pos).Select(x => x.Pos).DefaultIfEmpty(text.Length).First();
            found.Add((item.Pos, end));
        }
        return found;
    }
    private static int FrontMatterEnd(string text)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal)) return 0;
        var first = text.IndexOf('\n'); if (first < 0) throw new ArgumentException("Invalid frontmatter.");
        var close = text.IndexOf("\n---", first, StringComparison.Ordinal); if (close < 0) throw new ArgumentException("Invalid frontmatter.");
        var end = text.IndexOf('\n', close + 1); return end < 0 ? text.Length : end + 1;
    }
}
