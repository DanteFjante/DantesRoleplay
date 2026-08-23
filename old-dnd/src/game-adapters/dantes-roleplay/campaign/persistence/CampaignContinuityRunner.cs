using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Campaign;
using DantesRoleplay.Effects;
using DantesRoleplay.Operations;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>C3's sole chapter/arc lifecycle owner. All successful changes are one derived batch.</summary>
public sealed class CampaignContinuityRunner(DantesRoleplayDbContext db, IWorldStore world, IEffectApplier effects, IOperationLog log) : ICampaignContinuityRunner
{
    private const string Procedure = "procedure.campaign.chapter";
    private const string ChapterDefinition = "game.core.campaign.chapter";
    private const string ArcDefinition = "game.core.campaign.arc";
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly DantesRoleplayDbContext _db = db; private readonly IWorldStore _world = world; private readonly IEffectApplier _effects = effects; private readonly IOperationLog _log = log;

    public Task<CampaignContinuityResult> InitializeAsync(CampaignContinuitySeed seed, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default) =>
        RunAsync(seed?.CampaignId ?? string.Empty, intent, proceduresUsed, cancellationToken, async state =>
        {
            if (!ValidSeed(seed, out var problem)) return Draft.Fail(problem!);
            var confirmedSeed = seed!;
            if (state.Chapters.Count != 0 || state.Arcs.Count != 0) return Draft.Fail("CONTINUITY_ALREADY_INITIALIZED", "campaignId", "Campaign already has C3 chapter or arc state.");
            var chapterId = ChildId(confirmedSeed.CampaignId, confirmedSeed.Chapter.LocalKey); var arcId = ChildId(confirmedSeed.CampaignId, confirmedSeed.Arc.LocalKey);
            if (await _world.GetEntityAsync(chapterId) is not null || await _world.GetEntityAsync(arcId) is not null) return Draft.Fail("CHILD_ID_TAKEN", "seed", "A derived chapter or arc id is already in use.");
            return new Draft(chapterId, arcId,
            [
                Create(chapterId, confirmedSeed.Chapter.Title), Add(chapterId, ChapterDefinition, ChapterData("active", confirmedSeed.Chapter.Title, confirmedSeed.Chapter.PartyQuestion, confirmedSeed.Chapter.GmContext)),
                Create(arcId, confirmedSeed.Arc.Title), Add(arcId, ArcDefinition, ArcData("active", confirmedSeed.Arc.Title, confirmedSeed.Arc.PartyStake, confirmedSeed.Arc.GmContext)),
                Relate(confirmedSeed.CampaignId, chapterId, "game.core.campaign.has-chapter"), Relate(confirmedSeed.CampaignId, arcId, "game.core.campaign.has-arc"), Relate(chapterId, arcId, "game.core.campaign.chapter.in-arc")]);
        });

    public Task<CampaignContinuityResult> AdvanceAsync(string campaignId, string chapterId, string expectedStatus, string closingSummary, CampaignNextChapter nextChapter, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default) =>
        RunAsync(campaignId, intent, proceduresUsed, cancellationToken, async state =>
        {
            if (expectedStatus != "active" || !Text(closingSummary, 1000)) return Draft.Fail("INVALID_ADVANCE", "payload", "Advance needs expected active state and a closing summary.");
            if (!ValidNext(nextChapter, out var problem)) return Draft.Fail(problem!);
            if (!state.OneActive(out var current, out var arc) || current.Id != chapterId) return Draft.Fail("STALE_CHAPTER", "chapterId", "chapterId is not the campaign's sole active chapter.");
            var nextId = ChildId(campaignId, nextChapter.LocalKey); if (await _world.GetEntityAsync(nextId) is not null) return Draft.Fail("CHILD_ID_TAKEN", "nextChapter.localKey", "The derived next chapter id is already in use.");
            return new Draft(nextId, arc.Id,
            [Set(current.Id, ChapterDefinition, ChapterData("closed", current.Title, current.PartyQuestion, current.GmContext, closingSummary)), Create(nextId, nextChapter.Title), Add(nextId, ChapterDefinition, ChapterData("active", nextChapter.Title, nextChapter.PartyQuestion, nextChapter.GmContext)), Relate(campaignId, nextId, "game.core.campaign.has-chapter"), Relate(nextId, arc.Id, "game.core.campaign.chapter.in-arc")]);
        });

