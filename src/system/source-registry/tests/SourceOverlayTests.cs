using DantesRoleplay.Applications;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Sources;

namespace DantesRoleplay.SourceRegistry.Tests;

public sealed class SourceOverlayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"source-overlay-{Guid.NewGuid():N}");

    public SourceOverlayTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "catalog"));
        File.WriteAllText(Path.Combine(_root, "catalog", "effective.txt"), "effective");
    }

    [Fact]
    public void Higher_precedence_same_trust_wins_with_stable_redacted_fingerprint()
    {
        var application = ApplicationIdentifier.Parse("fixture-app");
        var baseDocument = Document(application, "base", SourceTrust.Trusted, 1, "catalog/item.txt", 'A');
        var overrideDocument = Document(application, "override", SourceTrust.Trusted, 10, "catalog/item.txt", 'B');
        var resolver = new SourceOverlayResolver();

        var first = resolver.Resolve(application, [overrideDocument, baseDocument]);
        var replay = resolver.Resolve(application, [baseDocument, overrideDocument]);

        Assert.True(first.IsValid);
        Assert.Equal(first.Fingerprint, replay.Fingerprint);
        Assert.Equal("override", Assert.Single(first.Winners).SourceId);
        var shadow = Assert.Single(first.Shadows);
        Assert.Equal("base", shadow.SourceId);
        Assert.Equal("lower-precedence", shadow.Reason);
        Assert.DoesNotContain(_root, first.Fingerprint, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => ((IList<EffectiveSourceDocument>)first.Winners).Clear());

        var changedWinnerPrecedence = resolver.Resolve(application,
            [overrideDocument with { Precedence = 11 }, baseDocument]);
        var changedShadowContent = resolver.Resolve(application,
            [overrideDocument, baseDocument with { ContentFingerprint = new string('C', 64) }]);
        Assert.NotEqual(first.Fingerprint, changedWinnerPrecedence.Fingerprint);
        Assert.NotEqual(first.Fingerprint, changedShadowContent.Fingerprint);
    }

    [Fact]
    public void Lower_trust_cannot_override_and_equal_precedence_is_an_invalid_candidate()
    {
        var application = ApplicationIdentifier.Parse("fixture-app");
        var trusted = Document(application, "trusted", SourceTrust.Trusted, 1, "catalog/item.txt", 'A');
        var untrusted = Document(application, "untrusted", SourceTrust.Untrusted, 99, "catalog/item.txt", 'B');
        var resolver = new SourceOverlayResolver();

        var protectedCandidate = resolver.Resolve(application, [trusted, untrusted]);
        Assert.True(protectedCandidate.IsValid);
        Assert.Equal("trusted", Assert.Single(protectedCandidate.Winners).SourceId);
        Assert.Equal("lower-trust", Assert.Single(protectedCandidate.Shadows).Reason);

        var conflict = resolver.Resolve(application,
            [Document(application, "one", SourceTrust.Trusted, 5, "catalog/conflict.txt", 'C'),
             Document(application, "two", SourceTrust.Trusted, 5, "catalog/conflict.txt", 'D')]);
        Assert.False(conflict.IsValid);
        Assert.Empty(conflict.Winners);
        Assert.Contains(conflict.Problems, problem => problem.Code == "SOURCE_OVERLAY_CONFLICT");

        var revealed = resolver.Resolve(application, [trusted]);
        Assert.True(revealed.IsValid);
        Assert.Equal("trusted", Assert.Single(revealed.Winners).SourceId);

        Assert.Throws<ArgumentException>(() => GenericSourceDocument.Create(application, "bad",
            (SourceTrust)99, 1, "catalog/item.txt", "text/plain", new string('A', 64), 1, true));
    }

    [Fact]
    public async Task Registered_glob_scanning_uses_only_resolved_roots_and_redacts_paths()
    {
        var application = ApplicationIdentifier.Parse("fixture-app");
        var sources = new InMemorySourceRegistry();
        sources.Register(new(application, "catalog", "workspace", "catalog/**/*.txt", SourceTrust.Trusted, 1, "catalog"));
        var scanner = new RegisteredSourceScanner(sources, new Roots([("workspace", _root)]), new LocalDocumentScanner());

        var scan = await scanner.ScanAsync(application);
        var document = Assert.Single(scan.Documents);
        Assert.Empty(scan.Problems);
        Assert.Equal("catalog/effective.txt", document.RelativePath);
        Assert.DoesNotContain(_root, document.RelativePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(':', document.RelativePath);

        var candidate = new SourceOverlayResolver().Resolve(application, scan.Documents, scan.Problems);
        Assert.True(candidate.IsValid);
        Assert.Equal("file:catalog/effective.txt", Assert.Single(candidate.Winners).LogicalIdentity);

        var unknownSources = new InMemorySourceRegistry();
        unknownSources.Register(new(application, "unknown", "absent", "catalog/**/*.txt", SourceTrust.Trusted, 1, "unknown"));
        var unknown = await new RegisteredSourceScanner(unknownSources, new Roots([]), new LocalDocumentScanner()).ScanAsync(application);
        Assert.Contains(unknown.Problems, problem => problem.Code == "SOURCE_ROOT_UNKNOWN");
        Assert.False(new SourceOverlayResolver().Resolve(application, unknown.Documents, unknown.Problems).IsValid);

        var absent = await new RegisteredSourceScanner(new InMemorySourceRegistry(), new Roots([]), new LocalDocumentScanner())
            .ScanAsync(application);
        Assert.Contains(absent.Problems, problem => problem.Code == "SOURCE_APPLICATION_SOURCES_UNAVAILABLE");
        Assert.False(new SourceOverlayResolver().Resolve(application, absent.Documents, absent.Problems).IsValid);
    }

    [Fact]
    public void Caller_supplied_scan_details_are_redacted_before_manifesting()
    {
        var application = ApplicationIdentifier.Parse("fixture-app");
        var rootPath = Path.Combine(_root, "private.txt");
        var candidate = new SourceOverlayResolver().Resolve(application, [],
            [new("SOURCE_SCAN_FAILED", "source", rootPath, $"Failed at {rootPath}")]);

        var problem = Assert.Single(candidate.Problems);
        Assert.Equal("", problem.LogicalPath);
        Assert.DoesNotContain(_root, problem.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_root, candidate.Fingerprint, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static GenericSourceDocument Document(
        ApplicationIdentifier application,
        string source,
        SourceTrust trust,
        int precedence,
        string path,
        char fingerprintCharacter) =>
        GenericSourceDocument.Create(application, source, trust, precedence, path, "text/plain",
            new string(fingerprintCharacter, 64), 1, true);

    private sealed class Roots(IReadOnlyList<(string Id, string Path)> values) : IAllowedSourceRootResolver
    {
        public bool TryResolve(string allowedRootId, out string canonicalPath)
        {
            var match = values.SingleOrDefault(value => value.Id == allowedRootId);
            canonicalPath = match.Path ?? string.Empty;
            return !string.IsNullOrEmpty(canonicalPath);
        }
    }
}
