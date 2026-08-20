using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.Tests;

/// <summary>
/// Slice 2: the catalog comes back in, and neither side quietly destroys the other's work.
///
/// There is one test per row of the drift table, because the table IS the feature. Everything else
/// in this slice is plumbing around the question "which side moved?", and the failure mode when
/// that question is answered wrongly is silent: an import that overwrites live work looks exactly
/// like an import that had nothing to do.
/// </summary>
public sealed class CatalogImportTests : IDisposable
{
    private readonly SqliteFixture _source = new();
    private readonly SqliteFixture _destination = new();

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"catalog-{Guid.NewGuid():n}");

    public void Dispose()
    {
        _source.Dispose();
        _destination.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ---- the gate ------------------------------------------------------------------------

    /// <summary>
    /// Export, import into a database that has never seen any of it, export again — and get the
    /// same files and the same fingerprints.
    ///
    /// This is the exit gate. Everything else here tests a decision; this tests that nothing is
    /// lost in the crossing.
    /// </summary>
    [Fact]
    public async Task A_catalog_round_trips_through_an_empty_database()
    {
        await using var source = await SeededAsync(_source);
        await new CatalogExporter(source).ExportAsync(_root);
        var before = await SnapshotAsync();

        await using var destination = _destination.CreateContext();

        var result = await Importer(destination).ApplyAsync(_root, new CatalogImportOptions());

        Assert.False(result.Aborted);
        Assert.Empty(result.Plan.Conflicts);
        Assert.True(result.Created > 0);
        Assert.Equal(0, result.Updated);

        // Same fingerprints on the far side, record for record.
        var reexported = Path.Combine(Path.GetTempPath(), $"catalog-again-{Guid.NewGuid():n}");

        try
        {
            await new CatalogExporter(destination).ExportAsync(reexported);
            var after = await SnapshotAsync(reexported);

            Assert.Equal(
                before.Keys.OrderBy(k => k, StringComparer.Ordinal),
                after.Keys.OrderBy(k => k, StringComparer.Ordinal));

            foreach (var (path, content) in before)
            {
                // The manifest carries an export timestamp and a source database name, so it is
                // expected to differ. Every record file is expected not to.
                if (path == CatalogLayout.ManifestFileName)
                {
                    continue;
                }

                Assert.Equal(content, after[path]);
            }
        }
        finally
        {
            if (Directory.Exists(reexported))
            {
                Directory.Delete(reexported, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_catalog_round_trip_preserves_authored_provenance_for_every_versioned_record()
    {
        await using var source = _source.CreateContext();
        var mechanics = new MechanicStore(source);
        var procedures = new ProcedureStore(source);
        var eventTypes = new EventTypeStore(source);
        var subscriptions = new SubscriptionStore(source);

        await mechanics.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.catalog.provenance",
            Category = "test",
            Name = "Catalog provenance",
            Description = "A rule used only to verify its author survives.",
            Matches = "catalog provenance",
            Requirements = """{"event":{"mode":"guard","types":["test.catalog.changed"]}}""",
            Source = "return { decision: 'allow', narration: 'ok', effects: [] };",
            Status = MechanicStatus.Active,
            CreatedBy = "mechanic author",
            ChangeNote = "Mechanic note\nwith context."
        });
        await procedures.WriteAsync(new WriteProcedureRequest
        {
            Id = "procedure.catalog.provenance",
            Category = "test",
            Name = "Catalog provenance procedure",
            Description = "A contract used only to verify its author survives.",
            Governs = "catalog import",
            Instructions = "Preserve provenance.",
            Status = ProcedureStatus.Active,
            CreatedBy = "procedure author",
            ChangeNote = "Procedure note."
        });
        await eventTypes.WriteAsync(new WriteEventTypeRequest
        {
            Id = "test.catalog.changed",
            Category = "test",
            Name = "Catalog changed",
            Description = "An event used only to verify its author survives.",
            PayloadSchema = """{"type":"object"}""",
            Status = EventTypeStatus.Active,
            CreatedBy = "event author",
            ChangeNote = "Event note."
        });
        await subscriptions.WriteAsync(new WriteSubscriptionRequest
        {
            Id = "subscription.catalog.provenance",
            Category = "test",
            EventTypeId = "test.catalog.changed",
            EventMechanicId = "mechanic.catalog.provenance",
            Mode = SubscriptionMode.Guard,
            Status = SubscriptionStatus.Active,
            CreatedBy = "subscription author",
            ChangeNote = "Subscription note."
        });

        await new CatalogExporter(source).ExportAsync(_root);

        await using var destination = _destination.CreateContext();
        var imported = await new CatalogImporter(
            destination,
            new MechanicStore(destination),
            new ProcedureStore(destination),
            new WorldStore(destination),
            new EventTypeStore(destination),
            new SubscriptionStore(destination)).ApplyAsync(_root, new CatalogImportOptions());

        Assert.False(imported.Aborted);
        var importedMechanic = await new MechanicStore(destination).GetAsync("mechanic.catalog.provenance");
        var importedProcedure = await new ProcedureStore(destination).GetAsync("procedure.catalog.provenance");
        var importedEvent = await new EventTypeStore(destination).GetAsync("test.catalog.changed");
        var importedSubscription = await new SubscriptionStore(destination).GetAsync("subscription.catalog.provenance");

        Assert.Equal("mechanic author", importedMechanic!.CreatedBy);
        Assert.Equal("Mechanic note\nwith context.", importedMechanic.ChangeNote);
        Assert.Equal("procedure author", importedProcedure!.CreatedBy);
        Assert.Equal("Procedure note.", importedProcedure.ChangeNote);
        Assert.Equal("event author", importedEvent!.CreatedBy);
        Assert.Equal("Event note.", importedEvent.ChangeNote);
        Assert.Equal("subscription author", importedSubscription!.CreatedBy);
        Assert.Equal("Subscription note.", importedSubscription.ChangeNote);
    }

    [Fact]
    public void Provenance_is_outside_every_catalog_content_fingerprint()
    {
        var mechanic = new MechanicFile("mechanic.hash.test", "test", "Test", "Description", "match", "{}", "return {};", "", MechanicStatus.Active);
        var procedure = new ProcedureFile("procedure.hash.test", "test", "Test", "Description", "governs", "Instructions", "", ProcedureStatus.Active);
        var eventType = new EventTypeFile("test.hash.changed", "test", "Test", "Description", "", EventTypeStatus.Active, "{}");
        var subscription = new SubscriptionFile("subscription.hash.test", "test", "test.hash.changed", "mechanic.hash.test", SubscriptionMode.Guard, 0, "{}", "[]", "{}", 1, "", SubscriptionStatus.Active);

        Assert.Equal(mechanic.ContentHash, (mechanic with { CreatedBy = "someone", ChangeNote = "A note." }).ContentHash);
        Assert.Equal(procedure.ContentHash, (procedure with { CreatedBy = "someone", ChangeNote = "A note." }).ContentHash);
        Assert.Equal(eventType.ContentHash, (eventType with { CreatedBy = "someone", ChangeNote = "A note." }).ContentHash);
        Assert.Equal(subscription.ContentHash, (subscription with { CreatedBy = "someone", ChangeNote = "A note." }).ContentHash);
    }

    // ---- the drift table, one row at a time -----------------------------------------------

    [Fact]
    public async Task Unchanged__a_catalog_that_matches_imports_nothing()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        var result = await Importer(db).ApplyAsync(_root, new CatalogImportOptions());

        Assert.Equal(0, result.Applied);
        Assert.True(result.Plan.IsClean);
    }

    [Fact]
    public async Task File_edited__the_edit_is_written_as_a_new_version()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        await RewriteAsync(Threshold, f => f with { Description = "Edited in the catalog." });

        var before = (await new MechanicStore(db).GetAsync(Threshold))!.Version;
        var result = await Importer(db).ApplyAsync(_root, new CatalogImportOptions());

        Assert.Equal(CatalogChange.FileEdited, ChangeFor(result.Plan, Threshold));
        Assert.Equal(1, result.Updated);

        var after = await new MechanicStore(db).GetAsync(Threshold);
        Assert.Equal(before + 1, after!.Version);
        Assert.Equal("Edited in the catalog.", after.Description);
        Assert.Equal("seed", after.CreatedBy);
    }

    /// <summary>
    /// The decision this whole feature turns on. An agent connected only over MCP cannot re-create
    /// lost work from a checkout; a developer can. So the database keeps what it authored, and the
    /// catalog is told to catch up.
    /// </summary>
    [Fact]
    public async Task Database_edited__live_work_is_left_alone_and_reported()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        await AuthorLiveAsync(db, Threshold, "Authored live over MCP.");

        var result = await Importer(db).ApplyAsync(_root, new CatalogImportOptions());

        Assert.Equal(CatalogChange.DatabaseEdited, ChangeFor(result.Plan, Threshold));
        Assert.Equal(0, result.Applied);
        Assert.Equal(
            "Authored live over MCP.",
            (await new MechanicStore(db).GetAsync(Threshold))!.Description);
    }

    [Fact]
    public async Task Conflict__both_sides_moved_so_nothing_at_all_is_written()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        await RewriteAsync(Threshold, f => f with { Description = "Edited in the catalog." });
        await AuthorLiveAsync(db, Threshold, "Authored live over MCP.");

        // A second, entirely uncontested file edit — it must NOT be applied either. A partly
        // synchronised catalog is harder to reason about than an unapplied one.
        await RewriteAsync(Adjust, f => f with { Description = "Also edited, but uncontested." });

        var versions = db.MechanicVersions.Count();
        var result = await Importer(db).ApplyAsync(_root, new CatalogImportOptions());

        Assert.True(result.Aborted);
        Assert.Equal(0, result.Applied);
        Assert.Equal(CatalogChange.Conflict, ChangeFor(result.Plan, Threshold));
        Assert.Equal(CatalogChange.FileEdited, ChangeFor(result.Plan, Adjust));
        Assert.Equal(versions, db.MechanicVersions.Count());
    }

    [Fact]
    public async Task Conflict__forcing_the_files_applies_the_catalog()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        await RewriteAsync(Threshold, f => f with { Description = "Edited in the catalog." });
        await AuthorLiveAsync(db, Threshold, "Authored live over MCP.");

        var result = await Importer(db).ApplyAsync(
            _root,
            new CatalogImportOptions(Force: CatalogForce.Files));

        Assert.False(result.Aborted);
        Assert.Equal(
            "Edited in the catalog.",
            (await new MechanicStore(db).GetAsync(Threshold))!.Description);
    }

    [Fact]
    public async Task Conflict__forcing_the_database_skips_the_file()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        await RewriteAsync(Threshold, f => f with { Description = "Edited in the catalog." });
        await AuthorLiveAsync(db, Threshold, "Authored live over MCP.");

        var result = await Importer(db).ApplyAsync(
            _root,
            new CatalogImportOptions(Force: CatalogForce.Database));

        Assert.False(result.Aborted);
        Assert.Equal(0, result.Applied);
        Assert.Equal(
            "Authored live over MCP.",
            (await new MechanicStore(db).GetAsync(Threshold))!.Description);
    }

    [Fact]
    public async Task New_in_files__a_rule_added_to_the_catalog_is_created()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        var added = new MechanicFile(
            "mechanic.test.added",
            "check",
            "Added in the catalog",
            "Written as a file, never seen by the database.",
            "added",
            "{}",
            "return { narration: 'added', effects: [] };",
            string.Empty,
            MechanicStatus.Active);

        await WriteAsync(CatalogLayout.MechanicMarkdown(added.Category, added.Id), added.ToMarkdown());
        await WriteAsync(CatalogLayout.MechanicSource(added.Category, added.Id), added.Source + "\n");

        var result = await Importer(db).ApplyAsync(_root, new CatalogImportOptions());

        Assert.Equal(CatalogChange.NewInFiles, ChangeFor(result.Plan, added.Id));
        Assert.Equal(1, result.Created);

        var stored = await new MechanicStore(db).GetAsync(added.Id);
        Assert.NotNull(stored);
        Assert.Equal(added.ContentHash, stored.SourceHash);
    }

    [Fact]
    public async Task New_in_database__a_rule_authored_after_the_export_is_left_and_reported()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        await AuthorLiveAsync(db, "mechanic.test.after-export", "Written after the export.");

        var result = await Importer(db).ApplyAsync(_root, new CatalogImportOptions());

        Assert.Equal(CatalogChange.NewInDatabase, ChangeFor(result.Plan, "mechanic.test.after-export"));
        Assert.Equal(0, result.Applied);
        Assert.NotNull(await new MechanicStore(db).GetAsync("mechanic.test.after-export"));
    }

    /// <summary>
    /// Import never deletes. Something else may compose the rule, and removing it is not a decision
    /// to make as a side effect of a sync.
    /// </summary>
    [Fact]
    public async Task Missing_from_files__the_record_is_reported_and_survives()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        File.Delete(Path.Combine(_root, "mechanics", "check", Threshold + ".md"));
        File.Delete(Path.Combine(_root, "mechanics", "check", Threshold + ".js"));

        var result = await Importer(db).ApplyAsync(_root, new CatalogImportOptions());

        Assert.Equal(CatalogChange.MissingFromFiles, ChangeFor(result.Plan, Threshold));
        Assert.False(result.Aborted);
        Assert.NotNull(await new MechanicStore(db).GetAsync(Threshold));
    }

    // ---- the guards ------------------------------------------------------------------------

    [Fact]
    public async Task A_dry_run_writes_nothing_at_all()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);
        await RewriteAsync(Threshold, f => f with { Description = "Edited in the catalog." });

        var versions = db.MechanicVersions.Count();
        var manifest = await ReadAsync(CatalogLayout.ManifestFileName);

        var result = await Importer(db).ApplyAsync(_root, new CatalogImportOptions(DryRun: true));

        Assert.Equal(1, result.Updated);
        Assert.False(result.ManifestUpdated);
        Assert.Equal(versions, db.MechanicVersions.Count());
        Assert.Equal(manifest, await ReadAsync(CatalogLayout.ManifestFileName));
    }

    /// <summary>
    /// With no manifest there is no common ancestor, so a difference is visible and
    /// unattributable. Reporting it as a conflict is the honest answer; guessing is not.
    /// </summary>
    [Fact]
    public async Task Without_a_manifest_every_difference_is_a_conflict()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        File.Delete(Path.Combine(_root, CatalogLayout.ManifestFileName));
        await RewriteAsync(Threshold, f => f with { Description = "Edited in the catalog." });

        var result = await Importer(db).ApplyAsync(_root, new CatalogImportOptions());

        Assert.False(result.Plan.HasManifest);
        Assert.True(result.Aborted);
        Assert.Equal(CatalogChange.Conflict, ChangeFor(result.Plan, Threshold));

        // Records that still match are not conflicts — only the ones that actually differ.
        Assert.Equal(CatalogChange.Unchanged, ChangeFor(result.Plan, Adjust));
    }

    /// <summary>
    /// A skipped record keeps its OLD manifest entry. Recording the database's new fingerprint
    /// would make the next import read the untouched file as a catalog edit and overwrite the very
    /// live work this import just protected.
    /// </summary>
    [Fact]
    public async Task The_manifest_keeps_nagging_about_a_record_the_import_skipped()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);
        await AuthorLiveAsync(db, Threshold, "Authored live over MCP.");

        var first = await Importer(db).ApplyAsync(_root, new CatalogImportOptions());
        Assert.Equal(CatalogChange.DatabaseEdited, ChangeFor(first.Plan, Threshold));

        var second = await Importer(db).ApplyAsync(_root, new CatalogImportOptions());
        Assert.Equal(CatalogChange.DatabaseEdited, ChangeFor(second.Plan, Threshold));
        Assert.Equal(0, second.Applied);
    }

    [Fact]
    public async Task Two_catalog_files_claiming_one_id_are_refused()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        var original = Path.Combine(_root, "mechanics", "check", Threshold + ".md");
        var duplicate = Path.Combine(_root, "mechanics", "check", "a-copy.md");
        File.Copy(original, duplicate);
        File.Copy(Path.ChangeExtension(original, ".js"), Path.ChangeExtension(duplicate, ".js"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Importer(db).PlanAsync(_root));

        Assert.Contains(Threshold, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Importing_an_unchanged_catalog_appends_no_versions()
    {
        await using var db = await SeededAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        var mechanics = db.MechanicVersions.Count();
        var contracts = db.ProcedureContractVersions.Count();

        await Importer(db).ApplyAsync(_root, new CatalogImportOptions());
        await Importer(db).ApplyAsync(_root, new CatalogImportOptions());

        Assert.Equal(mechanics, db.MechanicVersions.Count());
        Assert.Equal(contracts, db.ProcedureContractVersions.Count());
    }

    // ---- helpers ---------------------------------------------------------------------------

    private const string Threshold = "mechanic.check.threshold";
    private const string Adjust = "mechanic.value.adjust";

    private static async Task<DantesRoleplayDbContext> SeededAsync(SqliteFixture fixture)
    {
        var db = fixture.CreateContext();

        await new ContentHashBackfill(db).RunAsync();
        await new ProcedureSeeder(new ProcedureStore(db)).SeedAsync();
        await new MechanicSeeder(new MechanicStore(db)).SeedAsync();

        return db;
    }

    private static CatalogImporter Importer(DantesRoleplayDbContext db) =>
        new(db, new MechanicStore(db), new ProcedureStore(db), new WorldStore(db));

    private static CatalogChange ChangeFor(CatalogImportPlan plan, string id) =>
        plan.Entries.Single(e => e.Id == id).Change;

    /// <summary>Edits a rule in the catalog the way a developer would: through the file.</summary>
    private async Task RewriteAsync(string id, Func<MechanicFile, MechanicFile> edit)
    {
        var markdownPath = Directory
            .EnumerateFiles(Path.Combine(_root, CatalogLayout.MechanicsRoot), id + ".md", SearchOption.AllDirectories)
            .Single();

        var sourcePath = Path.ChangeExtension(markdownPath, CatalogLayout.SourceExtension);

        var edited = edit(MechanicFile.Parse(
            await File.ReadAllTextAsync(markdownPath),
            markdownPath,
            await File.ReadAllTextAsync(sourcePath)));

        await File.WriteAllTextAsync(markdownPath, edited.ToMarkdown());
        await File.WriteAllTextAsync(sourcePath, edited.Source + "\n");
    }

    /// <summary>Authors a rule the way an agent over MCP would: straight into the store.</summary>
    private static async Task AuthorLiveAsync(DantesRoleplayDbContext db, string id, string description)
    {
        var store = new MechanicStore(db);
        var existing = await store.GetAsync(id);

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = id,
            Category = existing?.Category ?? "check",
            Name = existing?.Name ?? "Authored live",
            Description = description,
            Matches = existing?.Matches ?? "live",
            Requirements = existing?.Requirements ?? "{}",
            Source = existing?.Source ?? "return { narration: 'live', effects: [] };",
            Scope = existing?.Scope ?? string.Empty,
            Status = MechanicStatus.Active,
            CreatedBy = "llm",
            ChangeNote = "Written over MCP."
        });
    }

    private Task<string> ReadAsync(string relativePath) =>
        File.ReadAllTextAsync(CatalogLayout.ToFileSystemPath(_root, relativePath));

    private async Task WriteAsync(string relativePath, string content)
    {
        var path = CatalogLayout.ToFileSystemPath(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    private async Task<Dictionary<string, string>> SnapshotAsync(string? root = null)
    {
        var from = root ?? _root;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, path).Replace(Path.DirectorySeparatorChar, '/');
            files[relative] = await File.ReadAllTextAsync(path);
        }

        return files;
    }
}
