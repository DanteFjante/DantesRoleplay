using System.Text.Json;
using DantesRoleplay.Effects;
using DantesRoleplay.Operations;
using DantesRoleplay.Quest;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Creates the deliberately narrow Q1 quest graph. Lifecycle changes belong to Q2.</summary>
public sealed class QuestCreator(
    DantesRoleplayDbContext db,
    IWorldStore world,
    IEffectApplier effects,
    IOperationLog log) : IQuestCreator
{
    private const string Procedure = "procedure.quest.create";
    private const string CampaignRoot = "game.core.campaign.root";
    private const string CampaignArc = "game.core.campaign.arc";
    private const string CampaignChapter = "game.core.campaign.chapter";
    private const string WorldRoot = "game.core.world.root";
    private const string Motive = "game.core.world.motive";
    private const string Location = "game.core.world.location";
    private const string Faction = "game.core.world.faction";
    private const string Fact = "game.core.world.fact";
    private const string Rumour = "game.core.world.rumour";
    private const string Secret = "game.core.world.secret";
    private const string Clue = "game.core.world.clue";

    private readonly DantesRoleplayDbContext _db = db;
    private readonly IWorldStore _world = world;
    private readonly IEffectApplier _effects = effects;
    private readonly IOperationLog _log = log;

    public async Task<QuestCreateResult> CreateAsync(
        QuestCreateRequest request,
        string intent = "",
        IReadOnlyList<string>? proceduresUsed = null,
        CancellationToken cancellationToken = default)
    {
        var cited = proceduresUsed is { Count: > 0 } ? proceduresUsed : [Procedure];
        var auditIntent = string.IsNullOrWhiteSpace(intent) ? "Create campaign quest." : intent;
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var problem = await ValidateAsync(request, cancellationToken);
            if (problem is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
                return await RejectAsync(request?.QuestId ?? string.Empty, problem, auditIntent, cited, CancellationToken.None);
            }

            var derivedEffects = Effects(request);
            var dry = await _effects.ApplyAsync(derivedEffects, true, cancellationToken);
            if (!dry.Valid || dry.Blocked)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
                return await RejectAsync(request.QuestId, new(
                    "QUEST_EFFECTS_REJECTED", "request", "Derived quest effects were rejected.", "Correct the request and retry."), auditIntent, cited, CancellationToken.None);
            }

            var operationId = Operation.NewId();
            var applied = await _effects.ApplyAsync(derivedEffects, false, cancellationToken, operationId);
            if (!applied.Valid || !applied.Applied || applied.Blocked)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
                return await RejectAsync(request.QuestId, new(
                    "QUEST_EFFECTS_REJECTED", "request", "Derived quest effects were not applied.", "Correct the request and retry."), auditIntent, cited, CancellationToken.None);
            }

            var operation = await _log.RecordAsync(
                "commit", $"Created quest '{request.QuestId}'.", true, auditIntent, request.QuestId, cited,
                consumesReadEvidence: true, cancellationToken: cancellationToken, id: operationId);
            await transaction.CommitAsync(cancellationToken);
            return new("created", request.QuestId, operation.Id, applied.AcceptedEvents.Count, []);
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return await RejectAsync(request?.QuestId ?? string.Empty, new(
                "QUEST_CREATE_FAILED", "request", "Quest creation could not be completed.", "Correct the request and retry."),
                auditIntent, cited, CancellationToken.None);
        }
    }

    private async Task<QuestProblem?> ValidateAsync(QuestCreateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || !Id(request.QuestId, "quest.") || !Text(request.Title, 160) ||
            !Text(request.Premise, 1000) || !Text(request.Summary, 1000) || !Visibility(request.Visibility) ||
            !Id(request.CampaignId) || !Id(request.ArcId) || request.ChapterIds is null || request.Objectives is null)
            return Bad("INVALID_QUEST", "request", "Quest identity, text, visibility, context, chapters, and objectives must be closed valid values.");

        if (request.ChapterIds.Count is < 1 or > 2 || request.ChapterIds.Any(id => !Id(id)) ||
            request.ChapterIds.Distinct(StringComparer.Ordinal).Count() != request.ChapterIds.Count)
            return Bad("INVALID_CHAPTER", "chapterIds", "Use one or two unique valid chapter ids.");

        if (request.Objectives.Count != 3 || request.Objectives.Any(objective => objective is null) ||
            request.Objectives.Count(objective => objective.Required) != 2 ||
            !request.Objectives.Select(objective => objective.DisplayOrder).SequenceEqual([1, 2, 3]) ||
            request.Objectives.Select(objective => objective.LocalKey).Distinct(StringComparer.Ordinal).Count() != 3)
            return Bad("INVALID_OBJECTIVES", "objectives", "Exactly three ordered objectives with exactly two required objectives are required.");

        if (await _world.GetEntityAsync(request.QuestId, cancellationToken) is not null)
            return Bad("QUEST_ID_TAKEN", "questId", "Quest id is already permanent.");

        foreach (var objective in request.Objectives)
        {
            if (await _world.GetEntityAsync($"{request.QuestId}.{objective.LocalKey}", cancellationToken) is not null)
                return Bad("OBJECTIVE_ID_TAKEN", "objectives", "A derived objective id is already permanent.");
        }

        var campaign = await _world.GetEntityAsync(request.CampaignId, cancellationToken);
        if (campaign is null || !Status(Component(campaign, CampaignRoot), "active"))
            return Bad("INVALID_CAMPAIGN", "campaignId", "Campaign must be an active C3 campaign.");

        var campaignLinks = await _world.GetRelationshipsAsync(campaign.Id, false, cancellationToken);
        var worldLinks = campaignLinks.Where(link => link.Kind == "game.core.campaign.in-world").ToArray();
        if (worldLinks.Length != 1)
            return Bad("INVALID_CAMPAIGN", "campaignId", "Campaign must have exactly one world link.");

        var campaignWorld = await _world.GetEntityAsync(worldLinks[0].ToEntityId, cancellationToken);
        if (campaignWorld is null || !Status(Component(campaignWorld, WorldRoot), "active"))
            return Bad("INVALID_CAMPAIGN", "campaignId", "Campaign world must be active.");

        var arc = await _world.GetEntityAsync(request.ArcId, cancellationToken);
        if (campaignLinks.Count(link => link.Kind == "game.core.campaign.has-arc" && link.ToEntityId == request.ArcId) != 1 ||
            arc is null || !Status(Component(arc, CampaignArc), "active"))
            return Bad("INVALID_ARC", "arcId", "Arc must be the campaign's active linked C3 arc.");

        foreach (var chapterId in request.ChapterIds)
        {
            var chapter = await _world.GetEntityAsync(chapterId, cancellationToken);
            if (campaignLinks.Count(link => link.Kind == "game.core.campaign.has-chapter" && link.ToEntityId == chapterId) != 1 ||
                chapter is null || !StatusOneOf(Component(chapter, CampaignChapter), "active", "closed"))
                return Bad("INVALID_CHAPTER", "chapterIds", "Chapter must be an active or closed linked C3 chapter.");

            var chapterArcLinks = (await _world.GetRelationshipsAsync(chapter.Id, false, cancellationToken))
                .Where(link => link.Kind == "game.core.campaign.chapter.in-arc").ToArray();
            if (chapterArcLinks.Length != 1 || chapterArcLinks[0].ToEntityId != arc.Id)
                return Bad("INVALID_CHAPTER_ARC", "chapterIds", "Each chapter must belong to the selected arc.");
        }

        foreach (var objective in request.Objectives)
        {
            var problem = await ValidateObjectiveAsync(objective, request, campaignLinks, campaignWorld.Id, cancellationToken);
            if (problem is not null) return problem;
        }

        return null;
    }

    private async Task<QuestProblem?> ValidateObjectiveAsync(
        QuestObjectiveInput objective,
        QuestCreateRequest request,
        IReadOnlyList<RelationshipView> campaignLinks,
        string worldId,
        CancellationToken cancellationToken)
    {
        if (!Id(objective.LocalKey, "objective.") || !Text(objective.Title, 160) ||
            !Text(objective.ActionableSummary, 1000) || !Visibility(objective.Visibility) ||
            objective.PrerequisiteLocalKeys is null || objective.References is null || objective.References.Count > 5)
            return Bad("INVALID_OBJECTIVE", "objectives", "Objective keys, text, visibility, dependencies, and references are invalid.");

        var earlier = request.Objectives.Take(objective.DisplayOrder - 1).Select(candidate => candidate.LocalKey)
            .ToHashSet(StringComparer.Ordinal);
        if (objective.PrerequisiteLocalKeys.Any(key => !Id(key, "objective.") || !earlier.Contains(key)) ||
            objective.PrerequisiteLocalKeys.Distinct(StringComparer.Ordinal).Count() != objective.PrerequisiteLocalKeys.Count)
            return Bad("INVALID_OBJECTIVE", "objectives.prerequisiteLocalKeys", "Objective prerequisites must be distinct earlier objective keys.");

        if (objective.References.Any(reference => reference is null) ||
            objective.References.Select(reference => reference.EntityId).Distinct(StringComparer.Ordinal).Count() != objective.References.Count)
            return Bad("INVALID_REFERENCE", "objectives.references", "Objective references must be distinct existing records.");

        foreach (var reference in objective.References)
        {
            if (!Id(reference.EntityId) || reference.Role is not ("actor" or "location" or "knowledge" or "faction") || !Visibility(reference.Audience))
                return Bad("INVALID_REFERENCE", "objectives.references", "Objective reference role, audience, and entity id are invalid.");

            var endpoint = await _world.GetEntityAsync(reference.EntityId, cancellationToken);
            if (endpoint is null)
                return Bad("INVALID_REFERENCE", "objectives.references", "Objective reference must name an existing compatible record.");

            var problem = await ValidateReferenceAsync(reference, endpoint, campaignLinks, worldId, cancellationToken);
            if (problem is not null) return problem;
        }

        return null;
    }

    private async Task<QuestProblem?> ValidateReferenceAsync(
        QuestReference reference,
        EntitySnapshot endpoint,
        IReadOnlyList<RelationshipView> campaignLinks,
        string worldId,
        CancellationToken cancellationToken)
    {
        var families = reference.Role switch
        {
            "actor" => new[] { Motive },
            "location" => new[] { Location },
            "faction" => new[] { Faction },
            "knowledge" => new[] { Fact, Rumour, Secret, Clue },
            _ => []
        };
        var matching = endpoint.Components.Where(component => families.Contains(component.DefinitionId, StringComparer.Ordinal)).ToArray();
        if (matching.Length != 1 || !ActiveReference(matching[0].DefinitionId, matching[0].Data))
            return Bad("INVALID_REFERENCE", "objectives.references", "Objective reference must have an active compatible component.");

        var inScope = reference.Role switch
        {
            "actor" => campaignLinks.Any(link => link.Kind == "game.core.campaign.references" && link.ToEntityId == endpoint.Id),
            "location" => await IsContainedByAsync(endpoint, worldId, cancellationToken),
            "faction" => await HasWorldLinkAsync(endpoint.Id, "game.core.world.faction.in-world", worldId, cancellationToken),
            "knowledge" => await HasWorldLinkAsync(endpoint.Id, "game.core.world.knowledge.in-world", worldId, cancellationToken),
            _ => false
        };
        if (!inScope)
            return Bad("INVALID_REFERENCE", "objectives.references", "Objective reference is outside the campaign world or references.");

        if (reference.Audience == "party" && !PartyVisible(matching[0].DefinitionId, matching[0].Data))
            return Bad("REFERENCE_NOT_VISIBLE", "objectives.references", "Party references cannot expose GM-only or unrevealed material.");

        return null;
    }

    private async Task<bool> HasWorldLinkAsync(string entityId, string kind, string worldId, CancellationToken cancellationToken) =>
        (await _world.GetRelationshipsAsync(entityId, false, cancellationToken))
        .Count(link => link.Kind == kind && link.ToEntityId == worldId) == 1;

    private async Task<bool> IsContainedByAsync(EntitySnapshot entity, string worldId, CancellationToken cancellationToken)
    {
        var current = entity;
        for (var depth = 0; depth < 32; depth++)
        {
            if (current.Id == worldId) return true;
            if (current.ContainerId is null) return false;
            var parent = await _world.GetEntityAsync(current.ContainerId, cancellationToken);
            if (parent is null) return false;
            current = parent;
        }
        return false;
    }

    private static IReadOnlyList<Effect> Effects(QuestCreateRequest request)
    {
        var effects = new List<Effect>
        {
            EntityCreate(request.QuestId, request.Title),
            ComponentAdd(request.QuestId, "game.core.quest.root", JsonSerializer.Serialize(new
            {
                status = "draft", request.Premise, request.Summary, request.Visibility
            })),
            Relationship(request.QuestId, request.CampaignId, "game.core.quest.in-campaign"),
            Relationship(request.QuestId, request.ArcId, "game.core.quest.in-arc")
        };
        effects.AddRange(request.ChapterIds.Select(chapterId => Relationship(request.QuestId, chapterId, "game.core.quest.in-chapter")));

        foreach (var objective in request.Objectives)
        {
            var objectiveId = $"{request.QuestId}.{objective.LocalKey}";
            effects.Add(EntityCreate(objectiveId, objective.Title));
            effects.Add(ComponentAdd(objectiveId, "game.core.quest.objective", JsonSerializer.Serialize(new
            {
                status = "dormant", objective.ActionableSummary, objective.Required, objective.Visibility, objective.DisplayOrder
            })));
            effects.Add(Relationship(request.QuestId, objectiveId, "game.core.quest.has-objective"));
            effects.AddRange(objective.PrerequisiteLocalKeys.Select(key =>
                Relationship(objectiveId, $"{request.QuestId}.{key}", "game.core.quest.objective.depends-on")));
            effects.AddRange(objective.References.Select(reference =>
                Relationship(objectiveId, reference.EntityId, "game.core.quest.objective.references",
                    JsonSerializer.Serialize(new { role = reference.Role, audience = reference.Audience }))));
        }
        return effects;
    }

    private async Task<QuestCreateResult> RejectAsync(string questId, QuestProblem problem, string intent, IReadOnlyList<string> cited, CancellationToken cancellationToken)
    {
        var operation = await _log.RecordAsync("commit", "Quest creation was rejected; no quest state was created.", false,
            intent, questId, cited, problem.Code, consumesReadEvidence: true, cancellationToken: cancellationToken);
        return new("rejected", questId, operation.Id, null, [problem]);
    }

    private static Effect EntityCreate(string id, string name) => new() { Type = EffectType.EntityCreate, EntityId = id, Name = name };
    private static Effect ComponentAdd(string entityId, string definitionId, string data) => new() { Type = EffectType.ComponentAdd, EntityId = entityId, DefinitionId = definitionId, Data = data };
    private static Effect Relationship(string from, string to, string kind, string data = "{}") => new() { Type = EffectType.RelationshipCreate, EntityId = from, ToEntityId = to, Kind = kind, Data = data };
    private static QuestProblem Bad(string code, string path, string reason) => new(code, path, reason, "Correct the request and retry.");
    private static string? Component(EntitySnapshot? entity, string definition) => entity?.Components.SingleOrDefault(component => component.DefinitionId == definition)?.Data;

    private static bool ActiveReference(string definition, string component)
    {
        if (!TryObject(component, out var data)) return false;
        var status = String(data, "status");
        return definition switch
        {
            Motive or Location or Faction or Fact or Secret => status == "active",
            Rumour => status is "unconfirmed" or "confirmed",
            Clue => status is "unrevealed" or "revealed",
            _ => false
        };
    }

    private static bool PartyVisible(string definition, string component)
    {
        if (!TryObject(component, out var data)) return false;
        if (definition == Secret || definition == Clue && String(data, "status") != "revealed") return false;
        return String(data, "visibility") is "public" or "party";
    }

    private static bool Status(string? json, string expected) => TryObject(json, out var data) && String(data, "status") == expected;
    private static bool StatusOneOf(string? json, params string[] expected) =>
        TryObject(json, out var data) && expected.Contains(String(data, "status"), StringComparer.Ordinal);
    private static bool TryObject(string? json, out JsonElement data)
    {
        data = default;
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            data = document.RootElement.Clone();
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static string? String(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Visibility(string? value) => value is "party" or "gm";
    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() &&
        value == value.ToLowerInvariant() && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static bool Id(string? value, string prefix) => Id(value) && value!.StartsWith(prefix, StringComparison.Ordinal) && value.Length > prefix.Length;
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
}
