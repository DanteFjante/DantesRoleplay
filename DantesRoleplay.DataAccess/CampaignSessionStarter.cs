using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Effects;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Creates the three-record S1 session graph in one campaign-owned root transaction.</summary>
public sealed class CampaignSessionStarter(
    DantesRoleplayDbContext db,
    ICampaignSessionValidator validator,
    IEffectApplier effects,
    IOperationLog log) : ICampaignSessionStarter
{
    private const string Procedure = "procedure.campaign.session";
    private const string Session = "game.core.campaign.session";
    private const string HasSession = "game.core.campaign.has-session";
    private readonly DantesRoleplayDbContext _db = db;
    private readonly ICampaignSessionValidator _validator = validator;
    private readonly IEffectApplier _effects = effects;
    private readonly IOperationLog _log = log;

    public async Task<CampaignSessionStartResult> StartAsync(CampaignSessionValidationRequest request, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default)
    {
        var campaignId = request?.CampaignId ?? string.Empty;
        var cited = proceduresUsed is { Count: > 0 } ? proceduresUsed : [Procedure];
        var auditIntent = string.IsNullOrWhiteSpace(intent) ? "Start campaign session." : intent;
        if (request is null || request.Operation != "start-session")
            return await RejectAsync(campaignId, request?.SessionId ?? string.Empty, Problem("INVALID_SESSION_REQUEST", "payload", "Session start requires operation start-session."), auditIntent, cited, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var readiness = await _validator.ValidateAsync(request with { Operation = "validate-session" }, cancellationToken);
            if (!readiness.Valid)
            {
                await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
                return await RejectAsync(request.CampaignId, request.SessionId, readiness.Problems[0], auditIntent, cited, CancellationToken.None);
            }

            var derived = Effects(request.CampaignId, request.SessionId, readiness.Ordinal!.Value);
            var dry = await _effects.ApplyAsync(derived, dryRun: true, cancellationToken: cancellationToken);
            if (!dry.Valid || dry.Blocked)
            {
                await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
                return await RejectAsync(request.CampaignId, request.SessionId, Problem("SESSION_EFFECTS_REJECTED", "payload", Failure(dry)), auditIntent, cited, CancellationToken.None);
            }

            var operationId = Operation.NewId();
            var applied = await _effects.ApplyAsync(derived, dryRun: false, cancellationToken: cancellationToken, rootOperationId: operationId);
            if (!applied.Valid || !applied.Applied || applied.Blocked)
            {
                await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
                return await RejectAsync(request.CampaignId, request.SessionId, Problem("SESSION_EFFECTS_REJECTED", "payload", Failure(applied)), auditIntent, cited, CancellationToken.None);
            }

            var operation = await _log.RecordAsync("commit", $"Started session '{request.SessionId}' for campaign '{request.CampaignId}'.", true, auditIntent, request.SessionId, cited, consumesReadEvidence: true, cancellationToken: cancellationToken, id: operationId);
            await transaction.CommitAsync(cancellationToken);
            return new("started", request.CampaignId, request.SessionId, "active", readiness.Ordinal, true, operation.Id, [], $"query(kind: \"entities\", id: \"{request.SessionId}\")");
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); throw;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
            return await RejectAsync(campaignId, request.SessionId, Problem("SESSION_START_FAILED", "payload", "Campaign session start could not be completed."), auditIntent, cited, CancellationToken.None);
        }
    }

    private static IReadOnlyList<Effect> Effects(string campaignId, string sessionId, int ordinal) =>
    [
        new() { Type = EffectType.EntityCreate, EntityId = sessionId, Name = $"Session {ordinal}" },
        new() { Type = EffectType.ComponentAdd, EntityId = sessionId, DefinitionId = Session, Data = JsonSerializer.Serialize(new { status = "active", ordinal }) },
        new() { Type = EffectType.RelationshipCreate, EntityId = campaignId, ToEntityId = sessionId, Kind = HasSession, Data = "{}" }
    ];

    private async Task<CampaignSessionStartResult> RejectAsync(string campaignId, string sessionId, CampaignSessionProblem problem, string intent, IReadOnlyList<string> cited, CancellationToken cancellationToken)
    {
        var operation = await _log.RecordAsync("commit", "Campaign session start was rejected; no session state was created.", false, intent, campaignId, cited, problem.Code, consumesReadEvidence: true, cancellationToken: cancellationToken);
        return new("rejected", campaignId, sessionId, null, null, false, operation.Id, [problem], "commit(kind: \"campaign\", payload: \"{\\\"operation\\\":\\\"validate-session\\\",\\\"campaignId\\\":\\\"...\\\",\\\"sessionId\\\":\\\"...\\\"}\")");
    }

    private static CampaignSessionProblem Problem(string code, string path, string reason) => new(code, path, reason, "Correct the request and validate session readiness again.");
    private static string Failure(EffectResult result) => result.Blocked ? $"A guard blocked session start: {result.BlockCode}: {result.BlockReason}" : result.Problems.Count == 0 ? "Derived session effects were not applied." : string.Join(" ", result.Problems.Select(x => x.Problem));
}
