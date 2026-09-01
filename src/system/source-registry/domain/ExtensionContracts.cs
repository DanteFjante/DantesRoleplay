using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.Applications;

namespace DantesRoleplay.Sources;

/// <summary>
/// Immutable installation metadata for one extension targeting a registered application. Source
/// membership identifies the files supplied by the extension; namespace roots identify records
/// supplied by it after catalog materialization. Priority edges are explicit and application-local.
/// </summary>
public sealed record ApplicationExtensionRegistration(
    ApplicationIdentifier ApplicationId,
    string ExtensionId,
    string DisplayName,
    string Description,
    string Classification,
    IReadOnlyList<string> SourceIds,
    IReadOnlyList<string> NamespaceIds,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> ConflictsWith,
    IReadOnlyList<string> HigherPriorityThan,
    bool OverridesBase);

public static class ApplicationExtensionClassifications
{
    public const string Homebrew = "homebrew";
    public const string Compatibility = "compatibility";
    public const string ThirdParty = "third-party";

    public static bool IsValid(string? value) => value is Homebrew or Compatibility or ThirdParty;
}

public static class ApplicationExtensionIdentity
{
    public const string Base = "base";

    public static bool IsValid(string? value) => value is { Length: >= 1 and <= 63 }
        && value != Base
        && char.IsAsciiLetterLower(value[0])
        && value.All(character => char.IsAsciiLetterLower(character)
            || char.IsAsciiDigit(character) || character == '-');
}

public static class ApplicationExtensionRegistrationFingerprint
{
    public static string Compute(ApplicationExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            applicationId = registration.ApplicationId.Value,
            registration.ExtensionId,
            registration.DisplayName,
            registration.Description,
            registration.Classification,
            sourceIds = registration.SourceIds.Order(StringComparer.Ordinal),
            namespaceIds = registration.NamespaceIds.Order(StringComparer.Ordinal),
            dependencies = registration.Dependencies.Order(StringComparer.Ordinal),
            conflictsWith = registration.ConflictsWith.Order(StringComparer.Ordinal),
            higherPriorityThan = registration.HigherPriorityThan.Order(StringComparer.Ordinal),
            registration.OverridesBase
        });
        return Convert.ToHexString(SHA256.HashData(canonical));
    }
}

public interface IApplicationExtensionRegistry
{
    ApplicationExtensionRegistration Register(ApplicationExtensionRegistration registration);
    ApplicationExtensionRegistration? Get(ApplicationIdentifier applicationId, string extensionId);
    IReadOnlyList<ApplicationExtensionRegistration> For(ApplicationIdentifier applicationId);
}

/// <summary>Compatibility registry for hosts that have not installed extensions.</summary>
public sealed class EmptyApplicationExtensionRegistry : IApplicationExtensionRegistry
{
    public ApplicationExtensionRegistration Register(ApplicationExtensionRegistration registration) =>
        throw new InvalidOperationException("This host does not provide extension registration.");

    public ApplicationExtensionRegistration? Get(ApplicationIdentifier applicationId, string extensionId) => null;

    public IReadOnlyList<ApplicationExtensionRegistration> For(ApplicationIdentifier applicationId) => [];
}

/// <summary>Persistence-free validator shared by SQLite registration and focused tests.</summary>
public sealed class InMemoryApplicationExtensionRegistry(
    ISourceRegistry sources) : IApplicationExtensionRegistry
{
    private readonly List<ApplicationExtensionRegistration> _registrations = [];

    public ApplicationExtensionRegistration Register(ApplicationExtensionRegistration registration)
    {
        var normalized = ApplicationExtensionValidation.Normalize(registration, sources,
            _registrations.Where(value => value.ApplicationId == registration.ApplicationId
                && value.ExtensionId != registration.ExtensionId).ToArray());
        var existing = _registrations.SingleOrDefault(value =>
            value.ApplicationId == normalized.ApplicationId && value.ExtensionId == normalized.ExtensionId);
        if (existing is not null)
        {
            if (ApplicationExtensionRegistrationFingerprint.Compute(existing)
                != ApplicationExtensionRegistrationFingerprint.Compute(normalized))
                throw new InvalidOperationException("Extension registrations are immutable.");
            return existing;
        }
        _registrations.Add(normalized);
        return normalized;
    }

    public ApplicationExtensionRegistration? Get(ApplicationIdentifier applicationId, string extensionId) =>
        _registrations.SingleOrDefault(value =>
            value.ApplicationId == applicationId && value.ExtensionId == extensionId);

    public IReadOnlyList<ApplicationExtensionRegistration> For(ApplicationIdentifier applicationId) =>
        _registrations.Where(value => value.ApplicationId == applicationId)
            .OrderBy(value => value.ExtensionId, StringComparer.Ordinal).ToArray();
}

