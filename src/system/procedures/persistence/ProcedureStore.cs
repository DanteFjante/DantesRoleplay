using DantesRoleplay.Categories;
using DantesRoleplay.Content;
using DantesRoleplay.Procedures;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Reads and writes procedure contracts. The whole of P1.
///
/// Search is a plain LIKE over id, name and description. That is a deliberate choice, not a
/// placeholder: with a few dozen contracts an agent needs a LIST, and a list plus substring
/// matching has no recall failure mode. See ARCHITECTURE.md §8.3.
/// </summary>
public sealed class ProcedureStore(DantesRoleplayDbContext db) : IProcedureStore
{
    /// <summary>
    /// How many rows are pulled back before ranking. Ranking happens in memory because the score
    /// is "how many tokens matched", which SQLite cannot express usefully. Safe while the manual
    /// is small; §8.3 names the point at which that stops being true.
    /// </summary>
    private const int CandidateCap = 500;

    private readonly DantesRoleplayDbContext _db = db;

    public async Task<IReadOnlyList<ProcedureSummary>> FindAsync(
        string? query = null,
        string? category = null,
        bool includeInactive = false,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
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
            // A category filter is a BRANCH filter: the node itself plus everything under it.
            // The trailing dot is load-bearing — without it "ruleset.dnd2024.play" would also
            // match "ruleset.dnd2024.player", silently widening a rule search.
            //
            // Written as equality-or-StartsWith rather than in CategoryPath because this half has
            // to translate to SQL; StartsWith becomes a LIKE the Category index can serve.
            var branch = category.Trim();
            var descendants = branch + ".";

            rows = rows.Where(r =>
                r.contract.Category == branch || r.contract.Category.StartsWith(descendants));
        }

