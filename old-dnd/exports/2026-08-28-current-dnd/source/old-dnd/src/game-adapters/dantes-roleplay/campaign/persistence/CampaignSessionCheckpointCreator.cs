using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Effects;
using DantesRoleplay.Operations;
using DantesRoleplay.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>S4 Slice 2 C8 root: stage one SP1 package and create its one named checkpoint graph.</summary>
public sealed class CampaignSessionCheckpointCreator(
    DantesRoleplayDbContext db,
    ICampaignSessionCheckpointValidator validator,
    ICampaignSessionEvidenceProducer producer,
    ISnapshotPackageStore packages,
    IEffectApplier effects,
    IOperationLog log) : ICampaignSessionCheckpointCreator
{
    private const string Procedure = "procedure.campaign.session";
    private const string Component = "game.core.campaign.session-checkpoint";
    private const string Link = "game.core.campaign.session.has-checkpoint";
    private readonly DantesRoleplayDbContext _db = db;
    private readonly ICampaignSessionCheckpointValidator _validator = validator;
    private readonly ICampaignSessionEvidenceProducer _producer = producer;
    private readonly ISnapshotPackageStore _packages = packages;
    private readonly IEffectApplier _effects = effects;
    private readonly IOperationLog _log = log;

    public async Task<CampaignSessionCheckpointResult> CreateAsync(CampaignSessionCheckpointRequest request, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default)
    {
        var sessionId = request?.SessionId ?? string.Empty;
        var cited = proceduresUsed is { Count: > 0 } ? proceduresUsed : [Procedure];
        var auditIntent = string.IsNullOrWhiteSpace(intent) ? "Capture campaign session checkpoint evidence." : intent;
        if (request is null || request.Operation != "checkpoint-session")
            return await RejectAsync(sessionId, null, Problem("INVALID_SESSION_CHECKPOINT_REQUEST", "payload", "Checkpoint capture requires operation checkpoint-session."), auditIntent, cited, cancellationToken);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var ready = await _validator.ValidateAsync(request with { Operation = "validate-session-checkpoint" }, cancellationToken);
            if (!ready.Valid) { await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); return await RejectAsync(sessionId, ready.CampaignId, ready.Problems[0], auditIntent, cited, CancellationToken.None); }
            var evidence = await _producer.ProduceAsync(sessionId, cancellationToken);
            if (!evidence.Produced) { await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); return await RejectAsync(sessionId, ready.CampaignId, evidence.Problems[0], auditIntent, cited, CancellationToken.None); }
            var operationId = Operation.NewId();
            var staged = await _packages.StageAsync(evidence.Proposal!, operationId, cancellationToken);
            if (!staged.Staged) { await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); return await RejectAsync(sessionId, ready.CampaignId, staged.Problems[0] is { } p ? new CampaignSessionProblem("SESSION_CHECKPOINT_CAPTURE_FAILED", p.Path, p.Reason, p.Recovery) : Problem("SESSION_CHECKPOINT_CAPTURE_FAILED", "sessionId", "Evidence staging was rejected."), auditIntent, cited, CancellationToken.None); }
            var checkpointId = "checkpoint." + Guid.NewGuid().ToString("n");
            var derived = Effects(checkpointId, sessionId, evidence.CampaignId!, evidence.WorldId!, staged.Reference!);
            var dry = await _effects.ApplyAsync(derived, dryRun: true, cancellationToken: cancellationToken);
            if (!dry.Valid || dry.Blocked) { await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); return await RejectAsync(sessionId, ready.CampaignId, Problem("SESSION_CHECKPOINT_EFFECTS_REJECTED", "payload", "Derived checkpoint effects were rejected."), auditIntent, cited, CancellationToken.None); }
            var applied = await _effects.ApplyAsync(derived, cancellationToken: cancellationToken, rootOperationId: operationId);
            if (!applied.Valid || !applied.Applied || applied.Blocked) { await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); return await RejectAsync(sessionId, ready.CampaignId, Problem("SESSION_CHECKPOINT_EFFECTS_REJECTED", "payload", "Derived checkpoint effects were not applied."), auditIntent, cited, CancellationToken.None); }
            var operation = await _log.RecordAsync("commit", $"Captured checkpoint '{checkpointId}' for ended session '{sessionId}'.", true, auditIntent, checkpointId, cited, consumesReadEvidence: true, cancellationToken: cancellationToken, id: operationId);
            await transaction.CommitAsync(cancellationToken);
            return new("created", sessionId, evidence.CampaignId, evidence.WorldId, checkpointId, staged.Reference!.ScopeContractVersion, staged.Reference.Availability, operation.Id, [], $"query(kind: \"session-checkpoint\", id: \"{checkpointId}\")");
        }
        catch (OperationCanceledException) { await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); throw; }
        catch (Exception) { await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); return await RejectAsync(sessionId, null, Problem("SESSION_CHECKPOINT_CAPTURE_FAILED", "payload", "Checkpoint evidence could not be captured."), auditIntent, cited, CancellationToken.None); }
    }

    private static IReadOnlyList<Effect> Effects(string checkpointId, string sessionId, string campaignId, string worldId, SnapshotPackageReference reference) =>
    [
        new() { Type = EffectType.EntityCreate, EntityId = checkpointId, Name = "Session checkpoint evidence" },
        new() { Type = EffectType.ComponentAdd, EntityId = checkpointId, DefinitionId = Component, Data = JsonSerializer.Serialize(new { protocolVersion = "session.s4.evidence-only.v1", sessionId, campaignId, worldId, package = new { id = reference.Id, scopeContractId = reference.ScopeContractId, scopeContractVersion = reference.ScopeContractVersion, producerId = reference.ProducerId, producerVersion = reference.ProducerVersion, contentEncoding = reference.ContentEncoding, boundaryFingerprint = reference.BoundaryFingerprint, digestAlgorithm = reference.DigestAlgorithm, contentDigest = reference.ContentDigest, byteCount = reference.ByteCount, capturedAt = reference.CapturedAt, availability = reference.Availability } }) },
        new() { Type = EffectType.RelationshipCreate, EntityId = sessionId, ToEntityId = checkpointId, Kind = Link, Data = "{}" }
    ];
    private async Task<CampaignSessionCheckpointResult> RejectAsync(string sessionId, string? campaignId, CampaignSessionProblem problem, string intent, IReadOnlyList<string> cited, CancellationToken cancellationToken)
    { var operation = await _log.RecordAsync("commit", "Campaign session checkpoint was rejected; no checkpoint state changed.", false, intent, sessionId, cited, problem.Code, consumesReadEvidence: true, cancellationToken: cancellationToken); return new("rejected", sessionId, campaignId, null, null, null, null, operation.Id, [problem], "commit(kind: \"campaign\", payload: \"{\\\"operation\\\":\\\"validate-session-checkpoint\\\",\\\"sessionId\\\":\\\"...\\\",\\\"expectedStatus\\\":\\\"ended\\\"}\")"); }
    private static CampaignSessionProblem Problem(string code, string path, string reason) => new(code, path, reason, "Validate the ended session checkpoint boundary again.");
}
