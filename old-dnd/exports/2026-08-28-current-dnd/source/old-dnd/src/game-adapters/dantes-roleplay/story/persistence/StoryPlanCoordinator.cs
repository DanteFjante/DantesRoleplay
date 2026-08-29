using DantesRoleplay.Security;
using DantesRoleplay.Story;

namespace DantesRoleplay.DataAccess;

/// <summary>Policy-gated application service. The worker can only run a plan created for the fixed development seat.</summary>
public sealed class StoryPlanCoordinator(
    IStoryPlanStore store,
    IAuthenticatedCampaignAudiencePolicy audience,
    StoryPlanWakeQueue wake) : IStoryPlanCoordinator
{
    private readonly IStoryPlanStore _store = store;
    private readonly IAuthenticatedCampaignAudiencePolicy _audience = audience;
    private readonly StoryPlanWakeQueue _wake = wake;

    public async Task<StoryPlanResult> StartAsync(StoryPlanStartRequest request, CancellationToken cancellationToken = default)
    {
        var valid = StoryPlanValidator.Validate(request);
        if (!valid.Valid) return Failure(valid.Problem!);
        var grant = await GrantAsync(request.CampaignId, cancellationToken);
        if (grant is null) return Failure(new("STORY_AUDIENCE_DENIED", "Story-plan access was denied."));

        var canonical = StoryPlanCanonical.Serialize(request);
        var duplicate = await _store.FindByRequestTokenAsync(request.RequestToken, cancellationToken: cancellationToken);
        if (duplicate is not null)
            return duplicate.PlanJson == canonical
                ? StoryPlanPersistence.ToResult(duplicate)
                : Failure(new("STORY_REQUEST_TOKEN_CONFLICT", "requestToken was already used for a different story plan."));

        var now = DateTime.UtcNow;
        var run = new StoryPlanRun
        {
            Id = $"story-plan.{Guid.NewGuid():n}", RequestToken = request.RequestToken,
            CampaignId = request.CampaignId, Objective = request.Objective, PlanJson = canonical,
            PrincipalId = grant.PrincipalId, PolicyRevision = grant.PolicyRevision,
            CreatedAtUtc = now, UpdatedAtUtc = now,
            Steps = request.Steps.Select((step, index) => new StoryPlanStepRun
            {
                StoryPlanId = $"story-plan.pending", StepIndex = index, StepId = step.Id, Kind = step.Kind,
                Intent = step.Intent, RoleEntityIdsJson = StoryPlanCanonical.SerializeRoles(step.RoleEntityIds), InputJson = step.Input
            }).ToList()
        };
        foreach (var step in run.Steps) step.StoryPlanId = run.Id;
        try { await _store.CreateAsync(run, cancellationToken); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            var replay = await _store.FindByRequestTokenAsync(request.RequestToken, cancellationToken: cancellationToken);
            return replay is not null && replay.PlanJson == canonical ? StoryPlanPersistence.ToResult(replay) : Failure(new("STORY_REQUEST_TOKEN_CONFLICT", "requestToken was already used for a different story plan."));
        }
        _wake.Wake(run.Id);
        return StoryPlanPersistence.ToResult(run);
    }

    public async Task<StoryPlanResult> CancelAsync(StoryPlanCancelRequest request, CancellationToken cancellationToken = default)
    {
        var valid = StoryPlanValidator.Validate(request);
        if (!valid.Valid) return Failure(valid.Problem!);
        var run = await _store.GetAsync(request.StoryPlanId, cancellationToken: cancellationToken);
        if (run is null || await GrantAsync(run.CampaignId, cancellationToken) is null) return Failure(new("STORY_PLAN_NOT_FOUND", "Story plan was not found."));
        if (StoryPlanStatus.IsTerminal(run.Status)) return StoryPlanPersistence.ToResult(run);
        if (!await _store.RequestCancelAsync(run.Id, request.ExpectedRevision, cancellationToken)) return Failure(new("INVALID_STORY_PLAN", "Story plan revision changed; query it and retry cancellation if still needed."));
        _wake.Wake(run.Id);
        return StoryPlanPersistence.ToResult((await _store.GetAsync(run.Id, cancellationToken: cancellationToken))!);
    }

    public async Task<StoryPlanResult> GetAsync(StoryPlanQueryRequest request, CancellationToken cancellationToken = default)
    {
        var valid = StoryPlanValidator.Validate(request);
        if (!valid.Valid) return Failure(valid.Problem!);
        var run = await _store.GetAsync(request.StoryPlanId, cancellationToken: cancellationToken);
        if (run is null || await GrantAsync(run.CampaignId, cancellationToken) is null) return Failure(new("STORY_PLAN_NOT_FOUND", "Story plan was not found."));
        if (request.AfterRevision == run.Revision && !StoryPlanStatus.IsTerminal(run.Status) && request.WaitSeconds > 0)
        {
            var deadline = DateTime.UtcNow.AddSeconds(request.WaitSeconds);
            do
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                run = await _store.GetAsync(request.StoryPlanId, cancellationToken: cancellationToken);
                if (run is null || await GrantAsync(run.CampaignId, cancellationToken) is null) return Failure(new("STORY_PLAN_NOT_FOUND", "Story plan was not found."));
            } while (run.Revision == request.AfterRevision && !StoryPlanStatus.IsTerminal(run.Status) && DateTime.UtcNow < deadline);
        }
        return StoryPlanPersistence.ToResult(run);
    }

    private async Task<AuthenticatedCampaignAudienceGrant?> GrantAsync(string campaignId, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await _audience.ResolveAsync(campaignId, cancellationToken);
            return resolved.Granted && resolved.Grant!.CampaignId == campaignId && resolved.Grant.Role == CampaignAudienceRoles.GameMaster
                ? resolved.Grant : null;
        }
        catch { return null; }
    }

    private static StoryPlanResult Failure(StoryPlanProblem problem) => new("", "", StoryPlanStatus.Failed, 0, "", 0, [], null, problem.Code, problem.Message);
}

internal static class StoryPlanCanonical
{
    public static string Serialize(StoryPlanStartRequest request) => StoryPlanPersistence.Write(new
    {
        operation = request.Operation, requestToken = request.RequestToken, campaignId = request.CampaignId,
        objective = request.Objective, steps = request.Steps.Select(step => new
        {
            id = step.Id, kind = step.Kind, intent = step.Intent,
            // Missing and explicitly empty roles have the same documented transport meaning.
            // Canonicalize both to the same persisted object so idempotency is semantic, not a
            // side effect of which optional spelling the remote caller chose.
            roleEntityIds = (step.RoleEntityIds ?? new Dictionary<string, string>())
                .OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value),
            input = step.Input
        })
    });

    public static string SerializeRoles(IReadOnlyDictionary<string, string>? roles) => StoryPlanPersistence.Write(
        (roles ?? new Dictionary<string, string>()).OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value));
}
