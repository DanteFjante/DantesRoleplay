using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;

namespace DantesRoleplay.Tests;

public sealed class OperationLogTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Records_an_operation_and_returns_it_newest_first()
    {
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        await log.RecordAsync("find_procedures", "listed 3", success: true);
        await log.RecordAsync(
            "write_procedure",
            "created procedure.system.modify v1",
            success: true,
            intent: "Add a modify procedure",
            subject: "procedure.system.modify",
            proceduresCited: ["procedure.contract.create"],
            consumesReadEvidence: true);

        var recent = await log.RecentAsync();

        Assert.Equal(2, recent.Count);
        Assert.Equal("write_procedure", recent[0].Tool);
        Assert.Equal("procedure.contract.create", recent[0].ProceduresCited);
        Assert.Equal("procedure.system.modify", recent[0].Subject);
    }

    [Fact]
    public async Task Failures_can_be_isolated()
    {
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        await log.RecordAsync("get_procedure", "ok", success: true);
        await log.RecordAsync("get_procedure", "not found", success: false, error: "UNKNOWN_PROCEDURE");

        var failures = await log.RecentAsync(failuresOnly: true);

        Assert.Single(failures);
        Assert.Equal("UNKNOWN_PROCEDURE", failures[0].Error);
    }

    [Fact]
    public async Task History_can_be_filtered_by_tool_and_subject()
    {
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        await log.RecordAsync("get_procedure", "read a", success: true, subject: "procedure.a");
        await log.RecordAsync("get_procedure", "read b", success: true, subject: "procedure.b");
        await log.RecordAsync("write_procedure", "wrote a", success: true, subject: "procedure.a");

        Assert.Equal(2, (await log.RecentAsync(tool: "get_procedure")).Count);
        Assert.Equal(2, (await log.RecentAsync(subject: "procedure.a")).Count);
        Assert.Single(await log.RecentAsync(tool: "write_procedure", subject: "procedure.a"));
    }

    [Fact]
    public async Task Procedures_actually_read_are_observed_from_the_log_not_from_the_caller()
    {
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        // The agent really opens one contract...
        await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.contract.create");

        // ...then claims it consulted a different one it never opened.
        var write = await log.RecordAsync(
            "write_procedure",
            "wrote",
            success: true,
            subject: "procedure.new",
            proceduresCited: ["procedure.system.modify"],
            consumesReadEvidence: true);

        Assert.Equal("procedure.system.modify", write.ProceduresCited);
        Assert.Equal("procedure.contract.create", write.ProceduresRead);
    }

    [Fact]
    public async Task A_failed_read_does_not_count_as_having_consulted_a_procedure()
    {
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        await log.RecordAsync("get_procedure", "not found", success: false, subject: "procedure.ghost");

        var write = await log.RecordAsync("write_procedure", "wrote", success: true, subject: "procedure.new");

        Assert.Equal(string.Empty, write.ProceduresRead);
    }

    [Fact]
    public async Task A_read_does_not_count_itself_as_its_own_prerequisite()
    {
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.a");
        var second = await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.b");

        Assert.Equal(string.Empty, second.ProceduresRead);
    }

    /// <summary>
    /// The known weakness of the window, asserted so that it stays a decision rather than becoming
    /// a surprise.
    ///
    /// Two runs that are not separated by orient() and fall inside MaxReadAge share their reading.
    /// Nothing observable distinguishes them: the stateless MCP host issues no session id, and the
    /// old proxy — "a write means the previous run ended" — was disproved by cold walk four, where
    /// a mid-run write ended a run that was still going.
    ///
    /// Given a choice of which error to make, this is the right one. Over-reporting needs a run
    /// that skips its documented first call AND follows another within half an hour; the old model
    /// under-reported on every correct multi-step run, which is the common case.
    /// </summary>
    [Fact]
    public async Task A_run_that_never_orients_inherits_the_previous_one_s_reading()
    {
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.old");
        await log.RecordAsync("write_procedure", "wrote", success: true, subject: "procedure.a", consumesReadEvidence: true);

        await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.new");
        var second = await log.RecordAsync("write_procedure", "wrote", success: true, subject: "procedure.b", consumesReadEvidence: true);

        Assert.Equal("procedure.new,procedure.old", second.ProceduresRead);
    }

    [Fact]
    public async Task An_operation_with_no_reads_does_not_reset_the_window()
    {
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.a");

        // Read-only tools in between change nothing about what was read. orient is excluded here
        // on purpose — it is a deliberate session boundary and DOES reset the window; see
        // Orienting_starts_a_fresh_run.
        await log.RecordAsync("history", "looked", success: true);
        await log.RecordAsync("find_procedures", "listed", success: true);

        var write = await log.RecordAsync("write_procedure", "wrote", success: true, subject: "procedure.b", consumesReadEvidence: true);

        Assert.Equal("procedure.a", write.ProceduresRead);
    }

    [Fact]
    public async Task A_dry_run_validates_without_spending_the_evidence()
    {
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        // The exact cold-walk sequence: an earlier run, then a fresh one that reads, dry runs and
        // commits. orient() is what separates them — it is the documented first call of a session
        // and the only boundary the log can actually observe.
        await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.old");
        await log.RecordAsync("write_procedure", "earlier commit", success: true,
            subject: "procedure.earlier", consumesReadEvidence: true);

        await log.RecordAsync("orient", "a new session begins", success: true);
        await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.contract.create");

        // dryRun: true — validates, changes nothing, and must leave the evidence intact.
        var dry = await log.RecordAsync("write_procedure", "dry run", success: true,
            subject: "procedure.new", consumesReadEvidence: false);
        Assert.Equal(string.Empty, dry.ProceduresRead);

        // The real commit is the operation that consumes it, so it is the one that reports it.
        var commit = await log.RecordAsync("write_procedure", "committed", success: true,
            subject: "procedure.new",
            proceduresCited: ["procedure.contract.create"],
            consumesReadEvidence: true);

        // Previously the dry run spent it and this came back empty, so history flagged
        // CitedWithoutReading against an agent that had followed the manual correctly.
        Assert.Equal("procedure.contract.create", commit.ProceduresRead);
    }

    [Fact]
    public async Task Orienting_starts_a_fresh_run()
    {
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        // A previous run that ended on a read, with no write to spend it.
        await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.stale");

        // A new session begins by orienting.
        await log.RecordAsync("orient", "oriented", success: true);
        await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.fresh");

        var commit = await log.RecordAsync("write_procedure", "committed", success: true,
            subject: "procedure.new", consumesReadEvidence: true);

        Assert.Equal("procedure.fresh", commit.ProceduresRead);
    }

    [Fact]
    public async Task A_dry_run_that_cites_a_procedure_is_not_an_unbacked_citation()
    {
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.contract.create");

        var dry = await log.RecordAsync("write_procedure", "dry run", success: true,
            subject: "procedure.new",
            proceduresCited: ["procedure.contract.create"],
            consumesReadEvidence: false);

        var commit = await log.RecordAsync("write_procedure", "committed", success: true,
            subject: "procedure.new",
            proceduresCited: ["procedure.contract.create"],
            consumesReadEvidence: true);

        // The dry run cites but does not consume, so it is not judged. history() counts an
        // unbacked citation only where ConsumedReadEvidence is set — otherwise a correct
        // dry-run-then-commit sequence reports itself as a manual violation.
        Assert.False(dry.ConsumedReadEvidence);
        Assert.Empty(dry.ProceduresRead);

        Assert.True(commit.ConsumedReadEvidence);
        Assert.Equal("procedure.contract.create", commit.ProceduresRead);

        var unbacked = (await log.RecentAsync())
            .Count(o => o.ConsumedReadEvidence
                     && o.ProceduresCited.Split(',', StringSplitOptions.RemoveEmptyEntries)
                         .Except(o.ProceduresRead.Split(',', StringSplitOptions.RemoveEmptyEntries))
                         .Any());

        Assert.Equal(0, unbacked);
    }

    [Fact]
    public async Task One_reading_of_the_manual_backs_every_operation_it_governs()
    {
        // Cold walk run four, exactly. The agent read three contracts, defined a component, then
        // applied effects. Under the old spend-it model the definition write consumed all three,
        // so the world write — the operation those contracts actually govern — reported no reads
        // and was flagged for citing what it had never opened.
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        await log.RecordAsync("orient", "oriented", success: true);
        await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.world.change");
        await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.world.model");

        var definition = await log.RecordAsync("define_component", "defined stats", success: true,
            subject: "stats",
            proceduresCited: ["procedure.world.model"],
            consumesReadEvidence: true);

        var write = await log.RecordAsync("apply_effects", "created an actor", success: true,
            subject: "orban,lantern",
            proceduresCited: ["procedure.world.change"],
            consumesReadEvidence: true);

        // Both operations are backed. Reading the manual once and then following it for several
        // steps is correct behaviour, and an audit that punishes it teaches the agent to re-read
        // the same contract before every write purely to look compliant.
        Assert.Contains("procedure.world.model", definition.ProceduresRead);
        Assert.Contains("procedure.world.change", write.ProceduresRead);

        Assert.Empty(Unbacked(await log.RecentAsync()));
    }

    [Fact]
    public async Task A_new_session_does_not_inherit_the_previous_one_s_reading()
    {
        // The other direction, and the reason the window exists at all: over-reporting writes a
        // false consultation into the audit trail as fact.
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        await log.RecordAsync("get_procedure", "read", success: true, subject: "procedure.world.change");
        await log.RecordAsync("orient", "a new session begins", success: true);

        var write = await log.RecordAsync("apply_effects", "changed something", success: true,
            subject: "orban",
            proceduresCited: ["procedure.world.change"],
            consumesReadEvidence: true);

        Assert.Empty(write.ProceduresRead);
        Assert.Single(Unbacked(await log.RecentAsync()));
    }

    /// <summary>Operations that claimed a procedure the log cannot show them reading.</summary>
    private static List<Operation> Unbacked(IReadOnlyList<Operation> operations) =>
        operations
            .Where(o => o.ConsumedReadEvidence
                     && o.ProceduresCited.Split(',', StringSplitOptions.RemoveEmptyEntries)
                         .Except(o.ProceduresRead.Split(',', StringSplitOptions.RemoveEmptyEntries))
                         .Any())
            .ToList();

    [Fact]
    public async Task History_finds_an_entity_inside_a_batch_that_touched_several()
    {
        // A batch write records every entity it touched. Exact matching would answer "what has
        // happened to this thing" with nothing whenever it was changed alongside another one,
        // which for a world made of interacting things is most of the time.
        await using var db = _fixture.CreateContext();
        var log = new OperationLog(db);

        await log.RecordAsync("apply_effects", "moved a thing", success: true, subject: "lantern,orban");
        await log.RecordAsync("apply_effects", "unrelated", success: true, subject: "tower");

        var first = await log.RecentAsync(subject: "lantern");
        var middleOfList = await log.RecentAsync(subject: "orban");
        var unrelated = await log.RecentAsync(subject: "orb");

        Assert.Single(first);
        Assert.Single(middleOfList);

        // 'orb' is a prefix of 'orban' and must not match it — a partial id is a different thing.
        Assert.Empty(unrelated);
    }
}
