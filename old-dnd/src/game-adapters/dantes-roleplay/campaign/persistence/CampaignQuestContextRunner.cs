using DantesRoleplay.Campaign;
using DantesRoleplay.Effects;
using DantesRoleplay.Operations;
using DantesRoleplay.Quest;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.DataAccess;

/// <summary>C4's sole owner for adding campaign-side context links to an existing active quest.</summary>
public sealed class CampaignQuestContextRunner(
    DantesRoleplayDbContext db,
    IWorldStore world,
    IQuestSummaryReader quests,
    IEffectApplier effects,
    IOperationLog log) : ICampaignQuestContextRunner
{
    private const string Procedure = "procedure.campaign.quest-context";
    private const string ArcQuest = "game.core.campaign.arc.features-quest";
    private const string ChapterQuest = "game.core.campaign.chapter.features-quest";
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IWorldStore _world = world;
    private readonly IQuestSummaryReader _quests = quests;
    private readonly IEffectApplier _effects = effects;
    private readonly IOperationLog _log = log;

    public async Task<CampaignQuestContextResult> AttachAsync(
        CampaignQuestContextRequest request,
        string intent = "",
        IReadOnlyList<string>? proceduresUsed = null,
        CancellationToken cancellationToken = default)
    {
        var campaignId = request?.CampaignId ?? string.Empty;
        var cited = proceduresUsed is { Count: > 0 } ? proceduresUsed : [Procedure, "procedure.quest.inspect"];
        var auditIntent = string.IsNullOrWhiteSpace(intent) ? "Attach quest context to campaign continuity." : intent;
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var problem = await ValidateAsync(request, cancellationToken);
            if (problem is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
                return await RejectAsync(request, problem, auditIntent, cited);
            }
            var confirmed = request!;

            var arcLinks = await _world.GetRelationshipsAsync(confirmed.ArcId, true, cancellationToken);
            var chapterLinks = await _world.GetRelationshipsAsync(confirmed.ChapterId, true, cancellationToken);
            var sameArc = arcLinks.Where(link => link.FromEntityId == confirmed.ArcId && link.ToEntityId == confirmed.QuestId && link.Kind == ArcQuest).ToArray();
            var sameChapter = chapterLinks.Where(link => link.FromEntityId == confirmed.ChapterId && link.ToEntityId == confirmed.QuestId && link.Kind == ChapterQuest).ToArray();
            if (sameArc.Length > 1 || sameChapter.Length > 1)
                return await RollbackRejectAsync(confirmed, Problem("QUEST_CONTEXT_GRAPH_INVALID", "questId", "Quest context links are duplicated."), transaction, auditIntent, cited);
            if (sameArc.Length == 1 && sameChapter.Length == 1)
                return await RollbackRejectAsync(confirmed, Problem("QUEST_CONTEXT_REPLAY", "questId", "Both requested quest context links already exist."), transaction, auditIntent, cited);

            var effects = new List<Effect>();
            if (sameArc.Length == 0) effects.Add(Relate(confirmed.ArcId, confirmed.QuestId, ArcQuest));
            if (sameChapter.Length == 0) effects.Add(Relate(confirmed.ChapterId, confirmed.QuestId, ChapterQuest));
            var dry = await _effects.ApplyAsync(effects, true, cancellationToken);
            if (!dry.Valid || dry.Blocked)
                return await RollbackRejectAsync(confirmed, Problem("QUEST_CONTEXT_EFFECTS_REJECTED", "payload", "Derived quest context links were rejected."), transaction, auditIntent, cited);

            var operationId = Operation.NewId();
            var applied = await _effects.ApplyAsync(effects, false, cancellationToken, operationId);
            if (!applied.Valid || !applied.Applied || applied.Blocked)
                return await RollbackRejectAsync(confirmed, Problem("QUEST_CONTEXT_EFFECTS_REJECTED", "payload", "Derived quest context links did not apply."), transaction, auditIntent, cited);
            var operation = await _log.RecordAsync("commit", $"Attached quest '{confirmed.QuestId}' to campaign '{confirmed.CampaignId}' with {effects.Count} context link(s).", true, auditIntent, confirmed.CampaignId, cited, consumesReadEvidence: true, cancellationToken: cancellationToken, id: operationId);
            await transaction.CommitAsync(cancellationToken);
            return new("attached", confirmed.CampaignId, confirmed.ArcId, confirmed.ChapterId, confirmed.QuestId, operation.Id, applied.AcceptedEvents.Count, [], ReadFix(confirmed.CampaignId));
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return await RejectAsync(request, Problem("QUEST_CONTEXT_FAILED", "payload", "Quest context could not be attached."), auditIntent, cited);
        }
    }

    private async Task<CampaignQuestContextProblem?> ValidateAsync(CampaignQuestContextRequest? request, CancellationToken ct)
    {
        if (request is null || !Id(request.CampaignId, "campaign.") || !Id(request.ArcId, "campaign.") || !Id(request.ChapterId, "campaign.") || !Id(request.QuestId, "quest.") || request.ExpectedQuestStatus != "active")
            return Problem("INVALID_QUEST_CONTEXT", "payload", "Attach requires canonical campaign, arc, chapter, and quest ids plus expected active quest status.");
        if (await _quests.GetAsync(request.QuestId, ct) is null)
            return Problem("QUEST_CONTEXT_UNAVAILABLE", "questId", "questId must name a readable active quest with valid Q3 context.");
        var campaign = await _world.GetEntityAsync(request.CampaignId, ct);
        if (!Status(campaign, "game.core.campaign.root", "active")) return Problem("INVALID_CAMPAIGN", "campaignId", "campaignId must name an active campaign.");
        var campaignLinks = await _world.GetRelationshipsAsync(request.CampaignId, false, ct);
        if (campaignLinks.Count(link => link.Kind == "game.core.campaign.has-arc" && link.ToEntityId == request.ArcId) != 1 ||
            campaignLinks.Count(link => link.Kind == "game.core.campaign.has-chapter" && link.ToEntityId == request.ChapterId) != 1)
            return Problem("QUEST_CONTEXT_SCOPE_MISMATCH", "campaignId", "Arc and chapter must belong to the selected campaign.");
        if (!Status(await _world.GetEntityAsync(request.ArcId, ct), "game.core.campaign.arc", "active") || !Status(await _world.GetEntityAsync(request.ChapterId, ct), "game.core.campaign.chapter", "active", "closed"))
            return Problem("QUEST_CONTEXT_LIFECYCLE_INVALID", "arcId/chapterId", "Arc must be active and chapter must be active or closed.");
        var chapterArc = (await _world.GetRelationshipsAsync(request.ChapterId, false, ct)).Where(link => link.Kind == "game.core.campaign.chapter.in-arc").ToArray();
        if (chapterArc.Length != 1 || chapterArc[0].ToEntityId != request.ArcId)
            return Problem("QUEST_CONTEXT_SCOPE_MISMATCH", "chapterId", "Chapter must belong to the selected arc.");
        var questLinks = await _world.GetRelationshipsAsync(request.QuestId, false, ct);
        if (questLinks.Count(link => link.Kind == "game.core.quest.in-campaign" && link.ToEntityId == request.CampaignId) != 1 ||
            questLinks.Count(link => link.Kind == "game.core.quest.in-arc" && link.ToEntityId == request.ArcId) != 1 ||
            questLinks.Count(link => link.Kind == "game.core.quest.in-chapter" && link.ToEntityId == request.ChapterId) != 1)
            return Problem("QUEST_CONTEXT_SCOPE_MISMATCH", "questId", "Quest owner scope must match the selected campaign, arc, and chapter.");
        var allQuestLinks = await _world.GetRelationshipsAsync(request.QuestId, true, ct);
        var contextLinks = allQuestLinks.Where(link => link.Kind is ArcQuest or ChapterQuest).ToArray();
        if (contextLinks.Any(link => link.ToEntityId != request.QuestId || link.Data != "{}"))
            return Problem("QUEST_CONTEXT_GRAPH_INVALID", "questId", "Existing campaign quest context links have invalid direction or metadata.");
        var incomingArcs = contextLinks.Where(link => link.Kind == ArcQuest).ToArray();
        if (incomingArcs.Length > 1 || incomingArcs.Any(link => link.FromEntityId != request.ArcId))
            return Problem("QUEST_CONTEXT_ARC_CONFLICT", "arcId", "Quest is already attached to another campaign arc.");
        foreach (var existingChapterLink in contextLinks.Where(link => link.Kind == ChapterQuest))
        {
            if (!campaignLinks.Any(link => link.Kind == "game.core.campaign.has-chapter" && link.ToEntityId == existingChapterLink.FromEntityId) ||
                !questLinks.Any(link => link.Kind == "game.core.quest.in-chapter" && link.ToEntityId == existingChapterLink.FromEntityId))
                return Problem("QUEST_CONTEXT_GRAPH_INVALID", "questId", "An existing chapter context link is outside the quest's campaign owner scope.");
            var existingChapterArc = (await _world.GetRelationshipsAsync(existingChapterLink.FromEntityId, false, ct))
                .Where(link => link.Kind == "game.core.campaign.chapter.in-arc").ToArray();
            if (existingChapterArc.Length != 1 || existingChapterArc[0].ToEntityId != request.ArcId)
                return Problem("QUEST_CONTEXT_GRAPH_INVALID", "questId", "An existing chapter context link belongs to another campaign arc.");
        }
        return null;
    }

    private async Task<CampaignQuestContextResult> RollbackRejectAsync(CampaignQuestContextRequest request, CampaignQuestContextProblem problem, IDbContextTransaction transaction, string intent, IReadOnlyList<string> cited)
    {
        await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear();
        return await RejectAsync(request, problem, intent, cited);
    }
    private async Task<CampaignQuestContextResult> RejectAsync(CampaignQuestContextRequest? request, CampaignQuestContextProblem problem, string intent, IReadOnlyList<string> cited)
    {
        var campaignId = request?.CampaignId ?? string.Empty;
        var operation = await _log.RecordAsync("commit", "Campaign quest context attachment was rejected; no state changed.", false, intent, campaignId, cited, problem.Code, consumesReadEvidence: true, cancellationToken: CancellationToken.None);
        return new("rejected", campaignId, request?.ArcId ?? string.Empty, request?.ChapterId ?? string.Empty, request?.QuestId ?? string.Empty, operation.Id, null, [problem], ReadFix(campaignId));
    }
    private static Effect Relate(string from, string to, string kind) => new() { Type = EffectType.RelationshipCreate, EntityId = from, ToEntityId = to, Kind = kind, Data = "{}" };
    private static CampaignQuestContextProblem Problem(string code, string path, string reason) => new(code, path, reason, "Correct the named ids/status and retry the attach-quest-context operation.");
    private static string ReadFix(string campaignId) => $"query(kind: \"campaign-resume\", id: \"{campaignId}\")";
    private static bool Id(string? value, string prefix) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.StartsWith(prefix, StringComparison.Ordinal) && value.Length <= 200 && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static bool Status(EntitySnapshot? entity, string definition, params string[] states)
    {
        try { using var document = System.Text.Json.JsonDocument.Parse(entity?.Components.SingleOrDefault(component => component.DefinitionId == definition)?.Data ?? string.Empty); return states.Contains(document.RootElement.GetProperty("status").GetString()); }
        catch { return false; }
    }
}
