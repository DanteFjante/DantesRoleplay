using System.Text.Json;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Slice 3's trusted-host owner for knowledge validity, contradiction, and supersession.</summary>
public sealed class KnowledgeTimelineCoordinator(
    DantesRoleplayDbContext db,
    IWorldStore world) : IKnowledgeTimelineCoordinator
{
    private const string Fact = "game.core.world.fact";
    private const string Rumour = "game.core.world.rumour";
    private const string Secret = "game.core.world.secret";
    private const string Clue = "game.core.world.clue";
    private const string Classification = "game.core.world.knowledge.classification";
    private const string Validity = "game.core.world.knowledge.validity";
    private const string KnowledgeWorld = "game.core.world.knowledge.in-world";
    private const string About = "game.core.world.knowledge.about";
    private const string Contradicts = "game.core.world.knowledge.contradicts";
    private const string Supersedes = "game.core.world.knowledge.supersedes";
    private const string WorldRoot = "game.core.world.root";
    private const string Clock = "game.core.world.clock";
    private const long MaximumMinute = 1_000_000_000;
    private const int MaximumReadRecords = 10_000;

    private readonly DantesRoleplayDbContext _db = db;
    private readonly IWorldStore _world = world;

    private sealed record KnowledgeRecord(string Id, string WorldId, string SubjectId, long? ValidFromMinute, long? ValidUntilMinute);

    public async Task<KnowledgeTimelineWriteResult> RecordValidityAsync(
        RecordKnowledgeValidityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || !Id(request.KnowledgeId) || !Interval(request.ValidFromMinute, request.ValidUntilMinute))
            return Reject(request?.KnowledgeId ?? string.Empty, string.Empty, "INVALID_KNOWLEDGE_VALIDITY_REQUEST", "payload", "Validity requires a canonical knowledge id, bounded start, and an optional strictly later end.");

        var knowledge = await ReadKnowledgeAsync(request.KnowledgeId, cancellationToken);
        if (knowledge.Problem is not null) return Reject(request.KnowledgeId, string.Empty, knowledge.Problem);
        var clock = await WorldClockAsync(knowledge.Value!.WorldId, cancellationToken);
        if (clock.Problem is not null) return Reject(request.KnowledgeId, string.Empty, clock.Problem);
        if (request.ValidFromMinute > clock.Minute)
            return Reject(request.KnowledgeId, string.Empty, "KNOWLEDGE_VALIDITY_FUTURE", "validFromMinute", "Slice 3 does not record scheduled future truth; validFromMinute cannot exceed the current world minute.");

        var data = ValidityData(request.ValidFromMinute, request.ValidUntilMinute);
        var entity = await _world.GetEntityAsync(request.KnowledgeId, cancellationToken);
        var existing = Component(entity!, Validity);
        if (existing == data) return new("replayed", request.KnowledgeId, string.Empty, []);
        await _world.SetComponentAsync(request.KnowledgeId, Validity, data, cancellationToken);
        return new("recorded", request.KnowledgeId, string.Empty, []);
    }

    public async Task<KnowledgeTimelineWriteResult> RecordContradictionAsync(
        RecordKnowledgeContradictionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || !Id(request.FirstKnowledgeId) || !Id(request.SecondKnowledgeId) || request.FirstKnowledgeId == request.SecondKnowledgeId)
            return Reject(request?.FirstKnowledgeId ?? string.Empty, request?.SecondKnowledgeId ?? string.Empty, "INVALID_KNOWLEDGE_CONTRADICTION_REQUEST", "payload", "Contradiction requires two distinct canonical knowledge ids.");

        var first = await ReadKnowledgeAsync(request.FirstKnowledgeId, cancellationToken);
        var second = await ReadKnowledgeAsync(request.SecondKnowledgeId, cancellationToken);
        if (first.Problem is not null) return Reject(request.FirstKnowledgeId, request.SecondKnowledgeId, first.Problem);
        if (second.Problem is not null) return Reject(request.FirstKnowledgeId, request.SecondKnowledgeId, second.Problem);
        if (!SameScope(first.Value!, second.Value!))
            return Reject(request.FirstKnowledgeId, request.SecondKnowledgeId, "KNOWLEDGE_LINK_SCOPE_MISMATCH", "payload", "Contradiction endpoints must share one scoped world and knowledge subject.");

        var (from, to) = Ordered(first.Value!.Id, second.Value!.Id);
        var links = await _world.GetRelationshipsAsync(from, includeIncoming: false, cancellationToken);
        if (links.Any(link => link.Kind == Contradicts && link.ToEntityId == to && Empty(link.Data)))
            return new("replayed", from, to, []);

        await _world.RelateAsync(from, to, Contradicts, "{}", cancellationToken);
        return new("recorded", from, to, []);
    }

    public async Task<KnowledgeTimelineWriteResult> RecordSupersessionAsync(
        RecordKnowledgeSupersessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || !Id(request.NewerKnowledgeId) || !Id(request.PriorKnowledgeId) || request.NewerKnowledgeId == request.PriorKnowledgeId)
            return Reject(request?.NewerKnowledgeId ?? string.Empty, request?.PriorKnowledgeId ?? string.Empty, "INVALID_KNOWLEDGE_SUPERSESSION_REQUEST", "payload", "Supersession requires distinct newer and prior canonical knowledge ids.");

        var newer = await ReadKnowledgeAsync(request.NewerKnowledgeId, cancellationToken);
        var prior = await ReadKnowledgeAsync(request.PriorKnowledgeId, cancellationToken);
        if (newer.Problem is not null) return Reject(request.NewerKnowledgeId, request.PriorKnowledgeId, newer.Problem);
        if (prior.Problem is not null) return Reject(request.NewerKnowledgeId, request.PriorKnowledgeId, prior.Problem);
        if (!SameScope(newer.Value!, prior.Value!) || newer.Value!.ValidFromMinute is null || prior.Value!.ValidUntilMinute is null || prior.Value.ValidUntilMinute != newer.Value.ValidFromMinute)
            return Reject(request.NewerKnowledgeId, request.PriorKnowledgeId, "KNOWLEDGE_SUPERSESSION_INTERVAL_INVALID", "payload", "Supersession requires same-world/same-subject timed records with prior end exactly equal to newer start.");

        var newerLinks = await _world.GetRelationshipsAsync(newer.Value.Id, includeIncoming: false, cancellationToken);
        if (newerLinks.Any(link => link.Kind == Supersedes && link.ToEntityId == prior.Value.Id && Empty(link.Data)))
            return new("replayed", newer.Value.Id, prior.Value.Id, []);
        if (newerLinks.Any(link => link.Kind == Supersedes && Empty(link.Data)))
            return Reject(newer.Value.Id, prior.Value.Id, "KNOWLEDGE_SUPERSESSION_BRANCH", "newerKnowledgeId", "A newer record may supersede only one prior record.");
        var priorLinks = await _world.GetRelationshipsAsync(prior.Value.Id, includeIncoming: true, cancellationToken);
        if (priorLinks.Any(link => link.Kind == Supersedes && link.ToEntityId == prior.Value.Id && Empty(link.Data)))
            return Reject(newer.Value.Id, prior.Value.Id, "KNOWLEDGE_SUPERSESSION_BRANCH", "priorKnowledgeId", "A prior record may have only one direct successor.");
        if (await WouldCycleAsync(newer.Value.Id, prior.Value.Id, cancellationToken))
            return Reject(newer.Value.Id, prior.Value.Id, "KNOWLEDGE_SUPERSESSION_CYCLE", "payload", "A supersession link may not create a cycle.");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _world.RelateAsync(newer.Value.Id, prior.Value.Id, Supersedes, "{}", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new("recorded", newer.Value.Id, prior.Value.Id, []);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<KnowledgeHistoryResult> ReadAsOfAsync(
        string worldId,
        long? asOfMinute = null,
        CancellationToken cancellationToken = default)
    {
        if (!Id(worldId)) return ReadFail(worldId, 0, "INVALID_KNOWLEDGE_HISTORY_QUERY", "worldId", "A history read requires a canonical world id.");
        var clock = await WorldClockAsync(worldId, cancellationToken);
        if (clock.Problem is not null) return ReadFail(worldId, 0, clock.Problem);
        var minute = asOfMinute ?? clock.Minute;
        if (minute is < 0 or > MaximumMinute) return ReadFail(worldId, minute, "INVALID_KNOWLEDGE_HISTORY_MINUTE", "asOfMinute", "asOfMinute must be within the world clock bounds.");

        var candidates = new Dictionary<string, EntitySnapshot>(StringComparer.Ordinal);
        foreach (var kind in new[] { Fact, Rumour, Secret, Clue })
        {
            var found = await _world.FindEntitiesAsync(withDefinitionId: kind, limit: MaximumReadRecords, cancellationToken: cancellationToken);
            foreach (var item in await _world.GetEntitiesAsync(found.Select(x => x.Id), cancellationToken)) candidates.TryAdd(item.Id, item);
        }
        if (candidates.Count > MaximumReadRecords) return ReadFail(worldId, minute, "KNOWLEDGE_HISTORY_LIMIT", "worldId", "The bounded history read exceeded its record limit.");

        var records = new List<KnowledgeRecord>();
        foreach (var candidate in candidates.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var record = await ReadKnowledgeAsync(candidate.Id, cancellationToken);
            if (record.Problem is not null) return ReadFail(worldId, minute, record.Problem);
            if (record.Value!.WorldId == worldId) records.Add(record.Value);
        }

        var byId = records.ToDictionary(record => record.Id, StringComparer.Ordinal);
        var links = new Dictionary<string, IReadOnlyList<RelationshipView>>(StringComparer.Ordinal);
        foreach (var record in records) links[record.Id] = await _world.GetRelationshipsAsync(record.Id, includeIncoming: true, cancellationToken);

        var temporal = records.ToDictionary(record => record.Id, record => TemporalStatus(record, minute), StringComparer.Ordinal);
        var projections = new List<KnowledgeTimelineProjection>();
        foreach (var record in records.OrderBy(record => record.Id, StringComparer.Ordinal))
        {
            var contradictionIds = links[record.Id]
                .Where(link => link.Kind == Contradicts && Empty(link.Data) && (link.FromEntityId == record.Id || link.ToEntityId == record.Id))
                .Select(link => link.FromEntityId == record.Id ? link.ToEntityId : link.FromEntityId)
                .Where(byId.ContainsKey).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var contested = Applicable(temporal[record.Id]) && contradictionIds.Any(id => Applicable(temporal[id]));
            var supersedes = links[record.Id]
                .Where(link => link.Kind == Supersedes && link.FromEntityId == record.Id && Empty(link.Data))
                .Select(link => link.ToEntityId).Where(byId.ContainsKey).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            projections.Add(new(record.Id, record.SubjectId, record.ValidFromMinute, record.ValidUntilMinute, temporal[record.Id], contested, contradictionIds, supersedes));
        }
        return new(worldId, minute, projections, []);
    }

    private async Task<(KnowledgeRecord? Value, KnowledgeTimelineProblem? Problem)> ReadKnowledgeAsync(string knowledgeId, CancellationToken cancellationToken)
    {
        var entity = await _world.GetEntityAsync(knowledgeId, cancellationToken);
        if (entity is null) return (null, Problem("KNOWLEDGE_NOT_FOUND", "knowledgeId", "knowledgeId must name an existing knowledge entity."));
        if (entity.Components.Count(component => component.DefinitionId is Fact or Rumour or Secret or Clue) != 1 || Component(entity, Classification) is null)
            return (null, Problem("KNOWLEDGE_KIND_INVALID", "knowledgeId", "Knowledge must have exactly one primary knowledge component and one classification."));
        var links = await _world.GetRelationshipsAsync(knowledgeId, includeIncoming: false, cancellationToken);
        var worlds = links.Where(link => link.Kind == KnowledgeWorld && Empty(link.Data)).Select(link => link.ToEntityId).ToArray();
        var subjects = links.Where(link => link.Kind == About && Empty(link.Data)).Select(link => link.ToEntityId).ToArray();
        if (worlds.Length != 1 || subjects.Length != 1) return (null, Problem("KNOWLEDGE_SCOPE_INVALID", "knowledgeId", "Knowledge must have exactly one world and one subject link."));
        var root = await _world.GetEntityAsync(worlds[0], cancellationToken);
        if (root is null || !Active(Component(root, WorldRoot))) return (null, Problem("KNOWLEDGE_WORLD_INVALID", "knowledgeId", "Knowledge must belong to an active world root."));
        var validity = entity.Components.Where(component => component.DefinitionId == Validity).ToArray();
        if (validity.Length > 1 || (validity.Length == 1 && !ValidityData(validity[0].Data, out var from, out var until)))
            return (null, Problem("KNOWLEDGE_VALIDITY_INVALID", "knowledgeId", "Knowledge validity must be one closed bounded interval."));
        if (validity.Length == 0) return (new(entity.Id, root.Id, subjects[0], null, null), null);
        ValidityData(validity[0].Data, out var start, out var end);
        return (new(entity.Id, root.Id, subjects[0], start, end), null);
    }

    private async Task<(long Minute, KnowledgeTimelineProblem? Problem)> WorldClockAsync(string worldId, CancellationToken cancellationToken)
    {
        var world = await _world.GetEntityAsync(worldId, cancellationToken);
        if (world is null || !Active(Component(world, WorldRoot)) || !ClockData(Component(world, Clock), out var minute))
            return (0, Problem("KNOWLEDGE_WORLD_CLOCK_INVALID", "worldId", "The active knowledge world must have one valid root clock."));
        return (minute, null);
    }

    private async Task<bool> WouldCycleAsync(string newerId, string priorId, CancellationToken cancellationToken)
    {
        var current = priorId;
        for (var depth = 0; depth < 100; depth++)
        {
            if (current == newerId) return true;
            var links = await _world.GetRelationshipsAsync(current, includeIncoming: false, cancellationToken);
            var next = links.Where(link => link.Kind == Supersedes && Empty(link.Data)).Select(link => link.ToEntityId).ToArray();
            if (next.Length == 0) return false;
            if (next.Length != 1) return true;
            current = next[0];
        }
        return true;
    }

    private static bool SameScope(KnowledgeRecord first, KnowledgeRecord second) => first.WorldId == second.WorldId && first.SubjectId == second.SubjectId;
    private static string TemporalStatus(KnowledgeRecord record, long minute) => record.ValidFromMinute is null ? "atemporal" : minute < record.ValidFromMinute ? "not-yet-effective" : record.ValidUntilMinute is not null && minute >= record.ValidUntilMinute ? "historical" : "effective";
    private static bool Applicable(string status) => status is "atemporal" or "effective";
    private static (string From, string To) Ordered(string first, string second) => string.CompareOrdinal(first, second) < 0 ? (first, second) : (second, first);
    private static bool Interval(long from, long? until) => from >= 0 && from <= MaximumMinute && (until is null || (until > from && until <= MaximumMinute));
    private static string ValidityData(long from, long? until) => until is null ? JsonSerializer.Serialize(new { validFromMinute = from }) : JsonSerializer.Serialize(new { validFromMinute = from, validUntilMinute = until });
    private static KnowledgeTimelineWriteResult Reject(string from, string to, KnowledgeTimelineProblem problem) => new("rejected", from, to, [problem]);
    private static KnowledgeTimelineWriteResult Reject(string from, string to, string code, string path, string reason) => Reject(from, to, Problem(code, path, reason));
    private static KnowledgeHistoryResult ReadFail(string world, long minute, KnowledgeTimelineProblem problem) => new(world, minute, [], [problem]);
    private static KnowledgeHistoryResult ReadFail(string world, long minute, string code, string path, string reason) => ReadFail(world, minute, Problem(code, path, reason));
    private static KnowledgeTimelineProblem Problem(string code, string path, string reason) => new(code, path, reason);
    private static string? Component(EntitySnapshot entity, string definition) => entity.Components.SingleOrDefault(component => component.DefinitionId == definition)?.Data;
    private static bool Id(string? id) => !string.IsNullOrWhiteSpace(id) && id == id.Trim() && id.Length <= 200 && id.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static bool Empty(string json) { try { using var d = JsonDocument.Parse(json); return d.RootElement.ValueKind == JsonValueKind.Object && !d.RootElement.EnumerateObject().Any(); } catch { return false; } }
    private static bool Active(string? json) { try { using var d = JsonDocument.Parse(json ?? string.Empty); return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("status", out var status) && status.GetString() == "active"; } catch { return false; } }
    private static bool ClockData(string? json, out long minute) { minute = 0; try { using var d = JsonDocument.Parse(json ?? string.Empty); var x = d.RootElement; return x.ValueKind == JsonValueKind.Object && x.EnumerateObject().Count() == 3 && x.TryGetProperty("calendarId", out var calendar) && calendar.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(calendar.GetString()) && x.TryGetProperty("currentMinute", out var current) && current.TryGetInt64(out minute) && minute is >= 0 and <= MaximumMinute && x.TryGetProperty("revision", out var revision) && revision.TryGetInt64(out var r) && r is >= 0 and <= int.MaxValue; } catch { return false; } }
    private static bool ValidityData(string json, out long from, out long? until) { from = 0; until = null; try { using var d = JsonDocument.Parse(json); var x = d.RootElement; if (x.ValueKind != JsonValueKind.Object || !x.TryGetProperty("validFromMinute", out var start) || !start.TryGetInt64(out from) || from is < 0 or > MaximumMinute) return false; var properties = x.EnumerateObject().Count(); if (!x.TryGetProperty("validUntilMinute", out var end)) return properties == 1; if (!end.TryGetInt64(out var endMinute) || !Interval(from, endMinute) || properties != 2) return false; until = endMinute; return true; } catch { return false; } }
}
