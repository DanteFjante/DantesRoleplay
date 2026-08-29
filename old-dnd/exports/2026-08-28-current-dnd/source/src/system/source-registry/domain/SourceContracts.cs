using DantesRoleplay.Applications;
using System.Security.Cryptography;
using System.Text.Json;

namespace DantesRoleplay.Sources;

public enum SourceTrust { Untrusted = 0, Trusted = 1 }

public sealed record SourceRegistration(
    ApplicationIdentifier ApplicationId,
    string SourceId,
    string AllowedRootId,
    string RelativePathOrGlob,
    SourceTrust Trust,
    int Precedence,
    string LogicalIdentity);

public static class SourceRegistrationFingerprint
{
    public static string Compute(SourceRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            applicationId = registration.ApplicationId.Value,
            sourceId = registration.SourceId,
            allowedRootId = registration.AllowedRootId,
            relativePathOrGlob = registration.RelativePathOrGlob,
            trust = registration.Trust.ToString().ToLowerInvariant(),
            precedence = registration.Precedence,
            logicalIdentity = registration.LogicalIdentity
        });
        return Convert.ToHexString(SHA256.HashData(canonical));
    }
}

public interface ISourceRegistry
{
    SourceRegistration Register(SourceRegistration registration);
    IReadOnlyList<SourceRegistration> For(ApplicationIdentifier applicationId);
    SourceRegistration? Get(ApplicationIdentifier applicationId, string sourceId);
    IReadOnlyList<SourceRegistration> List(ApplicationIdentifier applicationId, int limit);
}

public enum SourceScanStatus { Succeeded, Failed }

/// <summary>Immutable evidence from a scanner; recording it never performs a scan or activates an overlay.</summary>
public sealed record SourceScanReceipt(
    ApplicationIdentifier ApplicationId,
    string SourceId,
    int Generation,
    SourceScanStatus Status,
    string ContentFingerprint,
    DateTime RecordedAtUtc);

public interface ISourceScanReceiptStore
{
    SourceScanReceipt Record(SourceScanReceipt receipt);
    IReadOnlyList<SourceScanReceipt> For(ApplicationIdentifier applicationId, string sourceId);
    SourceScanReceipt? Latest(ApplicationIdentifier applicationId, string sourceId);
}

/// <summary>Pure source relationship validation; it intentionally never touches the filesystem.</summary>
public sealed class InMemorySourceRegistry : ISourceRegistry
{
    private readonly List<SourceRegistration> _registrations = [];

    public SourceRegistration Register(SourceRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (string.IsNullOrWhiteSpace(registration.SourceId) || string.IsNullOrWhiteSpace(registration.AllowedRootId)
            || string.IsNullOrWhiteSpace(registration.LogicalIdentity) || !Enum.IsDefined(registration.Trust)
            || !IsSafeRelativeSpecification(registration.RelativePathOrGlob))
            throw new ArgumentException("A source needs nonblank identities and a safe relative path or glob.", nameof(registration));

        var sameId = _registrations.SingleOrDefault(x => x.ApplicationId == registration.ApplicationId && x.SourceId == registration.SourceId);
        if (sameId is not null)
        {
            if (sameId != registration) throw new InvalidOperationException("Source IDs are immutable in one application.");
            return sameId;
        }

        var competitors = _registrations.Where(x => x.ApplicationId == registration.ApplicationId && x.LogicalIdentity == registration.LogicalIdentity).ToArray();
        if (competitors.Any(x => x.Precedence == registration.Precedence))
            throw new InvalidOperationException("Equal precedence for one logical identity is ambiguous.");
        if (competitors.Any(x => x.Trust > registration.Trust && x.Precedence < registration.Precedence))
            throw new InvalidOperationException("A lower-trust source cannot override a higher-trust source.");

        _registrations.Add(registration);
        return registration;
    }

    public IReadOnlyList<SourceRegistration> For(ApplicationIdentifier applicationId) => _registrations
        .Where(x => x.ApplicationId == applicationId)
        .OrderByDescending(x => x.Precedence).ThenBy(x => x.SourceId, StringComparer.Ordinal).ToArray();

    public SourceRegistration? Get(ApplicationIdentifier applicationId, string sourceId) =>
        _registrations.SingleOrDefault(x => x.ApplicationId == applicationId && x.SourceId == sourceId);

    public IReadOnlyList<SourceRegistration> List(ApplicationIdentifier applicationId, int limit)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return For(applicationId).Take(limit).ToArray();
    }

    private static bool IsSafeRelativeSpecification(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !Path.IsPathRooted(value)
        && value.Replace('\\', '/').Split('/').All(segment => segment is not "" and not "." and not "..");
}
