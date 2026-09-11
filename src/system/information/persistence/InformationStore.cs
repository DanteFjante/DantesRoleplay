using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Content;
using DantesRoleplay.Information;
using DantesRoleplay.SchemaValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.DataAccess;

/// <summary>Neutral persistence and bounded lexical ranking for user-defined information.</summary>
public sealed class InformationStore(DantesRoleplayDbContext db) : IInformationStore
{
    private const int MaximumMetadataBytes = 8_000;
    private const int MaximumCompatibilityEvidence = 3;
    private const int MaximumCompatibilityRecords = 500;
    private const int MaximumCompatibilityMetadataBytes = 1_000_000;
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IBoundedJsonSchemaValidator _schemas = new BoundedJsonSchemaValidator();

    public async Task<InformationSourceWriteResult> WriteSourceAsync(InformationSourceWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || !Id(request.Id) || !InformationScopes.IsScope(request.ScopeId) || !Text(request.Name, 200) || !TextOrEmpty(request.Description, 1000) || !Object(request.MetadataSchemaJson, MaximumMetadataBytes))
            return new("rejected", null, "INVALID_INFORMATION_SOURCE", "Source id, scopeId, name, description, or metadataSchema is invalid.");
        var schema = _schemas.Compile(request.MetadataSchemaJson);
        if (!schema.IsAccepted)
            return new("rejected", null, "INVALID_INFORMATION_SOURCE_SCHEMA", DiagnosticMessage("The metadata schema is invalid.", schema.Diagnostics));

        await using var transaction = await BeginWriteTransactionIfNeededAsync(cancellationToken);
        var hash = ContentHash.Of(request.Id, request.ScopeId, request.Name, request.Description, request.MetadataSchemaJson);
        var existing = await _db.Set<InformationSource>().SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (existing is not null)
        {
            // A reused scoped context can be tracking a source from before this writer reservation.
            // Reload it after the reservation so compatibility is checked against committed data.
            await _db.Entry(existing).ReloadAsync(cancellationToken);
            if (existing.ContentHash == hash) return new("unchanged", existing);
            if (!string.Equals(existing.MetadataSchemaJson, request.MetadataSchemaJson, StringComparison.Ordinal))
            {
                var compatibility = await FindIncompatibleRecordsAsync(existing.Id, schema, cancellationToken);
                if (!compatibility.Complete)
                    return new("rejected", existing, "INFORMATION_SOURCE_SCHEMA_VALIDATION_BUDGET_EXCEEDED",
                        "The metadata schema change was not written because validating existing records exceeded the bounded compatibility budget.");
                if (compatibility.Evidence.Count != 0)
                    return new("rejected", existing, "INFORMATION_SOURCE_SCHEMA_INCOMPATIBLE",
                        "The metadata schema rejects existing records: " + string.Join("; ", compatibility.Evidence));
            }
            existing.ScopeId = request.ScopeId; existing.Name = request.Name; existing.Description = request.Description; existing.MetadataSchemaJson = request.MetadataSchemaJson;
            existing.ContentHash = hash; existing.Revision++; existing.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new("revised", existing);
        }
        var now = DateTime.UtcNow;
        var created = new InformationSource { Id = request.Id, ScopeId = request.ScopeId, Name = request.Name, Description = request.Description, MetadataSchemaJson = request.MetadataSchemaJson, ContentHash = hash, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        _db.Set<InformationSource>().Add(created);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new("created", created);
    }

    public async Task<InformationRecordWriteResult> WriteRecordAsync(InformationRecordWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || !Id(request.Id) || !Id(request.SourceId) || !Text(request.Title, 500) || !Text(request.Content, 16_000) || !Object(request.MetadataJson, MaximumMetadataBytes))
            return new("rejected", null, "INVALID_INFORMATION_RECORD", "Record id, sourceId, title, content, or metadata is invalid.");

