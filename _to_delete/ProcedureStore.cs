using DantesRoleplay.Database;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Procedures;

/// <summary>
/// Reads and writes procedure contracts. The whole of P1.
///
/// Search is a plain LIKE over name and description. That is a deliberate choice, not a
/// placeholder for something better: with a few dozen contracts an agent needs a LIST, and a
/// list plus substring matching has no recall failure mode. See ARCHITECTURE.md §8.3 for the
/// triggers that would justify FTS5 or embeddings.
/// </summary>
public sealed class ProcedureStore(DantesRoleplayDbContext db)
{
    private readonly DantesRoleplayDbContext _db = db;

    /// <summary>
    /// List or search. A null/empty <paramref name="query"/> returns everything, which is the
    /// common case and the cheapest way for a cold model to see what exists.
    /// </summary>
    public async Task<IReadOnlyList<ProcedureSummary>> FindAsync(
        string? query = null,
        string? category = null,
        bool includeInactive = false,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        // Join each contract to its current version. Content lives only on the version row.
        var rows = _db.ProcedureContracts
            .Join(
                _db.ProcedureContractVersions,
                contract => new { ContractId = contract.Id, Version = contract.CurrentVersion },
                version => new { version.ContractId, version.Version },
                (contract, version) => new { contract, version });

        if (!includeInactive)
        {
            rows = rows.Where(r => r.contract.Status != ProcedureStatus.Archived);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            rows = rows.Where(r => r.contract.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{Escape(query.Trim())}%";
            rows = rows.Where(r =>
                EF.Functions.Like(r.contract.Id, pattern, "\\") ||
                EF.Functions.Like(r.version.Name, pattern, "\\") ||
                EF.Functions.Like(r.version.Description, pattern, "\\"));
        }

        return await rows
            .OrderBy(r => r.contract.Category)
            .ThenBy(r => r.contract.Id)
            .Take(limit)
            .Select(r => new ProcedureSummary(
                r.contract.Id,
                r.contract.Category,
                r.version.Name,
                r.version.Description,
                r.contract.Status,
                r.contract.CurrentVersion))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Fetch one contract. <paramref name="version"/> null means "whatever is live now";
    /// passing a number pins a historical revision, which is how an old operation stays legible.
    /// </summary>
    public async Task<ProcedureDetail?> GetAsync(
        string id,
        int? version = null,
        CancellationToken cancellationToken = default)
    {
        var contract = await _db.ProcedureContracts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (contract is null)
        {
            return null;
        }

        var wanted = version ?? contract.CurrentVersion;

        var revision = await _db.ProcedureContractVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.ContractId == id && v.Version == wanted, cancellationToken);

        if (revision is null)
        {
            return null;
        }

        var latest = await _db.ProcedureContractVersions
            .Where(v => v.ContractId == id)
            .MaxAsync(v => (int?)v.Version, cancellationToken) ?? contract.CurrentVersion;

        return ToDetail(contract, revision, latest);
    }

    /// <summary>
    /// Create a contract, or append a revision to an existing one. Content is never mutated in
    /// place — that invariant is what makes the audit trail worth having.
    /// </summary>
    public async Task<WriteProcedureResult> WriteAsync(
        WriteProcedureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Instructions);

        var now = DateTimeOffset.UtcNow;

        var contract = await _db.ProcedureContracts
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        var created = contract is null;

        if (contract is null)
        {
            contract = new ProcedureContract
            {
                Id = request.Id,
                Category = request.Category,
                Status = request.Status ?? ProcedureStatus.Active,
                CurrentVersion = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.ProcedureContracts.Add(contract);
        }
        else
        {
            contract.Category = request.Category;
            contract.UpdatedAt = now;

            if (request.Status is { } status)
            {
                contract.Status = status;
            }
        }

        // Read the high-water mark from the version table rather than trusting CurrentVersion,
        // so a contract that was rolled back cannot reuse a version number.
        var highest = await _db.ProcedureContractVersions
            .Where(v => v.ContractId == request.Id)
            .MaxAsync(v => (int?)v.Version, cancellationToken) ?? 0;

        var revision = new ProcedureContractVersion
        {
            ContractId = contract.Id,
            Version = highest + 1,
            Name = request.Name,
            Description = request.Description,
            Instructions = request.Instructions,
            Constraints = request.Constraints,
            CreatedBy = string.IsNullOrWhiteSpace(request.CreatedBy) ? "llm" : request.CreatedBy,
            ChangeNote = request.ChangeNote,
            CreatedAt = now
        };

        _db.ProcedureContractVersions.Add(revision);
        contract.CurrentVersion = revision.Version;

        await _db.SaveChangesAsync(cancellationToken);

        return new WriteProcedureResult(ToDetail(contract, revision, revision.Version), created);
    }

    /// <summary>All revisions of one contract, newest first. Content omitted — this is a timeline.</summary>
    public async Task<IReadOnlyList<ProcedureSummary>> GetVersionsAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var contract = await _db.ProcedureContracts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (contract is null)
        {
            return [];
        }

        return await _db.ProcedureContractVersions
            .AsNoTracking()
            .Where(v => v.ContractId == id)
            .OrderByDescending(v => v.Version)
            .Select(v => new ProcedureSummary(
                contract.Id,
                contract.Category,
                v.Name,
                v.Description,
                contract.Status,
                v.Version))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) =>
        _db.ProcedureContracts.AnyAsync(c => c.Id == id, cancellationToken);

    /// <summary>Distinct categories with contract counts — the cheapest possible orientation view.</summary>
    public async Task<IReadOnlyList<(string Category, int Count)>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.ProcedureContracts
            .AsNoTracking()
            .Where(c => c.Status != ProcedureStatus.Archived)
            .GroupBy(c => c.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderBy(g => g.Category)
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.Category, r.Count)).ToList();
    }

    private static ProcedureDetail ToDetail(
        ProcedureContract contract,
        ProcedureContractVersion revision,
        int latestVersion) =>
        new(
            contract.Id,
            contract.Category,
            revision.Name,
            revision.Description,
            revision.Instructions,
            revision.Constraints,
            contract.Status,
            revision.Version,
            latestVersion,
            revision.CreatedBy,
            revision.ChangeNote,
            revision.CreatedAt);

    /// <summary>
    /// LIKE treats % and _ as wildcards. A contract id such as "procedure.system_modify" would
    /// otherwise match more than the caller asked for.
    /// </summary>
    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
