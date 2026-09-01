using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.Applications;

/// <summary>Opaque application identity. The generic kernel never branches on a particular value.</summary>
public sealed record ApplicationIdentifier
{
    private ApplicationIdentifier(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Kernel-owned contracts use the reserved system namespace without pretending that the
    /// kernel is an installed application. User and catalog input must still go through
    /// <see cref="Parse"/>, which deliberately rejects this value.
    /// </summary>
    public static ApplicationIdentifier System { get; } = new("system");

    public bool IsSystem => Value == System.Value;

    public static ApplicationIdentifier Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 63
            || value == "system"
            || !char.IsAsciiLetterLower(value[0])
            || value.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')))
        {
            throw new ArgumentException("Application IDs must be lowercase ASCII segments and may not be 'system'.", nameof(value));
        }

        return new ApplicationIdentifier(value);
    }

    public override string ToString() => Value;
}

public sealed record ApplicationRegistration(
    ApplicationIdentifier Id,
    string DisplayName,
    string Description,
    IReadOnlyList<ApplicationIdentifier> BaseApplications);

public sealed record ApplicationRevision(
    ApplicationIdentifier ApplicationId,
    int Revision,
    string Fingerprint,
    IReadOnlyList<ApplicationIdentifier> BaseApplications);

public sealed record ApplicationDiscoveryPage(
    IReadOnlyList<ApplicationRegistration> Applications,
    string? NextApplicationId);

public interface IApplicationRegistry
{
    ApplicationRevision Register(ApplicationRegistration registration);
    ApplicationRevision? Get(ApplicationIdentifier applicationId);
    ApplicationRevision? Get(ApplicationIdentifier applicationId, int revision) =>
        Get(applicationId) is { } current && current.Revision == revision ? current : null;
    ApplicationRevision ReviseBaseApplications(
        ApplicationIdentifier applicationId,
        IReadOnlyList<ApplicationIdentifier> baseApplications,
        int expectedRevision,
        string expectedFingerprint) =>
        throw new NotSupportedException("This application registry does not support revision updates.");
    ApplicationRegistration? Describe(ApplicationIdentifier applicationId);
    IReadOnlyList<ApplicationRegistration> List(int limit);
    ApplicationDiscoveryPage ListPage(string? afterApplicationId, int limit);
}

/// <summary>Test-only persistence-free registry; SQLite ownership begins in Slice 3.</summary>
public sealed class InMemoryApplicationRegistry : IApplicationRegistry
{
    private readonly Dictionary<ApplicationIdentifier, ApplicationRegistration> _registrations = [];
    private readonly Dictionary<(ApplicationIdentifier ApplicationId, int Revision), ApplicationRevision> _revisions = [];

    public ApplicationRevision Register(ApplicationRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration = Copy(registration);
        if (string.IsNullOrWhiteSpace(registration.DisplayName))
            throw new ArgumentException("An application display name is required.", nameof(registration));
        if (registration.BaseApplications.Distinct().Count() != registration.BaseApplications.Count)
            throw new ArgumentException("An application may list each base only once.", nameof(registration));
        if (registration.BaseApplications.Contains(registration.Id))
            throw new ArgumentException("An application cannot be its own base.", nameof(registration));
        if (registration.BaseApplications.Any(baseId => !_registrations.ContainsKey(baseId)))
            throw new ArgumentException("Every base application must already be registered.", nameof(registration));

        if (_registrations.TryGetValue(registration.Id, out var existing))
        {
            if (!SameRegistration(existing, registration))
                throw new InvalidOperationException($"Application '{registration.Id}' already has a different immutable registration.");
            return Copy(Current(registration.Id)!);
        }

        var fingerprint = ApplicationRegistrationFingerprint.Compute(registration);
        var revision = new ApplicationRevision(registration.Id, 1, fingerprint, ReadOnly(registration.BaseApplications));
        _registrations.Add(registration.Id, registration);
        _revisions.Add((registration.Id, revision.Revision), revision);
        return Copy(revision);
    }

    public ApplicationRevision? Get(ApplicationIdentifier applicationId) =>
        Current(applicationId) is { } value ? Copy(value) : null;

    public ApplicationRevision? Get(ApplicationIdentifier applicationId, int revision) =>
        _revisions.TryGetValue((applicationId, revision), out var value)
            ? Copy(value) : null;

    public ApplicationRevision ReviseBaseApplications(
        ApplicationIdentifier applicationId,
        IReadOnlyList<ApplicationIdentifier> baseApplications,
        int expectedRevision,
        string expectedFingerprint)
    {
        if (!_registrations.TryGetValue(applicationId, out var current)
            || Current(applicationId) is not { } revision)
            throw new KeyNotFoundException("Application is not registered.");
        if (revision.Revision != expectedRevision || revision.Fingerprint != expectedFingerprint)
            throw new InvalidOperationException("The application revision is stale.");
        var candidate = Copy(current with { BaseApplications = ReadOnly(baseApplications) });
        if (candidate.BaseApplications.Distinct().Count() != candidate.BaseApplications.Count
            || candidate.BaseApplications.Contains(candidate.Id)
            || candidate.BaseApplications.Any(value => !_registrations.ContainsKey(value)))
            throw new ArgumentException("Application bases must be distinct, registered, and may not include the application itself.");
        if (candidate.BaseApplications.SequenceEqual(current.BaseApplications)) return Copy(revision);
        var next = new ApplicationRevision(applicationId, revision.Revision + 1,
            ApplicationRegistrationFingerprint.Compute(candidate), ReadOnly(candidate.BaseApplications));
        _registrations[applicationId] = candidate;
        _revisions.Add((applicationId, next.Revision), next);
        return Copy(next);
    }

