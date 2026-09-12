using System.Text;
using System.Security.Cryptography;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;
using DantesRoleplay.Tests;

namespace DantesRoleplay.ApplicationActivation.Tests;

public sealed class ApplicationActivationPreparationTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"activation-preparation-{Guid.NewGuid():N}");

    [Fact]
    public async Task Retained_bytes_survive_source_drift_and_definition_changes_preserve_old_revisions()
    {
        await using var db = _fixture.CreateContext();
        var setup = Setup(db, "retained-change");
        const string relativePath = "content/procedures/tools/procedure.fixture.inspect.md";
        var firstText = Procedure("Inspect the original fixture.");
        Write(relativePath, firstText);

        var first = await ActivateAsync(setup, null, "0123456789abcdef0123456789abcdef");
        Assert.Equal("retained-mechanic-body-v2", first.PreparationVersion);

        var secondText = Procedure("Inspect the revised fixture.");
        Write(relativePath, secondText);
        var second = await ActivateAsync(
            setup, first.ActivationFingerprint, "1123456789abcdef0123456789abcdef");
        Write(relativePath, Procedure("Unactivated source drift."));

        var reader = new ActivatedApplicationDocumentReader(setup.Service, setup.Sources, setup.Roots);
        Assert.Equal(secondText, reader.ReadText(setup.App, relativePath)!.Text);
        var catalog = new ActivatedApplicationCatalogMaterializer(
            setup.Applications, setup.Service, setup.Sources, setup.Roots).Build(setup.App);
        Assert.Contains("Inspect the revised fixture.", Assert.Single(catalog.Records).ContentJson);

        var oldEvidence = setup.Service.ReadDocumentEvidence(
            setup.App, first.ActivationRevision, "file:" + relativePath);
        Assert.Equal(Encoding.UTF8.GetBytes(firstText), oldEvidence!.RetainedBytes);
        var changes = setup.Service.ChangesAfter(setup.App, 0, 10);
        Assert.Equal([1, 2], changes.Select(value => value.Revision));
        Assert.Equal(first.ActivationFingerprint,
            setup.Service.RevisionChange(setup.App, 1)!.Fingerprint);
        Assert.Equal(second.ActivationFingerprint, setup.Service.CurrentChange(setup.App)!.Fingerprint);
        Assert.All(changes, change =>
        {
            Assert.Equal("rebuildable", change.DerivedIndex.Status);
            Assert.False(change.DerivedIndex.IsAvailable);
            Assert.False(change.DerivedIndex.IsActivationGate);
            Assert.NotEmpty(change.Discovery.RelativePaths);
            Assert.Equal(new string('F', 64), change.Dependencies.GraphFingerprint);
        });
    }

    [Fact]
    public async Task Malformed_mechanic_fails_preparation_before_switch_while_browser_module_is_inert()
    {
        await using var db = _fixture.CreateContext();
        var setup = Setup(db, "prepared-mechanic");
        const string markdownPath = "content/mechanics/check/mechanic.fixture.check.md";
        const string sourcePath = "content/mechanics/check/mechanic.fixture.check.js";
        Write(markdownPath, Mechanic());
        Write(sourcePath, "return { narration: 'prepared', effects: [] };");
        Write("content/assets/application.js", "export const browserOnly = true;");

        var first = await ActivateAsync(setup, null, "2123456789abcdef0123456789abcdef");
        Write(sourcePath, "return { narration: ;");
        var candidate = await setup.Previews.PreviewAsync(setup.App);
        var request = new ApplicationActivationRequest(
            setup.App, candidate.PreviewFingerprint, first.ActivationFingerprint);

        var exception = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            setup.Service.PreviewAsync(request, Context("3123456789abcdef0123456789abcdef")));

        Assert.Equal("EXECUTABLE_PREPARATION_FAILED", exception.Code);
        Assert.Equal(first.ActivationFingerprint, setup.Service.Current(setup.App)!.ActivationFingerprint);
        Assert.Single(setup.Service.ChangesAfter(setup.App, 0, 10));
    }

    [Fact]
    public async Task Retained_activation_rejects_source_that_breaks_out_of_the_mechanic_wrapper()
    {
        await using var db = _fixture.CreateContext();
        var setup = Setup(db, "prepared-wrapper-breakout");
        const string markdownPath = "content/mechanics/check/mechanic.fixture.check.md";
        const string sourcePath = "content/mechanics/check/mechanic.fixture.check.js";
        Write(markdownPath, Mechanic());
        Write(sourcePath, """
            return { narration: 'inside' };
            }); globalThis.wrapperEscaped = true; (function (ctx) {
            return { narration: 'outside' };
            """);

        var exception = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            ActivateAsync(setup, null, "3223456789abcdef0123456789abcdef"));

        Assert.Equal("EXECUTABLE_PREPARATION_FAILED", exception.Code);
        Assert.Null(setup.Service.Current(setup.App));
        Assert.Empty(db.Operations);
    }

    [Fact]
    public async Task Retained_activation_enforces_mechanic_parser_resource_fences()
    {
        await using var db = _fixture.CreateContext();
        var setup = Setup(db, "prepared-resource-fence");
        const string markdownPath = "content/mechanics/check/mechanic.fixture.check.md";
        const string sourcePath = "content/mechanics/check/mechanic.fixture.check.js";
        Write(markdownPath, Mechanic());
        Write(sourcePath, string.Concat(Enumerable.Repeat(
            "0;", JintMechanicEngine.MaximumMechanicTokens / 2 + 1)));

        var exception = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            ActivateAsync(setup, null, "3323456789abcdef0123456789abcdef"));

        Assert.Equal("EXECUTABLE_PREPARATION_FAILED", exception.Code);
        Assert.Null(setup.Service.Current(setup.App));
        Assert.Empty(db.Operations);
    }

    [Fact]
    public async Task Prepared_revision_missing_retained_bytes_never_falls_back_to_the_source_file()
    {
        await using var db = _fixture.CreateContext();
        var setup = Setup(db, "missing-retained-evidence");
        const string relativePath = "content/procedures/tools/procedure.fixture.inspect.md";
        Write(relativePath, Procedure("Still present in the mutable source."));
        var active = await ActivateAsync(setup, null, "5123456789abcdef0123456789abcdef");
        var evidence = Assert.Single(db.Set<ApplicationActivationDocumentEvidenceRecord>());
        evidence.RetainedBytes = null;
        await db.SaveChangesAsync();

        var exception = Assert.Throws<ActivatedApplicationDocumentReadException>(() =>
            new ActivatedApplicationDocumentReader(setup.Service, setup.Sources, setup.Roots)
                .ReadText(setup.App, relativePath));

        Assert.Equal("ACTIVATION_EVIDENCE_MISSING", exception.Code);
        Assert.Equal(active.ActivationFingerprint, setup.Service.Current(setup.App)!.ActivationFingerprint);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Candidate_length_or_hash_mismatch_fails_without_activation_or_audit(bool wrongLength)
    {
        await using var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var app = ApplicationIdentifier.Parse(wrongLength ? "candidate-length-drift" : "candidate-hash-drift");
        applications.Register(new(app, "Candidate drift", "Candidate drift fixture.", []));
        var sources = new InMemorySourceRegistry();
        var registration = sources.Register(new(app, "catalog", "fixture-root", "content/**/*",
            SourceTrust.Trusted, 0, "fixture-catalog"));
        var roots = new Root(_root);
        const string relativePath = "content/entry.json";
        const string content = "{\"value\":1}";
        Write(relativePath, content);
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var preview = new ApplicationPreviewResult(app, 1, applications.Get(app)!.Fingerprint,
            new string('A', 64), new string('B', 64), new string('C', 64), true,
            [new(registration.SourceId, SourceRegistrationFingerprint.Compute(registration), 1, 0)],
            [new("file:" + relativePath, registration.SourceId, SourceTrust.Trusted, 0,
                relativePath, "application/json",
                wrongLength ? Convert.ToHexString(SHA256.HashData(contentBytes)) : new string('D', 64),
                wrongLength ? contentBytes.LongLength + 1 : contentBytes.LongLength, true)], [], []);
        var service = new ApplicationActivationService(db, new Preview(preview),
            new EmptyApplicationExtensionRegistry(), sources, roots, new Impact(app), new OperationLog(db));

        var exception = await Assert.ThrowsAsync<ApplicationActivationException>(() => service.PreviewAsync(
            new(app, preview.PreviewFingerprint, null), Context("4123456789abcdef0123456789abcdef")));

        Assert.Equal("SOURCE_DOCUMENT_DRIFT", exception.Code);
        Assert.Null(service.Current(app));
        Assert.Empty(db.Operations);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private SetupState Setup(DantesRoleplayDbContext db, string id)
    {
        var applications = new SqliteApplicationRegistry(db);
        var app = ApplicationIdentifier.Parse(id);
        applications.Register(new(app, id, "Prepared activation fixture.", []));
        var sources = new InMemorySourceRegistry();
        sources.Register(new(app, "catalog", "fixture-root", "content/**/*",
            SourceTrust.Trusted, 0, "fixture-catalog"));
        var roots = new Root(_root);
        var previews = new ApplicationPreviewService(applications, sources,
            new RegisteredSourceScanner(sources, roots, new LocalDocumentScanner()),
            new SourceOverlayResolver());
        var service = new ApplicationActivationService(db, previews,
            new EmptyApplicationExtensionRegistry(), sources, roots, new Impact(app), new OperationLog(db));
        return new(app, applications, sources, roots, previews, service);
    }

    private static async Task<ActiveApplicationManifest> ActivateAsync(
        SetupState setup,
        string? expected,
        string token)
    {
        var preview = await setup.Previews.PreviewAsync(setup.App);
        Assert.True(preview.IsValid, string.Join(';', preview.Problems.Select(value => value.Code)));
        var request = new ApplicationActivationRequest(setup.App, preview.PreviewFingerprint, expected);
        var context = Context(token);
        await setup.Service.PreviewAsync(request, context);
        return (await setup.Service.ActivateAsync(request, context)).Activation;
    }

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static string Procedure(string description) => $$"""
        ---
        id: procedure.fixture.inspect
        category: tools.inspect
        name: Inspect fixture
        governs: query(kind: "fixture.inspect")
        status: active
        ---

        ## Description
        {{description}}

        ## Instructions
        1. Inspect it.

        ## Constraints
        - Preserve it.
        """;

    private static string Mechanic() => """
        ---
        id: mechanic.fixture.check
        category: fixture.check
        name: Check fixture
        scope: action
        status: active
        ---

        ## Description
        Check a fixture.

        ## Matches
        check fixture

        ## Requirements
        ```json
        {}
        ```
        """;

    private static ApplicationActivationContext Context(string token) => new(
        token, "Activate a prepared fixture.", ["procedure.system.use"],
        new AuthorizationAuditEvidence("principal." + new string('a', 64), "test", "modify",
            "system.private-host", "activation-preparation-test", true, "PRIVATE_OPERATOR_ALLOWED"));

    private sealed record SetupState(
        ApplicationIdentifier App,
        SqliteApplicationRegistry Applications,
        InMemorySourceRegistry Sources,
        Root Roots,
        ApplicationPreviewService Previews,
        ApplicationActivationService Service);

    private sealed class Root(string root) : IAllowedSourceRootResolver
    {
        public bool TryResolve(string allowedRootId, out string canonicalPath)
        {
            canonicalPath = root;
            return allowedRootId == "fixture-root";
        }
    }

    private sealed class Impact(ApplicationIdentifier app) : IProjectionImpactService
    {
        public ProjectionImpactReport Analyze(
            ApplicationIdentifier applicationId,
            string? rootId = null,
            bool transitive = true) => new(app, new string('F', 64), null, transitive, [], [], []);
    }

    private sealed class Preview(ApplicationPreviewResult result) : IApplicationPreviewService
    {
        public Task<ApplicationPreviewResult> PreviewAsync(
            ApplicationIdentifier applicationId,
            CancellationToken cancellationToken = default) => Task.FromResult(result);

        public Task<ApplicationPreviewResult> PreviewAsync(
            ApplicationIdentifier applicationId,
            IReadOnlyList<string> sourceIds,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
