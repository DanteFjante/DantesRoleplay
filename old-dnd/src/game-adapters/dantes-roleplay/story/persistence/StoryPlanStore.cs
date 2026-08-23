using System.Text.Json;
using DantesRoleplay.Story;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

public sealed record StoryPlanLease(string StoryPlanId, string LeaseOwner, int Revision, int StepIndex);

/// <summary>SQLite authority for durable story-plan state. The in-memory queue is only a wake-up hint.</summary>
public interface IStoryPlanStore
{
    Task<StoryPlanRun?> GetAsync(string storyPlanId, bool tracked = false, CancellationToken cancellationToken = default);
    Task<StoryPlanRun?> FindByRequestTokenAsync(string requestToken, bool tracked = false, CancellationToken cancellationToken = default);
    Task<StoryPlanRun> CreateAsync(StoryPlanRun run, CancellationToken cancellationToken = default);
    Task<bool> RequestCancelAsync(string storyPlanId, int expectedRevision, CancellationToken cancellationToken = default);
    Task<StoryPlanLease?> ClaimNextAsync(string leaseOwner, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<bool> RenewLeaseAsync(StoryPlanLease lease, DateTime nowUtc, CancellationToken cancellationToken = default);
}

public sealed class StoryPlanStore(DantesRoleplayDbContext db) : IStoryPlanStore
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private readonly DantesRoleplayDbContext _db = db;

    public async Task<StoryPlanRun?> GetAsync(string storyPlanId, bool tracked = false, CancellationToken cancellationToken = default)
    {
        var query = _db.StoryPlanRuns.Include(x => x.Steps).Where(x => x.Id == storyPlanId);
        return tracked
            ? await query.SingleOrDefaultAsync(cancellationToken)
            : await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<StoryPlanRun?> FindByRequestTokenAsync(string requestToken, bool tracked = false, CancellationToken cancellationToken = default)
    {
        var query = _db.StoryPlanRuns.Include(x => x.Steps).Where(x => x.RequestToken == requestToken);
        return tracked
            ? await query.SingleOrDefaultAsync(cancellationToken)
            : await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<StoryPlanRun> CreateAsync(StoryPlanRun run, CancellationToken cancellationToken = default)
    {
        _db.StoryPlanRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<bool> RequestCancelAsync(string storyPlanId, int expectedRevision, CancellationToken cancellationToken = default)
    {
        var run = await _db.StoryPlanRuns.SingleOrDefaultAsync(x => x.Id == storyPlanId, cancellationToken);
        if (run is null || StoryPlanStatus.IsTerminal(run.Status) || run.Revision != expectedRevision) return false;
        run.CancelRequested = true;
        run.Revision++;
        run.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<StoryPlanLease?> ClaimNextAsync(string leaseOwner, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var run = await _db.StoryPlanRuns.Include(x => x.Steps)
            .OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id)
            // A cancellation is a durable signal, not a terminal state. Claim it so the worker can
            // convert the plan to its final cancelled receipt between steps.
            .FirstOrDefaultAsync(x => x.Status != StoryPlanStatus.Completed &&
                x.Status != StoryPlanStatus.Blocked && x.Status != StoryPlanStatus.Failed &&
                x.Status != StoryPlanStatus.Cancelled &&
                (x.Status == StoryPlanStatus.Pending || x.LeaseUntilUtc == null || x.LeaseUntilUtc < nowUtc), cancellationToken);
        if (run is null) return null;
        var step = run.Steps.SingleOrDefault(x => x.StepIndex == run.NextStepIndex);
        if (step is null) return null;
        run.Status = StoryPlanStatus.Running;
        run.LeaseOwner = leaseOwner;
        run.LeaseUntilUtc = nowUtc.Add(LeaseDuration);
        run.Revision++;
        run.UpdatedAtUtc = nowUtc;
        if (step.Status == StoryPlanStepStatus.Pending)
        {
            step.Status = StoryPlanStepStatus.Running;
            step.StartedAtUtc = nowUtc;
        }
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return null; }
        return new(run.Id, leaseOwner, run.Revision, step.StepIndex);
    }

    public async Task<bool> RenewLeaseAsync(StoryPlanLease lease, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var updated = await _db.StoryPlanRuns.Where(x => x.Id == lease.StoryPlanId && x.LeaseOwner == lease.LeaseOwner &&
                x.Revision == lease.Revision && x.NextStepIndex == lease.StepIndex && !x.CancelRequested &&
                x.Status == StoryPlanStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LeaseUntilUtc, nowUtc.Add(LeaseDuration))
                .SetProperty(x => x.UpdatedAtUtc, nowUtc), cancellationToken);
        return updated == 1;
    }
}

internal static class StoryPlanPersistence
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static StoryPlanResult ToResult(StoryPlanRun run)
    {
        var steps = run.Steps.OrderBy(x => x.StepIndex).Select(step => ReadStep(step)).ToList();
        StoryHandoff? handoff = null;
        if (!string.IsNullOrEmpty(run.HandoffJson))
        {
            try { handoff = JsonSerializer.Deserialize<StoryHandoff>(run.HandoffJson, Json); }
            catch (JsonException) { }
        }
        return new(run.Id, run.CampaignId, run.Status, run.Revision, run.Objective, run.CompletedStepCount, steps, handoff, run.StopCode, run.StopMessage);
    }

    public static StoryPlanStepResult ReadStep(StoryPlanStepRun step)
    {
        if (!string.IsNullOrWhiteSpace(step.ResultJson))
        {
            try
            {
                var result = JsonSerializer.Deserialize<StoryPlanStepResult>(step.ResultJson, Json);
                if (result is not null) return result;
            }
            catch (JsonException) { }
        }
        return new(step.StepId, step.Kind, step.Status, "", [], "", [], [], step.ActionOperationId);
    }

    public static string Write(object value) => JsonSerializer.Serialize(value, Json);
}
