using System.Reflection;
using System.Text;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.DataAccess.Bootstrap;

/// <summary>
/// Loads the bootstrap contracts embedded in the core assembly into the database at startup.
///
/// Contracts are AUTHORED as markdown files, so they are editable and diffable in git, and
/// STORED in the database, so they get version history, runtime edits by the LLM, and a link
/// from every operation to the exact revision in force.
///
/// Reseeding is idempotent: each seeded revision records the fingerprint of the file it came
/// from, and a file is rewritten only when that fingerprint changes. A restart with no edits
/// writes nothing.
/// </summary>
public sealed class ProcedureSeeder(IProcedureStore store)
{
    private const string ResourceMarker = ".Bootstrap.";

    private readonly IProcedureStore _store = store;

    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        var written = 0;

        foreach (var file in Load())
        {
            var existing = await _store.GetAsync(file.Id, cancellationToken: cancellationToken);

            // Compare the STORED fingerprint against the file's, rather than re-hashing the
            // round-tripped content. Re-deriving would make this depend on every field surviving
            // storage byte-for-byte — line endings, trimming, enum formatting — and any drift
            // there silently reseeds every contract on every start.
            if (existing is not null && existing.SourceHash == file.ContentHash)
            {
                continue;
            }

            await _store.WriteAsync(
                new WriteProcedureRequest
                {
                    Id = file.Id,
                    Category = file.Category,
                    Name = file.Name,
                    Description = file.Description,
                    Governs = file.Governs,
                    Matches = file.Matches,
                    Instructions = file.Instructions,
                    Constraints = file.Constraints,
                    Status = file.Status,
                    CreatedBy = "seed",
                    ChangeNote = existing is null
                        ? "Seeded from bootstrap file."
                        : "Re-seeded: the bootstrap file changed."

                    // No SourceHash: the store computes it from the content it stores. Both sides
                    // of the comparison above now go through the same function, so it stays exact.
                },
                cancellationToken);

            written++;
        }

        return written;
    }

    /// <summary>
    /// Reads every embedded .md carrying the Bootstrap resource marker in the core assembly.
    /// Those resources are compiled directly from the canonical non-ruleset catalog procedures;
    /// Bootstrap is a runtime resource name now, not a second authoring directory. Anchored on the
    /// core assembly rather than the executing one so the host cannot accidentally shadow it.
    /// </summary>
    public static IReadOnlyList<ProcedureFile> Load()
    {
        var assembly = typeof(ProcedureContract).Assembly;
        var files = new List<ProcedureFile>();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.Contains(ResourceMarker, StringComparison.Ordinal)
                || !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Could not read embedded resource '{name}'.");

            using var reader = new StreamReader(stream, Encoding.UTF8);
            files.Add(ProcedureFile.Parse(reader.ReadToEnd(), name));
        }

        var duplicate = files.GroupBy(f => f.Id).FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Two bootstrap files declare id '{duplicate.Key}'.");
        }

        return files.OrderBy(f => f.Id, StringComparer.Ordinal).ToList();
    }
}