        var candidates = await rows
            .OrderBy(r => r.contract.Category)
            .ThenBy(r => r.contract.Id)
            .Take(CandidateCap)
            .Select(r => new ProcedureSummary(
                r.contract.Id,
                r.contract.Category,
                r.version.Name,
                r.version.Description,
                r.version.Governs,
                r.contract.Status,
                r.contract.CurrentVersion))
            .ToListAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(query))
        {
            return candidates.Take(limit).ToList();
        }

        // Match per TOKEN, not per phrase, and rank by how many tokens hit.
        //
        // Whole-phrase substring matching meant "create contract" found nothing while "contract"
        // found the right answer — a cold-walk finding. Requiring ALL tokens would just move the
        // cliff (a query containing one stray word would return nothing), so any token qualifies
        // and the count decides the order. At this scale a few extra ranked rows cost nothing,
        // and there is no zero-result cliff to fall off.
        //
        // No stopword list: noise words simply match nothing and contribute nothing. That is
        // deliberate — TravelRoleplay's matcher needed stopwords because it SCORED phrases, and
        // adding them there destabilised every other weight.
        var tokens = Tokenise(query);

        if (tokens.Count == 0)
        {
            return candidates.Take(limit).ToList();
        }

        return candidates
            .Select(c => new
            {
                Procedure = c,
                Hits = tokens.Count(t => Haystack(c).Contains(t, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Hits > 0)
            .OrderByDescending(x => x.Hits)
            .ThenBy(x => x.Procedure.Category, StringComparer.Ordinal)
            .ThenBy(x => x.Procedure.Id, StringComparer.Ordinal)
            .Take(limit)
            .Select(x => x.Procedure)
            .ToList();
    }

    /// <summary>Everything a query is matched against, in one string.</summary>
    private static string Haystack(ProcedureSummary p) =>
        $"{p.Id} {p.Name} {p.Description} {p.Governs}";

    /// <summary>
    /// Splits a query into searchable tokens. Punctuation splits, so "create contract" and
    /// "procedure.contract.create" both reduce usefully — but a token is still matched as a
    /// literal substring, so "sy_tem" cannot behave like a wildcard.
    /// </summary>
    private static List<string> Tokenise(string query) => query
        .Split([' ', ',', ';', ':', '/', '\\', '(', ')', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
        .Select(t => t.Trim())
        .Where(t => t.Length >= 3)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

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

    public async Task<IReadOnlyList<WriteCheck>> CheckAsync(
        WriteProcedureRequest request,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<WriteCheck>();

        var idOk = !string.IsNullOrWhiteSpace(request.Id)
                   && request.Id.Contains('.', StringComparison.Ordinal)
                   && request.Id.Trim() == request.Id
                   && !request.Id.Any(char.IsWhiteSpace);

        checks.Add(new WriteCheck(
            "id-format",
            idOk,
            idOk
                ? $"'{request.Id}' is a dotted identifier with no whitespace."
                : $"'{request.Id}' should be a dotted identifier with no whitespace, e.g. procedure.system.modify. Ids are permanent."));

        var existing = await GetAsync(request.Id, cancellationToken: cancellationToken);

        checks.Add(new WriteCheck(
            "create-or-revise",
            true,
            existing is null
                ? $"Creates a new contract at version 1."
                : $"Revises an existing contract; this becomes version {existing.LatestVersion + 1}. Nothing is overwritten."));

        var pathOk = CategoryPath.TryValidate(request.Category, out var pathProblem);

        checks.Add(new WriteCheck(
            "category-path",
            pathOk,
            pathOk
                ? $"'{request.Category}' is a valid category path."
                : pathProblem));

        // Archived paths still belong to the authored taxonomy. Hiding them here would make a
        // new sibling look like a new root and weaken the anti-sprawl guidance precisely when
        // historical content is most useful.
        var categories = await GetCategoriesAsync(
            includeInactive: true,
            cancellationToken: cancellationToken);
        var paths = categories.Select(c => c.Category).ToList();

        checks.Add(new WriteCheck(
            "category-known",
            true,
            DescribeCategory(request.Category, paths)));

        checks.Add(new WriteCheck(
            "governs-stated",
            !string.IsNullOrWhiteSpace(request.Governs),
            string.IsNullOrWhiteSpace(request.Governs)
                ? "No 'governs' given. Without it, a later agent cannot tell which operations this contract applies to, and the system's one rule becomes guesswork."
                : $"Governs: {request.Governs}"));

        checks.Add(new WriteCheck(
            "constraints-separated",
            true,
            string.IsNullOrWhiteSpace(request.Constraints)
                ? "No constraints given. That is valid, but a contract with no 'must not' is usually incomplete."
                : "Constraints are stated separately from instructions."));

        // §P12: the anti-sprawl guard has to be structural, not an instruction the model may skip.
        var others = (await FindAsync(cancellationToken: cancellationToken))
            .Where(p => !string.Equals(p.Id, request.Id, StringComparison.Ordinal))
            .Where(p => Overlaps(p.Name, request.Name) || (request.Governs.Length > 0 && Overlaps(p.Governs, request.Governs)))
            .Select(p => p.Id)
            .ToList();

        checks.Add(new WriteCheck(
            "no-near-duplicate",
            others.Count == 0,
            others.Count == 0
                ? "No existing contract has a similar name or governs the same operations."
                : $"These look like they may already cover this: {string.Join(", ", others)}. Prefer revising one over adding a near-duplicate."));

        return checks;
    }

    /// <summary>
    /// Where a category sits in the tree, said in one line.
    ///
    /// This used to list every category that existed, which was fine at eight and useless at
    /// ninety — and ninety is what a real ruleset looks like. Naming the nearest existing branch
    /// and its children keeps the anti-sprawl nudge (§P12) actionable at any size: the author is
    /// shown the siblings they might have meant, not the whole catalog.
    /// </summary>
    internal static string DescribeCategory(string category, IReadOnlyList<string> existing)
    {
        if (existing.Any(c => string.Equals(c, category, StringComparison.Ordinal)))
        {
            return $"'{category}' is an existing category.";
        }

        if (existing.Count == 0)
        {
            return $"'{category}' is the first category here.";
        }

        var nearest = CategoryPath.NearestKnownNode(category, existing);

        if (nearest is null)
        {
            var roots = CategoryPath.ChildNodes(null, existing);

            return $"'{category}' is a NEW ROOT. Existing roots: {string.Join(", ", roots)}. "
                + "Reuse one unless this is genuinely a separate area.";
        }

        var siblings = CategoryPath.ChildNodes(nearest, existing);

        return $"'{category}' is NEW. Its nearest existing branch is '{nearest}', "
            + $"whose children are: {string.Join(", ", siblings)}. "
            + "Reuse one of those unless this is genuinely a new leaf.";
    }

    public async Task<WriteProcedureResult> WriteAsync(
        WriteProcedureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Instructions);

        var now = DateTime.UtcNow;

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
            Governs = request.Governs,
            Instructions = request.Instructions,
            Constraints = request.Constraints,

            // Computed here, from the values actually being stored, and never accepted from the
            // caller. A caller that can supply its own fingerprint can mark drifted content as
            // clean, which is the one thing the fingerprint exists to prevent. Category and Status
            // come off the parent row because that is where the effective values live after the
            // create/revise branch above.
            SourceHash = ContentHash.ForProcedure(
                contract.Category,
                request.Name,
                request.Description,
                request.Governs,
                request.Instructions,
                request.Constraints,
                contract.Status),
            CreatedBy = string.IsNullOrWhiteSpace(request.CreatedBy) ? "llm" : request.CreatedBy,
            ChangeNote = request.ChangeNote,
            CreatedAt = now
        };

        _db.ProcedureContractVersions.Add(revision);
        contract.CurrentVersion = revision.Version;

        await _db.SaveChangesAsync(cancellationToken);

        return new WriteProcedureResult(ToDetail(contract, revision, revision.Version), created);
    }

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
                v.Governs,
                contract.Status,
                v.Version))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) =>
        _db.ProcedureContracts.AnyAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProcedureCategoryCount>> GetCategoriesAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        // Project the grouping into an ANONYMOUS type, not straight into the record. EF can
        // translate a grouped Count into an anonymous projection; constructing a user type in the
        // group selector makes the whole expression untranslatable.
        var categories = _db.ProcedureContracts.AsNoTracking();

        if (!includeInactive)
        {
            categories = categories.Where(c => c.Status != ProcedureStatus.Archived);
        }

        var rows = await categories
            .GroupBy(c => c.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.Category, StringComparer.Ordinal)
            .Select(r => new ProcedureCategoryCount(r.Category, r.Count))
            .ToList();
    }

    /// <summary>
    /// Crude token overlap, used only to raise a warning. Deliberately not clever: this needs to
    /// be predictable enough that an author can see why it fired, and a false positive costs a
    /// sentence of explanation while a false negative costs a duplicate contract forever.
    /// </summary>
    private static bool Overlaps(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        // Literal call fragments make contracts discoverable, but they are API vocabulary rather
        // than evidence that two contracts govern the same domain. Parent and child contracts are
        // expected to repeat them, so exclude them from the anti-sprawl comparison.
        static IEnumerable<string> Tokens(string value) => System.Text.RegularExpressions.Regex
            .Replace(
                value,
                "commit\\(kind:\\s*\"[^\"]+\"\\)",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Split([' ', ',', '.', '-', '_', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length > 3);

        var left = Tokens(a).ToHashSet(StringComparer.Ordinal);
        return left.Count != 0
            && Tokens(b).Distinct(StringComparer.Ordinal).Count(left.Contains) >= 2;
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
            revision.Governs,
            revision.Instructions,
            revision.Constraints,
            contract.Status,
            revision.Version,
            latestVersion,
            revision.CreatedBy,
            revision.ChangeNote,
            revision.CreatedAt)
        {
            SourceHash = revision.SourceHash
        };

    /// <summary>
    /// LIKE treats % and _ as wildcards. A dotted contract id such as "procedure.system_modify"
    /// would otherwise match more than the caller asked for.
    /// </summary>
    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
