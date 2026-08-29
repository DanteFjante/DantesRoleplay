using DantesRoleplay.Campaign;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Tools;

public sealed class CampaignTools
{
    public Task<ToolEnvelope> ValidateAsync(ICampaignBlueprintValidator validator, IOperationLog log, CampaignBlueprint blueprint, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "commit", "Validate campaign blueprint.", "commit:campaign", ["procedure.campaign.create"], async () =>
        {
            var result = await validator.ValidateAsync(blueprint, cancellationToken);
            return ToolOutcome.Ok(result, result.Valid ? "Campaign blueprint is valid; creation has not occurred." : "Campaign blueprint is invalid; no state changed.", result.Valid ? "Campaign creation is the next C2 operation." : "Correct the named problems and validate again.");
        }, consumesReadEvidence: false);

    public Task<ToolEnvelope> ValidateSessionAsync(ICampaignSessionValidator validator, IOperationLog log, CampaignSessionValidationRequest request, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "commit", "Validate campaign session readiness.", request.CampaignId, ["procedure.campaign.session"], async () =>
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            return result.Valid
                ? ToolOutcome.OkAbout(result.CampaignId, result, "Campaign session start is valid; no session state changed.", result.Next)
                : ToolOutcome.Fail(result.Problems[0].Code, result.Problems[0].Reason, result.Next, "Campaign session validation was rejected.");
        }, consumesReadEvidence: false);

    public async Task<ToolEnvelope> StartSessionAsync(ICampaignSessionStarter starter, CampaignSessionValidationRequest request, string intent, IReadOnlyList<string>? proceduresUsed, CancellationToken cancellationToken = default)
    {
        var result = await starter.StartAsync(request, intent, proceduresUsed, cancellationToken);
        return result.Started
            ? ToolEnvelope.Success(new { result.CampaignId, result.SessionId, Status = result.LifecycleStatus, result.Ordinal, result.ResumeAvailable, NextAction = result.Next }, result.OperationId, result.Next)
            : ToolEnvelope.Failure(result.Problems[0].Code, result.Problems[0].Reason, result.Next, result.OperationId);
    }

    public Task<ToolEnvelope> ValidateSessionEndAsync(ICampaignSessionEndValidator validator, IOperationLog log, CampaignSessionEndRequest request, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "commit", "Validate campaign session closure.", request.SessionId, ["procedure.campaign.session"], async () =>
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            return result.Valid
                ? ToolOutcome.OkAbout(result.SessionId, new { result.SessionId, result.CampaignId, PreviewAvailable = true, RecapSectionKeys = new[] { "arc", "chapter", "milestones" }, NextAction = result.Next }, "Campaign session closure is valid; no session state changed.", result.Next)
                : ToolOutcome.Fail(result.Problems[0].Code, result.Problems[0].Reason, result.Next, "Campaign session closure validation was rejected.");
        }, consumesReadEvidence: false);

    public async Task<ToolEnvelope> EndSessionAsync(ICampaignSessionEnder ender, CampaignSessionEndRequest request, string intent, IReadOnlyList<string>? proceduresUsed, CancellationToken cancellationToken = default)
    {
        var result = await ender.EndAsync(request, intent, proceduresUsed, cancellationToken);
        return result.Ended
            ? ToolEnvelope.Success(new { result.SessionId, result.CampaignId, result.PreviousStatus, result.CurrentStatus, result.RecapPresent, result.RecapSectionKeys, NextAction = result.Next }, result.OperationId, result.Next)
            : ToolEnvelope.Failure(result.Problems[0].Code, result.Problems[0].Reason, result.Next, result.OperationId);
    }

    public Task<ToolEnvelope> ValidateSessionCheckpointAsync(ICampaignSessionCheckpointValidator validator, IOperationLog log, CampaignSessionCheckpointRequest request, CancellationToken cancellationToken = default) => ToolRunner.RunAsync(log, "commit", "Validate campaign session checkpoint readiness.", request.SessionId, ["procedure.campaign.session"], async () => { var result = await validator.ValidateAsync(request, cancellationToken); return result.Valid ? ToolOutcome.OkAbout(result.SessionId, new { result.SessionId, result.CampaignId, CheckpointAvailable = true, NextAction = result.Next }, "Campaign session checkpoint is valid; no state changed.", result.Next) : ToolOutcome.Fail(result.Problems[0].Code, result.Problems[0].Reason, result.Next, "Campaign session checkpoint validation was rejected."); }, consumesReadEvidence: false);

    public async Task<ToolEnvelope> CheckpointSessionAsync(ICampaignSessionCheckpointCreator creator, CampaignSessionCheckpointRequest request, string intent, IReadOnlyList<string>? proceduresUsed, CancellationToken cancellationToken = default)
    { var result = await creator.CreateAsync(request, intent, proceduresUsed, cancellationToken); return result.Created ? ToolEnvelope.Success(new { result.CheckpointId, result.SessionId, result.CampaignId, result.WorldId, result.ScopeContractVersion, result.Availability, NextAction = result.Next }, result.OperationId, result.Next) : ToolEnvelope.Failure(result.Problems[0].Code, result.Problems[0].Reason, result.Next, result.OperationId); }

    public async Task<ToolEnvelope> CreateAsync(ICampaignBootstrapper bootstrapper, CampaignBlueprint blueprint, string reviewFingerprint, string intent, IReadOnlyList<string>? proceduresUsed, CancellationToken cancellationToken = default)
    {
        var result = await bootstrapper.CreateAsync(blueprint, reviewFingerprint, intent, proceduresUsed, cancellationToken);
        return result.Created
            ? ToolEnvelope.Success(result, result.OperationId, result.Next)
            : ToolEnvelope.Failure(result.Problems[0].Code, result.Problems[0].Reason, result.Next, result.OperationId);
    }

    public async Task<ToolEnvelope> AttachCharacterParticipationAsync(ICampaignCharacterParticipationAttacher attacher, CampaignCharacterParticipationAttachRequest request, string intent, IReadOnlyList<string>? proceduresUsed, CancellationToken cancellationToken = default)
    {
        var result = await attacher.AttachAsync(request, intent, proceduresUsed, cancellationToken);
        return result.Attached
            ? ToolEnvelope.Success(new { result.CampaignId, result.ActorId, result.ParticipationId, Status = result.Status, NextAction = result.Next }, result.OperationId, result.Next)
            : ToolEnvelope.Failure(result.Problems[0].Code, result.Problems[0].Reason, result.Next, result.OperationId);
    }

    public async Task<ToolEnvelope> ContinuityAsync(Func<Task<CampaignContinuityResult>> operation)
    {
        var result = await operation();
        return result.Succeeded ? ToolEnvelope.Success(result, result.OperationId, result.Next) : ToolEnvelope.Failure(result.Problems[0].Code, result.Problems[0].Reason, result.Next, result.OperationId);
    }

    public async Task<ToolEnvelope> AttachQuestContextAsync(ICampaignQuestContextRunner runner, CampaignQuestContextRequest request, string intent, IReadOnlyList<string>? proceduresUsed, CancellationToken cancellationToken = default)
    {
        var result = await runner.AttachAsync(request, intent, proceduresUsed, cancellationToken);
        return result.Attached
            ? ToolEnvelope.Success(result, result.OperationId, result.Next)
            : ToolEnvelope.Failure(result.Problems[0].Code, result.Problems[0].Reason, result.Next, result.OperationId);
    }

    public Task<ToolEnvelope> ResumeAsync(ICampaignResumeReader reader, IOperationLog log, string campaignId, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "query", "", campaignId, ["procedure.campaign.chapter", "procedure.campaign.quest-context"], async () =>
        {
            var result = await reader.GetAsync(campaignId, cancellationToken);
            return result is null ? ToolOutcome.Fail("CAMPAIGN_NOT_FOUND", "campaignId does not name a readable active campaign.", "query(kind: \"entities\", id: \"...\")", "Campaign resume was unavailable.") : ToolOutcome.OkAbout(campaignId, result, "Returned trusted-host campaign resume.", "query(kind: \"campaign-resume\", id: \"" + campaignId + "\")");
        }, consumesReadEvidence: false);

    public Task<ToolEnvelope> ResumeSessionAsync(ICampaignSessionResumeReader reader, IOperationLog log, string campaignId, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "query", "", campaignId, ["procedure.campaign.session", "procedure.campaign.chapter"], async () =>
        {
            var result = await reader.GetAsync(campaignId, cancellationToken);
            return result.Resumed
                ? ToolOutcome.OkAbout(campaignId, new { result.Session, result.Campaign, NextAction = result.Next }, "Returned the current active-session header and trusted-host C3 context.", result.Next)
                : ToolOutcome.Fail(result.Problems[0].Code, result.Problems[0].Reason, result.Next, "Campaign session resume was unavailable.");
        }, consumesReadEvidence: false);

    public Task<ToolEnvelope> SessionRecapAsync(ICampaignSessionRecapReader reader, IOperationLog log, string sessionId, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "query", "", sessionId, ["procedure.campaign.session"], async () =>
        {
            var result = await reader.GetAsync(sessionId, cancellationToken);
            return result.Found
                ? ToolOutcome.OkAbout(result.SessionId, new { result.SessionId, result.CampaignId, result.Recap, NextAction = result.Next }, "Returned the immutable trusted-host factual session recap.", result.Next)
                : ToolOutcome.Fail(result.Problems[0].Code, result.Problems[0].Reason, result.Next, "Session factual recap was unavailable.");
        }, consumesReadEvidence: false);
}