    public Task<CampaignContinuityResult> CloseAsync(string campaignId, string chapterId, string expectedStatus, string closingSummary, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default) =>
        RunAsync(campaignId, intent, proceduresUsed, cancellationToken, state =>
        {
            if (expectedStatus != "active" || !Text(closingSummary, 1000)) return Task.FromResult(Draft.Fail("INVALID_CLOSE", "payload", "Close needs expected active state and a trimmed closing summary."));
            var chapter = state.Chapters.SingleOrDefault(x => x.Id == chapterId && x.Status == "active");
            return Task.FromResult(chapter is null ? Draft.Fail("STALE_CHAPTER", "chapterId", "chapterId is not active in this campaign.") : new Draft(chapter.Id, state.ActiveArc?.Id, [Set(chapter.Id, ChapterDefinition, ChapterData("closed", chapter.Title, chapter.PartyQuestion, chapter.GmContext, closingSummary))]));
        });

    public Task<CampaignContinuityResult> ConcludeArcAsync(string campaignId, string arcId, string expectedStatus, string outcome, string closingSummary, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default) =>
        RunAsync(campaignId, intent, proceduresUsed, cancellationToken, state =>
        {
            if (expectedStatus != "active" || outcome is not ("resolved" or "abandoned") || !Text(closingSummary, 1000)) return Task.FromResult(Draft.Fail("INVALID_ARC_CONCLUSION", "payload", "Conclude requires expected active state, resolved or abandoned outcome, and a closing summary."));
            var arc = state.Arcs.SingleOrDefault(x => x.Id == arcId && x.Status == "active");
            return Task.FromResult(arc is null ? Draft.Fail("STALE_ARC", "arcId", "arcId is not active in this campaign.") : new Draft(state.ActiveChapter?.Id, arc.Id, [Set(arc.Id, ArcDefinition, ArcData(outcome, arc.Title, arc.PartyStake, arc.GmContext, closingSummary))]));
        });

