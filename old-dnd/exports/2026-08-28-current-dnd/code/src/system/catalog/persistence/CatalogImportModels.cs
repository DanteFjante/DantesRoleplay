namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// What happened to one record between the catalog and the database since they last agreed.
///
/// Three fingerprints answer this, not two: the file's, the database row's, and the manifest's
/// record of the last state at which they matched. With only the first two, a difference is
/// visible but unattributable — you can see that something changed and not which side changed it,
/// which is the same as knowing nothing except that it looks like knowing something.
/// </summary>
public enum CatalogChange
{
    /// <summary>File and database agree. Nothing to do.</summary>
    Unchanged,

    /// <summary>The file moved and the database did not — a developer edited the catalog. Import writes it.</summary>
    FileEdited,

    /// <summary>
    /// The database moved and the file did not — something authored this live over MCP. Import
    /// leaves it alone and says so. An LLM cannot re-create lost work from a checkout; a developer
    /// can, so the database is the side that gets the benefit of the doubt.
    /// </summary>
    DatabaseEdited,

    /// <summary>Both moved. Import refuses rather than choosing.</summary>
    Conflict,

    /// <summary>In the catalog, not in the database. Import creates it.</summary>
    NewInFiles,

    /// <summary>In the database, never exported. Import leaves it and suggests exporting.</summary>
    NewInDatabase,

    /// <summary>Was exported, now absent from the catalog. Reported only — import never deletes.</summary>
    MissingFromFiles,

    /// <summary>In the manifest and in neither side any more. Reported so the manifest entry is explicable.</summary>
    GoneFromBoth
}

/// <summary>Which side wins when a record moved on both. Never a default, never read from a config file.</summary>
public enum CatalogForce
{
    /// <summary>Refuse and report. The only safe answer when nobody has said which side is right.</summary>
    None,

    /// <summary>The catalog wins. Conflicting database revisions are superseded by a new version.</summary>
    Files,

    /// <summary>The database wins. Conflicting files are skipped and left on disk for an export to fix.</summary>
    Database
}

/// <param name="DryRun">Report the plan and write nothing — no rows, no manifest.</param>
public sealed record CatalogImportOptions(bool DryRun = false, CatalogForce Force = CatalogForce.None);

/// <param name="Detail">One sentence a person can act on, not a restatement of the enum.</param>
public sealed record CatalogImportPlanEntry(
    CatalogRecordKind Kind,
    string Id,
    CatalogChange Change,
    string Detail);

public sealed record CatalogImportPlan(string Root, bool HasManifest, IReadOnlyList<CatalogImportPlanEntry> Entries)
{
    public IEnumerable<CatalogImportPlanEntry> Conflicts =>
        Entries.Where(e => e.Change == CatalogChange.Conflict);

    public IEnumerable<CatalogImportPlanEntry> Writes =>
        Entries.Where(e => e.Change is CatalogChange.NewInFiles or CatalogChange.FileEdited);

    public IEnumerable<CatalogImportPlanEntry> NeedingExport => Entries.Where(e =>
        e.Change is CatalogChange.DatabaseEdited or CatalogChange.NewInDatabase);

    public bool IsClean => Entries.All(e => e.Change == CatalogChange.Unchanged);

    public int Count(CatalogChange change) => Entries.Count(e => e.Change == change);
}

/// <param name="Aborted">True when conflicts stopped the import before anything was written.</param>
public sealed record CatalogImportResult(
    CatalogImportPlan Plan,
    int Created,
    int Updated,
    int Skipped,
    bool Aborted,
    bool ManifestUpdated)
{
    public int Applied => Created + Updated;
}
