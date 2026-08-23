using System.Text.Json;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

/// <summary>Slice 2's trusted-host owner for durable interaction-sourced knowledge acquisition.</summary>
public sealed class KnowledgeAcquisitionCoordinator(
    DantesRoleplayDbContext db,
    IWorldStore world,
    IKnowledgeStateCoordinator states) : IKnowledgeAcquisitionCoordinator
{
    private const string Interaction = "game.core.world.interaction";
    private const string InteractionWorld = "game.core.world.interaction.in-world";
    private const string Participant = "game.core.world.interaction.participant";
    private const string Acquisition = "game.core.world.knowledge.acquisition";
    private const string AcquisitionWorld = "game.core.world.knowledge.acquisition.in-world";
    private const string Knower = "game.core.world.knowledge.acquisition.knower";
    private const string Knowledge = "game.core.world.knowledge.acquisition.knowledge";
    private const string Source = "game.core.world.knowledge.acquisition.source";
    private const string WorldRoot = "game.core.world.root";

    private readonly DantesRoleplayDbContext _db = db;
    private readonly IWorldStore _world = world;
    private readonly IKnowledgeStateCoordinator _states = states;

    private sealed record ExistingAcquisition(string Id, string Method, string ResultingState);

    public async Task<KnowledgeInteractionWriteResult> RecordInteractionAsync(
        RecordKnowledgeInteractionRequest request,
        CancellationToken cancellationToken = default)
    {
        var problems = new List<KnowledgeAcquisitionProblem>();
        if (request is null)
            return Reject(string.Empty, "INVALID_KNOWLEDGE_INTERACTION_REQUEST", "payload", "An interaction record requires a complete request.");

        ValidateRequestShape(request, problems);
        if (problems.Count > 0) return Rejected(request.InteractionId, problems);

        var world = await _world.GetEntityAsync(request.WorldId, cancellationToken);
        if (world is null || !Active(Component(world, WorldRoot)))
            return Reject(request.InteractionId, "INTERACTION_WORLD_INVALID", "worldId", "worldId must name an active world root.");

        foreach (var participantId in request.ParticipantIds)
            if (await _world.GetEntityAsync(participantId, cancellationToken) is null)
                problems.Add(Problem("INTERACTION_PARTICIPANT_NOT_FOUND", "participantIds", "Every interaction participant must name an existing entity."));

        var resolved = new Dictionary<string, EffectiveKnowledgeState>(StringComparer.Ordinal);
        foreach (var item in request.Acquisitions)
        {
            if (await _world.GetEntityAsync(item.KnowerId, cancellationToken) is null)
            {
                problems.Add(Problem("ACQUISITION_KNOWER_NOT_FOUND", "acquisitions.knowerId", "Every acquisition knower must name an existing actor entity."));
                continue;
            }

            var state = await _states.ResolveAsync(item.KnowerId, item.KnowledgeId, cancellationToken);
            if (!state.Resolved)
            {
                var failure = state.Problems.First();
                problems.Add(Problem(failure.Code, "acquisitions.knowledgeId", failure.Reason));
                continue;
            }

            if (state.Value!.WorldId != request.WorldId)
                problems.Add(Problem("ACQUISITION_WORLD_MISMATCH", "acquisitions.knowledgeId", "Every acquired knowledge record must belong to the interaction world."));
            else
                resolved[Key(item.KnowerId, item.KnowledgeId)] = state.Value;
        }

        var interaction = await _world.GetEntityAsync(request.InteractionId, cancellationToken);
        if (interaction is not null)
            await ValidateExistingInteractionAsync(interaction, request, problems, cancellationToken);

        var existing = interaction is null
            ? new Dictionary<string, ExistingAcquisition>(StringComparer.Ordinal)
            : await ExistingAcquisitionsAsync(request.InteractionId, request.WorldId, problems, cancellationToken);

        foreach (var item in request.Acquisitions)
        {
            var key = Key(item.KnowerId, item.KnowledgeId);
            if (existing.TryGetValue(key, out var present))
            {
                if (present.Method != item.Method || present.ResultingState != item.ResultingState)
                    problems.Add(Problem("ACQUISITION_REPLAY_MISMATCH", "acquisitions", "A replayed source/knower/knowledge triple must retain its recorded method and resulting state."));
                continue;
            }

            var collision = await _world.GetEntityAsync(item.AcquisitionId, cancellationToken);
            if (collision is not null)
                problems.Add(Problem("ACQUISITION_ID_CONFLICT", "acquisitions.acquisitionId", "A new acquisition id must not already name an entity."));
        }

        if (problems.Count > 0) return Rejected(request.InteractionId, problems);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var interactionRecorded = interaction is null;
            if (interactionRecorded)
            {
                await _world.CreateEntityAsync(request.Name, request.InteractionId, cancellationToken);
                await _world.SetComponentAsync(request.InteractionId, Interaction, InteractionData(request), cancellationToken);
                await _world.RelateAsync(request.InteractionId, request.WorldId, InteractionWorld, "{}", cancellationToken);
                foreach (var participantId in request.ParticipantIds)
                    await _world.RelateAsync(request.InteractionId, participantId, Participant, "{}", cancellationToken);
            }

            var results = new List<RecordedKnowledgeAcquisition>();
            var anyNew = interactionRecorded;
            foreach (var item in request.Acquisitions)
            {
                var key = Key(item.KnowerId, item.KnowledgeId);
                if (existing.TryGetValue(key, out var present))
                {
                    results.Add(new(present.Id, item.KnowerId, item.KnowledgeId, present.ResultingState, false, true));
                    continue;
                }

                await _world.CreateEntityAsync($"Knowledge acquisition: {item.KnowledgeId}", item.AcquisitionId, cancellationToken);
                await _world.SetComponentAsync(item.AcquisitionId, Acquisition, AcquisitionData(item), cancellationToken);
                await _world.RelateAsync(item.AcquisitionId, request.WorldId, AcquisitionWorld, "{}", cancellationToken);
                await _world.RelateAsync(item.AcquisitionId, item.KnowerId, Knower, "{}", cancellationToken);
                await _world.RelateAsync(item.AcquisitionId, item.KnowledgeId, Knowledge, "{}", cancellationToken);
                await _world.RelateAsync(item.AcquisitionId, request.InteractionId, Source, "{}", cancellationToken);

                var current = resolved[key];
                var stateUpdated = ShouldStrengthen(current.State, item.ResultingState);
                var effectiveState = current.State;
                if (stateUpdated)
                {
                    var updated = await _states.RecordStateAsync(new(item.KnowerId, item.KnowledgeId, item.ResultingState), cancellationToken);
                    if (!updated.Recorded)
                        throw new InvalidOperationException($"Knowledge state update was rejected: {updated.Problems.First().Code}.");
                    effectiveState = item.ResultingState;
                }

                results.Add(new(item.AcquisitionId, item.KnowerId, item.KnowledgeId, effectiveState, stateUpdated, false));
                anyNew = true;
            }

            await transaction.CommitAsync(cancellationToken);
            return new(anyNew ? "recorded" : "replayed", request.InteractionId, results, []);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task ValidateExistingInteractionAsync(
        EntitySnapshot interaction,
        RecordKnowledgeInteractionRequest request,
        List<KnowledgeAcquisitionProblem> problems,
        CancellationToken cancellationToken)
    {
        var data = Component(interaction, Interaction);
        if (interaction.Name != request.Name || !InteractionMatches(data, request))
            problems.Add(Problem("INTERACTION_REPLAY_MISMATCH", "interactionId", "A replayed interaction must retain its accepted kind, status, and summary."));

        var links = await _world.GetRelationshipsAsync(request.InteractionId, includeIncoming: false, cancellationToken);
        var worlds = links.Where(link => link.Kind == InteractionWorld && Empty(link.Data)).Select(link => link.ToEntityId).ToArray();
        if (worlds.Length != 1 || worlds[0] != request.WorldId)
            problems.Add(Problem("INTERACTION_WORLD_MISMATCH", "worldId", "A replayed interaction must retain exactly its original world link."));
        var participants = links.Where(link => link.Kind == Participant && Empty(link.Data)).Select(link => link.ToEntityId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var expected = request.ParticipantIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (!participants.SequenceEqual(expected, StringComparer.Ordinal))
            problems.Add(Problem("INTERACTION_PARTICIPANT_MISMATCH", "participantIds", "A replayed interaction must retain exactly its original participants."));
    }

    private async Task<Dictionary<string, ExistingAcquisition>> ExistingAcquisitionsAsync(
        string interactionId,
        string worldId,
        List<KnowledgeAcquisitionProblem> problems,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ExistingAcquisition>(StringComparer.Ordinal);
        var links = await _world.GetRelationshipsAsync(interactionId, includeIncoming: true, cancellationToken);
        foreach (var link in links.Where(link => link.Kind == Source && link.ToEntityId == interactionId && Empty(link.Data)))
        {
            var entity = await _world.GetEntityAsync(link.FromEntityId, cancellationToken);
            if (entity is null || !AcquisitionDataValid(Component(entity, Acquisition), out var method, out var state))
            {
                problems.Add(Problem("ACQUISITION_SOURCE_INVALID", "acquisitions.source", "Every source acquisition must have a closed valid acquisition component."));
                continue;
            }

            var outgoing = await _world.GetRelationshipsAsync(entity.Id, includeIncoming: false, cancellationToken);
            var worlds = outgoing.Where(x => x.Kind == AcquisitionWorld && Empty(x.Data)).Select(x => x.ToEntityId).ToArray();
            var knowers = outgoing.Where(x => x.Kind == Knower && Empty(x.Data)).Select(x => x.ToEntityId).ToArray();
            var knowledge = outgoing.Where(x => x.Kind == Knowledge && Empty(x.Data)).Select(x => x.ToEntityId).ToArray();
            if (worlds.Length != 1 || worlds[0] != worldId || knowers.Length != 1 || knowledge.Length != 1)
            {
                problems.Add(Problem("ACQUISITION_SOURCE_INVALID", "acquisitions.source", "Every source acquisition must have one interaction-world link, one knower, and one knowledge link."));
                continue;
            }

            var key = Key(knowers[0], knowledge[0]);
            if (!result.TryAdd(key, new(entity.Id, method!, state!)))
                problems.Add(Problem("ACQUISITION_SOURCE_DUPLICATE", "acquisitions.source", "One source may teach one knower one knowledge record only once."));
        }
        return result;
    }

    private static void ValidateRequestShape(RecordKnowledgeInteractionRequest request, List<KnowledgeAcquisitionProblem> problems)
    {
        if (!Id(request.InteractionId) || !Id(request.WorldId) || !Text(request.Name, 200) || !KnowledgeInteractionKinds.All.Contains(request.Kind) || !Text(request.Summary, 1000))
            problems.Add(Problem("INVALID_KNOWLEDGE_INTERACTION_REQUEST", "payload", "Interaction id, world id, trimmed name/summary, and closed kind are required."));
        if (request.ParticipantIds is null || request.ParticipantIds.Any(id => !Id(id)) || request.ParticipantIds.Distinct(StringComparer.Ordinal).Count() != request.ParticipantIds.Count)
            problems.Add(Problem("INVALID_INTERACTION_PARTICIPANTS", "participantIds", "Participants must be distinct canonical existing entity ids."));
        if (request.Acquisitions is null || request.Acquisitions.Count == 0)
            problems.Add(Problem("INVALID_KNOWLEDGE_ACQUISITIONS", "acquisitions", "An accepted knowledge interaction requires at least one acquisition."));
        else if (request.Acquisitions.Any(item => item is null || !Id(item.AcquisitionId) || !ActorId(item.KnowerId) || !Id(item.KnowledgeId) || !KnowledgeAcquisitionMethods.All.Contains(item.Method) || !KnowledgeEpistemicStates.All.Contains(item.ResultingState)))
            problems.Add(Problem("INVALID_KNOWLEDGE_ACQUISITIONS", "acquisitions", "Each acquisition requires canonical ids plus closed method and resulting state."));
        else if (request.Acquisitions.Select(item => item.AcquisitionId).Distinct(StringComparer.Ordinal).Count() != request.Acquisitions.Count || request.Acquisitions.Select(item => Key(item.KnowerId, item.KnowledgeId)).Distinct(StringComparer.Ordinal).Count() != request.Acquisitions.Count)
            problems.Add(Problem("DUPLICATE_KNOWLEDGE_ACQUISITION", "acquisitions", "One request may contain a source/knower/knowledge triple only once."));
    }

    private static bool ShouldStrengthen(string current, string proposed) => Strength(proposed) > Strength(current);
    private static int Strength(string state) => state switch { "known" => 3, "suspected" or "believed" or "doubted" or "disbelieved" => 2, "familiar" => 1, _ => 0 };
    private static string Key(string knower, string knowledge) => $"{knower}\u001f{knowledge}";
    private static string InteractionData(RecordKnowledgeInteractionRequest request) => JsonSerializer.Serialize(new { kind = request.Kind, status = "accepted", summary = request.Summary });
    private static string AcquisitionData(KnowledgeAcquisitionInput item) => JsonSerializer.Serialize(new { method = item.Method, resultingState = item.ResultingState });
    private static KnowledgeInteractionWriteResult Reject(string id, string code, string path, string reason) => Rejected(id, [Problem(code, path, reason)]);
    private static KnowledgeInteractionWriteResult Rejected(string id, IReadOnlyList<KnowledgeAcquisitionProblem> problems) => new("rejected", id, [], problems);
    private static KnowledgeAcquisitionProblem Problem(string code, string path, string reason) => new(code, path, reason);
    private static string? Component(EntitySnapshot entity, string definition) => entity.Components.SingleOrDefault(component => component.DefinitionId == definition)?.Data;
    private static bool ActorId(string? id) => Id(id) && id!.StartsWith("actor.", StringComparison.Ordinal);
    private static bool Id(string? id) => !string.IsNullOrWhiteSpace(id) && id == id.Trim() && id.Length <= 200 && id.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
    private static bool Active(string? json) { try { using var d = JsonDocument.Parse(json ?? string.Empty); return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("status", out var status) && status.GetString() == "active"; } catch { return false; } }
    private static bool Empty(string json) { try { using var d = JsonDocument.Parse(json); return d.RootElement.ValueKind == JsonValueKind.Object && !d.RootElement.EnumerateObject().Any(); } catch { return false; } }
    private static bool InteractionMatches(string? json, RecordKnowledgeInteractionRequest request) { try { using var d = JsonDocument.Parse(json ?? string.Empty); var x = d.RootElement; return x.ValueKind == JsonValueKind.Object && x.EnumerateObject().Count() == 3 && x.TryGetProperty("kind", out var kind) && kind.GetString() == request.Kind && x.TryGetProperty("status", out var status) && status.GetString() == "accepted" && x.TryGetProperty("summary", out var summary) && summary.GetString() == request.Summary; } catch { return false; } }
    private static bool AcquisitionDataValid(string? json, out string? method, out string? state) { method = null; state = null; try { using var d = JsonDocument.Parse(json ?? string.Empty); var x = d.RootElement; if (x.ValueKind != JsonValueKind.Object || x.EnumerateObject().Count() != 2 || !x.TryGetProperty("method", out var m) || !x.TryGetProperty("resultingState", out var s) || m.ValueKind != JsonValueKind.String || s.ValueKind != JsonValueKind.String || !KnowledgeAcquisitionMethods.All.Contains(m.GetString()!) || !KnowledgeEpistemicStates.All.Contains(s.GetString()!)) return false; method = m.GetString(); state = s.GetString(); return true; } catch { return false; } }
}
