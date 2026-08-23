using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Effects;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>S3 Slice 2's C8 root: one recap add and one complete active-to-ended replacement.</summary>
public sealed class CampaignSessionEnder(
    DantesRoleplayDbContext db,
    ICampaignSessionEndValidator validator,
    IEffectApplier effects,
    IOperationLog log) : ICampaignSessionEnder
{
    private const string Procedure = "procedure.campaign.session";
    private const string Session = "game.core.campaign.session";
    private const string Recap = "game.core.campaign.session-recap";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly DantesRoleplayDbContext _db = db;
    private readonly ICampaignSessionEndValidator _validator = validator;
    private readonly IEffectApplier _effects = effects;
    private readonly IOperationLog _log = log;

    public async Task<CampaignSessionEndResult> EndAsync(CampaignSessionEndRequest request, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default)
    {
        var sessionId = request?.SessionId ?? string.Empty;
        var cited = proceduresUsed is { Count: > 0 } ? proceduresUsed : [Procedure];
        var auditIntent = string.IsNullOrWhiteSpace(intent) ? "End campaign session." : intent;
        if (request is null || request.Operation != "end-session")
            return await RejectAsync(sessionId, null, Problem("INVALID_SESSION_END_REQUEST", "payload", "Session end requires operation end-session."), auditIntent, cited, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // The resolver reads C3 only after the root transaction exists. It never consumes a preview.
            var readiness = await _validator.ValidateAsync(request with { Operation = "validate-session-end" }, cancellationToken);
            if (!readiness.Valid)
            {
                await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
                return await RejectAsync(request.SessionId, readiness.CampaignId, readiness.Problems[0], auditIntent, cited, CancellationToken.None);
            }

            var derived = Effects(request.SessionId, readiness.Ordinal!.Value, readiness.Recap!);
            var dry = await _effects.ApplyAsync(derived, dryRun: true, cancellationToken: cancellationToken);
            if (!dry.Valid || dry.Blocked)
            {
                await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
                return await RejectAsync(request.SessionId, readiness.CampaignId, Problem("SESSION_END_EFFECTS_REJECTED", "payload", Failure(dry)), auditIntent, cited, CancellationToken.None);
            }

            var operationId = Operation.NewId();
            var applied = await _effects.ApplyAsync(derived, dryRun: false, cancellationToken: cancellationToken, rootOperationId: operationId);
            if (!applied.Valid || !applied.Applied || applied.Blocked)
            {
                await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
                return await RejectAsync(request.SessionId, readiness.CampaignId, Problem("SESSION_END_EFFECTS_REJECTED", "payload", Failure(applied)), auditIntent, cited, CancellationToken.None);
            }

            var operation = await _log.RecordAsync("commit", $"Ended session '{request.SessionId}' for campaign '{readiness.CampaignId}'.", true, auditIntent, request.SessionId, cited, consumesReadEvidence: true, cancellationToken: cancellationToken, id: operationId);
            await transaction.CommitAsync(cancellationToken);
            return new("ended", request.SessionId, readiness.CampaignId, "active", "ended", true, ["arc", "chapter", "milestones"], operation.Id, [], $"query(kind: \"session-recap\", id: \"{request.SessionId}\")");
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); throw;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
            return await RejectAsync(sessionId, null, Problem("SESSION_END_FAILED", "payload", "Campaign session end could not be completed."), auditIntent, cited, CancellationToken.None);
        }
    }

    private static IReadOnlyList<Effect> Effects(string sessionId, int ordinal, CampaignSessionRecap recap) =>
    [
        new() { Type = EffectType.ComponentAdd, EntityId = sessionId, DefinitionId = Recap, Data = JsonSerializer.Serialize(recap, Json) },
        new() { Type = EffectType.ComponentSet, EntityId = sessionId, DefinitionId = Session, Data = JsonSerializer.Serialize(new { status = "ended", ordinal }) }
    ];

    private async Task<CampaignSessionEndResult> RejectAsync(string sessionId, string? campaignId, CampaignSessionProblem problem, string intent, IReadOnlyList<string> cited, CancellationToken cancellationToken)
    {
        var operation = await _log.RecordAsync("commit", "Campaign session end was rejected; no session state changed.", false, intent, sessionId, cited, problem.Code, consumesReadEvidence: true, cancellationToken: cancellationToken);
        return new("rejected", sessionId, campaignId, null, null, false, [], operation.Id, [problem], "commit(kind: \"campaign\", payload: \"{\\\"operation\\\":\\\"validate-session-end\\\",\\\"sessionId\\\":\\\"...\\\",\\\"expectedStatus\\\":\\\"active\\\"}\")");
    }

    private static CampaignSessionProblem Problem(string code, string path, string reason) => new(code, path, reason, "Correct the request and validate session closure again.");
    private static string Failure(EffectResult result) => result.Blocked ? $"A guard blocked session end: {result.BlockCode}: {result.BlockReason}" : result.Problems.Count == 0 ? "Derived session-end effects were not applied." : string.Join(" ", result.Problems.Select(problem => problem.Problem));
}
