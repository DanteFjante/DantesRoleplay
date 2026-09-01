using DantesRoleplay.Applications;
using DantesRoleplay.Sources;

namespace DantesRoleplay.CatalogNavigation;

public sealed record CatalogExtensionContribution(
    string ExtensionId,
    string DisplayName,
    string Description,
    string Classification,
    IReadOnlyList<string> SourceIds,
    IReadOnlyList<string> NamespaceIds,
    IReadOnlyList<string> HigherPriorityThan,
    bool OverridesBase);

/// <summary>Immutable automatic extension-resolution evidence copied from an active application manifest.</summary>
public sealed class CatalogExtensionResolutionContext
{
    private CatalogExtensionResolutionContext(
        ApplicationIdentifier applicationId,
        string fingerprint,
        IReadOnlyList<CatalogExtensionContribution> extensions,
        IReadOnlyList<string> priorityOrder)
    {
        ApplicationId = applicationId;
        Fingerprint = fingerprint;
        Extensions = extensions;
        PriorityOrder = priorityOrder;
    }

    public ApplicationIdentifier ApplicationId { get; }
    public string Fingerprint { get; }
    public IReadOnlyList<CatalogExtensionContribution> Extensions { get; }
    public IReadOnlyList<string> PriorityOrder { get; }

    public static CatalogExtensionResolutionContext Create(
        ApplicationIdentifier applicationId,
        string fingerprint,
        IReadOnlyList<CatalogExtensionContribution> extensions)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(extensions);
        if (fingerprint is not { Length: 64 }
            || fingerprint.Any(value => !(char.IsAsciiDigit(value) || value is >= 'A' and <= 'F')))
            throw new ArgumentException("A resolution context requires an uppercase SHA-256 fingerprint.", nameof(fingerprint));
        var copied = extensions.OrderBy(value => value.ExtensionId, StringComparer.Ordinal).ToArray();
        if (copied.Select(value => value.ExtensionId).Distinct(StringComparer.Ordinal).Count() != copied.Length
            || copied.Any(value => string.IsNullOrWhiteSpace(value.DisplayName)
                || string.IsNullOrWhiteSpace(value.Description)
                || !ApplicationExtensionClassifications.IsValid(value.Classification)
                || value.SourceIds.Count == 0)
            || copied.Any(value => value.NamespaceIds.Count == 0
                || value.NamespaceIds.Any(root => !root.StartsWith(applicationId.Value + ".", StringComparison.Ordinal))))
            throw new ArgumentException("Extension resolution contributions are invalid.", nameof(extensions));

        var nodes = copied.Select(value => value.ExtensionId).Append("base")
            .ToHashSet(StringComparer.Ordinal);
        var edges = nodes.ToDictionary(value => value, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var extension in copied)
        {
            foreach (var lower in extension.HigherPriorityThan)
            {
                if (!nodes.Contains(lower))
                    throw new ArgumentException("An active extension priority edge references an inactive extension.", nameof(extensions));
                edges[extension.ExtensionId].Add(lower);
            }
            edges[extension.OverridesBase ? extension.ExtensionId : "base"]
                .Add(extension.OverridesBase ? "base" : extension.ExtensionId);
        }
        var reachability = Reachability(nodes, edges);
        foreach (var pair in nodes.Order(StringComparer.Ordinal).SelectMany((left, index) =>
                     nodes.Order(StringComparer.Ordinal).Skip(index + 1).Select(right => (left, right))))
            if (!reachability[pair.left].Contains(pair.right) && !reachability[pair.right].Contains(pair.left))
                throw new ArgumentException("The active extension precedence is not deterministic.", nameof(extensions));
        var order = nodes.OrderByDescending(value => reachability[value].Count)
            .ThenBy(value => value, StringComparer.Ordinal).ToArray();
        return new(applicationId, fingerprint, Array.AsReadOnly(copied), Array.AsReadOnly(order));
    }

    private static IReadOnlyDictionary<string, HashSet<string>> Reachability(
        IReadOnlySet<string> nodes,
        IReadOnlyDictionary<string, HashSet<string>> edges)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var reached = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(edges[node]);
            while (pending.TryPop(out var next))
            {
                if (next == node) throw new ArgumentException("The active extension precedence contains a cycle.");
                if (!reached.Add(next)) continue;
                foreach (var child in edges[next]) pending.Push(child);
            }
            result.Add(node, reached);
        }
        return result;
    }
}

