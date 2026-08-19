using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;

namespace DantesRoleplay.Tests;

/// <summary>
/// Slices 3 and 4: world state crosses the catalog, and history does not come back.
///
/// World state is held back from the ruleset slices on purpose. Rules are authored text that a
/// person reads in a diff; entities and components are machine-written state. They travel by
/// different rules — the catalog preserves rule source byte for byte and canonicalises component
/// data — and mixing the two decisions into one slice would have hidden that.
/// </summary>
public sealed class CatalogWorldTests : IDisposable
{
    private readonly SqliteFixture _source = new();
    private readonly SqliteFixture _destination = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"world-{Guid.NewGuid():n}");

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
    /// Entities, their components, their container and the relationships between them all survive
    /// a trip through files into a database that has never seen any of it.
    /// </summary>
    [Fact]
    public async Task A_world_round_trips_through_an_empty_database()
    {
        await using var source = await PopulatedAsync(_source);
        await new CatalogExporter(source).ExportAsync(_root);

        await using var destination = _destination.CreateContext();
        var result = await Importer(destination).ApplyAsync(_root, new CatalogImportOptions());

        Assert.False(result.Aborted);
        Assert.Empty(result.Plan.Conflicts);

        var world = new WorldStore(destination);

        var orban = await world.GetEntityAsync("orban");
        Assert.NotNull(orban);
        Assert.Equal("Orban", orban.Name);
        Assert.Equal("lantern-room", orban.ContainerId);
        Assert.Equal("standing", orban.ContainerSlot);
        Assert.Equal("""{"vigour":10,"resolve":4}""", orban.Components.Single(c => c.DefinitionId == "stats").Data);

        var carried = Assert.Single(await world.GetRelationshipsAsync("orban"));
        Assert.Equal("carries", carried.Kind);
        Assert.Equal("lantern", carried.ToEntityId);
        Assert.Equal("""{"hand":"left"}""", carried.Data);

        // And the second export matches the first, record for record.
        var plan = await Importer(destination).PlanAsync(_root);
        Assert.True(plan.IsClean, string.Join(", ", plan.Entries.Where(e => e.Change != CatalogChange.Unchanged).Select(e => $"{e.Id}: {e.Change}")));
    }

    [Fact]
    public async Task Rules_only_skips_world_state_entirely()
    {
        await using var db = await PopulatedAsync(_source);

        var result = await new CatalogExporter(db).ExportAsync(
            _root,
            new CatalogExportOptions(RulesOnly: true));

        Assert.Equal(0, result.Entities);
        Assert.False(Directory.Exists(Path.Combine(_root, CatalogLayout.WorldRoot)));

        var manifest = CatalogManifest.FromJson(
            await File.ReadAllTextAsync(Path.Combine(_root, CatalogLayout.ManifestFileName)),
            CatalogLayout.ManifestFileName);

        Assert.False(manifest.IncludesWorld);
        Assert.DoesNotContain(manifest.Records, r => r.Kind == CatalogRecordKind.Entity);

        // And a rules-only catalog does not then nag about every entity in the database.
        var plan = await Importer(db).PlanAsync(_root);
        Assert.DoesNotContain(plan.Entries, e => e.Kind == CatalogRecordKind.Entity);
    }

    /// <summary>
    /// Attaching a component whose definition does not exist is a hard failure in the store — an
    /// undeclared component type is almost always a typo, and a silently created one is invisible
    /// forever after. Bulk import must not become the way around that.
    /// </summary>
    [Fact]
    public async Task A_component_whose_definition_does_not_exist_is_rejected()
    {
        await using var source = await PopulatedAsync(_source);
        await new CatalogExporter(source).ExportAsync(_root);

        // Remove the definition from the catalog but leave the entity that carries it.
        File.Delete(Path.Combine(_root, CatalogLayout.ComponentsRoot, "stats.json"));

        await using var destination = _destination.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Importer(destination).ApplyAsync(_root, new CatalogImportOptions()));
    }

    // ---- drift on world records -------------------------------------------------------------

    [Fact]
    public async Task An_entity_edited_in_the_catalog_is_written()
    {
        await using var db = await PopulatedAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        await RewriteEntityAsync("orban", f => f with
        {
            Components = [new EntityComponent("stats", """{"vigour":18,"resolve":4}""")]
        });

        var result = await Importer(db).ApplyAsync(_root, new CatalogImportOptions());

        Assert.Equal(CatalogChange.FileEdited, ChangeFor(result.Plan, "orban"));

        var orban = await new WorldStore(db).GetEntityAsync("orban");
        Assert.Equal("""{"vigour":18,"resolve":4}""", orban!.Components.Single(c => c.DefinitionId == "stats").Data);
    }

    [Fact]
    public async Task An_entity_changed_live_is_left_alone()
    {
        await using var db = await PopulatedAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        await new WorldStore(db).SetComponentAsync("orban", "stats", """{"vigour":3,"resolve":3}""");

        var result = await Importer(db).ApplyAsync(_root, new CatalogImportOptions());

        Assert.Equal(CatalogChange.DatabaseEdited, ChangeFor(result.Plan, "orban"));

        var orban = await new WorldStore(db).GetEntityAsync("orban");
        Assert.Equal("""{"vigour":3,"resolve":3}""", orban!.Components.Single(c => c.DefinitionId == "stats").Data);
    }

    /// <summary>
    /// Component data is canonicalised on both sides, so the same data written with different
    /// spacing is recognised as the same data. Without this, every entity in the catalog would
    /// report as edited on every import, forever.
    /// </summary>
    [Fact]
    public async Task Reformatting_component_data_is_not_a_change()
    {
        await using var db = await PopulatedAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        var path = Path.Combine(_root, CatalogLayout.EntitiesRoot.Replace('/', Path.DirectorySeparatorChar), "orban.json");
        var reformatted = (await File.ReadAllTextAsync(path)).Replace("  ", "        ", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, reformatted);

        var plan = await Importer(db).PlanAsync(_root);

        Assert.Equal(CatalogChange.Unchanged, ChangeFor(plan, "orban"));
    }

    [Fact]
    public async Task A_relationship_removed_from_the_file_is_cut()
    {
        await using var db = await PopulatedAsync(_source);
        await new CatalogExporter(db).ExportAsync(_root);

        await File.WriteAllTextAsync(
            Path.Combine(_root, CatalogLayout.RelationshipsFileName.Replace('/', Path.DirectorySeparatorChar)),
            new RelationshipsFile([]).ToJson());

        var result = await Importer(db).ApplyAsync(_root, new CatalogImportOptions());

        Assert.False(result.Aborted);
        Assert.Empty(await new WorldStore(db).GetRelationshipsAsync("orban"));
    }

    /// <summary>
    /// A catalog states what the world IS. Re-importing a tombstone would resurrect a row somebody
    /// deleted on purpose, so a soft-deleted entity is simply not exported.
    /// </summary>
    [Fact]
    public async Task A_deleted_entity_is_not_exported()
    {
        await using var db = await PopulatedAsync(_source);
        await new WorldStore(db).DeleteEntityAsync("lantern");

        var result = await new CatalogExporter(db).ExportAsync(_root);

        Assert.False(File.Exists(Path.Combine(
            _root,
            CatalogLayout.EntitiesRoot.Replace('/', Path.DirectorySeparatorChar),
            "lantern.json")));

        Assert.Equal(2, result.Entities);
    }

    /// <summary>
    /// An entity id is the one identifier in this system the kernel does not validate — it is
    /// whatever was passed to CreateEntityAsync. Turning that into a path without checking would
    /// let a database row write outside the export root.
    /// </summary>
    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/id")]
    [InlineData("con")]
    [InlineData("trailing.")]
    public void An_entity_id_that_cannot_be_a_filename_is_refused(string id)
    {
        Assert.Throws<InvalidOperationException>(() => CatalogLayout.Entity(id));
    }

    // ---- history -----------------------------------------------------------------------------

    [Fact]
    public async Task History_is_written_only_when_asked_for()
    {
        await using var db = await PopulatedAsync(_source);
        await new OperationLog(db).RecordAsync("query", "looked at the world", success: true, subject: "orban");

        var without = await new CatalogExporter(db).ExportAsync(_root);
        Assert.Equal(0, without.Operations);
        Assert.False(File.Exists(HistoryPath()));

        var with = await new CatalogExporter(db).ExportAsync(
            _root,
            new CatalogExportOptions(WithHistory: true));

        Assert.Equal(1, with.Operations);

        var lines = (await File.ReadAllTextAsync(HistoryPath()))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(lines);
        Assert.Contains("\"subject\":\"orban\"", lines[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// The guarantee is not that import declines to read history — it is that there is no code path
    /// anywhere that writes an operation from a file. An operation id and a seed are the claim that
    /// a rule ran at a version and produced a roll; a log writable from a file is not evidence.
    /// </summary>
    [Fact]
    public async Task Importing_a_catalog_that_contains_history_writes_no_operations()
    {
        await using var source = await PopulatedAsync(_source);
        await new OperationLog(source).RecordAsync("query", "looked at the world", success: true, subject: "orban");
        await new CatalogExporter(source).ExportAsync(_root, new CatalogExportOptions(WithHistory: true));

        Assert.True(File.Exists(HistoryPath()));

        await using var destination = _destination.CreateContext();
        var result = await Importer(destination).ApplyAsync(_root, new CatalogImportOptions());

        Assert.False(result.Aborted);
        Assert.True(result.Created > 0);
        Assert.Empty(destination.Operations);
    }

    // ---- helpers -----------------------------------------------------------------------------

    private string HistoryPath() =>
        Path.Combine(_root, CatalogLayout.OperationsFileName.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A world small enough to assert on and complete enough to exercise every shape.</summary>
    private static async Task<DantesRoleplayDbContext> PopulatedAsync(SqliteFixture fixture)
    {
        var db = fixture.CreateContext();
        var world = new WorldStore(db);

        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");

        await world.CreateEntityAsync("Lantern Room", "lantern-room");
        await world.CreateEntityAsync("Orban", "orban");
        await world.CreateEntityAsync("Lantern", "lantern");

        await world.SetComponentAsync("orban", "stats", """{"vigour":10,"resolve":4}""");
        await world.MoveAsync("orban", "lantern-room", "standing");
        await world.RelateAsync("orban", "lantern", "carries", """{"hand":"left"}""");

        return db;
    }

    private static CatalogImporter Importer(DantesRoleplayDbContext db) =>
        new(db, new MechanicStore(db), new ProcedureStore(db), new WorldStore(db));

    private static CatalogChange ChangeFor(CatalogImportPlan plan, string id) =>
        plan.Entries.Single(e => e.Id == id).Change;

    private async Task RewriteEntityAsync(string id, Func<EntityFile, EntityFile> edit)
    {
        var path = Path.Combine(
            _root,
            CatalogLayout.EntitiesRoot.Replace('/', Path.DirectorySeparatorChar),
            id + ".json");

        var edited = edit(EntityFile.Parse(await File.ReadAllTextAsync(path), path));
        await File.WriteAllTextAsync(path, edited.ToJson());
    }
}
