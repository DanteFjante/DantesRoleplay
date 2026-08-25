using DantesRoleplay.HostSettings;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

public sealed class HostSettingOverrideStore(
    DantesRoleplayDbContext db,
    IOperationLog operations) : IHostSettingOverrideStore
{
    // Setting changes are rare, restart-scoped operator actions. Serializing them inside the
    // single supported host process closes the read-before-first-insert race as well as the
    // ordinary revision-update race, so every loser observes the advanced revision and receives
    // the same stable conflict instead of leaking a provider-specific database exception.
    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    public async Task<IReadOnlyDictionary<string, HostSettingOverrideHead>> GetHeadsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await db.HostSettingOverrides.AsNoTracking()
            .Select(head => new
            {
                Head = head,
                ValueJson = head.Versions
                    .Where(version => version.Version == head.CurrentVersion)
                    .Select(version => version.ValueJson)
                    .Single()
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(
            row => row.Head.Key,
            row => new HostSettingOverrideHead(
                row.Head.Key, row.Head.CurrentVersion, row.Head.AppliedVersion,
                row.ValueJson, row.Head.UpdatedAtUtc),
            StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<HostSettingOverrideRevision>> ListVersionsAsync(
        string key, int? beforeVersion, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (limit is < 1 or > 101) throw new ArgumentOutOfRangeException(nameof(limit));
        var query = db.HostSettingOverrideVersions.AsNoTracking().Where(row => row.SettingKey == key);
        if (beforeVersion.HasValue) query = query.Where(row => row.Version < beforeVersion.Value);
        return await query.OrderByDescending(row => row.Version).Take(limit)
            .Select(row => new HostSettingOverrideRevision(
                row.SettingKey, row.Version, row.ValueJson, row.CreatedAtUtc,
                row.CreatedBy, row.OperationId))
            .ToListAsync(cancellationToken);
    }

    public async Task<HostSettingOverrideRevision?> GetVersionAsync(
        string key, int version, CancellationToken cancellationToken = default) =>
        await db.HostSettingOverrideVersions.AsNoTracking()
            .Where(row => row.SettingKey == key && row.Version == version)
            .Select(row => new HostSettingOverrideRevision(
                row.SettingKey, row.Version, row.ValueJson, row.CreatedAtUtc,
                row.CreatedBy, row.OperationId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<HostSettingOverrideWriteResult> AppendAsync(
        HostSettingOverrideWrite write, CancellationToken cancellationToken = default)
    {
        Validate(write);
        await WriteGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var head = await db.HostSettingOverrides
                    .Include(candidate => candidate.Versions)
                    .SingleOrDefaultAsync(candidate => candidate.Key == write.Key, cancellationToken);
                var current = head?.CurrentVersion ?? 0;
                if (current != write.ExpectedRevision)
                    throw Conflict("SETTING_REVISION_STALE", $"Expected revision {write.ExpectedRevision}, but the current revision is {current}.");

                string? valueJson = write.ValueJson;
                if (write.RollbackTargetRevision.HasValue)
                {
                    var target = head?.Versions.SingleOrDefault(version =>
                        version.Version == write.RollbackTargetRevision.Value);
                    if (target is null || target.Version >= current)
                        throw Conflict("SETTING_REVISION_UNKNOWN", "The rollback target must be an existing earlier revision.");
                    valueJson = target.ValueJson;
                }

                var currentValue = head?.Versions.Single(version => version.Version == current).ValueJson;
                if ((head is null && valueJson is null) || (head is not null && currentValue == valueJson))
                    throw Conflict("SETTING_NO_CHANGE", "The requested value is already the staged value.");

                var now = DateTime.UtcNow;
                var operationId = Operation.NewId();
                var next = checked(current + 1);
                head ??= new HostSettingOverride
                {
                    Key = write.Key,
                    CurrentVersion = 0,
                    AppliedVersion = 0,
                    UpdatedAtUtc = now
                };
                if (head.CurrentVersion == 0) db.HostSettingOverrides.Add(head);
                head.CurrentVersion = next;
                head.UpdatedAtUtc = now;
                var revision = new HostSettingOverrideVersion
                {
                    SettingKey = write.Key,
                    Version = next,
                    ValueJson = valueJson,
                    CreatedAtUtc = now,
                    CreatedBy = write.Actor,
                    OperationId = operationId
                };
                head.Versions.Add(revision);

                await operations.RecordAsync(
                    write.Tool,
                    $"Staged host setting {write.Key} revision {next} for restart.",
                    true,
                    intent: "Change a host setting from the operator control center.",
                    subject: write.Key,
                    consumesReadEvidence: false,
                    cancellationToken: cancellationToken,
                    id: operationId);
                await transaction.CommitAsync(cancellationToken);
                return new(
                    new(write.Key, next, valueJson, now, write.Actor, operationId),
                    head.AppliedVersion);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            WriteGate.Release();
        }
    }

    public async Task<int> MarkPendingAppliedAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var pending = await db.HostSettingOverrides
                .Where(head => head.CurrentVersion > head.AppliedVersion)
                .ToListAsync(cancellationToken);
            if (pending.Count == 0) return 0;
            foreach (var head in pending) head.AppliedVersion = head.CurrentVersion;
            await operations.RecordAsync(
                "host.settings.startup",
                $"Applied {pending.Count} staged host setting override(s) at startup.",
                true,
                intent: "Apply validated staged host settings at startup.",
                subject: "host-settings",
                consumesReadEvidence: false,
                cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return pending.Count;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void Validate(HostSettingOverrideWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (string.IsNullOrWhiteSpace(write.Key) || write.Key.Length > 100 ||
            write.ExpectedRevision < 0 || string.IsNullOrWhiteSpace(write.Actor) ||
            write.Actor.Length > 200 || string.IsNullOrWhiteSpace(write.Tool) || write.Tool.Length > 100)
            throw new ArgumentException("The host setting write is invalid.", nameof(write));
        if (write.ValueJson?.Length > 16_000)
            throw new ArgumentException("The host setting value is too large.", nameof(write));
    }

    private static HostSettingOverrideStoreException Conflict(string code, string message) => new(code, message);
}