public static class ApplicationExtensionValidation
{
    public static ApplicationExtensionRegistration Normalize(
        ApplicationExtensionRegistration registration,
        ISourceRegistry sources,
        IReadOnlyList<ApplicationExtensionRegistration> existing)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (!ApplicationExtensionIdentity.IsValid(registration.ExtensionId)
            || string.IsNullOrWhiteSpace(registration.DisplayName) || registration.DisplayName.Length > 120
            || registration.DisplayName != registration.DisplayName.Trim()
            || string.IsNullOrWhiteSpace(registration.Description) || registration.Description.Length > 2_000
            || registration.Description != registration.Description.Trim()
            || !ApplicationExtensionClassifications.IsValid(registration.Classification))
            throw new ArgumentException("An extension requires a valid ID, display metadata, and classification.", nameof(registration));
        var sourceIds = Copy(registration.SourceIds, 100, 200, "source IDs");
        var namespaceIds = Copy(registration.NamespaceIds, 100, 200, "namespace IDs");
        var dependencies = CopyExtensions(registration.Dependencies, registration.ExtensionId, "dependencies");
        var conflicts = CopyExtensions(registration.ConflictsWith, registration.ExtensionId, "conflicts");
        var priority = CopyExtensions(registration.HigherPriorityThan, registration.ExtensionId, "priority edges");
        if (sourceIds.Count == 0 || namespaceIds.Count == 0)
            throw new ArgumentException("An extension requires at least one source and namespace contribution.", nameof(registration));
        if (dependencies.Intersect(conflicts, StringComparer.Ordinal).Any())
            throw new ArgumentException("An extension cannot both depend on and conflict with another extension.", nameof(registration));
        var availableSources = sources.For(registration.ApplicationId)
            .Select(value => value.SourceId).ToHashSet(StringComparer.Ordinal);
        if (sourceIds.Any(value => !availableSources.Contains(value)))
            throw new ArgumentException("Every extension source must already be registered for its target application.", nameof(registration));
        if (existing.Any(value => value.SourceIds.Intersect(sourceIds, StringComparer.Ordinal).Any()))
            throw new InvalidOperationException("One source cannot belong to more than one extension.");
        if (namespaceIds.Any(value => !Namespace(value, registration.ApplicationId.Value)))
            throw new ArgumentException("Extension namespace roots must be qualified by the target application.", nameof(registration));
        if (existing.Any(value => value.NamespaceIds.Intersect(namespaceIds, StringComparer.Ordinal).Any()))
            throw new InvalidOperationException("One namespace root cannot belong to more than one extension.");
        return new(registration.ApplicationId, registration.ExtensionId, registration.DisplayName,
            registration.Description, registration.Classification, sourceIds, namespaceIds,
            dependencies, conflicts, priority, registration.OverridesBase);
    }

    private static IReadOnlyList<string> CopyExtensions(
        IReadOnlyList<string>? values, string self, string name)
    {
        var copied = Copy(values, 100, 63, name);
        if (copied.Any(value => !ApplicationExtensionIdentity.IsValid(value) || value == self))
            throw new ArgumentException($"Extension {name} contain an invalid or self-referential ID.", nameof(values));
        return copied;
    }

    private static IReadOnlyList<string> Copy(
        IReadOnlyList<string>? values, int maximumCount, int maximumLength, string name)
    {
        values ??= [];
        if (values.Count > maximumCount || values.Any(value => string.IsNullOrWhiteSpace(value)
                || value.Length > maximumLength || value != value.Trim() || value.Any(char.IsControl))
            || values.Distinct(StringComparer.Ordinal).Count() != values.Count)
            throw new ArgumentException($"Extension {name} are invalid or unbounded.", nameof(values));
        return Array.AsReadOnly(values.Order(StringComparer.Ordinal).ToArray());
    }

    private static bool Namespace(string value, string applicationId) =>
        value.StartsWith(applicationId + ".", StringComparison.Ordinal)
        && value[(applicationId.Length + 1)..].Split('.').All(segment =>
            segment is { Length: >= 1 and <= 63 }
            && char.IsAsciiLetterLower(segment[0])
            && segment.All(character => char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character) || character == '-'));
}

public sealed record CompiledApplicationExtensionSet(
    ApplicationIdentifier ApplicationId,
    string Fingerprint,
    IReadOnlyList<ApplicationExtensionRegistration> Extensions,
    IReadOnlyList<string> PriorityOrder)
{
    public static CompiledApplicationExtensionSet Empty(ApplicationIdentifier applicationId)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            applicationId = applicationId.Value,
            extensions = Array.Empty<object>()
        })));
        return new(applicationId, fingerprint, [], [ApplicationExtensionIdentity.Base]);
    }
}

