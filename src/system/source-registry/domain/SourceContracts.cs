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

/// <summary>One registration withdrawn from resolution, kept as evidence of the withdrawal.</summary>
public sealed record RetiredSource(SourceRegistration Source, DateTime RetiredAtUtc, string Reason);

public interface ISourceRegistry
{
    SourceRegistration Register(SourceRegistration registration);
    IReadOnlyList<SourceRegistration> For(ApplicationIdentifier applicationId);
    SourceRegistration? Get(ApplicationIdentifier applicationId, string sourceId);
    IReadOnlyList<SourceRegistration> List(ApplicationIdentifier applicationId, int limit);

    /// <summary>
    /// Withdraws one registration from resolution, keeping the row and recording why.
    ///
    /// A source ID is permanent and its specification immutable, so a registration whose files
    /// have moved cannot be corrected -- it can only stop being consulted. Until this existed, a
    /// migration that relocated catalog content left its old registrations behind reporting
    /// SCAN_PATH_NOT_FOUND forever, which made the default application preview invalid and forced
    /// every activation to hand-assemble the surviving source IDs. Retirement is not deletion:
    /// the row, the timestamp and the reason stay, because a registration that silently vanished
    /// would be indistinguishable from one that was never made.
    /// </summary>
    RetiredSource Retire(ApplicationIdentifier applicationId, string sourceId, string reason);

    /// <summary>Every retired registration, for the operator who has to explain the gap.</summary>
    IReadOnlyList<RetiredSource> Retired(ApplicationIdentifier applicationId);
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
    private readonly List<RetiredSource> _retired = [];

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

    public RetiredSource Retire(ApplicationIdentifier applicationId, string sourceId, string reason)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var registration = _registrations.SingleOrDefault(x => x.ApplicationId == applicationId && x.SourceId == sourceId)
            ?? throw new InvalidOperationException("Only a registered source can be retired.");
        _registrations.Remove(registration);
        var retired = new RetiredSource(registration, DateTime.UtcNow, reason.Trim());
        _retired.Add(retired);
        return retired;
    }

    public IReadOnlyList<RetiredSource> Retired(ApplicationIdentifier applicationId) => _retired
        .Where(x => x.Source.ApplicationId == applicationId)
        .OrderBy(x => x.Source.SourceId, StringComparer.Ordinal).ToArray();

    private static bool IsSafeRelativeSpecification(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !Path.IsPathRooted(value)
        && value.Replace('\\', '/').Split('/').All(segment => segment is not "" and not "." and not "..");
}
