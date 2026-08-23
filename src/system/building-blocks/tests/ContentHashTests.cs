using DantesRoleplay.Content;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.Tests;

/// <summary>
/// The fingerprint is one function, and every layer gets the same answer from it.
///
/// This is the whole of Slice 0 of the catalog portability plan, and the reason it is a slice of
/// its own: export and import decide which side of a divergence is newer by comparing fingerprints.
/// If the file layer and the storage layer compute them differently — or if a row has none — that
/// comparison is not wrong in a way anyone can see. It reports conflicts that do not exist, or
/// misses ones that do, and every code path involved looks correct.
/// </summary>
public sealed class ContentHashTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    // ---- the gate: both layers agree ----------------------------------------------------

    /// <summary>
    /// The test the rest of this feature stands on.
    ///
    /// A rule authored as a file and the identical rule authored over MCP must fingerprint the
    /// same, because import has to be able to tell "the developer edited the file" from "an LLM
    /// rewrote the rule live", and it has nothing to compare but these two numbers.
    ///
    /// Before the store computed the fingerprint this could not pass at all: the MCP path wrote an
    /// empty string.
    /// </summary>
    [Fact]
    public async Task A_mechanic_written_through_the_store_fingerprints_the_same_as_the_file_it_came_from()
    {
        const string markdown = """
            ---
            id: mechanic.test.roundtrip
            category: test
            name: A round-tripping rule
            status: active
            ---

            ## Description
            Exists to be fingerprinted twice.

            ## Matches
            round trip

            ## Requirements
            ```json
            {}
            ```

            ## Source
            ```js
            return { narration: 'ok', effects: [] };
            ```
            """;

        var file = MechanicFile.Parse(markdown, "roundtrip.md");

        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        var written = await store.WriteAsync(new WriteMechanicRequest
        {
            Id = file.Id,
            Category = file.Category,
            Name = file.Name,
            Description = file.Description,
            Matches = file.Matches,
            Requirements = file.Requirements,
            Source = file.Source,
            Scope = file.Scope,
            Status = file.Status,
            CreatedBy = "test"
        });

        Assert.Equal(file.ContentHash, written.Mechanic.SourceHash);
    }

    /// <summary>The same guarantee on the contract side.</summary>
    [Fact]
    public async Task A_contract_written_through_the_store_fingerprints_the_same_as_the_file_it_came_from()
    {
        const string markdown = """
            ---
            id: procedure.test.roundtrip
            category: test
            name: A round-tripping contract
            governs: nothing in particular
            status: active
            ---

            ## Description
            Exists to be fingerprinted twice.

            ## Instructions
            1. Do the thing.

            ## Constraints
            - Never do the other thing.
            """;

        var file = ProcedureFile.Parse(markdown, "roundtrip.md");

        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        var written = await store.WriteAsync(new WriteProcedureRequest
        {
            Id = file.Id,
            Category = file.Category,
            Name = file.Name,
            Description = file.Description,
            Governs = file.Governs,
            Instructions = file.Instructions,
            Constraints = file.Constraints,
            Status = file.Status,
            CreatedBy = "test"
        });

        Assert.Equal(file.ContentHash, written.Procedure.SourceHash);
    }

    /// <summary>
    /// Nothing reaches storage without a fingerprint, whatever wrote it. The column used to be
    /// documented as "empty when written through MCP", which meant the entire live ruleset — the
    /// one population export exists to move — carried nothing at all.
    /// </summary>
    [Fact]
    public async Task Writing_over_the_mcp_path_still_produces_a_fingerprint()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        var written = await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.llm-authored",
            Category = "test",
            Name = "Written by an LLM",
            Description = "No file behind this one.",
            Matches = "llm",
            Source = "return { narration: 'ok', effects: [] };"
        });

        Assert.NotEqual(string.Empty, written.Mechanic.SourceHash);
        Assert.Equal(64, written.Mechanic.SourceHash.Length);
    }

    // ---- the properties the fingerprint has to have --------------------------------------

    /// <summary>
    /// The guard MechanicFile never had. Without a field separator ("ab", "c") and ("a", "bc")
    /// hash identically, so two genuinely different rules read to the seeder — and to import — as
    /// unchanged copies of each other. ProcedureFile had been fixed for this; the mechanic side
    /// had not, and nothing tested it.
    /// </summary>
    [Fact]
    public void Mechanic_field_boundaries_cannot_be_confused()
    {
        var left = ContentHash.ForMechanic("ab", "c", "d", "e", "{}", "s", "", MechanicStatus.Active);
        var right = ContentHash.ForMechanic("a", "bc", "d", "e", "{}", "s", "", MechanicStatus.Active);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Contract_field_boundaries_cannot_be_confused()
    {
        var left = ContentHash.ForProcedure("ab", "c", "d", "e", "f", "g", ProcedureStatus.Active);
        var right = ContentHash.ForProcedure("a", "bc", "d", "e", "f", "g", ProcedureStatus.Active);

        Assert.NotEqual(left, right);
    }

    /// <summary>
    /// The bootstrap parsers rebuild sections with StringBuilder.AppendLine, which emits
    /// Environment.NewLine. Without normalisation the same file seeded on Windows and on Linux
    /// fingerprinted differently — and a catalog exported on one and imported on the other would
    /// have reported every single record as drifted.
    /// </summary>
    [Fact]
    public void Line_endings_do_not_change_the_fingerprint()
    {
        Assert.Equal(
            ContentHash.Of("first\r\nsecond"),
            ContentHash.Of("first\nsecond"));

        Assert.Equal(
            ContentHash.Of("first\rsecond"),
            ContentHash.Of("first\nsecond"));
    }

    /// <summary>
    /// A parsed markdown section arrives trimmed; the same content arriving over MCP does not.
    /// Two channels for one rule must not mean two fingerprints for it.
    /// </summary>
    [Fact]
    public void Surrounding_whitespace_does_not_change_the_fingerprint()
    {
        Assert.Equal(
            ContentHash.Of("  content  \n"),
            ContentHash.Of("content"));
    }

    [Fact]
    public void A_null_field_and_an_empty_field_are_the_same_thing()
    {
        Assert.Equal(ContentHash.Of(null, "x"), ContentHash.Of("", "x"));
    }

    /// <summary>
    /// A field outside the hash cannot be edited at all: the fingerprint does not move, the seeder
    /// concludes nothing changed, and the edit is discarded forever. Varying one field at a time
    /// catches an omission however the hash is implemented.
    /// </summary>
    [Fact]
    public void Every_authored_mechanic_field_moves_the_fingerprint()
    {
        var baseline = ContentHash.ForMechanic(
            "check", "Name", "Description", "matches", "{}", "return {};", "", MechanicStatus.Active);

        var variants = new[]
        {
            ContentHash.ForMechanic("other", "Name", "Description", "matches", "{}", "return {};", "", MechanicStatus.Active),
            ContentHash.ForMechanic("check", "Other", "Description", "matches", "{}", "return {};", "", MechanicStatus.Active),
            ContentHash.ForMechanic("check", "Name", "Other", "matches", "{}", "return {};", "", MechanicStatus.Active),
            ContentHash.ForMechanic("check", "Name", "Description", "other", "{}", "return {};", "", MechanicStatus.Active),
            ContentHash.ForMechanic("check", "Name", "Description", "matches", """{"roles":{}}""", "return {};", "", MechanicStatus.Active),
            ContentHash.ForMechanic("check", "Name", "Description", "matches", "{}", "return { x: 1 };", "", MechanicStatus.Active),
            ContentHash.ForMechanic("check", "Name", "Description", "matches", "{}", "return {};", "campaign.one", MechanicStatus.Active),
            ContentHash.ForMechanic("check", "Name", "Description", "matches", "{}", "return {};", "", MechanicStatus.Draft)
        };

        Assert.All(variants, v => Assert.NotEqual(baseline, v));
        Assert.Equal(variants.Length, variants.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_authored_contract_field_moves_the_fingerprint()
    {
        var baseline = ContentHash.ForProcedure(
            "system", "Name", "Description", "governs", "1. Do it.", "- Never.", ProcedureStatus.Active);

        var variants = new[]
        {
            ContentHash.ForProcedure("other", "Name", "Description", "governs", "1. Do it.", "- Never.", ProcedureStatus.Active),
            ContentHash.ForProcedure("system", "Other", "Description", "governs", "1. Do it.", "- Never.", ProcedureStatus.Active),
            ContentHash.ForProcedure("system", "Name", "Other", "governs", "1. Do it.", "- Never.", ProcedureStatus.Active),
            ContentHash.ForProcedure("system", "Name", "Description", "other", "1. Do it.", "- Never.", ProcedureStatus.Active),
            ContentHash.ForProcedure("system", "Name", "Description", "governs", "1. Do something else.", "- Never.", ProcedureStatus.Active),
            ContentHash.ForProcedure("system", "Name", "Description", "governs", "1. Do it.", "- Always.", ProcedureStatus.Active),
            ContentHash.ForProcedure("system", "Name", "Description", "governs", "1. Do it.", "- Never.", ProcedureStatus.Deprecated)
        };

        Assert.All(variants, v => Assert.NotEqual(baseline, v));
        Assert.Equal(variants.Length, variants.Distinct(StringComparer.Ordinal).Count());
    }

    // ---- the backfill --------------------------------------------------------------------

    /// <summary>
    /// The exit gate, stated as an assertion: after startup there is no revision anywhere without
    /// a fingerprint. Runs the real initialisation order — backfill, then seed.
    /// </summary>
    [Fact]
    public async Task After_seeding_no_revision_is_left_without_a_fingerprint()
    {
        await using var db = _fixture.CreateContext();

        await new ContentHashBackfill(db).RunAsync();
        await new ProcedureSeeder(new ProcedureStore(db)).SeedAsync();
        await new MechanicSeeder(new MechanicStore(db)).SeedAsync();

        Assert.Empty(db.MechanicVersions.Where(v => v.SourceHash == string.Empty));
        Assert.Empty(db.ProcedureContractVersions.Where(v => v.SourceHash == string.Empty));
    }

    /// <summary>
    /// A row whose fingerprint is missing or was computed by an older formula gets corrected, and
    /// a second pass finds nothing left to do. Without idempotence this would rewrite every
    /// revision on every start.
    /// </summary>
    [Fact]
    public async Task The_backfill_corrects_stale_fingerprints_once_and_then_stays_quiet()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.test.stale",
            Category = "test",
            Name = "Stale",
            Description = "Its fingerprint is about to be vandalised.",
            Matches = "stale",
            Source = "return { narration: 'ok', effects: [] };",
            Status = MechanicStatus.Active
        });

        // Stand in for a row written before ContentHash existed: present, populated, meaningless.
        var row = db.MechanicVersions.Single(v => v.MechanicId == "mechanic.test.stale");
        var correct = row.SourceHash;
        row.SourceHash = "STALE-VALUE-FROM-AN-OLDER-FORMULA";
        await db.SaveChangesAsync();

        var first = await new ContentHashBackfill(db).RunAsync();
        Assert.Equal(1, first.MechanicVersions);

        var second = await new ContentHashBackfill(db).RunAsync();
        Assert.Equal(0, second.Total);

        Assert.Equal(correct, db.MechanicVersions.Single(v => v.MechanicId == "mechanic.test.stale").SourceHash);
    }

    /// <summary>
    /// The reason the backfill runs BEFORE the seeders. Seeding against stale fingerprints would
    /// append a new version of every bootstrap record on the first start after this landed, and
    /// then agree with itself forever afterwards, hiding that it happened.
    /// </summary>
    [Fact]
    public async Task Backfilling_then_seeding_twice_still_writes_nothing_the_second_time()
    {
        await using var db = _fixture.CreateContext();

        await new ContentHashBackfill(db).RunAsync();

        var contracts = new ProcedureSeeder(new ProcedureStore(db));
        var rules = new MechanicSeeder(new MechanicStore(db));

        Assert.True(await contracts.SeedAsync() > 0);
        Assert.True(await rules.SeedAsync() > 0);

        await new ContentHashBackfill(db).RunAsync();

        Assert.Equal(0, await contracts.SeedAsync());
        Assert.Equal(0, await rules.SeedAsync());
    }

    /// <summary>
    /// The backfill only recomputes a derived column. If it ever appended a version instead, the
    /// append-only history would gain a revision nobody wrote.
    /// </summary>
    [Fact]
    public async Task The_backfill_never_appends_a_version()
    {
        await using var db = _fixture.CreateContext();
        await new ProcedureSeeder(new ProcedureStore(db)).SeedAsync();

        var before = db.ProcedureContractVersions.Count();

        // Materialise first: modifying entities while streaming from an open SQLite reader is a
        // different bug to be debugging inside a test about something else.
        foreach (var row in db.ProcedureContractVersions.ToList())
        {
            row.SourceHash = "STALE";
        }

        await db.SaveChangesAsync();
        await new ContentHashBackfill(db).RunAsync();

        Assert.Equal(before, db.ProcedureContractVersions.Count());
    }
}
