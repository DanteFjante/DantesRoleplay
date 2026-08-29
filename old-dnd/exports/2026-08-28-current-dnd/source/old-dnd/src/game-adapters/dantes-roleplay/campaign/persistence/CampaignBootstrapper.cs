using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Effects;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// C2's single transaction owner. It turns an already reviewed C1 blueprint into the small,
/// closed structural batch that represents a campaign, or leaves no campaign trace at all.
/// </summary>
public sealed class CampaignBootstrapper(
    DantesRoleplayDbContext db,
    ICampaignBlueprintValidator validator,
    IEffectApplier effects,
    IOperationLog log) : ICampaignBootstrapper
{
    private const string Procedure = "procedure.campaign.create";
    private readonly DantesRoleplayDbContext _db = db;
    private readonly ICampaignBlueprintValidator _validator = validator;
    private readonly IEffectApplier _effects = effects;
    private readonly IOperationLog _log = log;

    public async Task<CampaignCreateResult> CreateAsync(CampaignBlueprint blueprint, string reviewFingerprint, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default)
    {
        var cited = proceduresUsed is { Count: > 0 } ? proceduresUsed : new[] { Procedure };
        var auditIntent = string.IsNullOrWhiteSpace(intent) ? "Create reviewed campaign." : intent;
        if (!Fingerprint(reviewFingerprint))
            return await RejectAsync(blueprint?.CampaignId ?? string.Empty, "INVALID_REVIEW_FINGERPRINT", "reviewFingerprint must be a 64-character lowercase hexadecimal value.", "reviewFingerprint", auditIntent, cited, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var review = await _validator.ValidateAsync(blueprint, cancellationToken);
            if (!review.Valid)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
                return await RejectAsync(blueprint?.CampaignId ?? string.Empty, review.Problems, auditIntent, cited, CancellationToken.None);
            }

            if (!string.Equals(review.ReviewFingerprint, reviewFingerprint, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
                return await RejectAsync(blueprint.CampaignId, "STALE_REVIEW", "The blueprint or its referenced world evidence changed after validation.", "reviewFingerprint", auditIntent, cited, CancellationToken.None);
            }

            var derived = Effects(blueprint, review);
            var dryRun = await _effects.ApplyAsync(derived, dryRun: true, cancellationToken: cancellationToken);
            if (!dryRun.Valid || dryRun.Blocked)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
                return await RejectAsync(blueprint.CampaignId, "CAMPAIGN_EFFECTS_REJECTED", EffectFailure(dryRun), "blueprint", auditIntent, cited, CancellationToken.None);
            }

            var operationId = Operation.NewId();
            var applied = await _effects.ApplyAsync(derived, dryRun: false, cancellationToken: cancellationToken, rootOperationId: operationId);
            if (!applied.Valid || !applied.Applied || applied.Blocked)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
                return await RejectAsync(blueprint.CampaignId, "CAMPAIGN_EFFECTS_REJECTED", EffectFailure(applied), "blueprint", auditIntent, cited, CancellationToken.None);
            }

            var operation = await _log.RecordAsync(
                "commit",
                $"Created campaign '{blueprint.CampaignId}' from reviewed existing-world references.",
                success: true,
                intent: auditIntent,
                subject: blueprint.CampaignId,
                proceduresCited: cited,
                consumesReadEvidence: true,
                cancellationToken: cancellationToken,
                id: operationId);
            await transaction.CommitAsync(cancellationToken);
            return new("created", blueprint.CampaignId, blueprint.ExistingWorldId, reviewFingerprint, review.ResolvedReferences.Count, applied.AcceptedEvents.Count, operation.Id, [], $"query(kind: \"entities\", id: \"{blueprint.CampaignId}\")");
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return await RejectAsync(blueprint?.CampaignId ?? string.Empty, "CAMPAIGN_CREATE_FAILED", ex.Message, "blueprint", auditIntent, cited, CancellationToken.None);
        }
    }

    private static IReadOnlyList<Effect> Effects(CampaignBlueprint blueprint, CampaignValidationResult review)
    {
        var root = JsonSerializer.Serialize(new
        {
            status = "active", title = blueprint.Title, premise = blueprint.Premise,
            partyGoals = blueprint.PartyGoals, toneAndBoundaries = blueprint.ToneAndBoundaries,
            rulesetScope = "dnd2024", creationMethod = "manual", reviewFingerprint = review.ReviewFingerprint
        });
        var effects = new List<Effect>
        {
            new() { Type = EffectType.EntityCreate, EntityId = blueprint.CampaignId, Name = blueprint.Title },
            new() { Type = EffectType.ComponentAdd, EntityId = blueprint.CampaignId, DefinitionId = "game.core.campaign.root", Data = root },
            new() { Type = EffectType.RelationshipCreate, EntityId = blueprint.CampaignId, ToEntityId = blueprint.ExistingWorldId, Kind = "game.core.campaign.in-world", Data = "{}" }
        };
        effects.AddRange(review.ResolvedReferences.Select(reference => new Effect
        {
            Type = EffectType.RelationshipCreate, EntityId = blueprint.CampaignId, ToEntityId = reference.EntityId,
            Kind = "game.core.campaign.references", Data = JsonSerializer.Serialize(new { role = reference.Role, audience = reference.Audience })
        }));
        return effects;
    }

    private async Task<CampaignCreateResult> RejectAsync(string campaignId, string code, string reason, string path, string intent, IReadOnlyList<string> cited, CancellationToken cancellationToken) =>
        await RejectAsync(campaignId, [new CampaignProblem(code, path, reason, "Correct the request and validate again.")], intent, cited, cancellationToken);

    private async Task<CampaignCreateResult> RejectAsync(string campaignId, IReadOnlyList<CampaignProblem> problems, string intent, IReadOnlyList<string> cited, CancellationToken cancellationToken)
    {
        var operation = await _log.RecordAsync("commit", "Campaign creation was rejected; no campaign state was created.", false, intent, campaignId, cited, problems[0].Code, consumesReadEvidence: true, cancellationToken: cancellationToken);
        return new("rejected", campaignId, null, null, null, null, operation.Id, problems, "commit(kind: \"campaign\", payload: \"{\\\"operation\\\":\\\"validate\\\",\\\"blueprint\\\":{...}}\")");
    }

    private static bool Fingerprint(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string EffectFailure(EffectResult result) => result.Blocked ? $"A guard blocked campaign creation: {result.BlockCode}: {result.BlockReason}" : result.Problems.Count == 0 ? "The derived campaign effects were not applied." : string.Join(" ", result.Problems.Select(x => x.Problem));
}
