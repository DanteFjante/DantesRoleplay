using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Characters;
using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Validates one immutable species definition and produces the actor's only selected-species
/// fragment. CH5 applies it; species traits, choices, Size, and Speed remain separate owners.
/// </summary>
public sealed class CharacterSpeciesSelectionResolver(
    IWorldStore world,
    ICampaignCharacterParticipationVerifier participation) : ICharacterSpeciesSelectionResolver
{
    private const string SelectionDefinition = "dnd2024.selected-species";
    private const string ContentDefinition = "dnd2024.character.content-definition";
    private const string ProfileDefinition = "dnd2024.species-profile";
    private const string SourceId = "source.dnd2024.srd-5.2.1";
    private readonly IWorldStore _world = world;
    private readonly ICampaignCharacterParticipationVerifier _participation = participation;

    public async Task<CharacterSpeciesSelectionPlan> PlanAsync(
        CharacterSpeciesSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = request?.ActorId ?? string.Empty;
        var speciesId = request?.SpeciesDefinitionId ?? string.Empty;
        if (!Id(actorId, "actor.")) return Invalid(actorId, speciesId, "INVALID_ACTOR_ID", "actorId", "actorId must be a canonical actor.* id.");
        if (!Id(speciesId, "content.")) return Invalid(actorId, speciesId, "INVALID_SPECIES_DEFINITION_ID", "speciesDefinitionId", "speciesDefinitionId must be a canonical content.* id.");

        var actor = await _world.GetEntityAsync(actorId, cancellationToken);
        if (actor is null) return Invalid(actorId, speciesId, "ACTOR_NOT_FOUND", "actorId", "actorId must name an existing or staged actor.");
        var selections = actor.Components.Where(component => component.DefinitionId == SelectionDefinition).ToArray();
        if (selections.Length != 0)
        {
            if (selections.Length != 1 || !ValidSelection(selections[0].Data))
                return Invalid(actorId, speciesId, "INVALID_EXISTING_SPECIES_SELECTION", "actorId", "Existing selected-species state is corrupt and cannot be replaced by this resolver.");
            return Invalid(actorId, speciesId, "SPECIES_ALREADY_SELECTED", "actorId", "A staged actor may select one species definition only.");
        }

        var scope = await _participation.ResolveActiveScopeAsync(actorId, cancellationToken);
        if (!scope.Valid) return Invalid(actorId, speciesId, "CAMPAIGN_SCOPE_REQUIRED", "actorId", "A valid active campaign participation is required before selecting a species.");

        var species = await _world.GetEntityAsync(speciesId, cancellationToken);
        if (species is null) return Invalid(actorId, speciesId, "SPECIES_DEFINITION_NOT_FOUND", "speciesDefinitionId", "The bound species definition does not exist.");
        var identities = species.Components.Where(component => component.DefinitionId == ContentDefinition).ToArray();
        var profiles = species.Components.Where(component => component.DefinitionId == ProfileDefinition).ToArray();
        if (identities.Length != 1 || profiles.Length != 1 || !ValidSpeciesDefinition(identities[0].Data, profiles[0].Data))
            return Invalid(actorId, speciesId, "INVALID_SPECIES_DEFINITION", "speciesDefinitionId", "The bound entity must have one matching active source-cited species identity and immutable profile.");

        var canonical = JsonSerializer.Serialize(new { speciesDefinitionId = speciesId });
        return new("valid", actorId, speciesId, scope.CampaignId, canonical,
            [new Effect { Type = EffectType.ComponentAdd, EntityId = actorId, DefinitionId = SelectionDefinition, Data = canonical }], []);
    }

    private static bool ValidSpeciesDefinition(string identityJson, string profileJson)
    {
        try
        {
            using var identityDocument = JsonDocument.Parse(identityJson);
            using var profileDocument = JsonDocument.Parse(profileJson);
            var identity = identityDocument.RootElement;
            var profile = profileDocument.RootElement;
            if (!Exact(identity, "kind", "contentKey", "contentVersion", "status", "sourceRef") ||
                !String(identity, "kind", out var kind) || kind != "species" ||
                !String(identity, "contentKey", out var key) || !Integer(identity, "contentVersion", out var version) || version < 1 ||
                !String(identity, "status", out var status) || status != "active" || !Source(identity.GetProperty("sourceRef"))) return false;
            if (!Exact(profile, "contentKey", "contentVersion", "sourceRef", "creatureType", "allowedSizes", "baseSpeed", "traitKeys", "choiceFamilies") ||
                !String(profile, "contentKey", out var profileKey) || profileKey != key || !Integer(profile, "contentVersion", out var profileVersion) || profileVersion != version ||
                !profile.TryGetProperty("sourceRef", out var profileSource) || !SameSource(identity.GetProperty("sourceRef"), profileSource) ||
                !String(profile, "creatureType", out var creatureType) || creatureType != "humanoid" ||
                !CanonicalStrings(profile, "allowedSizes", 1, 2, ["small", "medium"]) ||
                !Speed(profile) || !CanonicalStrings(profile, "traitKeys", 1, 8) || !CanonicalStrings(profile, "choiceFamilies", 0, 1)) return false;
            return true;
        }
        catch (JsonException) { return false; }
        catch (KeyNotFoundException) { return false; }
    }

    private static bool ValidSelection(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Exact(document.RootElement, "speciesDefinitionId") && String(document.RootElement, "speciesDefinitionId", out var id) && Id(id, "content.");
        }
        catch (JsonException) { return false; }
    }

    private static bool Speed(JsonElement profile)
    {
        if (!profile.TryGetProperty("baseSpeed", out var speed) || !Exact(speed, "walkFeet", "burrowFeet", "climbFeet", "flyFeet", "swimFeet")) return false;
        return Integer(speed, "walkFeet", out var walk) && walk is >= 5 and <= 1000 && walk % 5 == 0 &&
               Integer(speed, "burrowFeet", out var burrow) && burrow == 0 &&
               Integer(speed, "climbFeet", out var climb) && climb == 0 &&
               Integer(speed, "flyFeet", out var fly) && fly == 0 &&
               Integer(speed, "swimFeet", out var swim) && swim == 0;
    }

    private static bool CanonicalStrings(JsonElement root, string name, int minimum, int maximum, IReadOnlyList<string>? allowed = null)
    {
        if (!root.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() < minimum || values.GetArrayLength() > maximum) return false;
        var result = values.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null).ToArray();
        return result.All(value => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && (allowed is null || allowed.Contains(value!, StringComparer.Ordinal))) &&
               result.Distinct(StringComparer.Ordinal).Count() == result.Length;
    }

    private static bool Source(JsonElement source) => Exact(source, "sourceId", "locator") && String(source, "sourceId", out var sourceId) && sourceId == SourceId && String(source, "locator", out var locator) && locator.StartsWith("Character Origins > Character Species > ", StringComparison.Ordinal) && locator.Contains(", PDF page ", StringComparison.Ordinal);
    private static bool SameSource(JsonElement left, JsonElement right) => Source(left) && Source(right) && left.GetProperty("sourceId").GetString() == right.GetProperty("sourceId").GetString() && left.GetProperty("locator").GetString() == right.GetProperty("locator").GetString();
    private static bool Exact(JsonElement element, params string[] names) => element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal));
    private static bool String(JsonElement element, string name, out string value) { value = string.Empty; return element.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value = node.GetString() ?? string.Empty); }
    private static bool Integer(JsonElement element, string name, out int value) { value = default; return element.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out value); }
    private static bool Id(string? value, string prefix) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value.StartsWith(prefix, StringComparison.Ordinal) && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static CharacterSpeciesSelectionPlan Invalid(string actorId, string speciesId, string code, string path, string reason) => new("invalid", actorId, speciesId, null, null, [], [new(code, path, reason, "Correct the source-bound selection and retry the character creation root.")]);
}
