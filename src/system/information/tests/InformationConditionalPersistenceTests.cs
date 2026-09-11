using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Information;
using DantesRoleplay.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Information.Tests;

/// <summary>Internal persistence invariants only; source-owner authorization and public replay remain separate.</summary>
public sealed class InformationConditionalPersistenceTests
{
    [Fact]
    public async Task Conditional_record_update_retains_exact_legacy_baseline_and_rejects_stale_identical_write()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new InformationStore(db);
        await store.WriteSourceAsync(new("source", "knowledge.notes", "Notes"));
        await store.WriteRecordAsync(new("entry", "source", "Original", "Original text", "{ \"value\": 1 }"));
        await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
        await using var enlistment = await db.Database.UseTransactionAsync(transaction);
        var operation = await new OperationLog(db).RecordAsync("fixture", "Record conditional fixture.", true);
        var request = new InformationRecordWriteRequest("entry", "source", "Revised", "Retained new text", "{\"value\":2}");
        var result = await store.WriteRecordConditionallyAsync(request, 1, operation.Id);
        Assert.Equal("revised", result.Status);
        Assert.Equal("INFORMATION_REVISION_CONFLICT", (await store.WriteRecordConditionallyAsync(request, 1, operation.Id)).ErrorCode);
        var history = await db.Set<InformationContentRevisionRecord>().AsNoTracking()
            .Where(value => value.Kind == "record").OrderBy(value => value.Revision).ToArrayAsync();
        Assert.Equal(2, history.Length);
        Assert.Equal("baseline-retained", history[0].Origin);
        Assert.Equal("conditional-write", history[1].Origin);
        using var old = JsonDocument.Parse(history[0].ContentJson);
        Assert.Equal("Original text", old.RootElement.GetProperty("Content").GetString());
        Assert.Equal("{ \"value\": 1 }", old.RootElement.GetProperty("MetadataJson").GetString());
        Assert.Equal(2, (await db.Set<InformationRecord>().AsNoTracking().SingleAsync()).Revision);
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task Incompatible_source_schema_preserves_original_records_and_does_not_claim_history()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new InformationStore(db);
        await store.WriteSourceAsync(new("source", "knowledge.notes", "Notes"));
        await store.WriteRecordAsync(new("entry", "source", "Original", "Keep this", "{\"value\":\"text\"}"));
        await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
        await using var enlistment = await db.Database.UseTransactionAsync(transaction);
        var operation = await new OperationLog(db).RecordAsync("fixture", "Record conditional fixture.", true);
        var result = await store.WriteSourceConditionallyAsync(new("source", "knowledge.notes", "Notes", "",
            """{"type":"object","properties":{"value":{"type":"number"}},"required":["value"]}"""), 1, operation.Id);
        Assert.Equal("INFORMATION_SOURCE_SCHEMA_INCOMPATIBLE", result.ErrorCode);
        Assert.Empty(await db.Set<InformationContentRevisionRecord>().AsNoTracking().ToArrayAsync());
        Assert.Equal("{}", (await db.Set<InformationSource>().AsNoTracking().SingleAsync()).MetadataSchemaJson);
        Assert.Equal("{\"value\":\"text\"}", (await db.Set<InformationRecord>().AsNoTracking().SingleAsync()).MetadataJson);
    }

    [Fact]
    public async Task Owning_transaction_rollback_restores_current_content_and_discards_history_and_receipt()
    {
        using var fixture = new SqliteFixture();
        await using (var db = fixture.CreateContext())
        {
            var store = new InformationStore(db);
            await store.WriteSourceAsync(new("source", "knowledge.notes", "Notes"));
            await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
            await using var enlistment = await db.Database.UseTransactionAsync(transaction);
            var operation = await new OperationLog(db).RecordAsync("fixture", "Record conditional fixture.", true);
            Assert.Equal("revised", (await store.WriteSourceConditionallyAsync(new("source", "knowledge.notes", "Changed"), 1, operation.Id)).Status);
            await transaction.RollbackAsync();
        }
        await using var reopened = fixture.CreateContext();
        Assert.Equal("Notes", (await reopened.Set<InformationSource>().AsNoTracking().SingleAsync()).Name);
        Assert.Empty(await reopened.Set<InformationContentRevisionRecord>().AsNoTracking().ToArrayAsync());
        Assert.Empty(await reopened.Operations.AsNoTracking().ToArrayAsync());
    }
}
