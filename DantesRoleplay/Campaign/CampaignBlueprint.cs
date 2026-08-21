using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.World;

namespace DantesRoleplay.Campaign;

public sealed record CampaignReference(string EntityId, string Role, string Audience);
public sealed record CampaignChapter(string LocalKey, string PartyQuestion, string? GmContext = null);
public sealed record CampaignArc(string LocalKey, string PartyStake, string? GmContext = null);
public sealed record FutureQuestProblem(string Audience, string Summary);
public sealed record CampaignBlueprint(string CampaignId, string Title, string Premise, IReadOnlyList<string> PartyGoals, IReadOnlyList<string> ToneAndBoundaries, string RulesetScope, string ExistingWorldId, string StartingLocationId, IReadOnlyList<CampaignReference> References, CampaignChapter InitialChapter, CampaignArc InitialArc, FutureQuestProblem? FutureQuestShapedProblem = null);
public sealed record CampaignProblem(string Code, string Path, string Reason, string Recovery);
public sealed record CampaignReferenceEvidence(string EntityId, string Role, string Audience, string ComponentId);
public sealed record CampaignCreationCounts(int Entities, int RootComponents, int InWorldRelationships, int ReferenceRelationships);
public sealed record CampaignValidationResult(string Status, string CampaignId, string? WorldId, IReadOnlyList<CampaignReferenceEvidence> ResolvedReferences, CampaignCreationCounts? CreationCounts, IReadOnlyList<string> Warnings, IReadOnlyList<CampaignProblem> Problems, string? ReviewFingerprint)
{ public bool Valid => Status == "valid"; }
public interface ICampaignBlueprintValidator { Task<CampaignValidationResult> ValidateAsync(CampaignBlueprint blueprint, CancellationToken cancellationToken = default); }
public sealed record CampaignCreateResult(string Status, string CampaignId, string? WorldId, string? ReviewFingerprint, int? ReferenceCount, int? StructuralEventCount, string OperationId, IReadOnlyList<CampaignProblem> Problems, string Next)
{ public bool Created => Status == "created"; }
public interface ICampaignBootstrapper { Task<CampaignCreateResult> CreateAsync(CampaignBlueprint blueprint, string reviewFingerprint, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default); }
public sealed record CampaignChapterSeed(string LocalKey, string Title, string PartyQuestion, string? GmContext = null);
public sealed record CampaignArcSeed(string LocalKey, string Title, string PartyStake, string? GmContext = null);
public sealed record CampaignContinuitySeed(string CampaignId, CampaignChapterSeed Chapter, CampaignArcSeed Arc);
public sealed record CampaignNextChapter(string LocalKey, string Title, string PartyQuestion, string? GmContext = null);
public sealed record CampaignContinuityProblem(string Code, string Path, string Reason, string Recovery);
public sealed record CampaignContinuityResult(string Status, string CampaignId, string? ChapterId, string? ArcId, string OperationId, int? StructuralEventCount, IReadOnlyList<CampaignContinuityProblem> Problems, string Next)
{ public bool Succeeded => Status == "succeeded"; }
public interface ICampaignContinuityRunner
{
    Task<CampaignContinuityResult> InitializeAsync(CampaignContinuitySeed seed, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default);
    Task<CampaignContinuityResult> AdvanceAsync(string campaignId, string chapterId, string expectedStatus, string closingSummary, CampaignNextChapter nextChapter, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default);
    Task<CampaignContinuityResult> CloseAsync(string campaignId, string chapterId, string expectedStatus, string closingSummary, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default);
    Task<CampaignContinuityResult> ConcludeArcAsync(string campaignId, string arcId, string expectedStatus, string outcome, string closingSummary, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default);
}
public sealed record CampaignResumeReference(string EntityId, string Role, string Audience, string Name, string Summary, string? Visibility);
public sealed record CampaignResumeChapter(string Id, string Status, string Title, string PartyQuestion, string? GmContext);
public sealed record CampaignResumeArc(string Id, string Status, string Title, string PartyStake, string? GmContext);
public sealed record CampaignClosedChapterMilestone(string ChapterId, string Title, string ClosingSummary, DateTime Timestamp, int Sequence, string EventId);
public sealed record CampaignResume(string CampaignId, string Title, string Premise, IReadOnlyList<string> PartyGoals, IReadOnlyList<string> ToneAndBoundaries, string WorldId, CampaignResumeChapter? CurrentChapter, CampaignResumeArc? CurrentArc, IReadOnlyList<CampaignResumeReference> References, IReadOnlyList<CampaignClosedChapterMilestone> RecentMilestones, string TrustBoundary);
public interface ICampaignResumeReader { Task<CampaignResume?> GetAsync(string campaignId, CancellationToken cancellationToken = default); }

