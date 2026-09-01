using System.Collections.ObjectModel;

namespace DantesRoleplay.CatalogNamespaces;

public static class CatalogNamespaceKinds
{
    public const string Mechanic = "mechanic";
    public const string Procedure = "procedure";
    public const string ComponentDefinition = "component-definition";
    public const string ComponentType = "component-type";
    public const string EventType = "event-type";
    public const string Subscription = "subscription";
    public const string Entity = "entity";
    public const string Document = "document";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Mechanic, Procedure, ComponentDefinition, ComponentType, EventType, Subscription, Entity, Document],
        StringComparer.Ordinal);
}

public static class CatalogNamespaceReviewStatuses
{
    public const string NeedsReview = "needs-review";
    public const string Reviewed = "reviewed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [NeedsReview, Reviewed], StringComparer.Ordinal);
}

public sealed record CatalogNamespaceDefinition(
    string Id,
    string? ParentId,
    string Owner,
    string Description,
    IReadOnlyList<string> AllowedKinds,
    IReadOnlyList<string> Aliases,
    string ReviewStatus,
    string ReviewNote,
    DateTime? ReviewedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? DisabledAtUtc)
{
    public bool IsEnabled => DisabledAtUtc is null;
}

public sealed record CatalogNamespaceRegistration(
    string Id,
    string Owner,
    string Description,
    IReadOnlyList<string> AllowedKinds,
    IReadOnlyList<string>? Aliases = null,
    string ReviewStatus = CatalogNamespaceReviewStatuses.NeedsReview,
    string ReviewNote = "Not yet reviewed.");

public sealed record CatalogNamespaceSearchHit(
    CatalogNamespaceDefinition Namespace,
    int Rank);

public interface ICatalogNamespaceRegistry
{
    CatalogNamespaceDefinition Register(CatalogNamespaceRegistration registration);
    CatalogNamespaceDefinition? Get(string namespaceId, bool includeDisabled = false);
    IReadOnlyList<CatalogNamespaceDefinition> List(bool includeDisabled = false);
    IReadOnlyList<CatalogNamespaceSearchHit> Search(string query, int limit = 20, bool includeDisabled = false);
    CatalogNamespaceDefinition SetEnabled(string namespaceId, bool enabled);
    CatalogNamespaceDefinition SetReview(string namespaceId, string reviewStatus, string reviewNote);
    CatalogNamespaceDefinition RequireRecordNamespace(string qualifiedId, string recordKind);
}

public sealed record CatalogNamespaceOverlayProfileRegistration(
    string ApplicationId,
    string ProfileId,
    string Description);

public sealed record CatalogNamespaceOverlayProfile(
    string ApplicationId,
    string ProfileId,
    string Description,
    DateTime CreatedAtUtc);

public sealed record CatalogResolutionKeyRegistration(
    string ApplicationId,
    string ProfileId,
    string ResolutionKey,
    string RecordKind,
    string Description);

public sealed record CatalogResolutionKeyDefinition(
    string ApplicationId,
    string ProfileId,
    string ResolutionKey,
    string RecordKind,
    string Description,
    DateTime CreatedAtUtc);

public sealed record CatalogNamespaceOverlayRule(
    string ApplicationId,
    string ProfileId,
    string HigherNamespaceId,
    string LowerNamespaceId,
    string? RecordKind);

public sealed record CatalogResolutionCandidate(
    string QualifiedId,
    string NamespaceId,
    string RecordKind,
    string ResolutionKey);

public sealed record CatalogResolutionResult(
    string ProfileId,
    CatalogResolutionKeyDefinition ResolutionKey,
    CatalogResolutionCandidate Winner,
    IReadOnlyList<CatalogResolutionCandidate> Shadowed);

public interface ICatalogNamespaceOverlayRegistry
{
    CatalogNamespaceOverlayProfile RegisterProfile(CatalogNamespaceOverlayProfileRegistration registration);
    CatalogNamespaceOverlayProfile? GetProfile(string applicationId, string profileId);
    IReadOnlyList<CatalogNamespaceOverlayProfile> ProfilesForApplication(string applicationId);
    CatalogResolutionKeyDefinition RegisterResolutionKey(CatalogResolutionKeyRegistration registration);
    IReadOnlyList<CatalogResolutionKeyDefinition> ResolutionKeysForProfile(string applicationId, string profileId);
    CatalogNamespaceOverlayRule Register(CatalogNamespaceOverlayRule rule);
    IReadOnlyList<CatalogNamespaceOverlayRule> RulesForProfile(string applicationId, string profileId);
    CatalogResolutionResult Resolve(
        string applicationId,
        string profileId,
        IReadOnlyList<CatalogResolutionCandidate> candidates);
}

public static class CatalogOverlayIdentity
{
    public static bool IsProfileId(string value) => IsDottedId(value, 63);
    public static bool IsResolutionKey(string value) => IsDottedId(value, 200);

    private static bool IsDottedId(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength
        && value.Split('.').All(segment => segment.Length is >= 1 and <= 63
            && char.IsAsciiLetterLower(segment[0])
            && segment.All(character => char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character) || character == '-'));
}

public static class CatalogNamespaceIdentity
{
    public const string RootNamespaceId = "catalog-root";

    public static string NamespaceOf(string qualifiedId)
    {
        ValidateRecordId(qualifiedId);
        var separator = qualifiedId.LastIndexOf('.');
        if (separator < 0) return RootNamespaceId;
        var candidate = qualifiedId[..separator];
        return IsSegments(candidate, requireDot: false) ? candidate : RootNamespaceId;
    }

    public static string LocalNameOf(string qualifiedId)
    {
        ValidateRecordId(qualifiedId);
        var separator = qualifiedId.LastIndexOf('.');
        return separator < 0 || NamespaceOf(qualifiedId) == RootNamespaceId
            ? qualifiedId
            : qualifiedId[(separator + 1)..];
    }

    public static IReadOnlyList<string> NamespaceChain(string qualifiedId)
    {
        var leaf = NamespaceOf(qualifiedId);
        if (leaf == RootNamespaceId) return [RootNamespaceId];
        var parts = leaf.Split('.');
        return new ReadOnlyCollection<string>(parts.Select((_, index) =>
            string.Join('.', parts.Take(index + 1))).ToArray());
    }

    public static bool IsNamespaceId(string value) =>
        value == RootNamespaceId || IsSegments(value, requireDot: false);

    public static void ValidateRecordId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value != value.Trim()
            || value.Contains("..", StringComparison.Ordinal) || value.EndsWith('.')
            || value.Any(character => char.IsControl(character) || character is '/' or '\\'))
            throw new ArgumentException("A catalog ID must be a bounded, safe identifier.", nameof(value));
    }

    private static bool IsSegments(string value, bool requireDot)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || (requireDot && !value.Contains('.')))
            return false;
        return value.Split('.').All(segment => segment.Length is >= 1 and <= 63
            && (char.IsAsciiLetterLower(segment[0]) || char.IsAsciiDigit(segment[0]))
            && segment.All(character => char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character) || character == '-'));
    }
}

public sealed class CatalogNamespaceException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