        await using var transaction = await BeginWriteTransactionIfNeededAsync(cancellationToken);
        var source = await _db.Set<InformationSource>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.SourceId, cancellationToken);
        if (source is null)
            return new("rejected", null, "INFORMATION_SOURCE_NOT_FOUND", "The named information source does not exist.");
        if (!TryValidateMetadata(source.MetadataSchemaJson, request.MetadataJson, out var metadataError))
            return new("rejected", null, "INFORMATION_RECORD_METADATA_INVALID", metadataError);

        var hash = ContentHash.Of(request.Id, request.SourceId, request.Title, request.Content, request.MetadataJson);
        var existing = await _db.Set<InformationRecord>().SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (existing is not null)
        {
            // See WriteSourceAsync: compare and revise only the version current at the reservation.
            await _db.Entry(existing).ReloadAsync(cancellationToken);
            if (existing.ContentHash == hash) return new("unchanged", existing);
            existing.SourceId = request.SourceId; existing.Title = request.Title; existing.Content = request.Content; existing.MetadataJson = request.MetadataJson;
            existing.ContentHash = hash; existing.Revision++; existing.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new("revised", existing);
        }
        var now = DateTime.UtcNow;
        var created = new InformationRecord { Id = request.Id, SourceId = request.SourceId, Title = request.Title, Content = request.Content, MetadataJson = request.MetadataJson, ContentHash = hash, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        _db.Set<InformationRecord>().Add(created);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
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
    private static bool Object(string? value, int maximum) => BoundedObject(value, maximum, SystemJsonSchemaProfile.MaximumValueDepth);
    private static bool IdArray(string? value, int maximum) { try { using var json = JsonDocument.Parse(value ?? ""); return value!.Length <= maximum && json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String); } catch { return false; } }
    private static bool Schema(string? value, int maximum) { try { using var json = JsonDocument.Parse(value ?? ""); return value!.Length <= maximum && json.RootElement.ValueKind == JsonValueKind.Object; } catch { return false; } }

    private async Task<SchemaCompatibilityResult> FindIncompatibleRecordsAsync(string sourceId,
        SchemaCompilationResult schema, CancellationToken cancellationToken)
    {
        var evidence = new List<string>();
        var records = 0;
        var metadataBytes = 0;
        await foreach (var record in _db.Set<InformationRecord>().AsNoTracking()
                           .Where(value => value.SourceId == sourceId).OrderBy(value => value.Id)
                           .Select(value => new { value.Id, value.MetadataJson }).AsAsyncEnumerable()
                           .WithCancellation(cancellationToken))
        {
            records++;
            metadataBytes += Encoding.UTF8.GetByteCount(record.MetadataJson);
            if (records > MaximumCompatibilityRecords || metadataBytes > MaximumCompatibilityMetadataBytes)
                return new(false, evidence);
            if (TryValidateMetadata(schema, record.MetadataJson, out var detail)) continue;
            evidence.Add($"'{record.Id}': {detail}");
            if (evidence.Count == MaximumCompatibilityEvidence) break;
        }
        return new(true, evidence);
    }

    private bool TryValidateMetadata(string schemaJson, string metadataJson, out string error)
    {
        if (!Object(schemaJson, MaximumMetadataBytes))
        {
            error = "The source metadata schema is not a bounded JSON object.";
            return false;
        }
        var schema = _schemas.Compile(schemaJson);
        if (!schema.IsAccepted)
        {
            error = DiagnosticMessage("The source metadata schema is invalid.", schema.Diagnostics);
            return false;
        }
        return TryValidateMetadata(schema, metadataJson, out error);
    }

    private bool TryValidateMetadata(SchemaCompilationResult schema, string metadataJson, out string error)
    {
        if (!Object(metadataJson, MaximumMetadataBytes))
        {
            error = "Metadata must be a bounded JSON object with no duplicate properties.";
            return false;
        }
        var validation = _schemas.Validate(schema.ProfileId, schema.NormalizedSchema, metadataJson);
        if (validation.Status == SchemaValueStatus.Valid)
        {
            error = "";
            return true;
        }
        error = DiagnosticMessage("Metadata does not satisfy the source schema.", validation.Diagnostics);
        return false;
    }

    private async Task<InformationWriteTransaction?> BeginWriteTransactionIfNeededAsync(CancellationToken cancellationToken)
    {
        if (_db.Database.CurrentTransaction is not null) return null;
        if (_db.Database.GetDbConnection() is not SqliteConnection connection)
            return new(await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken), null);
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        var rawTransaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: false);
        try
        {
            var transaction = await _db.Database.UseTransactionAsync(rawTransaction, cancellationToken)
                ?? throw new InvalidOperationException("The information write transaction could not be enlisted.");
            return new(transaction, rawTransaction);
        }
        catch
        {
            await rawTransaction.DisposeAsync();
            throw;
        }
    }

    private static string DiagnosticMessage(string prefix, IReadOnlyList<SchemaDiagnostic> diagnostics)
    {
        var diagnostic = diagnostics.FirstOrDefault();
        if (diagnostic is null) return prefix;
        var pointer = diagnostic.Pointer.Length == 0 ? "" : $" at {diagnostic.Pointer}";
        return $"{prefix} {Truncate(diagnostic.Code, 100)}{Truncate(pointer, 200)}: {Truncate(diagnostic.Message, 500)}";
    }

    private static bool BoundedObject(string? value, int maximumBytes, int maximumDepth)
    {
        if (value is null || value.Length > maximumBytes || Encoding.UTF8.GetByteCount(value) > maximumBytes) return false;
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = maximumDepth
            });
            var propertyNames = new Stack<HashSet<string>>();
            var rootObject = false;
            var rootComplete = false;
            while (reader.Read())
            {
                if (rootComplete) return false;
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        if (propertyNames.Count == 0 && !rootObject) rootObject = true;
                        propertyNames.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.EndObject:
                        propertyNames.Pop();
                        if (propertyNames.Count == 0) rootComplete = true;
                        break;
                    case JsonTokenType.PropertyName:
                        if (propertyNames.Count == 0 || !propertyNames.Peek().Add(reader.GetString()!)) return false;
                        break;
                    case JsonTokenType.StartArray:
                        if (propertyNames.Count == 0) return false;
                        break;
                    case JsonTokenType.String:
                    case JsonTokenType.Number:
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                    case JsonTokenType.Null:
                        if (propertyNames.Count == 0) return false;
                        break;
                }
            }
            return rootObject && rootComplete && propertyNames.Count == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Truncate(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];

    private sealed record SchemaCompatibilityResult(bool Complete, IReadOnlyList<string> Evidence);

    private sealed class InformationWriteTransaction(
        IDbContextTransaction transaction,
        DbTransaction? rawTransaction) : IAsyncDisposable
    {
        private bool _completed;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await transaction.CommitAsync(cancellationToken);
            _completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed)
                await transaction.RollbackAsync(CancellationToken.None);
            await transaction.DisposeAsync();
            if (rawTransaction is not null)
                await rawTransaction.DisposeAsync();
        }
    }
}
