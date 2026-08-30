using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using Jint;

namespace DantesRoleplay.Tests;

/// <summary>
/// Slice 1 of catalog portability: the live ruleset comes out as ordinary files, and comes back
/// in as the same records.
///
/// The property that matters is not "files appeared" but "the fingerprint survived the trip". If
/// an exported rule parses back to something that fingerprints differently, then on the next
/// import every untouched rule reads as edited, and the drift detection Slice 2 is built on is
/// worse than useless — it is confidently wrong.
/// </summary>
public sealed class CatalogExportTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _fixture.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<DantesRoleplayDbContext> SeededAsync()
    {
        var db = _fixture.CreateContext();

        await new ContentHashBackfill(db).RunAsync();
        await new ProcedureSeeder(new ProcedureStore(db)).SeedAsync();
        await new MechanicSeeder(new MechanicStore(db)).SeedAsync();

        return db;
    }

    // ---- the round trip ------------------------------------------------------------------

    [Fact]
    public async Task Exported_markdown_ends_with_one_newline_and_no_blank_line()
    {
        await using var db = await SeededAsync();
        await new CatalogExporter(db).ExportAsync(_root);

        var mechanic = await ReadAsync(
            CatalogLayout.MechanicMarkdown("check", "mechanic.check.threshold"));
        var procedure = await ReadAsync(
            CatalogLayout.ProcedureMarkdown("world", "procedure.world.model"));

        Assert.EndsWith("\n", mechanic, StringComparison.Ordinal);
        Assert.EndsWith("\n", procedure, StringComparison.Ordinal);
        Assert.False(mechanic.EndsWith("\n\n", StringComparison.Ordinal));
        Assert.False(procedure.EndsWith("\n\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// The gate. An exported rule, read back from its .md and its .js, fingerprints exactly as the
    /// row it came from.
    ///
    /// `mechanic.check.threshold` is the fixture because its authored form is already in the
    /// repository catalog, so this walks the whole loop the
    /// catalog exists to support: file → database → file → database.
    /// </summary>
    [Fact]
    public async Task An_exported_mechanic_reparses_to_the_same_fingerprint()
    {
        await using var db = await SeededAsync();
        var store = new MechanicStore(db);

        await new CatalogExporter(db).ExportAsync(_root);

        var live = await store.GetAsync("mechanic.check.threshold");
        Assert.NotNull(live);

        var markdown = await ReadAsync(CatalogLayout.MechanicMarkdown(live.Category, live.Id));
        var source = await ReadAsync(CatalogLayout.MechanicSource(live.Category, live.Id));

        var reparsed = MechanicFile.Parse(markdown, "check-threshold.md", source);

        Assert.Equal(live.Id, reparsed.Id);
        Assert.Equal(live.SourceHash, reparsed.ContentHash);
    }

    [Fact]
    public async Task An_exported_contract_reparses_to_the_same_fingerprint()
    {
        await using var db = await SeededAsync();

        await new CatalogExporter(db).ExportAsync(_root);

        var live = await new ProcedureStore(db).GetAsync("procedure.world.model");
        Assert.NotNull(live);

        var markdown = await ReadAsync(CatalogLayout.ProcedureMarkdown(live.Category, live.Id));
        var reparsed = ProcedureFile.Parse(markdown, "world-model.md");

        Assert.Equal(live.Id, reparsed.Id);
        Assert.Equal(live.SourceHash, reparsed.ContentHash);
    }

    /// <summary>Every exported rule, not just the one with a fixture.</summary>
    [Fact]
    public async Task Every_exported_mechanic_reparses_to_the_same_fingerprint()
    {
        await using var db = await SeededAsync();
        var store = new MechanicStore(db);

        await new CatalogExporter(db).ExportAsync(_root);

        foreach (var summary in await store.FindAsync(includeInactive: true))
        {
            var live = await store.GetAsync(summary.Id);
            Assert.NotNull(live);

            var reparsed = MechanicFile.Parse(
                await ReadAsync(CatalogLayout.MechanicMarkdown(live.Category, live.Id)),
                live.Id,
                await ReadAsync(CatalogLayout.MechanicSource(live.Category, live.Id)));

            Assert.Equal(live.SourceHash, reparsed.ContentHash);
        }
    }

    // ---- what got written ----------------------------------------------------------------

    [Fact]
    public async Task Every_record_is_written_and_listed_in_the_manifest()
    {
        await using var db = await SeededAsync();

        var result = await new CatalogExporter(db).ExportAsync(_root);

        var manifest = CatalogManifest.FromJson(
            await ReadAsync(CatalogLayout.ManifestFileName),
            CatalogLayout.ManifestFileName);

        Assert.Equal(result.Mechanics, manifest.Records.Count(r => r.Kind == CatalogRecordKind.Mechanic));
        Assert.Equal(result.Procedures, manifest.Records.Count(r => r.Kind == CatalogRecordKind.Procedure));
        Assert.Equal(result.EventTypes, manifest.Records.Count(r => r.Kind == CatalogRecordKind.EventType));
        Assert.Equal(result.Subscriptions, manifest.Records.Count(r => r.Kind == CatalogRecordKind.Subscription));
        Assert.Equal(
            result.ComponentDefinitions,
            manifest.Records.Count(r => r.Kind == CatalogRecordKind.ComponentDefinition));
        Assert.Equal(result.Entities, manifest.Records.Count(r => r.Kind == CatalogRecordKind.Entity));

        // The relationship set is one record with no per-edge identity, so it is listed once and is
        // not part of the per-record count.
        Assert.Single(manifest.Records, r => r.Kind == CatalogRecordKind.Relationships);

        // Every manifest path resolves, and every mechanic has its JavaScript beside it.
        foreach (var entry in manifest.Records)
        {
            Assert.True(
                File.Exists(CatalogLayout.ToFileSystemPath(_root, entry.Path)),
                $"{entry.Id} is in the manifest but {entry.Path} does not exist.");

            if (entry.Kind == CatalogRecordKind.Mechanic)
            {
                var sourcePath = entry.Path[..^CatalogLayout.MarkdownExtension.Length]
                                 + CatalogLayout.SourceExtension;

                Assert.True(
                    File.Exists(CatalogLayout.ToFileSystemPath(_root, sourcePath)),
                    $"{entry.Id} has no .js beside its .md.");
            }
        }
    }

    /// <summary>
    /// A category becomes a directory path, so a rule is findable by browsing rather than by
    /// grepping a flat folder of dotted filenames.
    /// </summary>
    [Fact]
    public async Task A_category_becomes_a_directory_path()
    {
        await using var db = await SeededAsync();
        await new CatalogExporter(db).ExportAsync(_root);

        Assert.True(File.Exists(Path.Combine(
            _root, "mechanics", "check", "mechanic.check.threshold.md")));

        Assert.True(File.Exists(Path.Combine(
            _root, "mechanics", "check", "mechanic.check.threshold.js")));

        Assert.True(File.Exists(Path.Combine(
            _root, "procedures", "world", "procedure.world.model.md")));
    }

    /// <summary>
    /// The whole point of the .js sidecar. A rule's source has to be JavaScript an editor and a
    /// linter can read, not an escaped string — and the way to know is to parse it.
    ///
    /// Wrapped in a function expression because a mechanic's source is a function BODY: it ends in
    /// a top-level `return`, which is a syntax error in a bare script. Executing the wrapper only
    /// creates the function; nothing in the rule runs.
    /// </summary>
    [Fact]
    public async Task Every_exported_source_file_is_parseable_javascript()
    {
        await using var db = await SeededAsync();
        await new CatalogExporter(db).ExportAsync(_root);

        var sources = Directory.EnumerateFiles(
            Path.Combine(_root, CatalogLayout.MechanicsRoot),
            "*" + CatalogLayout.SourceExtension,
            SearchOption.AllDirectories).ToList();

        Assert.NotEmpty(sources);

        foreach (var path in sources)
        {
            var source = await File.ReadAllTextAsync(path);
            var engine = new Engine();

            var exception = Record.Exception(() => engine.Execute("(function (ctx) {\n" + source + "\n})"));

            Assert.True(exception is null, $"{Path.GetFileName(path)} is not parseable JavaScript: {exception?.Message}");
        }
    }

    /// <summary>
    /// Exporting twice writes byte-identical files.
    ///
    /// Slice 2's round-trip gate needs this: if export were not deterministic, every import would
    /// see drift that nobody caused, and a git diff of the catalog would be noise.
    /// </summary>
    [Fact]
    public async Task Exporting_twice_produces_identical_files()
    {
        await using var db = await SeededAsync();
        var exporter = new CatalogExporter(db);

        await exporter.ExportAsync(_root);
        var first = await SnapshotAsync();

        await exporter.ExportAsync(_root);
        var second = await SnapshotAsync();

        Assert.Equal(first.Keys.OrderBy(k => k, StringComparer.Ordinal), second.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var (path, content) in first)
        {
            // The manifest carries an export timestamp, so it is expected to differ. Nothing else is.
            if (path == CatalogLayout.ManifestFileName)
            {
                continue;
            }

            Assert.Equal(content, second[path]);
        }
    }

    // ---- what did NOT happen -------------------------------------------------------------

    /// <summary>
    /// Export writes files and nothing else. A capture that mutates what it captures is one people
    /// hesitate to run, and hesitating is how the database and the catalog drift apart.
    /// </summary>
    [Fact]
    public async Task Exporting_writes_nothing_to_the_database()
    {
        await using var db = await SeededAsync();

        var before = (
            Mechanics: db.Mechanics.Count(),
            MechanicVersions: db.MechanicVersions.Count(),
            Contracts: db.ProcedureContracts.Count(),
            ContractVersions: db.ProcedureContractVersions.Count(),
            Operations: db.Operations.Count());

        await new CatalogExporter(db).ExportAsync(_root);

        var after = (
            Mechanics: db.Mechanics.Count(),
            MechanicVersions: db.MechanicVersions.Count(),
            Contracts: db.ProcedureContracts.Count(),
            ContractVersions: db.ProcedureContractVersions.Count(),
            Operations: db.Operations.Count());

        Assert.Equal(before, after);
    }

    /// <summary>
    /// A file the export did not write is reported and left alone. Deleting a developer's file as
    /// a side effect of a capture is the one behaviour that would stop people running it.
    /// </summary>
    [Fact]
    public async Task Files_the_export_did_not_write_are_reported_and_left_alone()
    {
        await using var db = await SeededAsync();
        await new CatalogExporter(db).ExportAsync(_root);

        var stray = Path.Combine(_root, CatalogLayout.MechanicsRoot, "check", "mechanic.removed.md");
        await File.WriteAllTextAsync(stray, "a rule that is no longer in the database");

        var result = await new CatalogExporter(db).ExportAsync(_root);

        Assert.Contains("mechanics/check/mechanic.removed.md", result.Orphans);
        Assert.True(File.Exists(stray), "the orphan was deleted; export must never delete.");
    }

    /// <summary>
    /// A database whose fingerprints are stale cannot produce a trustworthy manifest, so export
    /// stops rather than writing one import would misread.
    /// </summary>
    [Fact]
    public async Task Exporting_refuses_when_a_stored_fingerprint_is_stale()
    {
        await using var db = await SeededAsync();

        var row = db.MechanicVersions.First();
        row.SourceHash = "STALE-VALUE-FROM-AN-OLDER-FORMULA";
        await db.SaveChangesAsync();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CatalogExporter(db).ExportAsync(_root));

        Assert.Contains("backfill-hashes", failure.Message, StringComparison.Ordinal);
    }

    // ---- the format's own guards ---------------------------------------------------------

    /// <summary>
    /// A section body containing a '## ' line would be read back as the start of a new section,
    /// silently truncating one field and inventing another. It is refused on the way out, where
    /// the author can still see what happened.
    /// </summary>
    [Fact]
    public void A_section_that_would_not_parse_back_is_refused_on_write()
    {
        var file = new MechanicFile(
            "mechanic.test.heading",
            "test",
            "Has a heading in its description",
            "Fine so far.\n## Matches\nsmuggled",
            "matches",
            "{}",
            "return {};",
            "",
            MechanicStatus.Active);

        var failure = Assert.Throws<InvalidOperationException>(() => file.ToMarkdown());
        Assert.Contains("## ", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Front matter is one line per field. A name containing a line break would lose everything
    /// after it on the way back in — a loss that happens during import, far from the cause.
    /// </summary>
    [Fact]
    public void A_front_matter_value_that_would_not_parse_back_is_refused_on_write()
    {
        var file = new MechanicFile(
            "mechanic.test.multiline",
            "test",
            "A name\nsplit over two lines",
            "description",
            "matches",
            "{}",
            "return {};",
            "",
            MechanicStatus.Active);

        Assert.Throws<InvalidOperationException>(() => file.ToMarkdown());
    }

    /// <summary>
    /// Both a '## Source' section and a sibling .js is an error, not a precedence decision. Two
    /// places holding one rule's source is the failure this whole feature exists to prevent.
    /// </summary>
    [Fact]
    public void A_rule_with_its_source_in_two_places_is_refused()
    {
        const string markdown = """
            ---
            id: mechanic.test.two-sources
            category: test
            name: Two sources
            ---

            ## Source
            ```js
            return { narration: 'from the section', effects: [] };
            ```
            """;

        var failure = Assert.Throws<InvalidOperationException>(
            () => MechanicFile.Parse(markdown, "two-sources.md", "return { narration: 'from the file' };"));

        Assert.Contains("both", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers -------------------------------------------------------------------------

    private Task<string> ReadAsync(string relativePath) =>
        File.ReadAllTextAsync(CatalogLayout.ToFileSystemPath(_root, relativePath));

    private async Task<Dictionary<string, string>> SnapshotAsync()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/');
            files[relative] = await File.ReadAllTextAsync(path);
        }

        return files;
    }
}