public sealed record CampaignSessionValidationRequest(string Operation, string CampaignId, string SessionId);
public sealed record CampaignSessionProblem(string Code, string Path, string Reason, string Recovery);
public sealed record CampaignSessionValidationResult(string Status, string CampaignId, string SessionId, int? Ordinal, IReadOnlyList<CampaignSessionProblem> Problems, string Next)
{ public bool Valid => Status == "valid"; }
public interface ICampaignSessionValidator
{
    Task<CampaignSessionValidationResult> ValidateAsync(CampaignSessionValidationRequest request, CancellationToken cancellationToken = default);
}
public sealed record CampaignSessionStartResult(string Status, string CampaignId, string SessionId, string? LifecycleStatus, int? Ordinal, bool ResumeAvailable, string OperationId, IReadOnlyList<CampaignSessionProblem> Problems, string Next)
{ public bool Started => Status == "started"; }
public interface ICampaignSessionStarter
{
    Task<CampaignSessionStartResult> StartAsync(CampaignSessionValidationRequest request, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default);
}
public sealed record CampaignSessionHeader(string SessionId, string CampaignId, string Status, int Ordinal);
public sealed record CampaignSessionResumeProblem(string Code, string Path, string Reason, string Recovery);
public sealed record CampaignSessionResumeResult(string Status, CampaignSessionHeader? Session, CampaignResume? Campaign, IReadOnlyList<CampaignSessionResumeProblem> Problems, string Next)
{ public bool Resumed => Status == "resumed"; }
public interface ICampaignSessionResumeReader
{
    Task<CampaignSessionResumeResult> GetAsync(string campaignId, CancellationToken cancellationToken = default);
}
public sealed record CampaignSessionEndRequest(string Operation, string SessionId, string ExpectedStatus);
public sealed record CampaignSessionRecapChapter(string Id, string Status, string Title, string PartyQuestion);
public sealed record CampaignSessionRecapArc(string Id, string Status, string Title, string PartyStake);
public sealed record CampaignSessionRecapMilestone(string ChapterId, string Title, string ClosingSummary, DateTime Timestamp, int Sequence);
public sealed record CampaignSessionRecap(string ProtocolVersion, CampaignSessionRecapChapter Chapter, CampaignSessionRecapArc Arc, IReadOnlyList<CampaignSessionRecapMilestone> Milestones);
public sealed record CampaignSessionEndValidationResult(string Status, string SessionId, string? CampaignId, int? Ordinal, CampaignSessionRecap? Recap, IReadOnlyList<CampaignSessionProblem> Problems, string Next)
{ public bool Valid => Status == "valid"; }
public interface ICampaignSessionEndValidator
{
    Task<CampaignSessionEndValidationResult> ValidateAsync(CampaignSessionEndRequest request, CancellationToken cancellationToken = default);
}
public sealed record CampaignSessionEndResult(string Status, string SessionId, string? CampaignId, string? PreviousStatus, string? CurrentStatus, bool RecapPresent, IReadOnlyList<string> RecapSectionKeys, string OperationId, IReadOnlyList<CampaignSessionProblem> Problems, string Next)
{ public bool Ended => Status == "ended"; }
public interface ICampaignSessionEnder
{
    Task<CampaignSessionEndResult> EndAsync(CampaignSessionEndRequest request, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default);
}
public sealed record CampaignSessionRecapReadResult(string Status, string SessionId, string? CampaignId, CampaignSessionRecap? Recap, IReadOnlyList<CampaignSessionProblem> Problems, string Next)
{ public bool Found => Status == "found"; }
public interface ICampaignSessionRecapReader
{
    Task<CampaignSessionRecapReadResult> GetAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>Read-only C1 validator. World records remain authoritative; it never creates campaign state.</summary>
public sealed class CampaignBlueprintValidator(IWorldStore world) : ICampaignBlueprintValidator
{
    private readonly IWorldStore _world = world;
    public async Task<CampaignValidationResult> ValidateAsync(CampaignBlueprint b, CancellationToken ct = default)
    {
        var problems = new List<CampaignProblem>(); var evidence = new List<CampaignReferenceEvidence>();
        if (b is null)
            return new("invalid", string.Empty, null, [], null, [], [new("INVALID_BLUEPRINT", "blueprint", "Campaign blueprint is required.", "Correct the blueprint and validate again.")], null);
        if (b.CampaignId is null || b.Title is null || b.Premise is null || b.RulesetScope is null || b.ExistingWorldId is null || b.StartingLocationId is null || b.PartyGoals is null || b.ToneAndBoundaries is null || b.References is null || b.InitialChapter is null || b.InitialArc is null || b.References.Any(x => x is null))
            return new("invalid", b.CampaignId ?? string.Empty, null, [], null, [], [new("INVALID_BLUEPRINT", "blueprint", "Campaign arrays and continuity seeds must be present and non-null.", "Correct the blueprint and validate again.")], null);
        if (!Id(b.CampaignId) || !b.CampaignId.StartsWith("campaign.", StringComparison.Ordinal) || b.CampaignId.Length is < 3 or > 100) Bad("INVALID_CAMPAIGN_ID", "campaignId", "Campaign id must be a trimmed lowercase campaign.* id.");
        if (!Text(b.Title, 160) || !Text(b.Premise, 1000) || b.PartyGoals.Count is < 1 or > 3 || b.PartyGoals.Any(x => !Text(x, 500)) || b.ToneAndBoundaries.Count is < 1 or > 8 || b.ToneAndBoundaries.Any(x => !Text(x, 300))) Bad("INVALID_CAMPAIGN_TEXT", "blueprint", "Title, premise, goals, and boundaries are not within their closed limits.");
        if (b.RulesetScope != "dnd2024") Bad("INVALID_RULESET_SCOPE", "rulesetScope", "The first campaign delivery supports dnd2024 only.");
        if (!Local(b.InitialChapter.LocalKey, "chapter.") || !Text(b.InitialChapter.PartyQuestion, 500) || !Local(b.InitialArc.LocalKey, "arc.") || !Text(b.InitialArc.PartyStake, 500) || b.InitialChapter.LocalKey == b.InitialArc.LocalKey) Bad("INVALID_CONTINUITY_SEED", "initialChapter/initialArc", "Chapter and arc local keys must be distinct valid local keys with nonblank open text.");
        if (b.FutureQuestShapedProblem is not null && (b.FutureQuestShapedProblem.Audience != "gm" || !Text(b.FutureQuestShapedProblem.Summary, 1000))) Bad("INVALID_FUTURE_QUEST", "futureQuestShapedProblem", "A future quest-shaped problem is GM-only planning prose.");
        var root = await _world.GetEntityAsync(b.ExistingWorldId, ct); if (root is null || !Active(Component(root, "game.core.world.root"))) Bad("INVALID_WORLD", "existingWorldId", "existingWorldId must name an active world root.");
        var start = await _world.GetEntityAsync(b.StartingLocationId, ct); if (start is null || !Active(Component(start, "game.core.world.location")) || !await InWorldAsync(start, b.ExistingWorldId, ct)) Bad("INVALID_START", "startingLocationId", "Starting location must be active and contained by the selected world.");
        if (b.References.Count is < 4 or > 12 || b.References.Select(x => x.EntityId).Distinct(StringComparer.Ordinal).Count() != b.References.Count) Bad("INVALID_REFERENCES", "references", "References must contain 4–12 unique entries.");
        foreach (var r in b.References)
        {
            var entity = await _world.GetEntityAsync(r.EntityId, ct); var component = ComponentFor(r.Role, entity);
            if (entity is null || component is null || r.Audience is not ("party" or "gm")) { Bad("INVALID_REFERENCE", $"references[{r.EntityId}]", "Reference role, audience, or entity is invalid."); continue; }
            if (r.Role == "start" && (r.EntityId != b.StartingLocationId || r.Audience != "party")) Bad("INVALID_START_REFERENCE", $"references[{r.EntityId}]", "Start must be the party-visible selected starting location.");
            if (r.Role == "knowledge" && r.Audience == "party" && !PartyKnowledge(component, entity)) Bad("HIDDEN_KNOWLEDGE", $"references[{r.EntityId}]", "Party knowledge cannot be secret, unrevealed clue, or GM-only material.");
            evidence.Add(new(r.EntityId, r.Role, r.Audience, component));
        }
        if (b.References.Count(x => x.Role == "start") != 1 || b.References.Count(x => x.Role == "npc") is < 2 or > 3 || b.References.Count(x => x.Role == "faction-stake") != 1) Bad("REFERENCE_CARDINALITY", "references", "Exactly one start, 2–3 NPCs, and one faction stake are required.");
        if (await _world.GetEntityAsync(b.CampaignId, ct) is not null) Bad("CAMPAIGN_ID_TAKEN", "campaignId", "Campaign id is already in use.");
        evidence = evidence.OrderBy(x => RoleRank(x.Role)).ThenBy(x => x.Audience == "party" ? 0 : 1).ThenBy(x => x.EntityId, StringComparer.Ordinal).ToList();
        if (problems.Count > 0) return new("invalid", b.CampaignId, root is null ? null : b.ExistingWorldId, evidence, null, [], problems, null);
        var canonical = JsonSerializer.Serialize(b) + "|" + string.Join("|", evidence.Select(x => $"{x.EntityId}:{x.Role}:{x.Audience}:{x.ComponentId}"));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new("valid", b.CampaignId, b.ExistingWorldId, evidence, new(1, 1, 1, evidence.Count), [], [], fingerprint);
        void Bad(string code, string path, string reason) => problems.Add(new(code, path, reason, "Correct the blueprint and validate again."));
    }
    private async Task<bool> InWorldAsync(EntitySnapshot entity, string root, CancellationToken ct) { for (var i = 0; i < 4 && entity.ContainerId is not null; i++) { if (entity.ContainerId == root) return true; var parent = await _world.GetEntityAsync(entity.ContainerId, ct); if (parent is null) return false; entity = parent; } return false; }
    private static string? ComponentFor(string role, EntitySnapshot? e) => e is null ? null : role switch { "start" when Active(Component(e, "game.core.world.location")) => "game.core.world.location", "npc" when Active(Component(e, "game.core.world.motive")) => "game.core.world.motive", "faction-stake" when Active(Component(e, "game.core.world.faction")) => "game.core.world.faction", "knowledge" when Knowledge(e, out var id) => id, _ => null };
    private static bool Knowledge(EntitySnapshot e, out string id) { foreach (var x in new[] { "game.core.world.fact", "game.core.world.rumour", "game.core.world.secret", "game.core.world.clue" }) if (Component(e, x) is not null) { id = x; return true; } id = ""; return false; }
    private static bool PartyKnowledge(string id, EntitySnapshot e) => id is "game.core.world.fact" or "game.core.world.rumour" && Visibility(Component(e, id)) is "public" or "party";
    private static bool Active(string? json) { try { using var d = JsonDocument.Parse(json ?? ""); return d.RootElement.TryGetProperty("status", out var x) && x.GetString() == "active"; } catch { return false; } }
    private static string? Visibility(string? json) { try { using var d = JsonDocument.Parse(json ?? ""); return d.RootElement.TryGetProperty("visibility", out var x) ? x.GetString() : null; } catch { return null; } }
    private static string? Component(EntitySnapshot e, string id) => e.Components.SingleOrDefault(x => x.DefinitionId == id)?.Data;
    private static bool Id(string? x) => !string.IsNullOrWhiteSpace(x) && x == x.Trim() && x == x.ToLowerInvariant(); private static bool Text(string? x, int max) => !string.IsNullOrWhiteSpace(x) && x == x.Trim() && x.Length <= max; private static bool Local(string? x, string prefix) => Id(x) && x!.StartsWith(prefix, StringComparison.Ordinal) && x.Length > prefix.Length; private static int RoleRank(string role) => role switch { "start" => 0, "npc" => 1, "faction-stake" => 2, "knowledge" => 3, _ => 4 };
}
