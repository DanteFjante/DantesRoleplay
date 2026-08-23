using System.Text.Json;
using DantesRoleplay.Content;
using DantesRoleplay.Information;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Neutral persistence and bounded lexical ranking for user-defined information.</summary>
public sealed class InformationStore(DantesRoleplayDbContext db) : IInformationStore
{
    private readonly DantesRoleplayDbContext _db = db;

    public async Task<InformationSourceWriteResult> WriteSourceAsync(InformationSourceWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || !Id(request.Id) || !InformationScopes.IsScope(request.ScopeId) || !Text(request.Name, 200) || !TextOrEmpty(request.Description, 1000) || !Object(request.MetadataSchemaJson, 8_000))
            return new("rejected", null, "INVALID_INFORMATION_SOURCE", "Source id, scopeId, name, description, or metadataSchema is invalid.");
        var hash = ContentHash.Of(request.Id, request.ScopeId, request.Name, request.Description, request.MetadataSchemaJson);
        var existing = await _db.Set<InformationSource>().SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.ContentHash == hash) return new("unchanged", existing);
            existing.ScopeId = request.ScopeId; existing.Name = request.Name; existing.Description = request.Description; existing.MetadataSchemaJson = request.MetadataSchemaJson;
            existing.ContentHash = hash; existing.Revision++; existing.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return new("revised", existing);
        }
        var now = DateTime.UtcNow;
        var created = new InformationSource { Id = request.Id, ScopeId = request.ScopeId, Name = request.Name, Description = request.Description, MetadataSchemaJson = request.MetadataSchemaJson, ContentHash = hash, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        _db.Set<InformationSource>().Add(created);
        await _db.SaveChangesAsync(cancellationToken);
        return new("created", created);
    }

    public async Task<InformationRecordWriteResult> WriteRecordAsync(InformationRecordWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || !Id(request.Id) || !Id(request.SourceId) || !Text(request.Title, 500) || !Text(request.Content, 16_000) || !Object(request.MetadataJson, 8_000))
            return new("rejected", null, "INVALID_INFORMATION_RECORD", "Record id, sourceId, title, content, or metadata is invalid.");
        if (!await _db.Set<InformationSource>().AnyAsync(x => x.Id == request.SourceId, cancellationToken))
            return new("rejected", null, "INFORMATION_SOURCE_NOT_FOUND", "The named information source does not exist.");
        var hash = ContentHash.Of(request.Id, request.SourceId, request.Title, request.Content, request.MetadataJson);
        var existing = await _db.Set<InformationRecord>().SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.ContentHash == hash) return new("unchanged", existing);
            existing.SourceId = request.SourceId; existing.Title = request.Title; existing.Content = request.Content; existing.MetadataJson = request.MetadataJson;
            existing.ContentHash = hash; existing.Revision++; existing.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return new("revised", existing);
        }
        var now = DateTime.UtcNow;
        var created = new InformationRecord { Id = request.Id, SourceId = request.SourceId, Title = request.Title, Content = request.Content, MetadataJson = request.MetadataJson, ContentHash = hash, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        _db.Set<InformationRecord>().Add(created);
        await _db.SaveChangesAsync(cancellationToken);
        return new("created", created);
    }

    public async Task<InformationActionContractWriteResult> WriteActionContractAsync(InformationActionContractWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || !Id(request.Id) || !InformationScopes.IsSelector(request.ScopeId) || !Text(request.Name, 200) || !TextOrEmpty(request.Description, 1000) || !Id(request.ExecutorId) || !Schema(request.InputSchemaJson, 8_000) || !IdArray(request.RuleRecordIdsJson, 8_000))
            return new("rejected", null, "INVALID_INFORMATION_ACTION_CONTRACT", "Contract id, scopeId, name, executorId, input schema, or rule record ids are invalid.");
        var ruleIds = JsonSerializer.Deserialize<string[]>(request.RuleRecordIdsJson)!;
        if (ruleIds.Length > 40 || ruleIds.Distinct(StringComparer.Ordinal).Count() != ruleIds.Length || !ruleIds.All(Id)) return new("rejected", null, "INVALID_INFORMATION_ACTION_CONTRACT", "Rule record ids must be a bounded array of distinct valid ids.");
        if (ruleIds.Length > 0)
        {
            var ruleRecords = await _db.Set<InformationRecord>().AsNoTracking().Include(x => x.Source).Where(x => ruleIds.Contains(x.Id)).ToListAsync(cancellationToken);
            if (ruleRecords.Count != ruleIds.Length || ruleRecords.Any(x => !InformationScopes.Matches(request.ScopeId, x.Source.ScopeId)))
                return new("rejected", null, "INFORMATION_ACTION_RULE_RECORD_INVALID", "Every rule record must exist within the contract namespace.");
        }
        var hash = ContentHash.Of(request.Id, request.ScopeId, request.Name, request.Description, request.ExecutorId, request.InputSchemaJson, request.RuleRecordIdsJson);
        var existing = await _db.Set<InformationActionContract>().SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.ContentHash == hash) return new("unchanged", existing);
            existing.ScopeId = request.ScopeId; existing.Name = request.Name; existing.Description = request.Description; existing.ExecutorId = request.ExecutorId; existing.InputSchemaJson = request.InputSchemaJson; existing.RuleRecordIdsJson = request.RuleRecordIdsJson; existing.ContentHash = hash; existing.Revision++; existing.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return new("revised", existing);
        }
        var now = DateTime.UtcNow;
        var created = new InformationActionContract { Id = request.Id, ScopeId = request.ScopeId, Name = request.Name, Description = request.Description, ExecutorId = request.ExecutorId, InputSchemaJson = request.InputSchemaJson, RuleRecordIdsJson = request.RuleRecordIdsJson, ContentHash = hash, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        _db.Set<InformationActionContract>().Add(created);
        await _db.SaveChangesAsync(cancellationToken);
        return new("created", created);
    }

    public async Task<IReadOnlyList<InformationCandidate>> SearchAsync(string scopeId, string question, IReadOnlyList<string>? sourceIds, int limit, CancellationToken cancellationToken = default)
    {
        IQueryable<InformationRecord> records = _db.Set<InformationRecord>().AsNoTracking().Include(x => x.Source);
        var scopePrefix = scopeId.EndsWith(".*", StringComparison.Ordinal) ? scopeId[..^1] : string.Empty;
        records = scopeId.EndsWith(".*", StringComparison.Ordinal)
            ? records.Where(x => x.Source.ScopeId.StartsWith(scopePrefix))
            : records.Where(x => x.Source.ScopeId == scopeId);
        if (sourceIds is { Count: > 0 }) records = records.Where(x => sourceIds.Contains(x.SourceId));
        var terms = question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
        return (await records.Take(500).ToListAsync(cancellationToken)).Select(record => new
            {
                Record = record,
                Score = terms.Sum(term => Count(record.Title, term) * 4 + Count(record.Content, term))
            })
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Record.Id, StringComparer.Ordinal).Take(limit)
            .Select((x, index) => new InformationCandidate(x.Record.Id, x.Record.SourceId, x.Record.Title, x.Record.Content, x.Record.ContentHash, x.Record.Revision, index + 1)).ToArray();
    }

    public async Task<IReadOnlyList<InformationActionContract>> FindActionContractsAsync(string scopeSelector, CancellationToken cancellationToken = default)
    {
        var contracts = await _db.Set<InformationActionContract>().AsNoTracking().OrderBy(x => x.Id).Take(100).ToArrayAsync(cancellationToken);
        return contracts.Where(x => InformationScopes.Overlaps(scopeSelector, x.ScopeId)).ToArray();
    }

    public async Task<InformationActionContract?> GetActionContractAsync(string scopeSelector, string contractId, CancellationToken cancellationToken = default) =>
        (await FindActionContractsAsync(scopeSelector, cancellationToken)).SingleOrDefault(x => x.Id == contractId);

    private static int Count(string text, string term) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(x => string.Equals(x.Trim(' ', '.', ',', ';', ':', '!', '?', '"', '\''), term, StringComparison.OrdinalIgnoreCase));
    private static bool Id(string? value) => Text(value, 200) && !value!.Any(char.IsWhiteSpace);
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
    private static bool TextOrEmpty(string? value, int maximum) => value is not null && value == value.Trim() && value.Length <= maximum;
    private static bool Object(string? value, int maximum) { try { using var json = JsonDocument.Parse(value ?? ""); return value!.Length <= maximum && json.RootElement.ValueKind == JsonValueKind.Object; } catch { return false; } }
    private static bool IdArray(string? value, int maximum) { try { using var json = JsonDocument.Parse(value ?? ""); return value!.Length <= maximum && json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String); } catch { return false; } }
    private static bool Schema(string? value, int maximum) { try { using var json = JsonDocument.Parse(value ?? ""); return value!.Length <= maximum && json.RootElement.ValueKind == JsonValueKind.Object; } catch { return false; } }
}
