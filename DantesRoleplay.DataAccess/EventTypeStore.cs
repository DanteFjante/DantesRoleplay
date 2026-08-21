using System.Text.Json;
using DantesRoleplay.Content;
using DantesRoleplay.Events;
using Json.Schema;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Append-only storage for the schemas that name events. Routing deliberately lives elsewhere:
/// this class establishes what an event means, while the routers decide which rules answer it.
/// </summary>
public sealed class EventTypeStore(DantesRoleplayDbContext db) : IEventTypeStore
{
    private readonly DantesRoleplayDbContext _db = db;

    public async Task<IReadOnlyList<EventTypeSummary>> FindAsync(
        string? query = null,
        string? category = null,
        string? scope = null,
        bool includeInactive = false,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var rows = _db.EventTypes.Join(
            _db.EventTypeVersions,
            type => new { EventTypeId = type.Id, Version = type.CurrentVersion },
            version => new { version.EventTypeId, version.Version },
            (type, version) => new { type, version });

        if (!includeInactive)
        {
            rows = rows.Where(row => row.type.Status != EventTypeStatus.Archived);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            rows = rows.Where(row => row.type.Category == category || row.type.Category.StartsWith(category + "."));
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            rows = rows.Where(row => row.type.Scope == scope || row.type.Scope == "");
        }

        var results = await rows
            .OrderBy(row => row.type.Id)
            .Select(row => new EventTypeSummary(
                row.type.Id,
                row.type.Category,
                row.version.Name,
                row.version.Description,
                row.type.Scope,
                row.type.Status,
                row.type.CurrentVersion))
            .ToListAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(query))
        {
            return results.Take(limit).ToList();
        }

        return results
            .Where(result => $"{result.Id} {result.Name} {result.Description}"
                .Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();
    }

    public async Task<EventTypeDetail?> GetAsync(
        string id,
        int? version = null,
        CancellationToken cancellationToken = default)
    {
        var type = await _db.EventTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (type is null)
        {
            return null;
        }

        var wantedVersion = version ?? type.CurrentVersion;
        var row = await _db.EventTypeVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.EventTypeId == id && candidate.Version == wantedVersion,
                cancellationToken);

        if (row is null)
        {
            return null;
        }

        var latestVersion = await _db.EventTypeVersions
            .Where(candidate => candidate.EventTypeId == id)
            .MaxAsync(candidate => (int?)candidate.Version, cancellationToken)
            ?? type.CurrentVersion;

        return Detail(type, row, latestVersion);
    }

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) =>
        _db.EventTypes.AnyAsync(type => type.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EventTypeCheck>> CheckAsync(
        WriteEventTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<EventTypeCheck>();
        var id = request.Id ?? string.Empty;
        var payloadSchema = request.PayloadSchema ?? string.Empty;
        var idOk = System.Text.RegularExpressions.Regex.IsMatch(
                id,
                "^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$")
            && id.Length is >= 3 and <= 200;

        checks.Add(new EventTypeCheck(
            "id-format",
            idOk,
            idOk
                ? "Permanent lower dotted id."
                : "Id must be 3–200 lowercase dotted segments (e.g. world.entity.created)."));

        var schemaOk = false;
        var schemaDetail = "Payload schema is not valid JSON Schema Draft 2020-12.";

        try
        {
            using var document = JsonDocument.Parse(payloadSchema);
            schemaOk = document.RootElement.ValueKind == JsonValueKind.Object;

            if (schemaOk)
            {
                _ = JsonSchema.FromText(EventPayloadRoleMetadata.WithoutExtension(payloadSchema));
                schemaDetail = "Payload schema is a JSON Schema Draft 2020-12 object.";
            }
            else
            {
                schemaDetail = "Payload schema must have an object root.";
            }
        }
        catch (Exception exception) when (exception is JsonException or JsonSchemaException)
        {
            schemaDetail = $"Payload schema is invalid: {exception.Message}";
        }

        checks.Add(new EventTypeCheck("payload-schema", schemaOk, schemaDetail));

        var metadataOk = EventPayloadRoleMetadata.TryRead(payloadSchema, out _, out var metadataProblem);
        checks.Add(new EventTypeCheck(
            "entity-payload-fields",
            metadataOk,
            metadataOk ? "Entity payload field metadata is absent or valid." : metadataProblem));

        var old = await GetAsync(id, cancellationToken: cancellationToken);
        checks.Add(new EventTypeCheck(
            "create-or-revise",
            true,
            old is null ? "Creates version 1." : $"Appends version {old.LatestVersion + 1}."));

        var hasChangeNote = old is null || !string.IsNullOrWhiteSpace(request.ChangeNote);
        checks.Add(new EventTypeCheck(
            "change-note",
            hasChangeNote,
            hasChangeNote
                ? "Change note present when revising."
                : "A nonempty changeNote is required when revising."));

        return checks;
    }

    public async Task<WriteEventTypeResult> WriteAsync(
        WriteEventTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        var checks = await CheckAsync(request, cancellationToken);
        var failure = checks.FirstOrDefault(check => check.Blocking && !check.Passed);

        if (failure is not null)
        {
            throw new ArgumentException(failure.Detail);
        }

        var now = DateTime.UtcNow;
        var type = await _db.EventTypes
            .FirstOrDefaultAsync(candidate => candidate.Id == request.Id, cancellationToken);
        var created = type is null;

        if (type is null)
        {
            type = new EventType
            {
                Id = request.Id,
                Category = request.Category,
                Scope = request.Scope,
                Status = request.Status ?? EventTypeStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.EventTypes.Add(type);
        }
        else
        {
            type.Category = request.Category;
            type.Scope = request.Scope;
            type.UpdatedAt = now;

            if (request.Status is { } status)
            {
                type.Status = status;
            }
        }

        var version = (await _db.EventTypeVersions
            .Where(candidate => candidate.EventTypeId == request.Id)
            .MaxAsync(candidate => (int?)candidate.Version, cancellationToken) ?? 0) + 1;

        var row = new EventTypeVersion
        {
            EventTypeId = type.Id,
            Version = version,
            Name = request.Name,
            Description = request.Description,
            PayloadSchema = request.PayloadSchema.Trim(),
            ChangeNote = request.ChangeNote,
            CreatedBy = string.IsNullOrWhiteSpace(request.CreatedBy) ? "llm" : request.CreatedBy,
            SourceHash = ContentHash.Of(
                type.Category,
                request.Name,
                request.Description,
                request.PayloadSchema,
                type.Scope,
                type.Status.ToString()),
            CreatedAt = now
        };

        _db.EventTypeVersions.Add(row);
        type.CurrentVersion = version;
        await _db.SaveChangesAsync(cancellationToken);

        return new WriteEventTypeResult(Detail(type, row, version), created);
    }

    private static EventTypeDetail Detail(EventType type, EventTypeVersion row, int latestVersion) =>
        new(
            type.Id,
            type.Category,
            row.Name,
            row.Description,
            row.PayloadSchema,
            type.Scope,
            type.Status,
            row.Version,
            latestVersion,
            row.CreatedBy,
            row.ChangeNote,
            row.CreatedAt)
        {
            SourceHash = row.SourceHash
        };
}
