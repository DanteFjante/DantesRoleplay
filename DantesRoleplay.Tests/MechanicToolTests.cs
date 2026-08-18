using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Mechanics;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class MechanicToolTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Dry_run_does_not_write_and_commit_is_audited()
    {
        await using var db = _fixture.CreateContext();
        var tool = new MechanicTools();

        var dryRun = await tool.WriteMechanicAsync(
            new MechanicStore(db),
            new OperationLog(db),
            "mechanic.check.noop",
            "check",
            "No-op check",
            "A harmless check.",
            "noop",
            "{}",
            "return { narration: 'ok', effects: [] };",
            status: "active",
            proceduresUsed: ["procedure.mechanic.create"],
            dryRun: true);

        Assert.True(dryRun.Ok, Json(dryRun));
        Assert.Empty(await new MechanicStore(db).FindAsync());
        Assert.False(db.Operations.Single().ConsumedReadEvidence);

        var committed = await tool.WriteMechanicAsync(
            new MechanicStore(db),
            new OperationLog(db),
            "mechanic.check.noop",
            "check",
            "No-op check",
            "A harmless check.",
            "noop",
            "{}",
            "return { narration: 'ok', effects: [] };",
            status: "active",
            proceduresUsed: ["procedure.mechanic.create"]);

        Assert.True(committed.Ok, Json(committed));
        var stored = await new MechanicStore(db).GetAsync("mechanic.check.noop");
        Assert.NotNull(stored);
        Assert.Equal(MechanicStatus.Active, stored.Status);
        Assert.Equal(2, await db.Operations.CountAsync(o => o.Tool == "write_mechanic"));
        Assert.True((await db.Operations.OrderByDescending(o => o.Timestamp).FirstAsync()).Success);
    }

    [Fact]
    public async Task Find_mechanics_reads_full_and_historical_versions()
    {
        await using var db = _fixture.CreateContext();
        var tool = new MechanicTools();
        var store = new MechanicStore(db);

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.check.versioned",
            Category = "check",
            Name = "Versioned check",
            Matches = "versioned check",
            Source = "return { narration: 'v1', effects: [] };",
            Status = MechanicStatus.Active
        });

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.check.versioned",
            Category = "check",
            Name = "Versioned check",
            Matches = "versioned check",
            Source = "return { narration: 'v2', effects: [] };"
        });

        var historical = await tool.FindMechanicsAsync(
            store,
            new OperationLog(db),
            id: "mechanic.check.versioned",
            version: 1);
        var current = await tool.FindMechanicsAsync(
            store,
            new OperationLog(db),
            id: "mechanic.check.versioned");

        Assert.True(historical.Ok, Json(historical));
        Assert.Contains("v1", Json(historical));
        Assert.True(current.Ok, Json(current));
        Assert.Contains("v2", Json(current));
    }

    [Fact]
    public async Task Blocking_checks_refuse_a_commit_and_near_duplicates_are_warnings()
    {
        await using var db = _fixture.CreateContext();
        var tool = new MechanicTools();
        var store = new MechanicStore(db);

        var invalid = await tool.WriteMechanicAsync(
            store,
            new OperationLog(db),
            "mechanic.check.invalid",
            "check",
            "Invalid check",
            "Broken requirements.",
            "broken",
            "{not json",
            "return { effects: [] };",
            dryRun: false);

        Assert.False(invalid.Ok);
        Assert.Equal("INVALID_MECHANIC", invalid.Error?.Code);
        Assert.Empty(await store.FindAsync());

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.check.original",
            Category = "check",
            Name = "Ability check",
            Matches = "ability check",
            Source = "return { effects: [] };",
            Status = MechanicStatus.Active
        });

        var duplicate = await tool.WriteMechanicAsync(
            store,
            new OperationLog(db),
            "mechanic.check.variant",
            "check",
            "Ability check variant",
            "Another ability check.",
            "ability check",
            "{}",
            "return { effects: [] };",
            dryRun: true);

        Assert.True(duplicate.Ok, Json(duplicate));
        Assert.Contains("no-near-duplicate", Json(duplicate.Data));
        Assert.Contains("false", Json(duplicate.Data));
    }

    [Fact]
    public async Task Invalid_status_returns_a_copyable_correction()
    {
        await using var db = _fixture.CreateContext();
        var result = await new MechanicTools().WriteMechanicAsync(
            new MechanicStore(db),
            new OperationLog(db),
            "mechanic.check.status",
            "check",
            "Status check",
            "Status handling.",
            "status check",
            "{}",
            "return { effects: [] };",
            status: "experimental");

        Assert.False(result.Ok);
        Assert.Equal("INVALID_STATUS", result.Error?.Code);
        Assert.Contains("commit(kind: \"mechanic\", payload:", result.Error?.Fix);
        Assert.Contains("status", result.Error?.Fix);
        Assert.Single(await db.Operations.Where(o => o.Tool == "write_mechanic").ToListAsync());
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value);
}
