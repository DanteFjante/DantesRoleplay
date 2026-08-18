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
        var result = await new QueryTool().QueryAsync(
            new ProcedureStore(db),
            new WorldStore(db),
            new MechanicStore(db),
            new OperationLog(db),
            "procedures");

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
            new ProcedureStore(db),
            new WorldStore(db),
            null!,
            new MechanicStore(db),
            null!,
            new OperationLog(db),
            "procedure",
            payload,
            dryRun: true);

        Assert.True(result.Ok, JsonSerializer.Serialize(result));
        Assert.Empty(await new ProcedureStore(db).FindAsync("procedure.test.verb"));
        Assert.Single(await db.Operations.Where(o => o.Tool == "commit").ToListAsync());
        Assert.Empty(await db.Operations.Where(o => o.Tool == "write_procedure").ToListAsync());
    }

    [Fact]
    public async Task Unknown_commit_kind_returns_a_recoverable_protocol_error()
    {
        await using var db = _fixture.CreateContext();
        var result = await new CommitTool().CommitAsync(
            null!,
            null!,
            null!,
            null!,
            null!,
            new OperationLog(db),
            "system",
            "{}");

        Assert.False(result.Ok);
        Assert.Equal("UNKNOWN_KIND", result.Error?.Code);
        Assert.Equal("query(kind: \"capabilities\")", result.Error?.Fix);
        Assert.Single(await db.Operations.Where(o => o.Tool == "commit").ToListAsync());
    }
}
