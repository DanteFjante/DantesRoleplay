using DantesRoleplay.DataAccess;
using DantesRoleplay.HostSettings;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class HostSettingOverrideStoreTests
{
    [Fact]
    public async Task Update_reset_and_rollback_append_audited_revisions_and_apply_only_at_startup()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new HostSettingOverrideStore(db, new OperationLog(db));

        var first = await store.AppendAsync(new(
            "local-completion.enabled", 0, "true", "local-operator", "control.settings.update"));
        Assert.Equal(1, first.Revision.Version);
        Assert.Equal(0, first.AppliedVersion);
        var pending = Assert.Single(await store.GetHeadsAsync());
        Assert.Equal(1, pending.Value.CurrentVersion);
        Assert.Equal(0, pending.Value.AppliedVersion);
        Assert.Equal("true", pending.Value.ValueJson);

        var noChange = await Assert.ThrowsAsync<HostSettingOverrideStoreException>(() =>
            store.AppendAsync(new(
                "local-completion.enabled", 1, "true", "local-operator", "control.settings.update")));
        Assert.Equal("SETTING_NO_CHANGE", noChange.Code);

        Assert.Equal(1, await store.MarkPendingAppliedAsync());
        Assert.Equal(0, await store.MarkPendingAppliedAsync());
        var reset = await store.AppendAsync(new(
            "local-completion.enabled", 1, null, "local-operator", "control.settings.reset"));
        Assert.Equal(2, reset.Revision.Version);
        Assert.Null(reset.Revision.ValueJson);
        var rollback = await store.AppendAsync(new(
            "local-completion.enabled", 2, null, "local-operator", "control.settings.rollback", 1));
        Assert.Equal(3, rollback.Revision.Version);
        Assert.Equal("true", rollback.Revision.ValueJson);

        var versions = await store.ListVersionsAsync("local-completion.enabled", null, 10);
        Assert.Equal([3, 2, 1], versions.Select(version => version.Version));
        Assert.Equal(4, await db.Operations.CountAsync());
        Assert.Equal(3, await db.HostSettingOverrideVersions.CountAsync());
    }

    [Fact]
    public async Task Stale_revision_and_unknown_rollback_write_nothing()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new HostSettingOverrideStore(db, new OperationLog(db));
        await store.AppendAsync(new(
            "local-completion.model", 0, "\"small\"", "alice", "control.settings.update"));

        var stale = await Assert.ThrowsAsync<HostSettingOverrideStoreException>(() => store.AppendAsync(new(
            "local-completion.model", 0, "\"large\"", "alice", "control.settings.update")));
        Assert.Equal("SETTING_REVISION_STALE", stale.Code);
        var missing = await Assert.ThrowsAsync<HostSettingOverrideStoreException>(() => store.AppendAsync(new(
            "local-completion.model", 1, null, "alice", "control.settings.rollback", 7)));
        Assert.Equal("SETTING_REVISION_UNKNOWN", missing.Code);
        Assert.Single(await db.HostSettingOverrideVersions.ToListAsync());
        Assert.Single(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_first_writes_commit_once_and_return_a_stable_stale_conflict()
    {
        using var fixture = new SqliteFixture();
        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();
        var firstStore = new HostSettingOverrideStore(firstDb, new OperationLog(firstDb));
        var secondStore = new HostSettingOverrideStore(secondDb, new OperationLog(secondDb));

        var writes = new[]
        {
            CaptureAsync(firstStore, "\"small\"", "alice"),
            CaptureAsync(secondStore, "\"large\"", "bob")
        };
        var results = await Task.WhenAll(writes);

        Assert.Single(results, result => result.Result is not null);
        var conflict = Assert.Single(results, result => result.Exception is not null).Exception!;
        Assert.Equal("SETTING_REVISION_STALE", conflict.Code);
        await using var verification = fixture.CreateContext();
        Assert.Single(await verification.HostSettingOverrideVersions.ToListAsync());
        Assert.Single(await verification.Operations.ToListAsync());
    }

    private static async Task<(HostSettingOverrideWriteResult? Result, HostSettingOverrideStoreException? Exception)>
        CaptureAsync(IHostSettingOverrideStore store, string valueJson, string actor)
    {
        try
        {
            return (await store.AppendAsync(new(
                "local-completion.model", 0, valueJson, actor, "control.settings.update")), null);
        }
        catch (HostSettingOverrideStoreException exception)
        {
            return (null, exception);
        }
    }
}
