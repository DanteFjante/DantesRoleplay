using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.Applications;

namespace DantesRoleplay.Sources;

/// <summary>Trusted host configuration that resolves an opaque allowed-root ID; never protocol input.</summary>
public interface IAllowedSourceRootResolver
{
    bool TryResolve(string allowedRootId, out string canonicalPath);
}

/// <summary>Safe host metadata for planning. It exposes configured opaque IDs, never paths.</summary>
public interface IAllowedSourceRootCatalog
{
    IReadOnlyList<string> ListIds(int limit);
}

public sealed class EmptyAllowedSourceRootResolver : IAllowedSourceRootResolver, IAllowedSourceRootCatalog
{
    public bool TryResolve(string allowedRootId, out string canonicalPath)
    {
        canonicalPath = string.Empty;
        return false;
    }

    public IReadOnlyList<string> ListIds(int limit)
    {
        if (limit is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(limit));
        return [];
    }
}

public sealed record RegisteredSourceScanResult(
    IReadOnlyList<GenericSourceDocument> Documents,
    IReadOnlyList<SourceOverlayProblem> Problems);

public interface IRegisteredSourceScanner
{
    Task<RegisteredSourceScanResult> ScanAsync(
        ApplicationIdentifier applicationId,
        CancellationToken cancellationToken = default);
}

/// <summary>A non-executable generic document discovered from one registered source.</summary>
public sealed record GenericSourceDocument(
    ApplicationIdentifier ApplicationId,
    string SourceId,
    SourceTrust Trust,
    int Precedence,
    string RelativePath,
    string MediaType,
    string ContentFingerprint,
    long Length,
    bool IsText)
{
    public string LogicalIdentity => $"file:{RelativePath}";

    public static GenericSourceDocument Create(
        ApplicationIdentifier applicationId,
        string sourceId,
        SourceTrust trust,
        int precedence,
        string relativePath,
        string mediaType,
        string contentFingerprint,
        long length,
        bool isText)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(mediaType)
            || !Enum.IsDefined(trust) || !IsNormalizedRelativePath(relativePath)
            || !IsSha256(contentFingerprint) || length < 0)
            throw new ArgumentException("A generic source document requires safe logical metadata and a SHA-256 fingerprint.");
        return new(applicationId, sourceId, trust, precedence, relativePath, mediaType,
            contentFingerprint.ToUpperInvariant(), length, isText);
    }

    public static bool IsNormalizedRelativePath(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value == value.Replace('\\', '/')
        && !value.StartsWith("/", StringComparison.Ordinal)
        && value.Split('/').All(segment => segment is not "" and not "." and not "..");

    internal static bool IsSha256(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);
}

public sealed record SourceOverlayProblem(string Code, string SourceId, string LogicalPath, string Message);

public sealed record EffectiveSourceDocument(
    string LogicalIdentity,
    string SourceId,
    SourceTrust Trust,
    int Precedence,
    string RelativePath,
    string MediaType,
    string ContentFingerprint,
    long Length,
    bool IsText);

public sealed record ShadowedSourceDocument(
    string LogicalIdentity,
    string SourceId,
    SourceTrust Trust,
    int Precedence,
    string RelativePath,
    string ContentFingerprint,
    string WinnerSourceId,
    string Reason);

public sealed record CandidateApplicationManifest(
    ApplicationIdentifier ApplicationId,
    string Fingerprint,
    bool IsValid,
    IReadOnlyList<EffectiveSourceDocument> Winners,
    IReadOnlyList<ShadowedSourceDocument> Shadows,
    IReadOnlyList<SourceOverlayProblem> Problems);

public interface ISourceOverlayResolver
{
    CandidateApplicationManifest Resolve(
        ApplicationIdentifier applicationId,
        IReadOnlyList<GenericSourceDocument> documents,
        IReadOnlyList<SourceOverlayProblem>? scanProblems = null);
}

