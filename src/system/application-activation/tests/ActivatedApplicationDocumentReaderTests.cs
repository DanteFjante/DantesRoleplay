using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Tests;

public sealed class ActivatedApplicationDocumentReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"active-document-{Guid.NewGuid():N}");

    [Fact]
    public void Exact_active_text_winner_is_read_and_file_drift_fails_closed()
    {
        Directory.CreateDirectory(Path.Combine(_root, "metadata"));
        const string relativePath = "metadata/authorized-knowledge.json";
        var path = Path.Combine(_root, "metadata", "authorized-knowledge.json");
        const string text = "{\"format\":\"fixture\"}";
        File.WriteAllText(path, text, new UTF8Encoding(false));
        var bytes = File.ReadAllBytes(path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var application = ApplicationIdentifier.Parse("fixture");
        var sources = new InMemorySourceRegistry();
        var registration = sources.Register(new(application, "fixture-source", "fixture-root",
            "metadata/*.json", SourceTrust.Trusted, 1, "fixture-source"));
        var manifest = new ActiveApplicationManifest(
            application, 2, 1, new('A', 64), new('B', 64), new('C', 64), new('D', 64),
            new('E', 64), new('F', 64), "fixture-v1", true,
            [new("fixture-source", SourceRegistrationFingerprint.Compute(registration), 1, 0)],
            [new("file:" + relativePath, "fixture-source", SourceTrust.Trusted, 1, relativePath,
                "application/json", hash, bytes.LongLength, true)],
            "operation.fixture", DateTime.UnixEpoch);
        var reader = new ActivatedApplicationDocumentReader(
            new Activation(manifest), sources, new Root(_root));

        var result = reader.ReadText(application, relativePath);

        Assert.NotNull(result);
        Assert.Equal(text, result.Text);
        Assert.Equal(hash, result.ContentFingerprint);

        File.WriteAllText(path, "{\"format\":\"drift\"}", new UTF8Encoding(false));
        var drift = Assert.Throws<ActivatedApplicationDocumentReadException>(() =>
            reader.ReadText(application, relativePath));
        Assert.Equal("ACTIVE_DOCUMENT_FILE_DRIFT", drift.Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class Activation(ActiveApplicationManifest manifest) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            applicationId == manifest.ApplicationId ? manifest : null;
    }

    private sealed class Root(string path) : IAllowedSourceRootResolver
    {
        public bool TryResolve(string allowedRootId, out string canonicalPath)
        {
            canonicalPath = path;
            return allowedRootId == "fixture-root";
        }
    }
}
