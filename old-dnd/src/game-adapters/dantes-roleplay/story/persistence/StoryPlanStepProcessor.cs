using System.Text.Json;
using System.Text;
using DantesRoleplay.Actions;
using DantesRoleplay.Campaign;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.Security;
using DantesRoleplay.Story;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Host-facing seam for one scoped, already-claimed step.</summary>
public interface IStoryPlanStepProcessor
{
    Task ProcessAsync(StoryPlanLease lease, CancellationToken cancellationToken);
}

/// <summary>Executes exactly one claimed story-plan step in one scoped DbContext.</summary>
internal sealed class StoryPlanStepProcessor(
    DantesRoleplayDbContext db,
    IStoryPlanStore store,
    IAuthenticatedCampaignAudiencePolicy audience,
    ICampaignResumeReader campaignResumes,
    IAuthorizedKnowledgeAnswerCoordinator knowledge,
    IProcedureStore procedures,
    IOperationLog log,
    StoryActionStepPreparer actionPreparer,
    StoryPlanActionExecutor actions) : IStoryPlanStepProcessor
{
    private static readonly TimeSpan StepDeadline = TimeSpan.FromMinutes(8);
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IStoryPlanStore _store = store;
    private readonly IAuthenticatedCampaignAudiencePolicy _audience = audience;
    private readonly ICampaignResumeReader _campaignResumes = campaignResumes;
    private readonly IAuthorizedKnowledgeAnswerCoordinator _knowledge = knowledge;
    private readonly IProcedureStore _procedures = procedures;
    private readonly IOperationLog _log = log;
    private readonly StoryActionStepPreparer _actionPreparer = actionPreparer;
    private readonly StoryPlanActionExecutor _actions = actions;

    public async Task ProcessAsync(StoryPlanLease lease, CancellationToken cancellationToken)
    {
        var run = await _db.StoryPlanRuns.Include(x => x.Steps).SingleOrDefaultAsync(x => x.Id == lease.StoryPlanId, cancellationToken);
        if (run is null || StoryPlanStatus.IsTerminal(run.Status) || run.LeaseOwner != lease.LeaseOwner || run.Revision != lease.Revision || run.LeaseUntilUtc <= DateTime.UtcNow) return;
        var step = run.Steps.SingleOrDefault(x => x.StepIndex == lease.StepIndex);
        if (step is null || step.Status != StoryPlanStepStatus.Running) return;
        if (run.CancelRequested) { await CancelAsync(run, cancellationToken); return; }
        if (!await AuthorizedAsync(run, cancellationToken)) { await BlockAsync(run, step, "STORY_AUDIENCE_DENIED", "Story-plan access was revoked.", [], cancellationToken); return; }

        // A step has one bounded lifetime. Renew before it performs any external/model work so a
        // stale worker cannot continue into the action path after cancellation or lease loss.
        if (!await RenewAsync(lease, cancellationToken)) { await ReconcileLostLeaseAsync(run, cancellationToken); return; }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(StepDeadline);

        try
        {
            switch (step.Kind)
            {
                case StoryPlanStepKind.CampaignContext:
                    await ProcessContextAsync(run, step, deadline.Token);
                    break;
                case StoryPlanStepKind.Knowledge:
                    await ProcessKnowledgeAsync(run, step, deadline.Token);
                    break;
                case StoryPlanStepKind.Action:
                    await ProcessActionAsync(run, step, lease, deadline.Token);
                    break;
                default:
                    await BlockAsync(run, step, "INVALID_STORY_PLAN", "The story-plan step kind is invalid.", [], deadline.Token);
                    break;
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await TimeoutAsync(run, step, cancellationToken);
        }
    }

    private async Task ProcessContextAsync(StoryPlanRun run, StoryPlanStepRun step, CancellationToken cancellationToken)
    {
        var fixedProcedure = await FixedProcedureAsync("procedure.campaign.chapter", "query(kind: \"campaign-resume\")", step, cancellationToken);
        if (fixedProcedure.Evidence is null) { await BlockAsync(run, step, fixedProcedure.Code, fixedProcedure.Message, [], cancellationToken); return; }
        if (!await AuthorizedAsync(run, cancellationToken)) { await BlockAsync(run, step, "STORY_AUDIENCE_DENIED", "Story-plan access was revoked.", [], cancellationToken); return; }
        var resume = await _campaignResumes.GetAsync(run.CampaignId, cancellationToken);
        if (resume is null) { await BlockAsync(run, step, "STORY_CONTEXT_UNAVAILABLE", "Campaign context is unavailable.", [], cancellationToken); return; }
        if (!TryContextFindings(resume, out var findings)) { await BlockAsync(run, step, "STORY_CONTEXT_TOO_LARGE", "Campaign context exceeds its safe result limit.", [], cancellationToken); return; }
        if (!await AuthorizedAsync(run, cancellationToken)) { await BlockAsync(run, step, "STORY_AUDIENCE_DENIED", "Story-plan access was revoked.", [], cancellationToken); return; }
        await CompleteAsync(run, step, new(step.StepId, step.Kind, StoryPlanStepStatus.Completed, "Campaign context loaded.", findings, "", [], []), [fixedProcedure.Evidence], cancellationToken);
    }

    private async Task ProcessKnowledgeAsync(StoryPlanRun run, StoryPlanStepRun step, CancellationToken cancellationToken)
    {
        var fixedProcedure = await FixedProcedureAsync("procedure.game.core.world.knowledge", "query(kind: \"knowledge-answer\")", step, cancellationToken);
        if (fixedProcedure.Evidence is null) { await BlockAsync(run, step, fixedProcedure.Code, fixedProcedure.Message, [], cancellationToken); return; }
        if (!await AuthorizedAsync(run, cancellationToken)) { await BlockAsync(run, step, "STORY_AUDIENCE_DENIED", "Story-plan access was revoked.", [], cancellationToken); return; }
        var answer = await _knowledge.AnswerAsync(new(run.CampaignId, step.Intent, CandidateLimit: 12), cancellationToken);
        if (answer.Status == "unavailable" || answer.ErrorCode is "KNOWLEDGE_UNAVAILABLE" or "KNOWLEDGE_INPUT_STALE") { await BlockAsync(run, step, "STORY_KNOWLEDGE_UNAVAILABLE", "Knowledge answering is unavailable.", answer.Unresolved, cancellationToken); return; }
        if (answer.Status == "denied") { await BlockAsync(run, step, "STORY_AUDIENCE_DENIED", "Knowledge access was denied.", [], cancellationToken); return; }
        var findings = answer.Answered
            ? answer.Statements.Select(x => $"[{x.Stance}/{x.PresentationKind}] {x.Text}").ToArray()
            : Array.Empty<string>();
        if (!BoundedFindings(findings) || !BoundedFindings(answer.Unresolved))
        {
            await BlockAsync(run, step, "STORY_KNOWLEDGE_UNAVAILABLE", "Knowledge results exceed the safe result limit.", [], cancellationToken);
            return;
        }
        if (!await AuthorizedAsync(run, cancellationToken)) { await BlockAsync(run, step, "STORY_AUDIENCE_DENIED", "Story-plan access was revoked.", [], cancellationToken); return; }
        await CompleteAsync(run, step, new(step.StepId, step.Kind, StoryPlanStepStatus.Completed,
            answer.Answered ? "Knowledge answer completed." : "No definite knowledge was found.", findings, "", answer.Unresolved, []), [fixedProcedure.Evidence], cancellationToken);
    }

    private async Task ProcessActionAsync(StoryPlanRun run, StoryPlanStepRun step, StoryPlanLease lease, CancellationToken cancellationToken)
    {
        StoryActionPreparation preparation = default!;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            preparation = await _actionPreparer.PrepareAsync(run.Objective, step, PriorSummaries(run), cancellationToken);
            if (preparation.ErrorCode != "STORY_PROCEDURE_STALE" || attempt == 1) break;
        }
        if (!preparation.Ready)
        {
            await BlockAsync(run, step, preparation.ErrorCode, preparation.ErrorMessage, preparation.MissingInformation ?? [], cancellationToken);
            return;
        }
        if (run.CancelRequested) { await CancelAsync(run, cancellationToken); return; }
        if (!await RenewAsync(lease, cancellationToken)) { await ReconcileLostLeaseAsync(run, cancellationToken); return; }
        var result = await _actions.ExecuteAsync(run, step, preparation, lease, cancellationToken);
        if (!result.Ok)
        {
            // ActionRunner clears its DbContext tracker after any rolled-back action. Reload the
            // durable plan before recording the terminal receipt rather than mutating detached rows.
            var current = await _db.StoryPlanRuns.Include(x => x.Steps).SingleOrDefaultAsync(x => x.Id == run.Id, cancellationToken);
            if (current is null || StoryPlanStatus.IsTerminal(current.Status)) return;
            var currentStep = current.Steps.SingleOrDefault(x => x.StepIndex == step.StepIndex);
            if (currentStep is null) return;
            if (current.LeaseOwner != lease.LeaseOwner || current.Revision != lease.Revision) return;
            if (result.Error?.Code == "CANCELLED" || current.CancelRequested) { await CancelAsync(current, cancellationToken); return; }
            var code = result.Error?.Code == "STORY_INTERNAL_FAILURE" ? "STORY_INTERNAL_FAILURE" : "STORY_ACTION_FAILED";
            await FailAsync(current, currentStep, code, result.Error?.Why ?? "The action failed.", cancellationToken);
        }
    }

    private async Task<FixedProcedure> FixedProcedureAsync(string id, string governedToken, StoryPlanStepRun step, CancellationToken cancellationToken)
    {
        var detail = await _procedures.GetAsync(id, cancellationToken: cancellationToken);
        if (detail is null || detail.Status != ProcedureStatus.Active || !detail.Governs.Contains(governedToken, StringComparison.Ordinal))
            return new(null, "STORY_PROCEDURE_NOT_FOUND", "The required procedure is unavailable.");
        if (Encoding.UTF8.GetByteCount(detail.Description) + Encoding.UTF8.GetByteCount(detail.Instructions) +
            Encoding.UTF8.GetByteCount(detail.Constraints) + Encoding.UTF8.GetByteCount(detail.Governs) > 12_000)
            return new(null, "STORY_PROCEDURE_CONTEXT_TOO_LARGE", "The required procedure exceeds the safe context limit.");
        await _log.RecordAsync("query", $"Read procedure '{detail.Id}' for a story-plan step.", true, step.Intent, detail.Id, consumesReadEvidence: false, cancellationToken: cancellationToken);
        return new(new(detail.Id, detail.Version, detail.SourceHash), "", "");
    }

    private sealed record FixedProcedure(ProcedureEvidence? Evidence, string Code, string Message);

    private async Task CompleteAsync(StoryPlanRun run, StoryPlanStepRun step, StoryPlanStepResult result, IReadOnlyList<ProcedureEvidence> evidence, CancellationToken cancellationToken)
    {
        var previousStep = (step.Status, step.ResultJson, step.ProcedureEvidenceJson, step.CompletedAtUtc);
        var previousRun = (run.Status, run.NextStepIndex, run.CompletedStepCount, run.LeaseOwner, run.LeaseUntilUtc, run.HandoffJson);
        step.Status = StoryPlanStepStatus.Completed; step.ResultJson = StoryPlanPersistence.Write(result); step.ProcedureEvidenceJson = StoryPlanPersistence.Write(evidence);
        step.CompletedAtUtc = DateTime.UtcNow; run.CompletedStepCount++; run.NextStepIndex++; run.UpdatedAtUtc = DateTime.UtcNow;
        if (run.NextStepIndex == run.Steps.Count)
        {
            run.Status = StoryPlanStatus.Completed; run.LeaseOwner = null; run.LeaseUntilUtc = null;
            if (!StoryPlanHandoffBuilder.TryBuild(run, out var handoff))
            {
                (step.Status, step.ResultJson, step.ProcedureEvidenceJson, step.CompletedAtUtc) = previousStep;
                (run.Status, run.NextStepIndex, run.CompletedStepCount, run.LeaseOwner, run.LeaseUntilUtc, run.HandoffJson) = previousRun;
                await FailAsync(run, step, "STORY_INTERNAL_FAILURE", "The final story handoff exceeds the safe result limit.", cancellationToken);
                return;
            }
            run.HandoffJson = StoryPlanPersistence.Write(handoff);
        }
        else { run.Status = StoryPlanStatus.Pending; run.LeaseOwner = null; run.LeaseUntilUtc = null; }
        run.Revision++;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task BlockAsync(StoryPlanRun run, StoryPlanStepRun step, string code, string message, IReadOnlyList<string> missing, CancellationToken cancellationToken) =>
        await StopAsync(run, step, StoryPlanStatus.Blocked, StoryPlanStepStatus.Blocked, code, message, missing, cancellationToken);

    private async Task FailAsync(StoryPlanRun run, StoryPlanStepRun step, string code, string message, CancellationToken cancellationToken) =>
        await StopAsync(run, step, StoryPlanStatus.Failed, StoryPlanStepStatus.Failed, code, message, [], cancellationToken);

    private async Task CancelAsync(StoryPlanRun run, CancellationToken cancellationToken)
    {
        var step = run.Steps.Single(x => x.StepIndex == run.NextStepIndex);
        await StopAsync(run, step, StoryPlanStatus.Cancelled, StoryPlanStepStatus.Skipped, "STORY_CANCELLED", "Story plan was cancelled.", [], cancellationToken);
    }

    private async Task StopAsync(StoryPlanRun run, StoryPlanStepRun step, string planStatus, string stepStatus, string code, string message, IReadOnlyList<string> missing, CancellationToken cancellationToken)
    {
        var result = new StoryPlanStepResult(step.StepId, step.Kind, stepStatus, "", [], "", missing, []);
        step.Status = stepStatus; step.ResultJson = StoryPlanPersistence.Write(result); step.ErrorCode = code; step.ErrorMessage = Safe(message); step.CompletedAtUtc = DateTime.UtcNow;
        foreach (var later in run.Steps.Where(x => x.StepIndex > step.StepIndex && x.Status == StoryPlanStepStatus.Pending))
        {
            later.Status = StoryPlanStepStatus.Skipped;
            later.ResultJson = StoryPlanPersistence.Write(new StoryPlanStepResult(later.StepId, later.Kind, StoryPlanStepStatus.Skipped, "", [], "", [], []));
            later.CompletedAtUtc = DateTime.UtcNow;
        }
        run.Status = planStatus; run.StopCode = code; run.StopMessage = Safe(message); run.LeaseOwner = null; run.LeaseUntilUtc = null; run.Revision++; run.UpdatedAtUtc = DateTime.UtcNow;
        if (StoryPlanHandoffBuilder.TryBuild(run, out var handoff))
        {
            run.HandoffJson = StoryPlanPersistence.Write(handoff);
        }
        else
        {
            step.ErrorCode = "STORY_INTERNAL_FAILURE";
            step.ErrorMessage = "The final story handoff exceeds the safe result limit.";
            run.Status = StoryPlanStatus.Failed;
            run.StopCode = "STORY_INTERNAL_FAILURE";
            run.StopMessage = "The final story handoff exceeds the safe result limit.";
            run.HandoffJson = string.Empty;
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> AuthorizedAsync(StoryPlanRun run, CancellationToken cancellationToken)
    {
        try
        {
            var resolution = await _audience.ResolveAsync(run.CampaignId, cancellationToken);
            return resolution.Granted && resolution.Grant!.Role == CampaignAudienceRoles.GameMaster &&
                   resolution.Grant.PrincipalId == run.PrincipalId && resolution.Grant.PolicyRevision == run.PolicyRevision;
        }
        catch { return false; }
    }

    private Task<bool> RenewAsync(StoryPlanLease lease, CancellationToken cancellationToken) =>
        _store.RenewLeaseAsync(lease, DateTime.UtcNow, cancellationToken);

    private async Task ReconcileLostLeaseAsync(StoryPlanRun run, CancellationToken cancellationToken)
    {
        var current = await _db.StoryPlanRuns.Include(x => x.Steps).SingleOrDefaultAsync(x => x.Id == run.Id, cancellationToken);
        if (current is not null && current.CancelRequested && !StoryPlanStatus.IsTerminal(current.Status))
            await CancelAsync(current, cancellationToken);
    }

    private async Task TimeoutAsync(StoryPlanRun run, StoryPlanStepRun step, CancellationToken cancellationToken)
    {
        var current = await _db.StoryPlanRuns.Include(x => x.Steps).SingleOrDefaultAsync(x => x.Id == run.Id, cancellationToken);
        if (current is null || StoryPlanStatus.IsTerminal(current.Status)) return;
        if (current.CancelRequested) { await CancelAsync(current, cancellationToken); return; }
        var currentStep = current.Steps.SingleOrDefault(x => x.StepIndex == step.StepIndex);
        if (currentStep is not null)
            await BlockAsync(current, currentStep, "STORY_STEP_TIMEOUT", "The story-plan step exceeded its safe execution time.", [], cancellationToken);
    }

    private static IReadOnlyList<string> PriorSummaries(StoryPlanRun run) => run.Steps.Where(x => x.Status == StoryPlanStepStatus.Completed)
        .OrderBy(x => x.StepIndex).Select(x => StoryPlanPersistence.ReadStep(x)).SelectMany(x => x.Findings.Append(x.Narration)).Where(x => !string.IsNullOrWhiteSpace(x)).Take(4).ToArray();
    private static string Safe(string value) => string.IsNullOrWhiteSpace(value) ? "The step could not be completed." : value.Length <= 1000 ? value : value[..1000];

    private static bool TryContextFindings(CampaignResume resume, out IReadOnlyList<string> findings)
    {
        var values = new List<string> { $"Campaign: {resume.Title}. Premise: {resume.Premise}" };
        values.AddRange(resume.PartyGoals.Select(x => $"Goal: {x}")
            .Concat(resume.ToneAndBoundaries.Select(x => $"Boundary: {x}")).Take(8));
        if (resume.CurrentChapter is { } chapter) values.Add($"Chapter: {chapter.Title}. Party question: {chapter.PartyQuestion}" + (string.IsNullOrWhiteSpace(chapter.GmContext) ? "" : $". GM context: {chapter.GmContext}"));
        if (resume.CurrentArc is { } arc) values.Add($"Arc: {arc.Title}. Party stake: {arc.PartyStake}" + (string.IsNullOrWhiteSpace(arc.GmContext) ? "" : $". GM context: {arc.GmContext}"));
        // References are already authorized by CampaignResumeReader. Preserve only their safe
        // prose: audience, role, visibility, and entity IDs are private routing metadata and
        // must not cross the story-plan boundary.
        values.AddRange(resume.References.Take(12).Select(x => $"Reference: {x.Name}. {x.Summary}"));
        values.AddRange(resume.RecentMilestones.Take(5).Select(x => $"Milestone: {x.Title}. {x.ClosingSummary}"));
        findings = values;
        return values.Count <= 32 && values.All(x => x.Length <= 500) && Encoding.UTF8.GetByteCount(StoryPlanPersistence.Write(values)) <= 8_000;
    }

    private static bool BoundedFindings(IReadOnlyList<string> values) =>
        values.Count <= 12 && values.All(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 500) &&
        Encoding.UTF8.GetByteCount(StoryPlanPersistence.Write(values)) <= 8_000;
}

internal static class StoryPlanHandoffBuilder
{
    public static StoryHandoff Build(StoryPlanRun run)
    {
        if (!TryBuild(run, out var handoff)) throw new StoryPlanResultLimitException();
        return handoff;
    }

    public static bool TryBuild(StoryPlanRun run, out StoryHandoff handoff)
    {
        var completed = run.Steps.OrderBy(x => x.StepIndex).Select(StoryPlanPersistence.ReadStep).Where(x => x.Status == StoryPlanStepStatus.Completed).ToArray();
        var contexts = completed.Where(x => x.Kind == StoryPlanStepKind.CampaignContext).SelectMany(x => x.Findings).Take(32).ToArray();
        var facts = completed.Where(x => x.Kind == StoryPlanStepKind.Knowledge).SelectMany(x => x.Findings).Take(32).ToArray();
        var narration = completed.Where(x => x.Kind == StoryPlanStepKind.Action).Select(x => x.Narration).Where(x => !string.IsNullOrWhiteSpace(x)).Take(32).ToArray();
        var affected = completed.SelectMany(x => x.AffectedEntityIds).Distinct(StringComparer.Ordinal).Take(32).ToArray();
        var unresolved = run.Steps.OrderBy(x => x.StepIndex).Select(StoryPlanPersistence.ReadStep).SelectMany(x => x.MissingInformation)
            .Append(run.StopMessage).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Take(32).ToArray();
        var outcome = run.Status switch
        {
            StoryPlanStatus.Completed => "completed all steps",
            StoryPlanStatus.Blocked => $"blocked after {run.CompletedStepCount} of {run.Steps.Count} steps",
            StoryPlanStatus.Failed => $"failed after {run.CompletedStepCount} of {run.Steps.Count} steps",
            _ => $"cancelled after {run.CompletedStepCount} of {run.Steps.Count} steps"
        };
        handoff = new(run.Objective, outcome, contexts, facts, narration, affected, unresolved, ["procedure.play.storytelling"]);
        return PublicResultSafe(handoff.Objective) && PublicResultSafe(handoff.Outcome) &&
            ListsSafe(handoff.ContextSummaries, handoff.FactsLearned, handoff.ActionNarrations, handoff.AffectedEntityIds, handoff.Unresolved, handoff.ProcedureIdsForNextTurn) &&
            Encoding.UTF8.GetByteCount(StoryPlanPersistence.Write(handoff)) <= 32_000;
    }

    public static bool PublicResultSafe(StoryPlanStepResult result) =>
        PublicResultSafe(result.Id) && PublicResultSafe(result.Kind) && PublicResultSafe(result.Status) && PublicResultSafe(result.Summary) &&
        PublicResultSafe(result.Narration) && PublicResultSafe(result.OperationId) &&
        ListsSafe(result.Findings, result.MissingInformation, result.AffectedEntityIds) &&
        Encoding.UTF8.GetByteCount(StoryPlanPersistence.Write(result)) <= 32_000;

    private static bool ListsSafe(params IReadOnlyList<string>[] lists) => lists.All(list => list.Count <= 32 && list.All(PublicResultSafe));
    private static bool PublicResultSafe(string value) => value is not null && value.Length <= 1_000;
}

/// <summary>Stages a successful action receipt in the same transaction as effects, events, and action audit.</summary>
internal sealed class StoryPlanActionExecutor(DantesRoleplayDbContext db, IStoryPlanActionRunner actions)
{
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IStoryPlanActionRunner _actions = actions;

    public Task<ActionRunResult> ExecuteAsync(StoryPlanRun run, StoryPlanStepRun step, StoryActionPreparation preparation, StoryPlanLease lease, CancellationToken cancellationToken)
    {
        var proposal = preparation.Proposal!;
        return _actions.RunWithParticipantAsync(new ActionRequest
        {
            Intent = proposal.Intent, RoleEntityIds = proposal.RoleEntityIds, Input = proposal.Input,
            Scope = proposal.Scope, ProceduresUsed = preparation.ProcedureEvidence.Select(x => x.Id).ToArray()
        }, new Participant(_db, run, step, preparation, lease), cancellationToken);
    }

    private sealed class Participant(DantesRoleplayDbContext db, StoryPlanRun run, StoryPlanStepRun step, StoryActionPreparation preparation, StoryPlanLease lease) : IActionCommitParticipant
    {
        public async Task StageAsync(ActionRunResult action, CancellationToken cancellationToken)
        {
            var canCommit = await db.StoryPlanRuns.AsNoTracking().AnyAsync(x => x.Id == run.Id &&
                x.LeaseOwner == lease.LeaseOwner && x.Revision == lease.Revision &&
                x.Status == StoryPlanStatus.Running && x.NextStepIndex == step.StepIndex &&
                !x.CancelRequested && x.LeaseUntilUtc > DateTime.UtcNow, cancellationToken);
            if (!canCommit) throw new OperationCanceledException("Story plan lease was lost or cancelled before the action committed.");
            var result = new StoryPlanStepResult(step.StepId, step.Kind, StoryPlanStepStatus.Completed, action.Summary, [], action.Output.Narration, [], action.AffectedEntityIds, action.OperationId);
            if (!StoryPlanHandoffBuilder.PublicResultSafe(result)) throw new StoryPlanResultLimitException();
            step.Status = StoryPlanStepStatus.Completed; step.ResultJson = StoryPlanPersistence.Write(result);
            step.ProcedureEvidenceJson = StoryPlanPersistence.Write(preparation.ProcedureEvidence); step.MechanicId = preparation.MechanicId;
            step.MechanicVersion = preparation.MechanicVersion; step.ActionOperationId = action.OperationId; step.CompletedAtUtc = DateTime.UtcNow;
            run.CompletedStepCount++; run.NextStepIndex++; run.Revision++; run.UpdatedAtUtc = DateTime.UtcNow;
            if (run.NextStepIndex == run.Steps.Count)
            {
                run.Status = StoryPlanStatus.Completed; run.LeaseOwner = null; run.LeaseUntilUtc = null;
                if (!StoryPlanHandoffBuilder.TryBuild(run, out var handoff)) throw new StoryPlanResultLimitException();
                run.HandoffJson = StoryPlanPersistence.Write(handoff);
            }
            else { run.Status = StoryPlanStatus.Pending; run.LeaseOwner = null; run.LeaseUntilUtc = null; }
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