public sealed record CatalogExtensionSearchSelection<T>(
    IReadOnlyList<T> Records,
    IReadOnlyList<CatalogResolutionDiagnosticView> Diagnostics,
    string ResolutionFingerprint);

public static class CatalogExtensionSearch
{
    public static bool Matches(string qualifiedId, string resolutionKey) =>
        string.Equals(qualifiedId, resolutionKey, StringComparison.Ordinal)
        || qualifiedId.EndsWith('.' + resolutionKey, StringComparison.Ordinal);

    public static CatalogExtensionSearchSelection<T> Apply<T>(
        CatalogExtensionResolutionContext? context,
        IReadOnlyList<T> values,
        Func<T, string> qualifiedId,
        Func<T, string> recordKind,
        bool includeShadowed = false)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(qualifiedId);
        ArgumentNullException.ThrowIfNull(recordKind);
        if (context is null || context.Extensions.Count == 0)
            return new(Array.AsReadOnly(values.ToArray()), [], context?.Fingerprint ?? "none");

        var order = context.PriorityOrder.Select((id, index) => (id, index))
            .ToDictionary(value => value.id, value => value.index, StringComparer.Ordinal);
        var groups = new Dictionary<(string Kind, string Key), List<(T Value, string Owner, string Id)>>();
        foreach (var value in values)
        {
            var id = qualifiedId(value);
            var (owner, key) = OwnerAndKey(context, id);
            var identity = (recordKind(value), key);
            if (!groups.TryGetValue(identity, out var group)) groups.Add(identity, group = []);
            group.Add((value, owner, id));
        }

        var retained = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = new List<CatalogResolutionDiagnosticView>();
        foreach (var (identity, candidates) in groups.OrderBy(value => value.Key.Kind, StringComparer.Ordinal)
                     .ThenBy(value => value.Key.Key, StringComparer.Ordinal))
        {
            var duplicateOwner = candidates.GroupBy(value => value.Owner, StringComparer.Ordinal)
                .FirstOrDefault(value => value.Count() > 1);
            if (duplicateOwner is not null)
                throw new InvalidOperationException(
                    $"Extension '{duplicateOwner.Key}' contributes more than one '{identity.Kind}' record for resolution key '{identity.Key}'.");
            var ranked = candidates.OrderBy(value => order[value.Owner])
                .ThenBy(value => value.Id, StringComparer.Ordinal).ToArray();
            retained.Add(ranked[0].Id);
            if (includeShadowed && ranked.Length > 1)
            {
                foreach (var candidate in ranked.Skip(1)) retained.Add(candidate.Id);
                diagnostics.Add(new(identity.Key, identity.Kind, ranked[0].Id,
                    Array.AsReadOnly(ranked.Skip(1).Select(value => value.Id).ToArray())));
            }
        }
        return new(Array.AsReadOnly(values.Where(value => retained.Contains(qualifiedId(value))).ToArray()),
            Array.AsReadOnly(diagnostics.ToArray()), context.Fingerprint);
    }

    public static (string Owner, string Key) OwnerAndKey(
        CatalogExtensionResolutionContext context,
        string qualifiedId)
    {
        var matches = context.Extensions.SelectMany(extension => extension.NamespaceIds.Select(root =>
                (extension.ExtensionId, Root: root)))
            .Where(value => qualifiedId == value.Root
                || qualifiedId.StartsWith(value.Root + ".", StringComparison.Ordinal))
            .OrderByDescending(value => value.Root.Length).ThenBy(value => value.ExtensionId, StringComparer.Ordinal)
            .ToArray();
        if (matches.Length > 1 && matches[0].Root.Length == matches[1].Root.Length)
            throw new InvalidOperationException($"Catalog record '{qualifiedId}' belongs to ambiguous extension namespaces.");
        if (matches.Length != 0)
            return (matches[0].ExtensionId, qualifiedId[(matches[0].Root.Length + 1)..]);
        var prefix = context.ApplicationId.Value + ".";
        if (!qualifiedId.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("A catalog record is outside its active application namespace.");
        return ("base", qualifiedId[prefix.Length..]);
    }
}
