using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Characters;
using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// CH2's internal ability writer. It deliberately knows no allocation-policy, origin, class, or
/// level rule: a root must first obtain canonical scores from the CH2 policy validator.
/// </summary>
public sealed class CharacterAbilityScoreRecorder(IWorldStore world, ICampaignCharacterParticipationVerifier participation) : ICharacterAbilityScoreRecorder
{
    private const string Definition = "dnd2024.abilities";
    private static readonly string[] AbilityIds = ["str", "dex", "con", "int", "wis", "cha"];
    private readonly IWorldStore _world = world;
    private readonly ICampaignCharacterParticipationVerifier _participation = participation;

    public async Task<CharacterAbilityScoreRecordPlan> PlanAsync(
        CharacterAbilityScoreRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = request?.ActorId ?? string.Empty;
        if (!Id(actorId)) return Invalid(actorId, "INVALID_ACTOR_ID", "actorId", "actorId must be a canonical lowercase dotted id.");
        var actor = await _world.GetEntityAsync(actorId, cancellationToken);
        if (actor is null) return Invalid(actorId, "ACTOR_NOT_FOUND", "actorId", "actorId must name an existing actor.");
        if (actor.Components.Any(x => x.DefinitionId == Definition)) return Invalid(actorId, "ABILITIES_ALREADY_EXIST", "actorId", "The actor already has ability-score state.");

        var scope = await _participation.ResolveActiveScopeAsync(actorId, cancellationToken);
        if (!scope.Valid) return Invalid(actorId, "CAMPAIGN_SCOPE_REQUIRED", "actorId", "A valid active campaign participation is required before recording ability scores.");
        if (!TryScores(request?.CanonicalScoresJson, out var canonical, out var problem))
            return Invalid(actorId, problem!.Code, problem.Path, problem.Reason);

        return new("valid", actorId, scope.CampaignId, canonical,
            [new Effect { Type = EffectType.ComponentAdd, EntityId = actorId, DefinitionId = Definition, Data = canonical! }], []);
    }

    private static bool TryScores(string? json, out string? canonical, out CharacterAbilityAssignmentProblem? problem)
    {
        canonical = null;
        problem = null;

        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Exact(root))
                return Fail("INVALID_SCORES", "scores", "Scores must be an object with exactly str, dex, con, int, wis, and cha.", out problem);
            var scores = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var ability in AbilityIds)
            {
                if (!root.TryGetProperty(ability, out var node) || node.ValueKind != JsonValueKind.Number || !node.TryGetInt32(out var score) || score is < 1 or > 30)
                    return Fail("INVALID_SCORES", ability, "Every score must be an integer within the existing ability-score range of 1 through 30.", out problem);
                scores.Add(ability, score);
            }
            canonical = JsonSerializer.Serialize(new { str = scores["str"], dex = scores["dex"], con = scores["con"], @int = scores["int"], wis = scores["wis"], cha = scores["cha"] });
            return true;
        }
        catch (JsonException)
        {
            return Fail("INVALID_SCORES", "scores", "Scores must be valid JSON.", out problem);
        }
    }

    private static bool Exact(JsonElement element) =>
        element.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).SequenceEqual(AbilityIds.Order(StringComparer.Ordinal));

    private static bool Fail(string code, string path, string reason, out CharacterAbilityAssignmentProblem? problem)
    {
        problem = new(code, path, reason, "Correct the validated scores and retry the character creation root.");
        return false;
    }

    private static CharacterAbilityScoreRecordPlan Invalid(string actorId, string code, string path, string reason) =>
        new("invalid", actorId, null, null, [], [new(code, path, reason, "Correct the validated scores and retry the character creation root.")]);

    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value == value.ToLowerInvariant() && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
}
