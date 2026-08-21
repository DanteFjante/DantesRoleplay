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

    public Task<ToolEnvelope> ResumeAsync(ICampaignResumeReader reader, IOperationLog log, string campaignId, CancellationToken cancellationToken = default) =>
        ToolRunner.RunAsync(log, "query", "", campaignId, ["procedure.campaign.chapter"], async () =>
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
}
