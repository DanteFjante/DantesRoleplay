using System.Reflection;
using System.Text;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Events;

namespace DantesRoleplay.DataAccess.Bootstrap;

/// <summary>
/// Loads the nine <c>world.*</c> structural event types embedded in the core assembly.
///
/// These are kernel contracts rather than game content, and the distinction is not academic: every
/// accepted world change records an event against one of them, so a database that does not have
/// them cannot change the world at all. They shipped as catalog files only, which left a fresh
/// install unable to run `commit(kind: "effects")` until somebody happened to import the catalog —
/// a dependency nothing declared and no test covered.
///
/// The canonical catalog files are embedded at build time, so the kernel is self-sufficient without
/// maintaining a second authored copy.
///
/// Parsing reuses <see cref="EventTypeFile"/> — the catalog's own parser — rather than a second
/// reader of the same format. Same rule as the mechanic and procedure seeders: one parser per
/// format, however many sources feed it.
/// </summary>
public sealed class EventTypeSeeder(IEventTypeStore store)
{
    private const string ResourceMarker = ".EventTypes.";
    private const string SchemaSuffix = ".schema.json";
    private const string DefinitionSuffix = ".json";

    private readonly IEventTypeStore _store = store;

    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        var written = 0;

        foreach (var file in Load())
        {
            var existing = await _store.GetAsync(file.Id, cancellationToken: cancellationToken);

            // The stored fingerprint against the file's, not a re-derivation from round-tripped
            // content — the same reasoning as the other two seeders, and the same bug avoided.
            if (existing is not null && existing.SourceHash == file.ContentHash)
            {
                continue;
            }

            await _store.WriteAsync(
                new WriteEventTypeRequest
                {
                    Id = file.Id,
                    Category = file.Category,
                    Name = file.Name,
                    Description = file.Description,
                    PayloadSchema = file.Schema,
                    Scope = file.Scope,
                    Status = file.Status,
                    CreatedBy = "seed",
                    ChangeNote = existing is null
                        ? "Seeded from the embedded structural event types."
                        : "Re-seeded: the embedded structural event type changed."
                },
                cancellationToken);

            written++;
        }

        return written;
    }

    public static IReadOnlyList<EventTypeFile> Load()
    {
        var assembly = typeof(EventType).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var files = new List<EventTypeFile>();

        foreach (var name in resources.Where(IsDefinition).OrderBy(n => n, StringComparer.Ordinal))
        {
            // The schema is a sidecar, exactly as in the catalog. A definition without one is a
            // half-shipped contract, so it fails here rather than registering a type that would
            // accept any payload at all.
            var schemaName = name[..^DefinitionSuffix.Length] + SchemaSuffix;

            if (!resources.Contains(schemaName))
            {
                throw new InvalidOperationException(
                    $"The embedded event type '{name}' has no '{SchemaSuffix}' sidecar. A registered "
                    + "type with no payload schema would validate nothing.");
            }

            files.Add(EventTypeFile.Parse(Read(assembly, name), name, Read(assembly, schemaName)));
        }

        var duplicate = files.GroupBy(f => f.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Two embedded event types declare id '{duplicate.Key}'.");
        }

        return files.OrderBy(f => f.Id, StringComparer.Ordinal).ToList();
    }

    private static bool IsDefinition(string name) =>
        name.EndsWith(DefinitionSuffix, StringComparison.OrdinalIgnoreCase)
        && !name.EndsWith(SchemaSuffix, StringComparison.OrdinalIgnoreCase);

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Could not read embedded resource '{name}'.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
