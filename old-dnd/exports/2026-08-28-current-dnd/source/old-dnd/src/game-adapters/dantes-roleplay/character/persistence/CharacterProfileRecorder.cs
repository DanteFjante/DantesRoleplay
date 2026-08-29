using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Characters;
using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>CH1's internal profile-effect planner. C15 supplies the only campaign scope check.</summary>
public sealed class CharacterProfileRecorder(IWorldStore world, ICampaignCharacterParticipationVerifier participation) : ICharacterProfileRecorder
{
    private const string Definition = "dnd2024.character.profile";
    private readonly IWorldStore _world = world;
    private readonly ICampaignCharacterParticipationVerifier _participation = participation;

    public async Task<CharacterProfileRecordPlan> PlanAsync(CharacterProfileRecordRequest request, CancellationToken cancellationToken = default)
    {
        var actorId = request?.ActorId ?? string.Empty;
        if (!Id(actorId)) return Invalid(actorId, "INVALID_ACTOR_ID", "actorId", "actorId must be a canonical lowercase dotted id.");
        var actor = await _world.GetEntityAsync(actorId, cancellationToken);
        if (actor is null) return Invalid(actorId, "ACTOR_NOT_FOUND", "actorId", "actorId must name an existing actor.");
        if (!Text(actor.Name, 160)) return Invalid(actorId, "INVALID_ACTOR_NAME", "actorId", "The actor's existing entity name must be a trimmed display name.");
        if (actor.Components.Any(x => x.DefinitionId == Definition)) return Invalid(actorId, "PROFILE_ALREADY_EXISTS", "actorId", "The actor already has a character profile.");

        var scope = await _participation.ResolveActiveScopeAsync(actorId, cancellationToken);
        if (!scope.Valid) return Invalid(actorId, "CAMPAIGN_SCOPE_REQUIRED", "actorId", "A valid active campaign participation is required before recording a profile.");
        if (!TryProfile(request?.ProfileJson, out var profileJson, out var problem)) return Invalid(actorId, problem!.Code, problem.Path, problem.Reason);

        return new("valid", actorId, scope.CampaignId, profileJson,
            [new Effect { Type = EffectType.ComponentAdd, EntityId = actorId, DefinitionId = Definition, Data = profileJson! }], []);
    }

    private static bool TryProfile(string? json, out string? canonical, out CharacterProfileProblem? problem)
    {
        canonical = null; problem = null;
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { problem = Problem("INVALID_PROFILE", "profile", "Profile must be a JSON object."); return false; }
            var allowed = new Dictionary<string, int>(StringComparer.Ordinal) { ["pronouns"] = 80, ["appearance"] = 1000, ["biography"] = 2000 };
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!allowed.TryGetValue(property.Name, out var maximum)) { problem = Problem("INVALID_PROFILE_FIELD", property.Name, "Profile contains an unknown field."); return false; }
                if (property.Value.ValueKind != JsonValueKind.String || !Text(property.Value.GetString(), maximum)) { problem = Problem("INVALID_PROFILE_FIELD", property.Name, "Profile fields must be nonempty trimmed text within their closed limit."); return false; }
                values[property.Name] = property.Value.GetString()!;
            }
            canonical = JsonSerializer.Serialize(new
            {
                pronouns = values.TryGetValue("pronouns", out var pronouns) ? pronouns : null,
                appearance = values.TryGetValue("appearance", out var appearance) ? appearance : null,
                biography = values.TryGetValue("biography", out var biography) ? biography : null
            }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            return true;
        }
        catch (JsonException) { problem = Problem("INVALID_PROFILE", "profile", "Profile must be valid JSON."); return false; }
    }

    private static CharacterProfileProblem Problem(string code, string path, string reason) => new(code, path, reason, "Correct the profile and retry the character creation root.");
    private static CharacterProfileRecordPlan Invalid(string actorId, string code, string path, string reason) => new("invalid", actorId, null, null, [], [new(code, path, reason, "Correct the profile and retry the character creation root.")]);
    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value == value.ToLowerInvariant() && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
}
