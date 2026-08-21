using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class SessionFeature4Tests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), "dantes-roleplay-session-s4-tests-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task Validates_one_ended_s3_session_for_checkpoint_without_mutation()
    {
        var setup = await ArrangeAsync();
        var validator = new CampaignSessionCheckpointValidator(setup.World, new CampaignSessionRecapReader(setup.World));
        var before = await CountsAsync(setup.Db);

        var result = await validator.ValidateAsync(new("validate-session-checkpoint", setup.SessionId, "ended"));

        Assert.True(result.Valid, Describe(result));
        Assert.Equal(setup.CampaignId, result.CampaignId);
        Assert.Contains("checkpoint-session", result.Next, StringComparison.Ordinal);
        Assert.Equal(before, await CountsAsync(setup.Db));

        await using var fresh = _fixture.CreateContext();
        var freshResult = await new CampaignSessionCheckpointValidator(new WorldStore(fresh), new CampaignSessionRecapReader(new WorldStore(fresh)))
            .ValidateAsync(new("validate-session-checkpoint", setup.SessionId, "ended"));
        Assert.True(freshResult.Valid, Describe(freshResult));
    }

    [Fact]
    public async Task Rejects_nonended_or_malformed_checkpoint_graph_without_mutation()
    {
        var setup = await ArrangeAsync();
        var validator = new CampaignSessionCheckpointValidator(setup.World, new CampaignSessionRecapReader(setup.World));
        var before = await CountsAsync(setup.Db);

        var invalidRequest = await validator.ValidateAsync(new("checkpoint-session", setup.SessionId, "ended"));
        Assert.False(invalidRequest.Valid);
        Assert.Equal("INVALID_SESSION_CHECKPOINT_REQUEST", Assert.Single(invalidRequest.Problems).Code);

        await setup.World.SetComponentAsync(setup.SessionId, "game.core.campaign.session", "{\"status\":\"active\",\"ordinal\":1}");
        var active = await validator.ValidateAsync(new("validate-session-checkpoint", setup.SessionId, "ended"));
        Assert.False(active.Valid);
        Assert.Equal("SESSION_CHECKPOINT_REQUIRES_ENDED_SESSION", Assert.Single(active.Problems).Code);
        Assert.Equal(before, await CountsAsync(setup.Db));
    }

    [Fact]
    public async Task Rejects_existing_and_reversed_checkpoint_links()
    {
        var setup = await ArrangeAsync();
        var validator = new CampaignSessionCheckpointValidator(setup.World, new CampaignSessionRecapReader(setup.World));
        await setup.World.CreateEntityAsync("Existing checkpoint", "checkpoint.test.session-evidence");
        await setup.World.RelateAsync(setup.SessionId, "checkpoint.test.session-evidence", "game.core.campaign.session.has-checkpoint", "{}");

        var existing = await validator.ValidateAsync(new("validate-session-checkpoint", setup.SessionId, "ended"));
        Assert.False(existing.Valid);
        Assert.Equal("SESSION_CHECKPOINT_ALREADY_EXISTS", Assert.Single(existing.Problems).Code);

        var other = await ArrangeAsync(".reversed");
        var reversedValidator = new CampaignSessionCheckpointValidator(other.World, new CampaignSessionRecapReader(other.World));
        await other.World.CreateEntityAsync("Reversed checkpoint", "checkpoint.test.session-evidence.reversed");
        await other.World.RelateAsync("checkpoint.test.session-evidence.reversed", other.SessionId, "game.core.campaign.session.has-checkpoint", "{}");
        var reversed = await reversedValidator.ValidateAsync(new("validate-session-checkpoint", other.SessionId, "ended"));
        Assert.False(reversed.Valid);
        Assert.Equal("SESSION_CHECKPOINT_SCOPE_INVALID", Assert.Single(reversed.Problems).Code);
    }

    private async Task<Setup> ArrangeAsync(string suffix = "")
    {
        if (!Directory.Exists(_catalogCopy)) Copy(Catalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        if (!await db.ComponentDefinitions.AnyAsync())
        {
            var import = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions());
            Assert.False(import.Aborted, JsonSerializer.Serialize(import));
        }
        var campaignId = "campaign.test.s4" + suffix;
        var sessionId = "session.test.s4" + suffix;
        await world.CreateEntityAsync("S4 Campaign", campaignId);
        await world.CreateEntityAsync("S4 Session", sessionId);
        await world.SetComponentAsync(sessionId, "game.core.campaign.session", "{\"status\":\"ended\",\"ordinal\":1}");
        await world.SetComponentAsync(sessionId, "game.core.campaign.session-recap", "{\"protocolVersion\":\"session.s0.c3-only.v1\",\"chapter\":{\"id\":\"campaign.test.s4.chapter\",\"status\":\"active\",\"title\":\"The Checkpoint\",\"partyQuestion\":\"What survived the boundary?\"},\"arc\":{\"id\":\"campaign.test.s4.arc\",\"status\":\"active\",\"title\":\"The Proof\",\"partyStake\":\"Can the evidence remain bounded?\"},\"milestones\":[]}");
        await world.RelateAsync(campaignId, sessionId, "game.core.campaign.has-session", "{}");
        return new(db, world, campaignId, sessionId);
    }

    private static async Task<Counts> CountsAsync(DantesRoleplayDbContext db) => new(
        await db.Entities.CountAsync(), await db.Components.CountAsync(), await db.Relationships.CountAsync(),
        await db.Events.CountAsync(), await db.Operations.CountAsync());
    private static string Describe(CampaignSessionCheckpointValidationResult result) => string.Join("; ", result.Problems.Select(problem => problem.Code + ": " + problem.Reason));
    private static string Catalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file))); }
    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true); }
    private sealed record Setup(DantesRoleplayDbContext Db, WorldStore World, string CampaignId, string SessionId);
    private sealed record Counts(int Entities, int Components, int Relationships, int Events, int Operations);
}
