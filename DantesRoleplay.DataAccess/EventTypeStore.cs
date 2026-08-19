using System.Text.Json;
using DantesRoleplay.Content;
using DantesRoleplay.Events;
using Microsoft.EntityFrameworkCore;
using Json.Schema;

namespace DantesRoleplay.DataAccess;

public sealed class EventTypeStore(DantesRoleplayDbContext db) : IEventTypeStore
{
    private readonly DantesRoleplayDbContext _db = db;
    public async Task<IReadOnlyList<EventTypeSummary>> FindAsync(string? query = null, string? category = null, string? scope = null, bool includeInactive = false, int limit = 50, CancellationToken cancellationToken = default)
    {
        var rows = _db.EventTypes.Join(_db.EventTypeVersions, e => new { EventTypeId = e.Id, Version = e.CurrentVersion }, v => new { v.EventTypeId, v.Version }, (e, v) => new { e, v });
        if (!includeInactive) rows = rows.Where(x => x.e.Status != EventTypeStatus.Archived);
        if (!string.IsNullOrWhiteSpace(category)) rows = rows.Where(x => x.e.Category == category || x.e.Category.StartsWith(category + "."));
        if (!string.IsNullOrWhiteSpace(scope)) rows = rows.Where(x => x.e.Scope == scope || x.e.Scope == "");
        var list = await rows.OrderBy(x => x.e.Id).Select(x => new EventTypeSummary(x.e.Id, x.e.Category, x.v.Name, x.v.Description, x.e.Scope, x.e.Status, x.e.CurrentVersion)).ToListAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(query)) return list.Take(limit).ToList();
        return list.Where(x => ($"{x.Id} {x.Name} {x.Description}").Contains(query, StringComparison.OrdinalIgnoreCase)).Take(limit).ToList();
    }
    public async Task<EventTypeDetail?> GetAsync(string id, int? version = null, CancellationToken cancellationToken = default)
    {
        var type = await _db.EventTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken); if (type is null) return null;
        var wanted = version ?? type.CurrentVersion; var row = await _db.EventTypeVersions.AsNoTracking().FirstOrDefaultAsync(x => x.EventTypeId == id && x.Version == wanted, cancellationToken); if (row is null) return null;
        var latest = await _db.EventTypeVersions.Where(x => x.EventTypeId == id).MaxAsync(x => (int?)x.Version, cancellationToken) ?? type.CurrentVersion;
        return Detail(type, row, latest);
    }
    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) => _db.EventTypes.AnyAsync(x => x.Id == id, cancellationToken);
    public async Task<IReadOnlyList<EventTypeCheck>> CheckAsync(WriteEventTypeRequest request, CancellationToken cancellationToken = default)
    {
        var checks = new List<EventTypeCheck>();
        var idOk = System.Text.RegularExpressions.Regex.IsMatch(request.Id ?? "", "^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$") && request.Id.Length is >= 3 and <= 200;
        checks.Add(new("id-format", idOk, idOk ? "Permanent lower dotted id." : "Id must be 3–200 lowercase dotted segments (e.g. world.entity.created)."));
        var schemaOk = false; var schemaDetail = "Payload schema is not valid JSON Schema Draft 2020-12.";
        try { using var document = JsonDocument.Parse(request.PayloadSchema); schemaOk = document.RootElement.ValueKind == JsonValueKind.Object; if (schemaOk) _ = JsonSchema.FromText(request.PayloadSchema); schemaDetail = schemaOk ? "Payload schema is a JSON Schema Draft 2020-12 object." : "Payload schema must have an object root."; } catch (Exception ex) when (ex is JsonException or JsonSchemaException) { schemaDetail = $"Payload schema is invalid: {ex.Message}"; }
        checks.Add(new("payload-schema", schemaOk, schemaDetail));
        var old = await GetAsync(request.Id, cancellationToken: cancellationToken);
        checks.Add(new("create-or-revise", true, old is null ? "Creates version 1." : $"Appends version {old.LatestVersion + 1}."));
        checks.Add(new("change-note", old is null || !string.IsNullOrWhiteSpace(request.ChangeNote), old is null || !string.IsNullOrWhiteSpace(request.ChangeNote) ? "Change note present when revising." : "A nonempty changeNote is required when revising."));
        return checks;
    }
    public async Task<WriteEventTypeResult> WriteAsync(WriteEventTypeRequest request, CancellationToken cancellationToken = default)
    {
        var checks = await CheckAsync(request, cancellationToken); var failure = checks.FirstOrDefault(x => x.Blocking && !x.Passed); if (failure is not null) throw new ArgumentException(failure.Detail);
        var now = DateTime.UtcNow; var type = await _db.EventTypes.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken); var created = type is null;
        if (type is null) { type = new EventType { Id = request.Id, Category = request.Category, Scope = request.Scope, Status = request.Status ?? EventTypeStatus.Draft, CreatedAt = now, UpdatedAt = now }; _db.EventTypes.Add(type); }
        else { type.Category = request.Category; type.Scope = request.Scope; type.UpdatedAt = now; if (request.Status is { } status) type.Status = status; }
        var version = (await _db.EventTypeVersions.Where(x => x.EventTypeId == request.Id).MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var row = new EventTypeVersion { EventTypeId = type.Id, Version = version, Name = request.Name, Description = request.Description, PayloadSchema = request.PayloadSchema.Trim(), ChangeNote = request.ChangeNote, CreatedBy = string.IsNullOrWhiteSpace(request.CreatedBy) ? "llm" : request.CreatedBy, SourceHash = ContentHash.Of(type.Category, request.Name, request.Description, request.PayloadSchema, type.Scope, type.Status.ToString()), CreatedAt = now };
        _db.EventTypeVersions.Add(row); type.CurrentVersion = version; await _db.SaveChangesAsync(cancellationToken); return new(Detail(type, row, version), created);
    }
    private static EventTypeDetail Detail(EventType type, EventTypeVersion row, int latest) => new(type.Id, type.Category, row.Name, row.Description, row.PayloadSchema, type.Scope, type.Status, row.Version, latest, row.CreatedBy, row.ChangeNote, row.CreatedAt) { SourceHash = row.SourceHash };
}
