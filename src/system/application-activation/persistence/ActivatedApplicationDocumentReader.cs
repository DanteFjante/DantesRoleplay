using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.Sources;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Fail-closed filesystem adapter for exact fingerprinted active text winners.</summary>
public sealed class ActivatedApplicationDocumentReader(
    IApplicationActivationReader activations,
    ISourceRegistry sources,
    IAllowedSourceRootResolver allowedRoots) : IActivatedApplicationDocumentReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public ActivatedApplicationTextDocument? ReadText(
        ApplicationIdentifier applicationId,
        string relativePath)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        if (!GenericSourceDocument.IsNormalizedRelativePath(relativePath))
            throw Failure("ACTIVE_DOCUMENT_PATH_INVALID", "The active document locator is invalid.");

        var activation = activations.Current(applicationId);
        if (activation is null) return null;
        var winners = activation.Winners.Where(value =>
            value.RelativePath == relativePath).ToArray();
        if (winners.Length == 0) return null;
        if (winners.Length != 1)
            throw Failure("ACTIVE_DOCUMENT_AMBIGUOUS", "The active document locator is ambiguous.");

        var winner = winners[0];
        if (!winner.IsText)
            throw Failure("ACTIVE_DOCUMENT_NOT_TEXT", "The active document is not text.");
        var retained = activation.Sources.Where(value => value.SourceId == winner.SourceId).ToArray();
        var registration = sources.Get(applicationId, winner.SourceId);
        if (retained.Length != 1 || registration is null ||
            SourceRegistrationFingerprint.Compute(registration) != retained[0].RegistrationFingerprint)
            throw Failure("ACTIVE_DOCUMENT_SOURCE_DRIFT", "The active document source registration has changed.");
        if (!allowedRoots.TryResolve(registration.AllowedRootId, out var configuredRoot) ||
            string.IsNullOrWhiteSpace(configuredRoot))
            throw Failure("ACTIVE_DOCUMENT_ROOT_UNAVAILABLE", "The active document source root is unavailable.");

        try
        {
            var root = Path.GetFullPath(configuredRoot);
            var path = Path.GetFullPath(Path.Combine(root,
                winner.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(root, path))
                throw Failure("ACTIVE_DOCUMENT_OUTSIDE_ROOT", "The active document escapes its allowed source root.");
            var bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != winner.Length || Hash(bytes) != winner.ContentFingerprint)
                throw Failure("ACTIVE_DOCUMENT_FILE_DRIFT", "The active document no longer matches its retained evidence.");
            var text = StrictUtf8.GetString(bytes);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
            return new(applicationId, activation.ActivationRevision, activation.ActivationFingerprint,
                winner.SourceId, winner.RelativePath, winner.ContentFingerprint, text,
                Array.AsReadOnly(activation.Sources.Select(value => value.SourceId)
                    .Order(StringComparer.Ordinal).ToArray()));
        }
        catch (ActivatedApplicationDocumentReadException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or DecoderFallbackException)
        {
            throw Failure("ACTIVE_DOCUMENT_UNAVAILABLE", "The active document could not be read safely.", exception);
        }
    }

    private static bool Inside(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static ActivatedApplicationDocumentReadException Failure(
        string code,
        string message,
        Exception? inner = null) => new(code, message, inner);
}