/// <summary>Pure, deterministic overlay resolution. It has no file, database, catalog, or activation access.</summary>
public sealed class SourceOverlayResolver : ISourceOverlayResolver
{
    public CandidateApplicationManifest Resolve(
        ApplicationIdentifier applicationId,
        IReadOnlyList<GenericSourceDocument> documents,
        IReadOnlyList<SourceOverlayProblem>? scanProblems = null)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(documents);
        // Scanner diagnostics are evidence, not an exception channel. Preserve their stable code
        // and source identity, but replace caller-supplied path/message text with a closed message.
        var problems = (scanProblems ?? []).Select(problem => new SourceOverlayProblem(
            SafeCode(problem.Code), SafeSourceId(problem.SourceId), "",
            "A registered source reported a scan problem.")).ToList();
        var validDocuments = new List<GenericSourceDocument>();
        foreach (var document in documents)
        {
            if (document.ApplicationId != applicationId
                || !GenericSourceDocument.IsNormalizedRelativePath(document.RelativePath)
                || !GenericSourceDocument.IsSha256(document.ContentFingerprint)
                || !Enum.IsDefined(document.Trust)
                || document.Length < 0)
            {
                problems.Add(new("SOURCE_DOCUMENT_INVALID", document.SourceId, "", "The source document metadata is invalid."));
                continue;
            }
            validDocuments.Add(document with { ContentFingerprint = document.ContentFingerprint.ToUpperInvariant() });
        }

        var winners = new List<EffectiveSourceDocument>();
        var shadows = new List<ShadowedSourceDocument>();
        foreach (var group in validDocuments.GroupBy(document => document.LogicalIdentity, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var highestTrust = group.Max(document => document.Trust);
            var eligible = group.Where(document => document.Trust == highestTrust).ToArray();
            var highestPrecedence = eligible.Max(document => document.Precedence);
            var leaders = eligible.Where(document => document.Precedence == highestPrecedence)
                .OrderBy(document => document.SourceId, StringComparer.Ordinal).ToArray();
            if (leaders.Length != 1)
            {
                problems.Add(new("SOURCE_OVERLAY_CONFLICT", "", group.Key,
                    "Equal-trust sources at equal precedence cannot both define one logical document."));
                continue;
            }

            var winner = leaders[0];
            winners.Add(new(group.Key, winner.SourceId, winner.Trust, winner.Precedence, winner.RelativePath, winner.MediaType,
                winner.ContentFingerprint, winner.Length, winner.IsText));
            foreach (var shadow in group.Where(document => document != winner)
                         .OrderBy(document => document.SourceId, StringComparer.Ordinal))
            {
                var reason = shadow.Trust < winner.Trust ? "lower-trust" : "lower-precedence";
                shadows.Add(new(group.Key, shadow.SourceId, shadow.Trust, shadow.Precedence,
                    shadow.RelativePath, shadow.ContentFingerprint, winner.SourceId, reason));
            }
        }

        var orderedProblems = problems
            .OrderBy(problem => problem.Code, StringComparer.Ordinal)
            .ThenBy(problem => problem.SourceId, StringComparer.Ordinal)
            .ThenBy(problem => problem.LogicalPath, StringComparer.Ordinal)
            .ThenBy(problem => problem.Message, StringComparer.Ordinal)
            .ToArray();
        var orderedWinners = winners.OrderBy(value => value.LogicalIdentity, StringComparer.Ordinal).ToArray();
        var orderedShadows = shadows.OrderBy(value => value.LogicalIdentity, StringComparer.Ordinal)
            .ThenBy(value => value.SourceId, StringComparer.Ordinal).ToArray();
        var fingerprint = Fingerprint(applicationId, orderedWinners, orderedShadows, orderedProblems);
        return new(applicationId, fingerprint, orderedProblems.Length == 0,
            ReadOnly(orderedWinners), ReadOnly(orderedShadows), ReadOnly(orderedProblems));
    }

    private static string Fingerprint(
        ApplicationIdentifier applicationId,
        IReadOnlyList<EffectiveSourceDocument> winners,
        IReadOnlyList<ShadowedSourceDocument> shadows,
        IReadOnlyList<SourceOverlayProblem> problems)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            applicationId = applicationId.Value,
            winners,
            shadows,
            problems
        });
        return Convert.ToHexString(SHA256.HashData(canonical));
    }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(values.ToArray());

    private static string SafeCode(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 100 && value.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_')
        ? value : "SOURCE_SCAN_PROBLEM";

    private static string SafeSourceId(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 200 && value.All(c => !char.IsControl(c) && c is not '/' and not '\\' and not ':')
        ? value : "";
}
