using DantesRoleplay.Applications;
using DantesRoleplay.LocalAI;

namespace DantesRoleplay.Sources;

/// <summary>Adapts registered relative sources to the generic local scanner without exposing host paths.</summary>
public sealed class RegisteredSourceScanner(
    ISourceRegistry sources,
    IAllowedSourceRootResolver allowedRoots,
    ILocalDocumentScanner scanner) : IRegisteredSourceScanner
{
    public async Task<RegisteredSourceScanResult> ScanAsync(
        ApplicationIdentifier applicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var documents = new List<GenericSourceDocument>();
        var problems = new List<SourceOverlayProblem>();
        var registeredSources = sources.For(applicationId);
        if (registeredSources.Count == 0)
            problems.Add(new("SOURCE_APPLICATION_SOURCES_UNAVAILABLE", "", "",
                "The application has no registered sources available for scanning."));
        foreach (var source in registeredSources)
        {
            if (!allowedRoots.TryResolve(source.AllowedRootId, out var root) || string.IsNullOrWhiteSpace(root))
            {
                problems.Add(new("SOURCE_ROOT_UNKNOWN", source.SourceId, "", "The configured allowed root is unavailable."));
                continue;
            }

            string canonicalRoot;
            string specification;
            try
            {
                canonicalRoot = Path.GetFullPath(root);
                specification = Path.GetFullPath(Path.Combine(canonicalRoot,
                    source.RelativePathOrGlob.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                problems.Add(new("SOURCE_SPECIFICATION_INVALID", source.SourceId, "", "The source specification is invalid."));
                continue;
            }
            if (!Inside(canonicalRoot, specification))
            {
                problems.Add(new("SOURCE_PATH_OUTSIDE_ROOT", source.SourceId, "", "The source specification escapes its allowed root."));
                continue;
            }

            var result = await scanner.ScanAsync(new([specification]), new() { AllowedRoots = [canonicalRoot] }, cancellationToken);
            foreach (var problem in result.Problems)
                problems.Add(new(problem.Code, source.SourceId, "", "The registered source could not be scanned."));
            foreach (var document in result.Documents)
            {
                var relative = Path.GetRelativePath(canonicalRoot, document.Path).Replace('\\', '/');
                if (!GenericSourceDocument.IsNormalizedRelativePath(relative))
                {
                    problems.Add(new("SOURCE_DOCUMENT_OUTSIDE_ROOT", source.SourceId, "", "A scanned document escaped its allowed root."));
                    continue;
                }
                documents.Add(GenericSourceDocument.Create(applicationId, source.SourceId, source.Trust, source.Precedence,
                    relative, document.MediaType, document.Sha256, document.Length, document.Text is not null));
            }
        }

        return new(documents.OrderBy(value => value.SourceId, StringComparer.Ordinal)
                .ThenBy(value => value.RelativePath, StringComparer.Ordinal).ToArray(),
            problems.OrderBy(value => value.Code, StringComparer.Ordinal)
                .ThenBy(value => value.SourceId, StringComparer.Ordinal).ToArray());
    }

    private static bool Inside(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }
}
