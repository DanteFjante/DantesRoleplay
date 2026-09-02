using DantesRoleplay.Content;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Brings every stored revision's fingerprint up to the one current definition of it, and reports
/// on the ones that are not there yet.
///
/// Two populations of rows need this, and neither can be left alone:
///
/// - Rows written over MCP, which never had a fingerprint at all. Before this existed the column
///   was documented as "empty when written through MCP", so every rule an LLM ever authored — the
///   entire live D&amp;D ruleset — carried nothing. Those are exactly the rows catalog export has to
///   reason about, so "we only fingerprint what came from a file" is not a position that survives
///   contact with export.
/// - Rows seeded before <see cref="ContentHash"/> existed, whose fingerprint came from an older
///   formula: no field separator on the mechanic side, and platform-dependent line endings on
///   both. Those values look populated and are not comparable with anything computed today, which
///   is worse than empty because nothing about them reads as wrong.
///
/// Runs at startup, before the seeders, so their "has this file changed?" comparison is made
/// against current fingerprints rather than stale ones. It is idempotent by construction — a row
/// whose stored value already equals the recomputed one is not touched — so after the first run it
/// is two scans of a few dozen rows and no writes.
///
/// It covers HISTORICAL version rows too, not just the current one. Their content is immutable, so
/// the fingerprint of that content is well defined, and export needs to be able to speak about any
/// version rather than only the latest.
///
/// This changes no authored content. It recomputes a derived column, so it is not an exception to
/// the append-only rule — there is no new version and no history to lose.
/// </summary>
public sealed class ContentHashBackfill(DantesRoleplayDbContext db)
{
    private readonly DantesRoleplayDbContext _db = db;

    /// <summary>
    /// What every revision's fingerprint is, and what it should be. Reads only.
    ///
    /// The reporting tool and the correcting pass both go through here, so there is exactly one
    /// answer to "what should this row's fingerprint be" — which is the same argument that put
    /// <see cref="ContentHash"/> in the core project rather than in two parsers.
    /// </summary>
    public async Task<IReadOnlyList<ContentHashRow>> AuditAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<ContentHashRow>();

        // Category, Scope and Status live on the parent row, so the join is not optional: a
        // fingerprint computed without them would not match the one the store writes.
        var mechanics = await _db.MechanicVersions
            .Join(
                _db.Mechanics,
                version => version.MechanicId,
                mechanic => mechanic.Id,
                (version, mechanic) => new { version, mechanic })
            .ToListAsync(cancellationToken);

        foreach (var row in mechanics)
        {
            rows.Add(new ContentHashRow(
                ContentHashKind.Mechanic,
                row.version.MechanicId,
                row.version.Version,
                row.version.SourceHash,
                ContentHash.ForMechanic(
                    row.mechanic.Category,
                    row.version.Name,
                    row.version.Description,
                    row.version.Matches,
                    row.version.Requirements,
                    row.version.Source,
                    row.mechanic.Scope,
                    row.mechanic.Status)));
        }

        var contracts = await _db.ProcedureContractVersions
            .Join(
                _db.ProcedureContracts,
                version => version.ContractId,
                contract => contract.Id,
                (version, contract) => new { version, contract })
            .ToListAsync(cancellationToken);

        foreach (var row in contracts)
        {
            rows.Add(new ContentHashRow(
                ContentHashKind.Procedure,
                row.version.ContractId,
                row.version.Version,
                row.version.SourceHash,
                ContentHash.ForProcedure(
                    row.contract.Category,
                    row.version.Name,
                    row.version.Description,
                    row.version.Governs,
                    row.version.Instructions,
                    row.version.Constraints,
                    row.contract.Status)));
        }

        return rows;
    }

    /// <summary>Corrects every missing or stale fingerprint. Returns how many were corrected.</summary>
    public async Task<ContentHashBackfillResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var audit = await AuditAsync(cancellationToken);
        var stale = audit.Where(r => !r.IsCurrent).ToList();

        if (stale.Count == 0)
        {
            return new ContentHashBackfillResult(0, 0);
        }

        var mechanicIds = stale
            .Where(r => r.Kind == ContentHashKind.Mechanic)
            .Select(r => (r.Id, r.Version, r.Expected))
            .ToList();

        foreach (var (id, version, expected) in mechanicIds)
        {
            var row = await _db.MechanicVersions
                .FirstAsync(v => v.MechanicId == id && v.Version == version, cancellationToken);

            row.SourceHash = expected;
        }

        var contractIds = stale
            .Where(r => r.Kind == ContentHashKind.Procedure)
            .Select(r => (r.Id, r.Version, r.Expected))
            .ToList();

        foreach (var (id, version, expected) in contractIds)
        {
            var row = await _db.ProcedureContractVersions
                .FirstAsync(v => v.ContractId == id && v.Version == version, cancellationToken);

            row.SourceHash = expected;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new ContentHashBackfillResult(mechanicIds.Count, contractIds.Count);
    }
}

public enum ContentHashKind
{
    Mechanic,
    Procedure
}

/// <param name="Stored">What the row carries now. Empty means it was never fingerprinted.</param>
/// <param name="Expected">What <see cref="ContentHash"/> says its content fingerprints to.</param>
public sealed record ContentHashRow(
    ContentHashKind Kind,
    string Id,
    int Version,
    string Stored,
    string Expected)
{
    public bool IsMissing => Stored.Length == 0;

    public bool IsCurrent => string.Equals(Stored, Expected, StringComparison.Ordinal);
}

/// <param name="MechanicVersions">Mechanic revisions whose fingerprint was missing or stale.</param>
/// <param name="ProcedureVersions">Contract revisions whose fingerprint was missing or stale.</param>
public sealed record ContentHashBackfillResult(int MechanicVersions, int ProcedureVersions)
{
    public int Total => MechanicVersions + ProcedureVersions;
}