    private async Task<CampaignContinuityResult> RunAsync(string campaignId, string intent, IReadOnlyList<string>? proceduresUsed, CancellationToken cancellationToken, Func<State, Task<Draft>> make)
    {
        var cited = proceduresUsed is { Count: > 0 } ? proceduresUsed : new[] { Procedure }; var auditIntent = string.IsNullOrWhiteSpace(intent) ? "Change campaign continuity." : intent;
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var loaded = await LoadAsync(campaignId, cancellationToken);
            if (loaded.State is null) { await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); return await RejectAsync(campaignId, loaded.Problem!, auditIntent, cited, CancellationToken.None); }
            var draft = await make(loaded.State);
            if (draft.Problem is not null) { await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); return await RejectAsync(campaignId, draft.Problem, auditIntent, cited, CancellationToken.None); }
            var dry = await _effects.ApplyAsync(draft.Effects, true, cancellationToken); if (!dry.Valid || dry.Blocked) { await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); return await RejectAsync(campaignId, new("CONTINUITY_EFFECTS_REJECTED", "payload", Failure(dry), "Correct the request and retry."), auditIntent, cited, CancellationToken.None); }
            var operationId = Operation.NewId(); var applied = await _effects.ApplyAsync(draft.Effects, false, cancellationToken, operationId);
            if (!applied.Valid || !applied.Applied || applied.Blocked) { await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); return await RejectAsync(campaignId, new("CONTINUITY_EFFECTS_REJECTED", "payload", Failure(applied), "Correct the request and retry."), auditIntent, cited, CancellationToken.None); }
            var operation = await _log.RecordAsync("commit", $"Changed continuity for campaign '{campaignId}'.", true, auditIntent, campaignId, cited, consumesReadEvidence: true, cancellationToken: cancellationToken, id: operationId);
            await transaction.CommitAsync(cancellationToken); return new("succeeded", campaignId, draft.ChapterId, draft.ArcId, operation.Id, applied.AcceptedEvents.Count, [], $"query(kind: \"campaign-resume\", id: \"{campaignId}\")");
        }
        catch (OperationCanceledException) { await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); throw; }
        catch (Exception ex) { await transaction.RollbackAsync(CancellationToken.None); _db.ChangeTracker.Clear(); return await RejectAsync(campaignId, new("CONTINUITY_FAILED", "payload", ex.Message, "Correct the request and retry."), auditIntent, cited, CancellationToken.None); }
    }

    private async Task<(State? State, CampaignContinuityProblem? Problem)> LoadAsync(string campaignId, CancellationToken ct)
    {
        var campaign = await _world.GetEntityAsync(campaignId, ct); if (campaign is null || !Status(Component(campaign, "game.core.campaign.root"), "active")) return (null, new("INVALID_CAMPAIGN", "campaignId", "campaignId must name an active C2 campaign.", "Create or select an active campaign."));
        var links = await _world.GetRelationshipsAsync(campaignId, false, ct); if (links.Count(x => x.Kind == "game.core.campaign.in-world") != 1 || links.Count(x => x.Kind == "game.core.campaign.references") is < 4 or > 12) return (null, new("BROKEN_CAMPAIGN_GRAPH", "campaignId", "Campaign lacks its required C2 world/reference links.", "Repair the campaign through its owning workflow."));
        var chapters = new List<Chapter>(); foreach (var link in links.Where(x => x.Kind == "game.core.campaign.has-chapter")) { var entity = await _world.GetEntityAsync(link.ToEntityId, ct); var parsed = Chapter.Parse(entity?.Id, Component(entity, ChapterDefinition)); if (parsed is null) return (null, new("BROKEN_CHAPTER", "campaign", "A campaign chapter link has invalid state.", "Repair the campaign through its owning workflow.")); chapters.Add(parsed); }
        var arcs = new List<Arc>(); foreach (var link in links.Where(x => x.Kind == "game.core.campaign.has-arc")) { var entity = await _world.GetEntityAsync(link.ToEntityId, ct); var parsed = Arc.Parse(entity?.Id, Component(entity, ArcDefinition)); if (parsed is null) return (null, new("BROKEN_ARC", "campaign", "A campaign arc link has invalid state.", "Repair the campaign through its owning workflow.")); arcs.Add(parsed); }
        foreach (var chapter in chapters) { var arcLinks = await _world.GetRelationshipsAsync(chapter.Id, false, ct); if (arcLinks.Count(x => x.Kind == "game.core.campaign.chapter.in-arc") != 1 || !arcs.Any(x => x.Id == arcLinks.Single(x => x.Kind == "game.core.campaign.chapter.in-arc").ToEntityId)) return (null, new("BROKEN_CHAPTER_ARC_LINK", "campaign", "Every campaign chapter must have exactly one same-campaign arc link.", "Repair the campaign through its owning workflow.")); }
        return (new State(campaignId, chapters, arcs), null);
    }

    private async Task<CampaignContinuityResult> RejectAsync(string campaignId, CampaignContinuityProblem problem, string intent, IReadOnlyList<string> cited, CancellationToken ct)
    { var operation = await _log.RecordAsync("commit", "Campaign continuity change was rejected; no state changed.", false, intent, campaignId, cited, problem.Code, consumesReadEvidence: true, cancellationToken: ct); return new("rejected", campaignId, null, null, operation.Id, null, [problem], $"query(kind: \"campaign-resume\", id: \"{campaignId}\")"); }
    private static Effect Create(string id, string name) => new() { Type = EffectType.EntityCreate, EntityId = id, Name = name };
    private static Effect Add(string id, string definition, string data) => new() { Type = EffectType.ComponentAdd, EntityId = id, DefinitionId = definition, Data = data };
    private static Effect Set(string id, string definition, string data) => new() { Type = EffectType.ComponentSet, EntityId = id, DefinitionId = definition, Data = data };
    private static Effect Relate(string from, string to, string kind) => new() { Type = EffectType.RelationshipCreate, EntityId = from, ToEntityId = to, Kind = kind, Data = "{}" };
    private static string ChapterData(string status, string title, string question, string? gm, string? closing = null) => JsonSerializer.Serialize(new { status, title, partyQuestion = question, gmContext = gm, closingSummary = closing }, Json);
    private static string ArcData(string status, string title, string stake, string? gm, string? closing = null) => JsonSerializer.Serialize(new { status, title, partyStake = stake, gmContext = gm, closingSummary = closing }, Json);
    private static bool ValidSeed(CampaignContinuitySeed? seed, out CampaignContinuityProblem? problem) { if (seed is null || !Id(seed.CampaignId) || !ValidChapter(seed.Chapter) || !ValidArc(seed.Arc) || seed.Chapter.LocalKey == seed.Arc.LocalKey) { problem = new("INVALID_SEED", "seed", "Seed requires valid distinct chapter and arc local keys with trimmed text.", "Correct the seed and retry."); return false; } problem = null; return true; }
    private static bool ValidNext(CampaignNextChapter? next, out CampaignContinuityProblem? problem) { if (next is null || !Local(next.LocalKey, "chapter.") || !Text(next.Title, 160) || !Text(next.PartyQuestion, 500) || (next.GmContext is not null && !Text(next.GmContext, 1000))) { problem = new("INVALID_NEXT_CHAPTER", "nextChapter", "Next chapter requires a valid local key and trimmed title/question/context.", "Correct nextChapter and retry."); return false; } problem = null; return true; }
    private static bool ValidChapter(CampaignChapterSeed? x) => x is not null && Local(x.LocalKey, "chapter.") && Text(x.Title, 160) && Text(x.PartyQuestion, 500) && (x.GmContext is null || Text(x.GmContext, 1000));
    private static bool ValidArc(CampaignArcSeed? x) => x is not null && Local(x.LocalKey, "arc.") && Text(x.Title, 160) && Text(x.PartyStake, 500) && (x.GmContext is null || Text(x.GmContext, 1000));
    private static string ChildId(string campaignId, string localKey) => $"{campaignId}.{localKey}";
    private static bool Id(string? x) => !string.IsNullOrWhiteSpace(x) && x == x.Trim() && x == x.ToLowerInvariant(); private static bool Local(string? x, string prefix) => Id(x) && x!.StartsWith(prefix, StringComparison.Ordinal) && x.Length > prefix.Length && x.All(c => char.IsLower(c) || char.IsDigit(c) || c is '.' or '-'); private static bool Text(string? x, int max) => !string.IsNullOrWhiteSpace(x) && x == x.Trim() && x.Length <= max;
    private static string? Component(EntitySnapshot? entity, string definition) => entity?.Components.SingleOrDefault(x => x.DefinitionId == definition)?.Data;
    private static bool Status(string? json, string expected) { try { using var doc = JsonDocument.Parse(json ?? ""); return doc.RootElement.GetProperty("status").GetString() == expected; } catch { return false; } }
    private static string Failure(EffectResult r) => r.Blocked ? $"A guard blocked continuity: {r.BlockCode}: {r.BlockReason}" : r.Problems.Count == 0 ? "Derived effects did not apply." : string.Join(" ", r.Problems.Select(x => x.Problem));

    private sealed record Draft(string? ChapterId, string? ArcId, IReadOnlyList<Effect> Effects, CampaignContinuityProblem? Problem = null) { public static Draft Fail(string code, string path, string reason) => new(null, null, [], new(code, path, reason, "Correct the request and retry.")); public static Draft Fail(CampaignContinuityProblem problem) => new(null, null, [], problem); }
    private sealed record State(string CampaignId, IReadOnlyList<Chapter> Chapters, IReadOnlyList<Arc> Arcs) { public Chapter? ActiveChapter => Chapters.FirstOrDefault(x => x.Status == "active"); public Arc? ActiveArc => Arcs.FirstOrDefault(x => x.Status == "active"); public bool OneActive(out Chapter chapter, out Arc arc) { var activeChapters = Chapters.Where(x => x.Status == "active").ToList(); var activeArcs = Arcs.Where(x => x.Status == "active").ToList(); chapter = activeChapters.FirstOrDefault()!; arc = activeArcs.FirstOrDefault()!; return activeChapters.Count == 1 && activeArcs.Count == 1; } }
    private sealed record Chapter(string Id, string Status, string Title, string PartyQuestion, string? GmContext, string? ClosingSummary) { public static Chapter? Parse(string? id, string? json) => Read(json, (s, d) => new Chapter(id!, s, d.GetProperty("title").GetString()!, d.GetProperty("partyQuestion").GetString()!, Optional(d, "gmContext"), Optional(d, "closingSummary")), ["active", "closed"]); }
    private sealed record Arc(string Id, string Status, string Title, string PartyStake, string? GmContext, string? ClosingSummary) { public static Arc? Parse(string? id, string? json) => Read(json, (s, d) => new Arc(id!, s, d.GetProperty("title").GetString()!, d.GetProperty("partyStake").GetString()!, Optional(d, "gmContext"), Optional(d, "closingSummary")), ["active", "resolved", "abandoned"]); }
    private static T? Read<T>(string? json, Func<string, JsonElement, T> make, IReadOnlyList<string> allowed) where T : class { if (string.IsNullOrWhiteSpace(json)) return null; try { using var d = JsonDocument.Parse(json); var root = d.RootElement; var status = root.GetProperty("status").GetString(); return status is not null && allowed.Contains(status) && Text(root.GetProperty("title").GetString(), 160) ? make(status, root) : null; } catch { return null; } }
    private static string? Optional(JsonElement root, string property) => root.TryGetProperty(property, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
}
