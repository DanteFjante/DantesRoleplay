using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Sources;

namespace DantesRoleplay.ApplicationPreview;

/// <summary>
/// Reads only the exact winning mechanic contracts and trusted review records, compares their
/// closed authored declarations, and converts unresolved deterministic overlap into preview
/// problems. JavaScript is treated as bounded text evidence and is never executed here.
/// </summary>
public sealed class ApplicationAntiSprawlGate(IAllowedSourceRootResolver allowedRoots)
    : IApplicationAntiSprawlGate
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<ApplicationAntiSprawlEvaluation> EvaluateAsync(
        ApplicationIdentifier applicationId,
        IReadOnlyList<SourceRegistration> registrations,
        IReadOnlyList<EffectiveSourceDocument> winners,
        CompiledApplicationExtensionSet extensionSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(winners);
        ArgumentNullException.ThrowIfNull(extensionSet);
        var registrationsById = registrations.ToDictionary(value => value.SourceId, StringComparer.Ordinal);
        var winnersByPath = winners.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        var problems = new List<SourceOverlayProblem>();
        var mechanics = new List<CatalogAntiSprawlMechanic>();
        var reviews = new List<CatalogAntiSprawlReview>();

        problems.AddRange(extensionSet.Extensions
            .SelectMany(extension => extension.NamespaceIds.Select(root => (extension.ExtensionId, Root: root)))
            .GroupBy(value => value.Root, StringComparer.Ordinal)
            .Where(group => group.Select(value => value.ExtensionId).Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(_ => Problem("ANTI_SPRAWL_NAMESPACE_CONFLICT",
                "More than one selected extension claims the same mechanic namespace root.")));

        foreach (var winner in winners.Where(IsMechanicMarkdown).OrderBy(value => value.RelativePath, StringComparer.Ordinal))
        {
            var sourcePath = Path.ChangeExtension(winner.RelativePath, ".js").Replace('\\', '/');
            if (!winnersByPath.TryGetValue(sourcePath, out var sourceWinner)
                || sourceWinner.SourceId != winner.SourceId)
            {
                problems.Add(Problem("ANTI_SPRAWL_MECHANIC_SOURCE_MISSING",
                    "A candidate mechanic has no same-source JavaScript sidecar for anti-sprawl analysis."));
                continue;
            }
            try
            {
                var markdown = await ReadAsync(winner, registrationsById, cancellationToken);
                var source = await ReadAsync(sourceWinner, registrationsById, cancellationToken);
                var file = MechanicFile.Parse(markdown, winner.RelativePath, source);
                var qualifiedId = file.Id.StartsWith(applicationId.Value + ".", StringComparison.Ordinal)
                    ? file.Id : applicationId.Value + "." + file.Id;
                var ownership = NamespaceOwnership(applicationId, qualifiedId, extensionSet);
                mechanics.Add(CatalogAntiSprawlMechanic.Create(file, qualifiedId,
                    ownership.Owner, ownership.Key, ownership.Ambiguous));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException or DecoderFallbackException)
            {
                problems.Add(Problem("ANTI_SPRAWL_MECHANIC_INVALID",
                    "A candidate mechanic could not be read and parsed for anti-sprawl analysis."));
            }
        }

        foreach (var winner in winners.Where(IsReview).OrderBy(value => value.RelativePath, StringComparer.Ordinal))
        {
            // Only trusted authored evidence may waive an activation conflict. An untrusted review
            // remains inert input; any conflict it attempts to waive therefore stays unresolved.
            if (winner.Trust != SourceTrust.Trusted) continue;
            try
            {
                reviews.Add(CatalogAntiSprawlReview.Parse(
                    await ReadAsync(winner, registrationsById, cancellationToken), winner.RelativePath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException or DecoderFallbackException or System.Text.Json.JsonException)
            {
                problems.Add(Problem("ANTI_SPRAWL_REVIEW_INVALID",
                    "A trusted anti-sprawl review record is malformed or unreadable."));
            }
        }

        CatalogAntiSprawlAnalysis analysis;
        try
        {
            analysis = CatalogAntiSprawlAnalyzer.Analyze(mechanics, reviews);
        }
        catch (InvalidOperationException)
        {
            problems.Add(Problem("ANTI_SPRAWL_REVIEW_AMBIGUOUS",
                "More than one trusted anti-sprawl review claims the same mechanic pair."));
            return new(problems, []);
        }
        problems.AddRange(analysis.Blocking.Select(finding => Problem(finding.Code, finding.Summary)));
        return new(problems.OrderBy(value => value.Code, StringComparer.Ordinal)
            .ThenBy(value => value.Message, StringComparer.Ordinal).ToArray(), analysis.Findings);
    }

    private async Task<string> ReadAsync(
        EffectiveSourceDocument winner,
        IReadOnlyDictionary<string, SourceRegistration> registrations,
        CancellationToken cancellationToken)
    {
        if (!winner.IsText || !registrations.TryGetValue(winner.SourceId, out var registration)
            || !allowedRoots.TryResolve(registration.AllowedRootId, out var configuredRoot)
            || string.IsNullOrWhiteSpace(configuredRoot))
            throw new IOException("Registered source text is unavailable.");
        var root = Path.GetFullPath(configuredRoot);
        var path = Path.GetFullPath(Path.Combine(root,
            winner.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!Inside(root, path)) throw new IOException("Registered source path escaped its root.");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.LongLength != winner.Length || Hash(bytes) != winner.ContentFingerprint)
            throw new IOException("Registered source text changed after scanning.");
        var text = StrictUtf8.GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    private static (string Owner, string Key, bool Ambiguous) NamespaceOwnership(
        ApplicationIdentifier applicationId,
        string qualifiedId,
        CompiledApplicationExtensionSet extensionSet)
    {
        var matches = extensionSet.Extensions.SelectMany(extension => extension.NamespaceIds.Select(root =>
                (Owner: extension.ExtensionId, Root: root)))
            .Where(value => qualifiedId == value.Root || qualifiedId.StartsWith(value.Root + ".", StringComparison.Ordinal))
            .OrderByDescending(value => value.Root.Length)
            .ThenBy(value => value.Owner, StringComparer.Ordinal).ToArray();
        var ambiguous = matches.Length > 1 && matches[0].Root.Length == matches[1].Root.Length;
        if (matches.Length != 0)
            return (matches[0].Owner,
                qualifiedId.Length == matches[0].Root.Length ? "" : qualifiedId[(matches[0].Root.Length + 1)..],
                ambiguous);
        var prefix = applicationId.Value + ".";
        return ("base", qualifiedId.StartsWith(prefix, StringComparison.Ordinal)
            ? qualifiedId[prefix.Length..] : qualifiedId, false);
    }

    private static bool IsMechanicMarkdown(EffectiveSourceDocument value) =>
        value.IsText && value.RelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        && value.RelativePath.Split('/').Contains("mechanics", StringComparer.Ordinal);

    private static bool IsReview(EffectiveSourceDocument value)
        => value.IsText && value.RelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && CatalogAntiSprawlReviewCatalog.IsReviewPath(value.RelativePath);

    private static bool Inside(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static SourceOverlayProblem Problem(string code, string message) => new(code, "", "", message);
}
