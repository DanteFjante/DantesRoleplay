using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.MCPServer.Tools;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class VerbToolTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Query_dispatches_procedure_listing_and_records_the_public_verb()
    {
        await using var db = _fixture.CreateContext();
        // Named, not positional. DI supplies these by type at runtime, so the order here is an
        // accident of the signature — and inserting a store anywhere but the end used to break
        // this call silently until compile. See KNOWN_ISSUES.
        var result = await new QueryTool().QueryAsync(
            procedures: new ProcedureStore(db),
            world: new WorldStore(db),
            mechanics: new MechanicStore(db),
            eventTypes: new EventTypeStore(db),
            subscriptions: new SubscriptionStore(db),
            events: new EventLedger(db),
            log: new OperationLog(db),
            notifications: new NotificationStore(db),
            kind: "procedures");

        Assert.True(result.Ok, JsonSerializer.Serialize(result));
        Assert.Single(await db.Operations.Where(o => o.Tool == "query").ToListAsync());
        Assert.Empty(await db.Operations.Where(o => o.Tool == "find_procedures").ToListAsync());
    }

    [Fact]
    public async Task Commit_dry_run_validates_a_procedure_without_writing_and_records_commit()
    {
        await using var db = _fixture.CreateContext();
        var payload = JsonSerializer.Serialize(new
        {
            id = "procedure.test.verb",
            category = "test",
            name = "Verb test",
            description = "A procedure used to verify the commit adapter.",
            instructions = "1. Read the result."
        });

        var result = await new CommitTool().CommitAsync(
            procedures: new ProcedureStore(db),
            world: new WorldStore(db),
            effects: null!,
            mechanics: new MechanicStore(db),
            eventTypes: new EventTypeStore(db),
            subscriptions: new SubscriptionStore(db),
            actions: null!,
            log: new OperationLog(db),
            notifications: new NotificationStore(db),
            kind: "procedure",
            payload: payload,
            dryRun: true);

        Assert.True(result.Ok, JsonSerializer.Serialize(result));
        Assert.Empty(await new ProcedureStore(db).FindAsync("procedure.test.verb"));
        Assert.Single(await db.Operations.Where(o => o.Tool == "commit").ToListAsync());
        Assert.Empty(await db.Operations.Where(o => o.Tool == "write_procedure").ToListAsync());
    }

    /// <summary>
    /// A `fix` is only useful if it can be pasted back. Each commit call this system suggests is
    /// taken apart the way a client would: the payload argument must be a JSON string, and the
    /// string it carries must itself be a JSON object.
    /// </summary>
    [Fact]
    public void Every_suggested_commit_call_is_literally_callable()
    {
        foreach (var kind in VerbSurface.CommitKindNames)
        {
            var call = VerbSurface.CommitCall(kind, dryRun: true);
            var prefix = $"commit(kind: \"{kind}\", payload: ";

            Assert.StartsWith(prefix, call, StringComparison.Ordinal);

            var argument = call[prefix.Length..].Split(", dryRun: true)")[0].TrimEnd(')');
            var payload = JsonSerializer.Deserialize<string>(argument);

            Assert.NotNull(payload);
            Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(payload!).RootElement.ValueKind);
        }
    }

    [Fact]
    public async Task Unknown_commit_kind_returns_a_recoverable_protocol_error()
    {
        await using var db = _fixture.CreateContext();
        var result = await new CommitTool().CommitAsync(
            procedures: null!,
            world: null!,
            effects: null!,
            mechanics: null!,
            eventTypes: null!,
            subscriptions: null!,
            actions: null!,
            log: new OperationLog(db),
            notifications: null!,
            kind: "system",
            payload: "{}");

        Assert.False(result.Ok);
        Assert.Equal("UNKNOWN_KIND", result.Error?.Code);
        Assert.Equal("query(kind: \"capabilities\")", result.Error?.Fix);
        Assert.Single(await db.Operations.Where(o => o.Tool == "commit").ToListAsync());
    }
}
