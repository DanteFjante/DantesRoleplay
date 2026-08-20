using System.Reflection;
using System.Text;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.DataAccess.Bootstrap;

/// <summary>
/// Loads the canonical generic catalog mechanics embedded in the core assembly at startup.
///
/// There is a tension worth naming here. This system's premise is that the game is authored in
/// play, and shipping rules works against that — so what ships is deliberately the smallest set
/// that demonstrates the shape rather than anything resembling a game. Two rules, both entirely
/// generic: one that tests a number against a threshold, one that adjusts a number. Neither knows
/// what the numbers mean.
///
/// They earn their place by giving the first session something to read. An agent asked to write a
/// rule with no example produces something plausible and wrong; an agent with two working examples
/// copies the shape. They are also ordinary rows once seeded — revisable, versioned, and
/// deprecatable like anything written later.
/// </summary>
public sealed class MechanicSeeder(IMechanicStore store)
{
    private const string ResourceMarker = ".Rules.";

    private readonly IMechanicStore _store = store;

    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        var written = 0;

        foreach (var file in Load())
        {
            var existing = await _store.GetAsync(file.Id, cancellationToken: cancellationToken);

            // Compare the STORED fingerprint rather than re-deriving one from round-tripped
            // content — same reasoning as the procedure seeder, and the same bug avoided.
            if (existing is not null && existing.SourceHash == file.ContentHash)
            {
                continue;
            }

            await _store.WriteAsync(
                new WriteMechanicRequest
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
                    CreatedBy = "seed",
                    ChangeNote = existing is null
                        ? "Seeded from the embedded catalog mechanic."
                        : "Re-seeded: the embedded catalog mechanic changed."

                    // No SourceHash: the store computes it from the content it stores. The seeder
                    // supplying one was the only way an incorrect fingerprint could ever be
                    // written, and the comparison above is still exact because both sides now go
                    // through the same function.
                },
                cancellationToken);

            written++;
        }

        return written;
    }

    public static IReadOnlyList<MechanicFile> Load()
    {
        var assembly = typeof(Mechanic).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var files = new List<MechanicFile>();

        foreach (var name in resources.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceName = name[..^".md".Length] + ".js";
            var source = resources.Contains(sourceName) ? Read(assembly, sourceName) : null;

            files.Add(MechanicFile.Parse(Read(assembly, name), name, source));
        }

        var duplicate = files.GroupBy(f => f.Id).FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Two embedded catalog mechanics declare id '{duplicate.Key}'.");
        }

        return files.OrderBy(f => f.Id, StringComparer.Ordinal).ToList();
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Could not read embedded resource '{name}'.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
