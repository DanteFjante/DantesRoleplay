using System.Text.Json;
using DantesRoleplay.Categories;
using DantesRoleplay.Mechanics;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Storage for game rules. Deliberately a near-copy of <see cref="ProcedureStore"/>.
///
/// The similarity is the design, not laziness: an agent that has learned find → get → dry run →
/// write for contracts already knows how to author a mechanic. Where the two differ, it is because
/// a mechanic is executable — the extra checks are that the requirements parse and that the
/// components they name exist, both of which are failures the author can fix before anything runs.
/// </summary>
public sealed class MechanicStore(DantesRoleplayDbContext db) : IMechanicStore
{
    /// <summary>Ranking happens in memory, so bound what is pulled back to rank.</summary>
    private const int CandidateCap = 500;

    private readonly DantesRoleplayDbContext _db = db;

    public async Task<IReadOnlyList<MechanicSummary>> FindAsync(
        string? query = null,
        string? category = null,
        string? scope = null,
        bool includeInactive = false,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var rows = _db.Mechanics
            .Join(
                _db.MechanicVersions,
                mechanic => new { MechanicId = mechanic.Id, Version = mechanic.CurrentVersion },
                version => new { version.MechanicId, version.Version },
                (mechanic, version) => new { mechanic, version });

        if (!includeInactive)
        {
            rows = rows.Where(r => r.mechanic.Status != MechanicStatus.Archived);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            // A branch filter, exactly as in ProcedureStore. The trailing dot stops
            // "ruleset.dnd2024.play" from also matching "ruleset.dnd2024.player" — a silent
            // widening that would be worse here than for contracts, because a wider candidate set
            // is what makes an action resolve with the wrong rule.
            var branch = category.Trim();
            var descendants = branch + ".";

            rows = rows.Where(r =>
                r.mechanic.Category == branch || r.mechanic.Category.StartsWith(descendants));
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            // Shared mechanics are ALWAYS included. A campaign that silently lost the base rules
            // would present as "the system forgot how to do the thing it did yesterday", which is
            // among the worst failures to diagnose.
            rows = rows.Where(r => r.mechanic.Scope == scope || r.mechanic.Scope == "");
        }

        var candidates = await rows
            .OrderBy(r => r.mechanic.Category)
            .ThenBy(r => r.mechanic.Id)
            .Take(CandidateCap)
            .Select(r => new MechanicSummary(
                r.mechanic.Id,
                r.mechanic.Category,
                r.version.Name,
                r.version.Description,
                r.version.Matches,
                r.mechanic.Scope,
                r.mechanic.Status,
                r.mechanic.CurrentVersion))
            .ToListAsync(cancellationToken);

        // Scope-specific overrides rank above shared ones. This IS the inheritance chain, and it
        // is three lines rather than a table, which is why the scope column was the right call.
        var scoped = candidates
            .OrderByDescending(c => !string.IsNullOrEmpty(scope) && c.Scope == scope)
            .ToList();

        if (string.IsNullOrWhiteSpace(query))
        {
            return scoped.Take(limit).ToList();
        }

        // Token matching with ranking, same as the procedure store, and for the same cold-walk
        // reason: whole-phrase matching turned a slightly-wrong query into "nothing exists".
        var tokens = Tokenise(query);

        if (tokens.Count == 0)
        {
            return scoped.Take(limit).ToList();
        }

        return scoped
            .Select(c => new
            {
                Mechanic = c,
                Hits = tokens.Count(t => Haystack(c).Contains(t, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Hits > 0)
            .OrderByDescending(x => x.Hits)
            .ThenByDescending(x => !string.IsNullOrEmpty(scope) && x.Mechanic.Scope == scope)
            .ThenBy(x => x.Mechanic.Id, StringComparer.Ordinal)
            .Take(limit)
            .Select(x => x.Mechanic)
            .ToList();
    }

    /// <summary>
    /// The author's match phrases are in here, which is what makes free-text intent matching work
    /// at all: "I try to shove him" finds a mechanic whose phrases include "shove".
    /// </summary>
    private static string Haystack(MechanicSummary m) =>
        $"{m.Id} {m.Name} {m.Description} {m.Matches}";

    private static List<string> Tokenise(string query) => query
        .Split([' ', ',', ';', ':', '/', '\\', '(', ')', '"', '\'', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
        .Select(t => t.Trim())
        .Where(t => t.Length >= 3)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public async Task<MechanicDetail?> GetAsync(
        string id,
        int? version = null,
        CancellationToken cancellationToken = default)
    {
        var mechanic = await _db.Mechanics
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        if (mechanic is null)
        {
            return null;
        }

        var wanted = version ?? mechanic.CurrentVersion;

        var revision = await _db.MechanicVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.MechanicId == id && v.Version == wanted, cancellationToken);

        if (revision is null)
        {
            return null;
        }

        var latest = await _db.MechanicVersions
            .Where(v => v.MechanicId == id)
            .MaxAsync(v => (int?)v.Version, cancellationToken) ?? mechanic.CurrentVersion;

        return ToDetail(mechanic, revision, latest);
    }

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) =>
        _db.Mechanics.AnyAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MechanicCheck>> CheckAsync(
        WriteMechanicRequest request,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<MechanicCheck>();

        var idOk = !string.IsNullOrWhiteSpace(request.Id)
                   && request.Id.Contains('.', StringComparison.Ordinal)
                   && request.Id.Trim() == request.Id
                   && !request.Id.Any(char.IsWhiteSpace);

        checks.Add(new MechanicCheck(
            "id-format",
            idOk,
            idOk
                ? $"'{request.Id}' is a dotted identifier with no whitespace."
                : $"'{request.Id}' should be a dotted identifier with no whitespace, e.g. mechanic.check.ability. Ids are permanent.",
            Blocking: true));

        var existing = await GetAsync(request.Id, cancellationToken: cancellationToken);

        checks.Add(new MechanicCheck(
            "create-or-revise",
            true,
            existing is null
                ? "Creates a new mechanic at version 1."
                : $"Revises an existing mechanic; this becomes version {existing.LatestVersion + 1}. The old source stays readable."));

        // Requirements have to parse, because a mechanic whose projection spec is malformed cannot
        // be given any data at all — it would fail at run time with nothing useful to say.
        MechanicRequirements? requirements = null;
        var requirementsError = string.Empty;

        try
        {
            requirements = MechanicRequirements.Parse(request.Requirements);
        }
        catch (JsonException ex)
        {
            requirementsError = ex.Message;
        }

        checks.Add(new MechanicCheck(
            "requirements-parse",
            requirements is not null,
            requirements is not null
                ? requirements.Roles.Count == 0
                    ? "No roles declared. Valid — a mechanic that needs no world data is fine — but unusual."
                    : $"Declares {requirements.Roles.Count} role(s): {string.Join(", ", requirements.Roles.Keys)}."
                : $"Requirements are not valid JSON: {requirementsError}. Expected {{\"roles\":{{\"<name>\":{{\"components\":[\"...\"]}}}}}}.",
            Blocking: true));

        // Naming a component that does not exist is a typo that would otherwise surface as an
        // empty object mid-run, which reads to the mechanic like "this thing has no stats".
        if (requirements is not null && requirements.Roles.Count > 0)
        {
            var wanted = requirements.AllComponentIds();

            var known = await _db.ComponentDefinitions
                .Where(d => wanted.Contains(d.Id))
                .Select(d => d.Id)
                .ToListAsync(cancellationToken);

            var missing = wanted.Except(known, StringComparer.Ordinal).ToList();

            checks.Add(new MechanicCheck(
                "components-exist",
                missing.Count == 0,
                missing.Count == 0
                    ? $"All {wanted.Count} component definition(s) named in the requirements exist."
                    : $"These component definitions do not exist: {string.Join(", ", missing)}. define_component them first, or the mechanic will read empty data and behave as though the entity has none.",
                Blocking: true));
        }

        checks.Add(new MechanicCheck(
            "source-present",
            !string.IsNullOrWhiteSpace(request.Source),
            string.IsNullOrWhiteSpace(request.Source)
                ? "No source given. A mechanic with no source cannot do anything."
                : $"{request.Source.Split('\n').Length} line(s) of source.",
            Blocking: true));

        checks.Add(new MechanicCheck(
            "matches-stated",
            !string.IsNullOrWhiteSpace(request.Matches),
            string.IsNullOrWhiteSpace(request.Matches)
                ? "No match phrases given. Without them this mechanic is findable by name only, so run_action will rarely surface it."
                : $"Match phrases: {request.Matches.Replace('\n', '/')}",
            Blocking: true));

        var pathOk = CategoryPath.TryValidate(request.Category, out var pathProblem);

        checks.Add(new MechanicCheck(
            "category-path",
            pathOk,
            pathOk
                ? $"'{request.Category}' is a valid category path."
                : pathProblem,
            Blocking: true));

        var categories = await GetCategoriesAsync(cancellationToken);

        checks.Add(new MechanicCheck(
            "category-known",
            true,
            ProcedureStore.DescribeCategory(request.Category, [.. categories.Select(c => c.Category)]),
            Blocking: false));

        // §P12. This matters more for mechanics than for contracts: a duplicated contract is
        // confusing, whereas two mechanics matching the same phrase means the same action resolves
        // differently depending on which one retrieval happened to rank first.
        var others = (await FindAsync(scope: request.Scope, cancellationToken: cancellationToken))
            .Where(m => !string.Equals(m.Id, request.Id, StringComparison.Ordinal))
            .Where(m => Overlaps(m.Name, request.Name) || Overlaps(m.Matches, request.Matches))
            .Select(m => m.Id)
            .ToList();

        checks.Add(new MechanicCheck(
            "no-near-duplicate",
            others.Count == 0,
            others.Count == 0
                ? "No existing mechanic has a similar name or answers to the same phrases."
                : $"These may already cover this: {string.Join(", ", others)}. Two mechanics matching the same phrase makes the same action resolve differently depending on ranking. Prefer revising one.",
            Blocking: false));

        return checks;
    }

    public async Task<WriteMechanicResult> WriteAsync(
        WriteMechanicRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source);

        var now = DateTime.UtcNow;

        var mechanic = await _db.Mechanics.FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);
        var created = mechanic is null;

        if (mechanic is null)
        {
            mechanic = new Mechanic
            {
                Id = request.Id,
                Category = request.Category,
                Scope = request.Scope,
                Status = request.Status ?? MechanicStatus.Draft,
                CurrentVersion = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Mechanics.Add(mechanic);
        }
        else
        {
            mechanic.Category = request.Category;
            mechanic.Scope = request.Scope;
            mechanic.UpdatedAt = now;

            if (request.Status is { } status)
            {
                mechanic.Status = status;
            }
        }

        // High-water mark from the version table, not CurrentVersion, so a rollback cannot cause a
        // version number to be reused — the same reasoning as the procedure store.
        var highest = await _db.MechanicVersions
            .Where(v => v.MechanicId == request.Id)
            .MaxAsync(v => (int?)v.Version, cancellationToken) ?? 0;

        var revision = new MechanicVersion
        {
            MechanicId = mechanic.Id,
            Version = highest + 1,
            Name = request.Name,
            Description = request.Description,
            Matches = request.Matches,
            Requirements = string.IsNullOrWhiteSpace(request.Requirements) ? "{}" : request.Requirements,
            Source = request.Source,
            SourceHash = request.SourceHash,
            CreatedBy = string.IsNullOrWhiteSpace(request.CreatedBy) ? "llm" : request.CreatedBy,
            ChangeNote = request.ChangeNote,
            CreatedAt = now
        };

        _db.MechanicVersions.Add(revision);
        mechanic.CurrentVersion = revision.Version;

        await _db.SaveChangesAsync(cancellationToken);

        return new WriteMechanicResult(ToDetail(mechanic, revision, revision.Version), created);
    }

    public async Task<IReadOnlyList<MechanicCategoryCount>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        // Anonymous projection then map: EF cannot group into a constructed record.
        var rows = await _db.Mechanics
            .AsNoTracking()
            .GroupBy(m => m.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.Category, StringComparer.Ordinal)
            .Select(r => new MechanicCategoryCount(r.Category, r.Count))
            .ToList();
    }

    // ---- helpers ----------------------------------------------------------------------

    /// <summary>
    /// Crude on purpose: two significant words in common is enough to warrant a look. A cleverer
    /// similarity measure would need tuning, and an untuned threshold that fires at the wrong rate
    /// gets ignored — which is worse than a blunt one that fires honestly.
    /// </summary>
    private static bool Overlaps(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        var left = Tokenise(a);
        var right = Tokenise(b);

        return left.Intersect(right, StringComparer.OrdinalIgnoreCase).Count() >= 2;
    }

    private static MechanicDetail ToDetail(Mechanic mechanic, MechanicVersion revision, int latest) =>
        new(
            mechanic.Id,
            mechanic.Category,
            revision.Name,
            revision.Description,
            revision.Matches,
            revision.Requirements,
            revision.Source,
            mechanic.Scope,
            mechanic.Status,
            revision.Version,
            latest,
            revision.CreatedBy,
            revision.ChangeNote,
            revision.CreatedAt)
        {
            SourceHash = revision.SourceHash
        };
}
