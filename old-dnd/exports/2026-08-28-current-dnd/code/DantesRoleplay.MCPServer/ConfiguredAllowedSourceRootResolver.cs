using DantesRoleplay.Sources;

namespace DantesRoleplay.MCPServer;

/// <summary>Canonical host configuration; protocol callers can select IDs but never define paths.</summary>
public sealed class ConfiguredAllowedSourceRootResolver : IAllowedSourceRootResolver, IAllowedSourceRootCatalog
{
    private readonly IReadOnlyDictionary<string, string> _roots;

    public ConfiguredAllowedSourceRootResolver(IReadOnlyDictionary<string, string>? roots)
    {
        var configured = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, path) in roots ?? new Dictionary<string, string>())
        {
            if (!ValidId(id) || string.IsNullOrWhiteSpace(path) || path.Length > 4096)
                throw new ArgumentException("Allowed source roots require a lowercase ID and a bounded path.", nameof(roots));
            configured.Add(id, Path.GetFullPath(path));
        }
        _roots = configured;
    }

    public bool TryResolve(string allowedRootId, out string canonicalPath) =>
        _roots.TryGetValue(allowedRootId, out canonicalPath!);

    public IReadOnlyList<string> ListIds(int limit)
    {
        if (limit is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(limit));
        return _roots.Keys.OrderBy(value => value, StringComparer.Ordinal).Take(limit).ToArray();
    }

    private static bool ValidId(string value) => value is { Length: >= 1 and <= 63 }
        && char.IsAsciiLetterLower(value[0])
        && value.All(character => char.IsAsciiLetterLower(character)
            || char.IsAsciiDigit(character) || character == '-');
}