    private ApplicationRevision? Current(ApplicationIdentifier applicationId) =>
        _revisions.Where(value => value.Key.ApplicationId == applicationId)
            .OrderByDescending(value => value.Key.Revision)
            .Select(value => value.Value)
            .FirstOrDefault();

    public ApplicationRegistration? Describe(ApplicationIdentifier applicationId) =>
        _registrations.TryGetValue(applicationId, out var value) ? Copy(value) : null;

    public IReadOnlyList<ApplicationRegistration> List(int limit)
    {
        ValidateLimit(limit);
        return _registrations.Values.OrderBy(value => value.Id.Value, StringComparer.Ordinal)
            .Take(limit).Select(Copy).ToArray();
    }

    public ApplicationDiscoveryPage ListPage(string? afterApplicationId, int limit)
    {
        ValidateLimit(limit);
        var after = ValidateAfter(afterApplicationId);
        var values = _registrations.Values
            .Where(value => after is null || string.CompareOrdinal(value.Id.Value, after) > 0)
            .OrderBy(value => value.Id.Value, StringComparer.Ordinal)
            .Take(limit + 1)
            .Select(Copy)
            .ToArray();
        var hasMore = values.Length > limit;
        var page = hasMore ? values[..limit] : values;
        return new(Array.AsReadOnly(page), hasMore ? page[^1].Id.Value : null);
    }

    private static ApplicationRegistration Copy(ApplicationRegistration value) =>
        new(value.Id, value.DisplayName, value.Description, ReadOnly(value.BaseApplications));

    private static ApplicationRevision Copy(ApplicationRevision value) =>
        new(value.ApplicationId, value.Revision, value.Fingerprint, ReadOnly(value.BaseApplications));

    private static bool SameRegistration(ApplicationRegistration left, ApplicationRegistration right) =>
        left.Id == right.Id && left.DisplayName == right.DisplayName && left.Description == right.Description
        && left.BaseApplications.SequenceEqual(right.BaseApplications);

    private static IReadOnlyList<ApplicationIdentifier> ReadOnly(IEnumerable<ApplicationIdentifier> values) =>
        Array.AsReadOnly(values.ToArray());

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
    }

    private string? ValidateAfter(string? afterApplicationId)
    {
        if (afterApplicationId is null) return null;
        var parsed = ApplicationIdentifier.Parse(afterApplicationId);
        if (!_registrations.ContainsKey(parsed)) throw new InvalidOperationException("CURSOR_STALE");
        return parsed.Value;
    }

}

public static class ApplicationRegistrationFingerprint
{
    public static string Compute(ApplicationRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        // Base order is part of application identity. A JSON envelope also prevents newlines in
        // authored metadata from creating delimiter collisions between otherwise different rows.
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = registration.Id.Value,
            displayName = registration.DisplayName,
            description = registration.Description,
            baseApplications = registration.BaseApplications.Select(x => x.Value).ToArray()
        });
        return Convert.ToHexString(SHA256.HashData(canonical));
    }
}

public sealed record StateSpaceBinding
{
    public StateSpaceBinding(string stateSpaceId, ApplicationRevision applicationRevision,
        string manifestFingerprint, string? resolutionFingerprint = null,
        EcsStateSpaceScope scope = EcsStateSpaceScope.Runtime)
    {
        ArgumentNullException.ThrowIfNull(applicationRevision);
        if (string.IsNullOrWhiteSpace(stateSpaceId) || stateSpaceId.Length > 200
            || applicationRevision.Revision < 1
            || applicationRevision.BaseApplications is null
            || !UpperSha256(applicationRevision.Fingerprint)
            || !UpperSha256(manifestFingerprint)
            || resolutionFingerprint is not null && !UpperSha256(resolutionFingerprint))
            throw new ArgumentException("A state space requires an identity and exact immutable application/manifest fingerprints.");
        StateSpaceId = stateSpaceId;
        ApplicationRevision = applicationRevision with
        {
            BaseApplications = Array.AsReadOnly(applicationRevision.BaseApplications.ToArray())
        };
        ManifestFingerprint = manifestFingerprint;
        ResolutionFingerprint = resolutionFingerprint ?? manifestFingerprint;
        Scope = scope;
    }

    public string StateSpaceId { get; }
    public ApplicationRevision ApplicationRevision { get; }
    public string ManifestFingerprint { get; }
    public string ResolutionFingerprint { get; }
    public EcsStateSpaceScope Scope { get; }

    private static bool UpperSha256(string value) => value is { Length: 64 }
        && value.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');
}
