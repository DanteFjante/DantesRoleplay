using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Information;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

public sealed partial class InformationStore
{
    // Internal persistence primitive only. Its compiled caller owns the immediate writer,
    // source ownership/grant revalidation, command replay, and operation receipt. The public
    // unchecked InformationStore ABI cannot accidentally enter this conditional path.
    internal async Task<InformationSourceWriteResult> WriteSourceConditionallyAsync(
        InformationSourceWriteRequest request, int expectedRevision, string operationId, CancellationToken cancellationToken = default)
    {
        RequireConditionalTransaction(expectedRevision);
        var before = await _db.Set<InformationSource>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == request.Id, cancellationToken);
        if ((before?.Revision ?? 0) != expectedRevision)
            return new("rejected", null, "INFORMATION_REVISION_CONFLICT", "The source revision changed.");
        var previousJson = before is null ? null : SourceJson(before);
        var result = await WriteSourceAsync(request, cancellationToken);
        if (result.Status == "rejected") return result;
        var after = await _db.Set<InformationSource>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == request.Id, cancellationToken)
            ?? throw new InvalidOperationException("The information source write did not retain its result.");
        if (after.Revision != (result.Status == "unchanged" ? expectedRevision : checked(expectedRevision + 1)))
            throw new InvalidOperationException("The information source write returned an inconsistent revision.");
        if (before is not null)
            await RetainInformationAsync("source", before.Id, before.Revision, previousJson!, operationId, "baseline-retained", cancellationToken);
        await RetainInformationAsync("source", after.Id, after.Revision, SourceJson(after), operationId,
            result.Status == "unchanged" ? "baseline-retained" : "conditional-write", cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    internal async Task<InformationRecordWriteResult> WriteRecordConditionallyAsync(
        InformationRecordWriteRequest request, int expectedRevision, string operationId, CancellationToken cancellationToken = default)
    {
        RequireConditionalTransaction(expectedRevision);
        var before = await _db.Set<InformationRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == request.Id, cancellationToken);
        if ((before?.Revision ?? 0) != expectedRevision)
            return new("rejected", null, "INFORMATION_REVISION_CONFLICT", "The record revision changed.");
        var previousJson = before is null ? null : RecordJson(before);
        var source = await _db.Set<InformationSource>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == request.SourceId, cancellationToken);
        if (source is not null)
            await RetainInformationAsync("source", source.Id, source.Revision, SourceJson(source), operationId,
                "baseline-retained", cancellationToken);
        var result = await WriteRecordAsync(request, cancellationToken);
        if (result.Status == "rejected") return result;
        var after = await _db.Set<InformationRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == request.Id, cancellationToken)
            ?? throw new InvalidOperationException("The information record write did not retain its result.");
        if (after.Revision != (result.Status == "unchanged" ? expectedRevision : checked(expectedRevision + 1)))
            throw new InvalidOperationException("The information record write returned an inconsistent revision.");
        if (before is not null)
            await RetainInformationAsync("record", before.Id, before.Revision, previousJson!, operationId, "baseline-retained", cancellationToken);
        await RetainInformationAsync("record", after.Id, after.Revision, RecordJson(after), operationId,
            result.Status == "unchanged" ? "baseline-retained" : "conditional-write", cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    private void RequireConditionalTransaction(int expectedRevision)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Conditional information writes require their owner's writer transaction.");
        if (expectedRevision is < 0 or int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
    }

    private async Task RetainInformationAsync(string kind, string id, int revision, string content,
        string operationId, string origin, CancellationToken cancellationToken)
    {
        var fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/information-content-revision/v1", content);
        var prior = await _db.Set<InformationContentRevisionRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Kind == kind && value.Id == id && value.Revision == revision, cancellationToken);
        // Earlier pending retention of an unchanged baseline is the same immutable row.
        var pending = _db.Set<InformationContentRevisionRecord>().Local
            .SingleOrDefault(value => value.Kind == kind && value.Id == id && value.Revision == revision);
        prior ??= pending;
        if (prior is not null)
        {
            var priorFingerprint = InteractionCanonicalJson.Fingerprint(
                "dantes-roleplay/information-content-revision/v1", prior.ContentJson);
            if (prior.ContentFingerprint != priorFingerprint
                || prior.ContentJson != content && !LegacyRecordMatches(prior.ContentJson, content))
                throw new InvalidOperationException("Information history no longer matches its immutable revision.");
            return;
        }
        _db.Add(new InformationContentRevisionRecord
        {
            Kind = kind, Id = id, Revision = revision, ContentJson = content, ContentFingerprint = fingerprint,
            RetainedByOperationId = operationId, Origin = origin
        });
    }

    private static string SourceJson(InformationSource value) => value.MetadataSchemaQualifiedId is null
        ? InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            kind = "source", value.Id, value.Revision, value.ScopeId, value.Name, value.Description,
            value.MetadataSchemaJson, value.ContentHash, value.CreatedAtUtc, value.UpdatedAtUtc
        }))
        : InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            kind = "source", value.Id, value.Revision, value.ScopeId, value.Name, value.Description,
            value.MetadataSchemaJson, value.MetadataSchemaQualifiedId, value.MetadataSchemaVersion,
            value.MetadataSchemaHash, value.ContentHash, value.CreatedAtUtc, value.UpdatedAtUtc
        }));
    private static string RecordJson(InformationRecord value) => InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
    {
        kind = "record", value.Id, value.Revision, value.SourceId, value.Title, value.Content,
        value.MetadataJson, value.MetadataSchemaSourceRevision, value.ContentHash, value.CreatedAtUtc, value.UpdatedAtUtc
    }));

    private static bool LegacyRecordMatches(string retained, string current)
    {
        try
        {
            var legacy = JsonNode.Parse(retained) as JsonObject;
            var proposed = JsonNode.Parse(current) as JsonObject;
            if (legacy is null || proposed is null || legacy.ContainsKey("MetadataSchemaSourceRevision")) return false;
            proposed.Remove("MetadataSchemaSourceRevision");
            return JsonNode.DeepEquals(legacy, proposed);
        }
        catch (JsonException) { return false; }
    }
}
