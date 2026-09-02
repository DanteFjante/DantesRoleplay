using System.Text.Json;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.Mechanics;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class MechanicHandlerTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Dry_run_does_not_write_and_commit_is_audited()
    {
        await using var db = _fixture.CreateContext();
        var tool = new MechanicHandler();

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
        var tool = new MechanicHandler();
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
        var tool = new MechanicHandler();
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

    /// <summary>
    /// A namespace refusal is thrown during SaveChanges, deep inside the store, and the store
    /// shares its DbContext with the audit log — so the rejected mechanic stayed tracked as Added
    /// and the audit row's own SaveChanges threw the same refusal again. That second throw escaped
    /// the tool wrapper, and the caller got an unstructured tool-invocation error with no code, no
    /// fix and no audit row, for a rejection that had a perfectly good code all along. A session
    /// spent an afternoon ruling out payload size, category, status and near-duplicates against
    /// what was really "that namespace is not registered".
    /// </summary>
    [Fact]
    public async Task An_unregistered_namespace_is_refused_with_its_own_code_not_an_opaque_failure()
    {
        await using var db = _fixture.CreateContext();
        new SqliteCatalogNamespaceRegistry(db).Register(new CatalogNamespaceRegistration(
            "mechanic", "mechanic", "Registered mechanic namespace.", [CatalogNamespaceKinds.Mechanic],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed fixture."));

        var result = await new MechanicHandler().WriteMechanicAsync(
            new MechanicStore(db),
            new OperationLog(db),
            "mechanic.game.core.world.quest.register",
            "quest",
            "Register a quest",
            "Creates one quest.",
            "register a quest",
            "{}",
            "return { effects: [] };");

        Assert.False(result.Ok, Json(result));
        Assert.NotNull(result.Error);
        Assert.Equal("NAMESPACE_UNKNOWN", result.Error!.Code);
        Assert.Contains("mechanic.game.core.world.quest", result.Error.Why, StringComparison.Ordinal);
        Assert.NotEmpty(result.Error.Fix);
    }

    [Fact]
    public async Task Invalid_status_returns_a_copyable_correction()
    {
        await using var db = _fixture.CreateContext();
        var result = await new MechanicHandler().WriteMechanicAsync(
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
