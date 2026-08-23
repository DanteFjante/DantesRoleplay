using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using DantesRoleplay.Campaign;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;
using DantesRoleplay.Snapshots;
using DantesRoleplay.World;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class SnapshotFeature1Tests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly SqliteFixture _fixture = new();
    private readonly string _catalogCopy = Path.Combine(Path.GetTempPath(), "dantes-roleplay-snapshot-tests-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task Produces_deterministic_closed_ended_session_evidence_inside_an_existing_transaction()
    {
        var setup = await ArrangeAsync();
        var lifecycleBefore = Component(await setup.World.GetEntityAsync(setup.SessionId), "game.core.campaign.session");
        var recapBefore = Component(await setup.World.GetEntityAsync(setup.SessionId), "game.core.campaign.session-recap");
        var scopesBefore = await setup.World.GetRelationshipsAsync(setup.CampaignId, false);

        var producer = CreateProducer(setup);
        var missingTransaction = await producer.ProduceAsync(setup.SessionId);
        Assert.False(missingTransaction.Produced);
        Assert.Equal("SNAPSHOT_TRANSACTION_REQUIRED", Assert.Single(missingTransaction.Problems).Code);

        CampaignSessionEvidenceProductionResult first;
        await using (var transaction = await setup.Db.Database.BeginTransactionAsync())
        {
            first = await producer.ProduceAsync(setup.SessionId);
            Assert.True(first.Produced, Describe(first));
            Assert.Equal(setup.CampaignId, first.CampaignId);
            Assert.Equal(setup.WorldId, first.WorldId);
            await transaction.CommitAsync();
        }

        var proposal = Assert.IsType<SnapshotCaptureProposal>(first.Proposal);
        Assert.Equal("procedure.campaign.session", proposal.ScopeContractId);
        Assert.True(proposal.ScopeContractVersion > 0);
        Assert.Equal("snapshot.producer.campaign-session-evidence", proposal.ProducerId);
        Assert.Equal(1, proposal.ProducerVersion);
        Assert.Equal("dantes-canonical-json-v1", proposal.ContentEncoding);
        Assert.Matches("^[0-9a-f]{64}$", proposal.BoundaryFingerprint);
        Assert.InRange(proposal.Content.Length, 1, 65_536);

        using (var payload = JsonDocument.Parse(proposal.Content))
        {
            var root = payload.RootElement;
            Assert.Equal("dantes.snapshot.campaign-session-evidence", root.GetProperty("format").GetString());
            Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
            Assert.Equal(setup.SessionId, root.GetProperty("session").GetProperty("id").GetString());
            Assert.Equal("ended", root.GetProperty("session").GetProperty("status").GetString());
            Assert.Equal(setup.CampaignId, root.GetProperty("scope").GetProperty("campaignId").GetString());
            Assert.Equal(setup.WorldId, root.GetProperty("scope").GetProperty("worldId").GetString());
            Assert.Equal("The Archive Bargain", root.GetProperty("recap").GetProperty("chapter").GetProperty("title").GetString());
            Assert.Equal("The Ledger Signal", Assert.Single(root.GetProperty("recap").GetProperty("milestones").EnumerateArray()).GetProperty("title").GetString());
            var text = root.GetRawText();
            Assert.DoesNotContain("gmContext", text, StringComparison.Ordinal);
            Assert.False(root.TryGetProperty("events", out _));
            Assert.False(root.TryGetProperty("quests", out _));
            Assert.False(root.TryGetProperty("characters", out _));
            Assert.False(root.TryGetProperty("items", out _));
        }

        Assert.Equal(lifecycleBefore, Component(await setup.World.GetEntityAsync(setup.SessionId), "game.core.campaign.session"));
        Assert.Equal(recapBefore, Component(await setup.World.GetEntityAsync(setup.SessionId), "game.core.campaign.session-recap"));
        Assert.Equal(scopesBefore, await setup.World.GetRelationshipsAsync(setup.CampaignId, false));

        await using var fresh = _fixture.CreateContext();
        var freshWorld = new WorldStore(fresh);
        var freshProducer = new CampaignSessionEvidenceProducer(fresh, freshWorld, new CampaignSessionRecapReader(freshWorld), new ProcedureStore(fresh));
        await using var freshTransaction = await fresh.Database.BeginTransactionAsync();
        var second = await freshProducer.ProduceAsync(setup.SessionId);
        Assert.True(second.Produced, Describe(second));
        await freshTransaction.CommitAsync();

        Assert.Equal(proposal.BoundaryFingerprint, second.Proposal!.BoundaryFingerprint);
        Assert.Equal(proposal.Content.ToArray(), second.Proposal.Content.ToArray());
    }

    [Fact]
    public async Task Changes_the_boundary_fingerprint_when_a_valid_evidence_source_changes_and_rejects_active_sessions()
    {
        var setup = await ArrangeAsync();
        var producer = CreateProducer(setup);

        string before;
        await using (var transaction = await setup.Db.Database.BeginTransactionAsync())
        {
            var produced = await producer.ProduceAsync(setup.SessionId);
            Assert.True(produced.Produced, Describe(produced));
            before = produced.Proposal!.BoundaryFingerprint;
            await transaction.CommitAsync();
        }

        await setup.World.SetComponentAsync(setup.SessionId, "game.core.campaign.session-recap", JsonSerializer.Serialize(Recap("Changed title"), Json));
        await using (var transaction = await setup.Db.Database.BeginTransactionAsync())
        {
            var changed = await producer.ProduceAsync(setup.SessionId);
            Assert.True(changed.Produced, Describe(changed));
            Assert.NotEqual(before, changed.Proposal!.BoundaryFingerprint);
            Assert.Contains("Changed title", Encoding.UTF8.GetString(changed.Proposal.Content.Span));
            await transaction.CommitAsync();
        }

        await setup.World.SetComponentAsync(setup.SessionId, "game.core.campaign.session", "{\"status\":\"active\",\"ordinal\":1}");
        await using var activeTransaction = await setup.Db.Database.BeginTransactionAsync();
        var active = await producer.ProduceAsync(setup.SessionId);
        Assert.False(active.Produced);
        Assert.Equal("SESSION_NOT_ENDED", Assert.Single(active.Problems).Code);
        await activeTransaction.RollbackAsync();
    }

    [Fact]
    public void Proposal_defensively_copies_content()
    {
        var source = new byte[] { 1, 2, 3 };
        var proposal = new SnapshotCaptureProposal("procedure.campaign.session", 1, "snapshot.producer.campaign-session-evidence", 1, "dantes-canonical-json-v1", new string('a', 64), source);
        source[0] = 99;
        Assert.Equal(new byte[] { 1, 2, 3 }, proposal.Content.ToArray());
    }

    [Fact]
    public async Task Stages_only_inside_the_callers_transaction_and_rolls_back_or_commits_with_it()
    {
        var setup = await ArrangeAsync();
        var producer = CreateProducer(setup);
        var store = new SnapshotPackageStore(setup.Db);
        var fake = new SnapshotCaptureProposal("procedure.campaign.session", 1, "snapshot.producer.campaign-session-evidence", 1, "dantes-canonical-json-v1", new string('b', 64), Encoding.UTF8.GetBytes("{}"));
        var noTransaction = await store.StageAsync(fake, new string('a', 32));
        Assert.False(noTransaction.Staged);
        Assert.Equal("SNAPSHOT_TRANSACTION_REQUIRED", Assert.Single(noTransaction.Problems).Code);
        Assert.Empty(await setup.Db.SnapshotPackages.AsNoTracking().ToListAsync());

        SnapshotPackageReference rolledBack;
        await using (var transaction = await setup.Db.Database.BeginTransactionAsync())
        {
            var produced = await producer.ProduceAsync(setup.SessionId);
            Assert.True(produced.Produced, Describe(produced));
            var staged = await store.StageAsync(produced.Proposal!, new string('c', 32));
            Assert.True(staged.Staged, string.Join("; ", staged.Problems.Select(problem => problem.Code)));
            rolledBack = staged.Reference!;
            Assert.StartsWith("snapshot.", rolledBack.Id, StringComparison.Ordinal);
            Assert.Equal(41, rolledBack.Id.Length);
            Assert.Equal("sha256", rolledBack.DigestAlgorithm);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(produced.Proposal!.Content.Span)).ToLowerInvariant(), rolledBack.ContentDigest);
            Assert.Equal(produced.Proposal.Content.Length, rolledBack.ByteCount);
            await transaction.RollbackAsync();
        }
        setup.Db.ChangeTracker.Clear();
        Assert.Null(await setup.Db.SnapshotPackages.AsNoTracking().SingleOrDefaultAsync(package => package.Id == rolledBack.Id));

        SnapshotPackageReference committed;
        await using (var transaction = await setup.Db.Database.BeginTransactionAsync())
        {
            var produced = await producer.ProduceAsync(setup.SessionId);
            Assert.True(produced.Produced, Describe(produced));
            var staged = await store.StageAsync(produced.Proposal!, new string('d', 32));
            Assert.True(staged.Staged, string.Join("; ", staged.Problems.Select(problem => problem.Code)));
            committed = staged.Reference!;
            await transaction.CommitAsync();
        }

        await using var fresh = _fixture.CreateContext();
        var persisted = await fresh.SnapshotPackages.AsNoTracking().SingleAsync(package => package.Id == committed.Id);
        Assert.Equal(committed.ContentDigest, persisted.ContentDigest);
        Assert.Equal(committed.ByteCount, persisted.ByteCount);
        Assert.Equal("available", persisted.Availability);
        Assert.Equal(new string('d', 32), persisted.RootOperationId);
        Assert.Null(await fresh.Entities.AsNoTracking().SingleOrDefaultAsync(entity => entity.Id == committed.Id));

        var verifier = new SnapshotPackageStore(fresh);
        var verified = await verifier.VerifyAsync(committed);
        Assert.True(verified.Verified, string.Join("; ", verified.Problems.Select(problem => problem.Code)));
        Assert.Equal(committed, verified.Reference);
        var metadataMismatches = new[]
        {
            committed with { ScopeContractId = "procedure.other" },
            committed with { ScopeContractVersion = committed.ScopeContractVersion + 1 },
            committed with { ProducerId = "snapshot.producer.other" },
            committed with { ProducerVersion = committed.ProducerVersion + 1 },
            committed with { BoundaryFingerprint = new string('0', 64) },
            committed with { ContentDigest = new string('0', 64) },
            committed with { ByteCount = committed.ByteCount + 1 },
            committed with { CapturedAt = committed.CapturedAt.AddTicks(1) }
        };
        foreach (var mismatch in metadataMismatches)
        {
            var result = await verifier.VerifyAsync(mismatch);
            Assert.False(result.Verified);
            Assert.Equal("SNAPSHOT_REFERENCE_MISMATCH", Assert.Single(result.Problems).Code);
        }
        var malformed = await verifier.VerifyAsync(committed with { ContentDigest = "not-a-digest" });
        Assert.False(malformed.Verified);
        Assert.Equal("INVALID_SNAPSHOT_REFERENCE", Assert.Single(malformed.Problems).Code);
        var missing = await verifier.VerifyAsync(committed with { Id = "snapshot." + new string('e', 32) });
        Assert.False(missing.Verified);
        Assert.Equal("SNAPSHOT_NOT_FOUND", Assert.Single(missing.Problems).Code);

        await fresh.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON;");
        await fresh.Database.ExecuteSqlRawAsync("UPDATE snapshot_package SET Availability = 'retired' WHERE Id = {0}", committed.Id);
        fresh.ChangeTracker.Clear();
        var unavailable = await verifier.VerifyAsync(committed);
        Assert.False(unavailable.Verified);
        Assert.Equal("SNAPSHOT_UNAVAILABLE", Assert.Single(unavailable.Problems).Code);
        await fresh.Database.ExecuteSqlRawAsync("UPDATE snapshot_package SET Availability = 'available' WHERE Id = {0}", committed.Id);
        await fresh.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF;");

        var corruptible = await fresh.SnapshotPackages.SingleAsync(package => package.Id == committed.Id);
        var tampered = corruptible.Content.ToArray();
        tampered[0] ^= 0x01;
        corruptible.Content = tampered;
        await fresh.SaveChangesAsync();
        fresh.ChangeTracker.Clear();
        var corrupt = await verifier.VerifyAsync(committed);
        Assert.False(corrupt.Verified);
        Assert.Equal("SNAPSHOT_CORRUPT", Assert.Single(corrupt.Problems).Code);
    }

    [Fact]
    public async Task Migrated_schema_blocks_package_updates_and_deletes()
    {
        var file = Path.Combine(Path.GetTempPath(), "dantes-roleplay-snapshot-migration-" + Guid.NewGuid().ToString("n") + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite($"Data Source={file}").Options;
            await using var db = new DantesRoleplayDbContext(options);
            await db.Database.MigrateAsync();
            var store = new SnapshotPackageStore(db);
            SnapshotPackageReference reference;
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                var staged = await store.StageAsync(new SnapshotCaptureProposal("procedure.campaign.session", 1, "snapshot.producer.campaign-session-evidence", 1, "dantes-canonical-json-v1", new string('f', 64), Encoding.UTF8.GetBytes("{}")), new string('a', 32));
                Assert.True(staged.Staged, string.Join("; ", staged.Problems.Select(problem => problem.Code)));
                reference = staged.Reference!;
                await transaction.CommitAsync();
            }

            var update = await db.SnapshotPackages.SingleAsync(package => package.Id == reference.Id);
            update.Availability = "retired";
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            var delete = await db.SnapshotPackages.SingleAsync(package => package.Id == reference.Id);
            db.SnapshotPackages.Remove(delete);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void Production_dependency_registration_exposes_only_internal_snapshot_services()
    {
        var services = new ServiceCollection();
        services.AddDantesRoleplayDataAccess("Data Source=:memory:");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<SnapshotPackageStore>(scope.ServiceProvider.GetRequiredService<ISnapshotPackageStore>());
        Assert.IsType<CampaignSessionEvidenceProducer>(scope.ServiceProvider.GetRequiredService<ICampaignSessionEvidenceProducer>());
    }

    private async Task<Setup> ArrangeAsync()
    {
        Copy(Catalog(), _catalogCopy);
        var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        var import = await new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), world).ApplyAsync(_catalogCopy, new CatalogImportOptions());
        Assert.False(import.Aborted, JsonSerializer.Serialize(import));

        const string worldId = "world.test.snapshot-evidence";
        const string campaignId = "campaign.test.snapshot-evidence";
        const string sessionId = "session.test.snapshot-evidence.1";
        await world.CreateEntityAsync("Snapshot World", worldId);
        await world.SetComponentAsync(worldId, "game.core.world.root", "{\"status\":\"active\",\"summary\":\"A bounded test world.\",\"visibility\":\"gm\"}");
        await world.CreateEntityAsync("Snapshot Campaign", campaignId);
        await world.CreateEntityAsync("Snapshot Session", sessionId);
        await world.SetComponentAsync(sessionId, "game.core.campaign.session", "{\"status\":\"ended\",\"ordinal\":1}");
        await world.SetComponentAsync(sessionId, "game.core.campaign.session-recap", JsonSerializer.Serialize(Recap("The Archive Bargain"), Json));
        await world.RelateAsync(campaignId, sessionId, "game.core.campaign.has-session", "{}");
        await world.RelateAsync(campaignId, worldId, "game.core.campaign.in-world", "{}");
        return new(db, world, campaignId, worldId, sessionId);
    }

    private static CampaignSessionEvidenceProducer CreateProducer(Setup setup) =>
        new(setup.Db, setup.World, new CampaignSessionRecapReader(setup.World), new ProcedureStore(setup.Db));

    private static CampaignSessionRecap Recap(string chapterTitle) => new(
        "session.s0.c3-only.v1",
        new("campaign.test.snapshot-evidence.chapter.second", "active", chapterTitle, "Who can safely receive the signal?"),
        new("campaign.test.snapshot-evidence.arc.main", "active", "The Observatory's Claim", "Can the group keep the signal from becoming leverage?"),
        [new("campaign.test.snapshot-evidence.chapter.opening", "The Ledger Signal", "The party confirmed the signal.", new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc), 0)]);

    private static string Component(EntitySnapshot? entity, string definitionId) => Assert.Single(entity!.Components, component => component.DefinitionId == definitionId).Data;
    private static string Describe(CampaignSessionEvidenceProductionResult result) => string.Join("; ", result.Problems.Select(problem => problem.Code + ": " + problem.Reason));
    private static string Catalog() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return Path.Combine(directory.FullName, "catalog"); throw new DirectoryNotFoundException(); }
    private static void Copy(string source, string target) { Directory.CreateDirectory(target); foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory))); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file))); }
    public void Dispose() { _fixture.Dispose(); if (Directory.Exists(_catalogCopy)) Directory.Delete(_catalogCopy, recursive: true); }
    private sealed record Setup(DantesRoleplayDbContext Db, WorldStore World, string CampaignId, string WorldId, string SessionId);
}
