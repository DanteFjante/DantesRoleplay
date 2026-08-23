using DantesRoleplay.Campaign;
using DantesRoleplay.Effects;
using DantesRoleplay.Operations;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>C15 Slice 2's sole attach transaction owner; it never changes the attached actor.</summary>
public sealed class CampaignCharacterParticipationAttacher(
    DantesRoleplayDbContext db,
    IWorldStore world,
    IEffectApplier effects,
    IOperationLog log) : ICampaignCharacterParticipationAttacher
{
    private const string Procedure = "procedure.campaign.character-participation";
    private const string CampaignRoot = "game.core.campaign.root";
    private const string Participation = "game.core.campaign.character-participation";
    private const string HasParticipation = "game.core.campaign.has-character-participation";
    private const string ForActor = "game.core.campaign.character-participation.for-actor";
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IWorldStore _world = world;
    private readonly IEffectApplier _effects = effects;
    private readonly IOperationLog _log = log;

    public async Task<CampaignCharacterParticipationResult> AttachAsync(CampaignCharacterParticipationAttachRequest request, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default)
    {
        var cited = proceduresUsed is { Count: > 0 } ? proceduresUsed : new[] { Procedure };
        var campaignId = request?.CampaignId ?? string.Empty;
        var actorId = request?.ActorId ?? string.Empty;
        var auditIntent = string.IsNullOrWhiteSpace(intent) ? "Attach a character participation to a campaign." : intent;
        if (request is null || request.Operation != "attach-character-participation" || !Id(campaignId, "campaign.") || !Id(actorId, "actor."))
            return await RejectAsync(campaignId, actorId, "INVALID_PARTICIPATION_REQUEST", "payload", "Attachment requires exactly the attach operation and canonical campaign.* and actor.* ids.", auditIntent, cited, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var campaign = await _world.GetEntityAsync(campaignId, cancellationToken);
            if (campaign is null || !Active(Component(campaign, CampaignRoot)))
            {
                await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
                return await RejectAsync(campaignId, actorId, "CAMPAIGN_NOT_ACTIVE", "campaignId", "campaignId must name an active campaign root.", auditIntent, cited, CancellationToken.None);
            }
            if (await _world.GetEntityAsync(actorId, cancellationToken) is null)
            {
                await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
                return await RejectAsync(campaignId, actorId, "ACTOR_NOT_FOUND", "actorId", "actorId must name a pre-existing actor.", auditIntent, cited, CancellationToken.None);
            }

            var existing = (await _world.GetRelationshipsAsync(actorId, true, cancellationToken)).Where(x => x.Kind == ForActor && x.ToEntityId == actorId).ToArray();
            if (existing.Length != 0)
            {
                await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
                return await RejectAsync(campaignId, actorId, "ACTOR_ALREADY_ATTACHED", "actorId", "Actor already has campaign participation history and cannot be attached again by C15.", auditIntent, cited, CancellationToken.None);
            }

            var participationId = ParticipationId(campaignId, actorId);
            if (!Id(participationId, "campaign."))
            {
                await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
                return await RejectAsync(campaignId, actorId, "INVALID_PARTICIPATION_ID", "actorId", "The confirmed derived participation id exceeds the canonical id boundary.", auditIntent, cited, CancellationToken.None);
            }
            if (await _world.GetEntityAsync(participationId, cancellationToken) is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
                return await RejectAsync(campaignId, actorId, "PARTICIPATION_ID_TAKEN", "actorId", "The server-derived participation id is already in use.", auditIntent, cited, CancellationToken.None);
            }

            var bundle = new Effect[]
            {
                new() { Type = EffectType.EntityCreate, EntityId = participationId, Name = "Campaign character participation" },
                new() { Type = EffectType.ComponentAdd, EntityId = participationId, DefinitionId = Participation, Data = "{\"status\":\"active\"}" },
                new() { Type = EffectType.RelationshipCreate, EntityId = campaignId, ToEntityId = participationId, Kind = HasParticipation, Data = "{}" },
                new() { Type = EffectType.RelationshipCreate, EntityId = participationId, ToEntityId = actorId, Kind = ForActor, Data = "{}" }
            };
            var dry = await _effects.ApplyAsync(bundle, dryRun: true, cancellationToken: cancellationToken);
            if (!dry.Valid || dry.Blocked)
            {
                await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
                return await RejectAsync(campaignId, actorId, "PARTICIPATION_EFFECTS_REJECTED", "payload", Failure(dry), auditIntent, cited, CancellationToken.None);
            }

            var operationId = Operation.NewId();
            var applied = await _effects.ApplyAsync(bundle, dryRun: false, cancellationToken: cancellationToken, rootOperationId: operationId);
            if (!applied.Valid || !applied.Applied || applied.Blocked)
            {
                await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
                return await RejectAsync(campaignId, actorId, "PARTICIPATION_EFFECTS_REJECTED", "payload", Failure(applied), auditIntent, cited, CancellationToken.None);
            }
            var operation = await _log.RecordAsync("commit", $"Attached actor '{actorId}' to campaign '{campaignId}'.", true, auditIntent, campaignId, cited, consumesReadEvidence: true, cancellationToken: cancellationToken, id: operationId);
            await transaction.CommitAsync(cancellationToken);
            return new("attached", campaignId, actorId, participationId, operation.Id, [], $"query(kind: \"entities\", id: \"{participationId}\")");
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
            return await RejectAsync(campaignId, actorId, "PARTICIPATION_ATTACH_FAILED", "payload", ex.Message, auditIntent, cited, CancellationToken.None);
        }
    }

    private async Task<CampaignCharacterParticipationResult> RejectAsync(string campaignId, string actorId, string code, string path, string reason, string intent, IReadOnlyList<string> cited, CancellationToken ct)
    {
        var operation = await _log.RecordAsync("commit", "Campaign character participation attachment was rejected; no state changed.", false, intent, campaignId, cited, code, consumesReadEvidence: true, cancellationToken: ct);
        return new("rejected", campaignId, actorId, null, operation.Id, [new(code, path, reason, "Correct the request and retry campaign participation attachment.")], "commit(kind: \"campaign\", payload: \"{\\\"operation\\\":\\\"attach-character-participation\\\",...}\")");
    }

    private static string ParticipationId(string campaignId, string actorId) => $"{campaignId}.participation.{actorId}";
    private static bool Id(string? value, string prefix) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value.StartsWith(prefix, StringComparison.Ordinal) && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static string? Component(EntitySnapshot entity, string definitionId) => entity.Components.SingleOrDefault(x => x.DefinitionId == definitionId)?.Data;
    private static bool Active(string? json) { try { using var document = System.Text.Json.JsonDocument.Parse(json ?? string.Empty); return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && document.RootElement.TryGetProperty("status", out var status) && status.ValueKind == System.Text.Json.JsonValueKind.String && status.GetString() == "active"; } catch { return false; } }
    private static string Failure(EffectResult result) => result.Blocked ? $"A guard blocked participation attachment: {result.BlockCode}: {result.BlockReason}" : result.Problems.Count == 0 ? "Derived participation effects were not applied." : string.Join(" ", result.Problems.Select(x => x.Problem));
}
