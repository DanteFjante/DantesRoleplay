using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Records what the agent did.
///
/// The interesting part is <see cref="RecentlyReadProceduresAsync"/>. The audit log's whole point
/// is answering "was the operating manual actually followed?", and a self-reported list of
/// consulted procedures cannot answer that — an agent that skipped the manual can still claim it
/// didn't. But every get_procedure call is itself logged, so what was really read is already
/// recorded. Deriving the observed list from the log needs no session state, which matters
/// because the MCP host runs stateless.
/// </summary>
public sealed class OperationLog(DantesRoleplayDbContext db) : IOperationLog
{
    /// <summary>
    /// Outer bound on how far back a read counts as "consulted". The real boundary is the last
    /// orient() (see <see cref="RecentlyReadProceduresAsync"/>); this catches a session that was
    /// abandoned rather than re-oriented, so its reads do not leak into whatever happens next hour.
    /// </summary>
    private static readonly TimeSpan MaxReadAge = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The PUBLIC verb a contract read is recorded under. It was `get_procedure` until the
    /// three-verb migration, and leaving it there silently killed the whole derivation: reads
    /// still recorded the contract id as their subject, but under the tool name `query`, so
    /// nothing matched and every honest commit came back flagged for citing what it had never
    /// opened. Nothing failed — 177 tests passed — because the audit only lies, it does not throw.
    /// </summary>
    private const string ReadTool = "query";

    private readonly DantesRoleplayDbContext _db = db;

    public async Task<Operation?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return await _db.Operations
            .AsNoTracking()
            .SingleOrDefaultAsync(operation => operation.Id == id, cancellationToken);
    }

    public async Task<Operation> RecordAsync(
        string tool,
        string summary,
        bool success,
        string intent = "",
        string subject = "",
        IEnumerable<string>? proceduresCited = null,
        string error = "",
        bool consumesReadEvidence = false,
        CancellationToken cancellationToken = default,
        string mechanicId = "",
        int? mechanicVersion = null,
        long? seed = null,
        string projectionJson = "",
        string guardEvidenceJson = "",
        string id = "")
    {
        // Derived BEFORE writing this row, so a get_procedure call never counts itself as one of
        // its own prerequisites. Only recorded for operations the manual governs: for a read or a
        // dry run, "which procedures had been read" is not a claim about anything.
        var read = consumesReadEvidence && tool != ReadTool
            ? await RecentlyReadProceduresAsync(cancellationToken)
            : [];

        var operation = new Operation
        {
            // A caller that allocated the id up front — a world change, which needs it as the
            // correlation id of its events before the transaction opens — passes it back here.
            Id = string.IsNullOrWhiteSpace(id) ? Operation.NewId() : id.Trim(),
            Timestamp = DateTime.UtcNow,
            Tool = tool,
            Subject = subject,
            Intent = intent,
            ProceduresCited = proceduresCited is null ? string.Empty : string.Join(",", proceduresCited),
            ProceduresRead = string.Join(",", read),
            ConsumedReadEvidence = consumesReadEvidence,
            Summary = summary,
            Success = success,
            Error = error,
            MechanicId = mechanicId,
            MechanicVersion = mechanicVersion,
            Seed = seed,
            ProjectionJson = projectionJson,
            GuardEvidenceJson = guardEvidenceJson
        };

        _db.Operations.Add(operation);
        await _db.SaveChangesAsync(cancellationToken);

        return operation;
    }

    public async Task<IReadOnlyList<string>> RecentlyReadProceduresAsync(
        CancellationToken cancellationToken = default)
    {
        var floor = DateTime.UtcNow - MaxReadAge;

        // A read is a WINDOW, not a currency.
        //
        // Three cold walks were spent narrowing what "spends" a read, and the fourth showed the
        // metaphor itself was wrong. The agent read three contracts, called define_component, then
        // apply_effects. The definition write consumed all three, so the world write that followed
        // — the operation those contracts actually govern — reported no reads and was flagged for
        // citing what it had never opened. It had opened all of them.
        //
        // Reading the manual once and then following it for several steps is CORRECT behaviour.
        // A model that spends evidence punishes it, and the only way to satisfy such a model is to
        // re-read the same contract before every write, which is busywork performed to look
        // compliant. The cold-walk subject worked that out and suggested it, which is precisely
        // the finding: the audit was teaching the agent to game it.
        //
        // So the window is bounded by the session, not by what happened inside it. orient() is
        // documented as the first call of a session and is treated as the boundary; MaxReadAge
        // catches a session that was abandoned rather than re-oriented. This still needs no
        // session identifier, which matters because the MCP host is stateless.
        //
        // A heuristic, not session identity: an agent that re-orients mid-task discards evidence
        // it legitimately gathered. That direction is the safe one — under-reporting shows up as a
        // flag inviting a look, whereas over-reporting writes a false consultation into the audit
        // trail as fact.
        var lastOrient = await _db.Operations
            .AsNoTracking()
            .Where(o => o.Tool == "orient" && o.Success && o.Timestamp >= floor)
            .MaxAsync(o => (DateTime?)o.Timestamp, cancellationToken);

        var since = lastOrient ?? floor;

        // One verb now serves every read, so the tool name alone no longer identifies a contract
        // read — a mechanic read and a world read record subjects too. The subject having to BE a
        // procedure id is what narrows it, and it narrows it exactly: no other kind can produce a
        // subject that is in this table.
        var subjects = await _db.Operations
            .AsNoTracking()
            .Where(o => o.Tool == ReadTool
                && o.Success
                && o.Timestamp > since
                && o.Subject != ""
                && _db.ProcedureContracts.Any(p => p.Id == o.Subject))
            .OrderByDescending(o => o.Timestamp)
            .Select(o => o.Subject)
            .Take(100)
            .ToListAsync(cancellationToken);

        return subjects.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<Operation>> RecentAsync(
        int limit = 20,
        bool failuresOnly = false,
        string? tool = null,
        string? subject = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Operations.AsNoTracking();

        if (failuresOnly)
        {
            query = query.Where(o => !o.Success);
        }

        if (!string.IsNullOrWhiteSpace(tool))
        {
            query = query.Where(o => o.Tool == tool);
        }

        if (!string.IsNullOrWhiteSpace(subject))
        {
            // Most tools record one subject; a batch write records every entity it touched, comma
            // separated. Exact match alone would answer "what happened to this entity" with
            // nothing whenever the entity was changed alongside another one — which for a game is
            // most of the time.
            var needle = subject.Trim();

            query = query.Where(o =>
                o.Subject == needle
                || o.Subject.StartsWith(needle + ",")
                || o.Subject.EndsWith("," + needle)
                || o.Subject.Contains("," + needle + ","));
        }

        // Ordering on Timestamp is why it is a DateTime and not a DateTimeOffset: SQLite cannot
        // translate ORDER BY over DateTimeOffset at all.
        return await query
            .OrderByDescending(o => o.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