/// <summary>Compiles one selected, closed extension graph or rejects it before activation.</summary>
public static class ApplicationExtensionSetCompiler
{
    public static CompiledApplicationExtensionSet Compile(
        ApplicationIdentifier applicationId,
        IReadOnlyList<ApplicationExtensionRegistration> available,
        IReadOnlyList<string>? selectedExtensionIds = null)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(available);
        var availableById = available.ToDictionary(value => value.ExtensionId, StringComparer.Ordinal);
        var selected = selectedExtensionIds is null
            ? available.OrderBy(value => value.ExtensionId, StringComparer.Ordinal).ToArray()
            : Select(availableById, selectedExtensionIds);
        if (selected.Length == 0) return CompiledApplicationExtensionSet.Empty(applicationId);
        if (selected.Any(value => value.ApplicationId != applicationId))
            throw Error("EXTENSION_APPLICATION_MISMATCH", "An extension targets another application.");

        var selectedIds = selected.Select(value => value.ExtensionId).ToHashSet(StringComparer.Ordinal);
        foreach (var extension in selected)
        {
            var missing = extension.Dependencies.FirstOrDefault(value => !selectedIds.Contains(value));
            if (missing is not null)
                throw Error("EXTENSION_DEPENDENCY_MISSING",
                    $"Extension '{extension.ExtensionId}' requires extension '{missing}'.");
            var conflict = extension.ConflictsWith.FirstOrDefault(selectedIds.Contains);
            if (conflict is not null)
                throw Error("EXTENSION_CONFLICT",
                    $"Extensions '{extension.ExtensionId}' and '{conflict}' cannot be active together.");
            var unknownPriority = extension.HigherPriorityThan.FirstOrDefault(value => !availableById.ContainsKey(value));
            if (unknownPriority is not null)
                throw Error("EXTENSION_PRIORITY_UNKNOWN",
                    $"Extension '{extension.ExtensionId}' references unknown priority target '{unknownPriority}'.");
        }

        var nodes = selectedIds.Append(ApplicationExtensionIdentity.Base).ToHashSet(StringComparer.Ordinal);
        var edges = nodes.ToDictionary(value => value, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var extension in selected)
        {
            foreach (var lower in extension.HigherPriorityThan.Where(selectedIds.Contains))
                edges[extension.ExtensionId].Add(lower);
            if (extension.OverridesBase) edges[extension.ExtensionId].Add(ApplicationExtensionIdentity.Base);
            else edges[ApplicationExtensionIdentity.Base].Add(extension.ExtensionId);
        }
        var reachability = Reachability(nodes, edges);
        foreach (var pair in nodes.Order(StringComparer.Ordinal).SelectMany((left, index) =>
                     nodes.Order(StringComparer.Ordinal).Skip(index + 1).Select(right => (left, right))))
        {
            if (!reachability[pair.left].Contains(pair.right)
                && !reachability[pair.right].Contains(pair.left))
                throw Error("EXTENSION_PRIORITY_AMBIGUOUS",
                    $"Extension priority does not order '{pair.left}' and '{pair.right}'.");
        }
        var order = nodes.OrderByDescending(value => reachability[value].Count)
            .ThenBy(value => value, StringComparer.Ordinal).ToArray();
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            applicationId = applicationId.Value,
            extensions = selected.OrderBy(value => value.ExtensionId, StringComparer.Ordinal).Select(value => new
            {
                value.ExtensionId,
                registrationFingerprint = ApplicationExtensionRegistrationFingerprint.Compute(value)
            }),
            priorityOrder = order
        });
        return new(applicationId, Convert.ToHexString(SHA256.HashData(canonical)),
            Array.AsReadOnly(selected), Array.AsReadOnly(order));
    }

    private static ApplicationExtensionRegistration[] Select(
        IReadOnlyDictionary<string, ApplicationExtensionRegistration> available,
        IReadOnlyList<string> selected)
    {
        if (selected.Count > 100 || selected.Distinct(StringComparer.Ordinal).Count() != selected.Count
            || selected.Any(value => !ApplicationExtensionIdentity.IsValid(value)))
            throw Error("EXTENSION_SELECTION_INVALID", "Selected extension IDs are invalid or duplicated.");
        var values = selected.Select(value => available.TryGetValue(value, out var extension)
            ? extension
            : throw Error("EXTENSION_SELECTION_UNKNOWN", $"Extension '{value}' is not registered."))
            .OrderBy(value => value.ExtensionId, StringComparer.Ordinal).ToArray();
        return values;
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
                if (next == node)
                    throw Error("EXTENSION_PRIORITY_CYCLE", "Extension priority contains a cycle.");
                if (!reached.Add(next)) continue;
                foreach (var child in edges[next]) pending.Push(child);
            }
            result.Add(node, reached);
        }
        return result;
    }

    private static InvalidOperationException Error(string code, string message) =>
        new ApplicationExtensionSetException(code, message);
}

public sealed class ApplicationExtensionSetException(string code, string message)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
