namespace DantesRoleplay.Information;

/// <summary>Validation and containment checks for neutral hierarchical information namespaces.</summary>
public static class InformationScopes
{
    public static bool IsScope(string? value) => Valid(value, allowWildcard: false);
    public static bool IsSelector(string? value) => Valid(value, allowWildcard: true);

    public static bool Matches(string selector, string scope)
    {
        if (!IsSelector(selector) || !IsScope(scope)) return false;
        if (selector == "*") return true;
        if (!selector.EndsWith(".*", StringComparison.Ordinal)) return string.Equals(selector, scope, StringComparison.Ordinal);
        var prefix = selector[..^1];
        return scope.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>True only when every scope selected by <paramref name="requested"/> is allowed by <paramref name="granted"/>.</summary>
    public static bool Contains(string granted, string requested)
    {
        if (!IsSelector(granted) || !IsSelector(requested)) return false;
        if (granted == "*") return true;
        if (!granted.EndsWith(".*", StringComparison.Ordinal)) return string.Equals(granted, requested, StringComparison.Ordinal);
        var prefix = granted[..^1];
        return requested.StartsWith(prefix, StringComparison.Ordinal);
    }

    public static bool Overlaps(string first, string second)
    {
        if (!IsSelector(first) || !IsSelector(second)) return false;
        if (first == "*" || second == "*") return true;
        if (!first.EndsWith(".*", StringComparison.Ordinal)) return Matches(second, first);
        if (!second.EndsWith(".*", StringComparison.Ordinal)) return Matches(first, second);
        return first[..^1].StartsWith(second[..^1], StringComparison.Ordinal) || second[..^1].StartsWith(first[..^1], StringComparison.Ordinal);
    }

    private static bool Valid(string? value, bool allowWildcard)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > 200) return false;
        if (allowWildcard && value == "*") return true;
        var stem = allowWildcard && value.EndsWith(".*", StringComparison.Ordinal) ? value[..^2] : value;
        if (stem.Length == 0 || stem.StartsWith(".", StringComparison.Ordinal) || stem.EndsWith(".", StringComparison.Ordinal)) return false;
        return stem.Split('.').All(segment => segment.Length > 0 && segment.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'));
    }
}
