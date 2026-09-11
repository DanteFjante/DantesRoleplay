using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DantesRoleplay.Procedures;

/// <summary>Rebuildable views of authoritative text, never new authored procedure identities.</summary>
internal static partial class ProcedureManualSections
{
    internal const int MaximumSectionCharacters = 2000;
    internal const string Format = "procedure-manual-sections-v1";
    internal sealed record Section(string Reference, string Heading, string ParentContext, string Text,
        string ContentFingerprint);

    internal static string Fingerprint(ProcedureDetail procedure) => Hash(JsonSerializer.Serialize(new
    {
        Format, procedure.Id, procedure.Version, procedure.Category, procedure.Status,
        procedure.Name, procedure.Description, procedure.Governs, procedure.Matches,
        procedure.Instructions, procedure.Constraints
    }));

    internal static IReadOnlyList<Section> Derive(string id, string instructions, string constraints)
    {
        if (instructions.Length + constraints.Length > 64_000)
            throw new ArgumentException("Manual source exceeds the bounded derivation limit.");
        var result = new List<Section>();
        Split("instructions", instructions);
        Split("constraints", constraints);
        return result;

        void Split(string field, string content)
        {
            var parents = new List<(int Level, string Heading)>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var body = new StringBuilder();
            var heading = field;
            var parent = "";
            var fenced = false;
            void Flush()
            {
                var text = body.ToString().Trim();
                body.Clear();
                if (text.Length == 0) return;
                var identity = field + "/" + parent + "/" + heading;
                var occurrence = occurrences.GetValueOrDefault(identity) + 1;
                occurrences[identity] = occurrence;
                var partNumber = 0;
                for (var offset = 0; offset < text.Length;)
                {
                    var length = Math.Min(MaximumSectionCharacters, text.Length - offset);
                    if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])) length--;
                    var part = text.Substring(offset, length);
                    result.Add(new(id + "#" + field + "/" + Hash(identity)[..16].ToLowerInvariant()
                        + "/" + occurrence + "/" + ++partNumber,
                        heading, parent, part, Hash(part)));
                    offset += length;
                }
            }
            foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                    fenced = !fenced;
                var match = fenced ? Match.Empty : HeadingPattern().Match(line);
                if (match.Success)
                {
                    Flush();
                    var level = match.Groups[1].Length;
                    parents.RemoveAll(value => value.Level >= level);
                    heading = match.Groups[2].Value.Trim();
                    parent = string.Join(" > ", parents.Select(value => value.Heading));
                    parents.Add((level, heading));
                }
                body.Append(line).Append('\n');
            }
            Flush();
        }
    }

    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [GeneratedRegex(@"^ {0,3}(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingPattern();
}
