using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Campaign;
using DantesRoleplay.Characters;
using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Resolves a trusted background's source-cited ability increase into one ordinary ability merge
/// fragment. It has no background-selection, receipt, transaction, or direct-write authority.
/// </summary>
public sealed class BackgroundAbilityScoreIncreaseResolver(
    IWorldStore world,
    ICampaignCharacterParticipationVerifier participation) : IBackgroundAbilityScoreIncreaseResolver
{
    private const string ContentDefinition = "dnd2024.character.content-definition";
    private const string OptionsDefinition = "dnd2024.background.ability-increase-options";
    private const string AbilitiesDefinition = "dnd2024.abilities";
    private const string SourceId = "source.dnd2024.srd-5.2.1";
    private static readonly string[] AbilityIds = ["str", "dex", "con", "int", "wis", "cha"];
    private readonly IWorldStore _world = world;
    private readonly ICampaignCharacterParticipationVerifier _participation = participation;

    public async Task<BackgroundAbilityScoreIncreasePlan> PlanAsync(
        BackgroundAbilityScoreIncreaseRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = request?.ActorId ?? string.Empty;
        var backgroundId = request?.BackgroundDefinitionId ?? string.Empty;
        if (!Id(actorId, "actor.")) return Invalid(actorId, backgroundId, "INVALID_ACTOR_ID", "actorId", "actorId must be a canonical actor.* id.");
        if (!Id(backgroundId, "content.")) return Invalid(actorId, backgroundId, "INVALID_BACKGROUND_ID", "backgroundDefinitionId", "The bound background must be a canonical content.* id.");

        var actor = await _world.GetEntityAsync(actorId, cancellationToken);
        if (actor is null) return Invalid(actorId, backgroundId, "ACTOR_NOT_FOUND", "actorId", "actorId must name an existing or staged actor.");
        var abilities = actor.Components.SingleOrDefault(component => component.DefinitionId == AbilitiesDefinition);
        if (abilities is null || !TryAbilities(abilities.Data, out var scores))
            return Invalid(actorId, backgroundId, "BASE_ABILITIES_REQUIRED", "actorId", "CH2 base ability-score state must exist and be a valid closed six-score object.");

        var scope = await _participation.ResolveActiveScopeAsync(actorId, cancellationToken);
        if (!scope.Valid) return Invalid(actorId, backgroundId, "CAMPAIGN_SCOPE_REQUIRED", "actorId", "A valid active campaign participation is required before resolving a background increase.");

        var background = await _world.GetEntityAsync(backgroundId, cancellationToken);
        if (background is null) return Invalid(actorId, backgroundId, "BACKGROUND_NOT_FOUND", "backgroundDefinitionId", "The bound background definition does not exist.");
        var identity = background.Components.SingleOrDefault(component => component.DefinitionId == ContentDefinition);
        var options = background.Components.SingleOrDefault(component => component.DefinitionId == OptionsDefinition);
        if (identity is null || options is null || !TryProfile(identity.Data, options.Data, out var profile))
            return Invalid(actorId, backgroundId, "INVALID_BACKGROUND_PROFILE", "backgroundDefinitionId", "The bound entity must be an active matching source-cited background identity and ability-increase profile.");

        if (!TrySelection(request?.SelectionJson, profile!, out var selected, out var selectionProblem))
            return Invalid(actorId, backgroundId, selectionProblem!.Code, selectionProblem.Path, selectionProblem.Reason);

        var delta = new JsonObject();
        foreach (var ability in AbilityIds)
        {
            if (!selected!.TryGetValue(ability, out var increase)) continue;
            var updated = scores![ability] + increase;
            if (updated > 20)
                return Invalid(actorId, backgroundId, "ABILITY_SCORE_CAP_EXCEEDED", ability, "A background ability increase cannot raise a raw score above 20.");
            delta[ability] = updated;
        }

        var canonical = delta.ToJsonString();
        return new("valid", actorId, backgroundId, scope.CampaignId, canonical,
            [new Effect { Type = EffectType.ComponentMerge, EntityId = actorId, DefinitionId = AbilitiesDefinition, Data = canonical }], []);
    }

    private static bool TryProfile(string identityJson, string optionsJson, out Profile? profile)
    {
        profile = null;
        try
        {
            using var identityDocument = JsonDocument.Parse(identityJson);
            using var optionsDocument = JsonDocument.Parse(optionsJson);
            var identity = identityDocument.RootElement;
            var options = optionsDocument.RootElement;
            if (!Exact(identity, "kind", "contentKey", "contentVersion", "status", "sourceRef") ||
                !String(identity, "kind", out var kind) || kind != "background" ||
                !String(identity, "contentKey", out var key) || !Integer(identity, "contentVersion", out var version) || version < 1 ||
                !String(identity, "status", out var status) || status != "active" || !Source(identity.GetProperty("sourceRef"))) return false;
            if (!Exact(options, "contentKey", "contentVersion", "sourceRef", "eligibleAbilities", "allowedPatterns") ||
                !String(options, "contentKey", out var profileKey) || profileKey != key ||
                !Integer(options, "contentVersion", out var profileVersion) || profileVersion != version ||
                !options.TryGetProperty("sourceRef", out var profileSource) || !SameSource(identity.GetProperty("sourceRef"), profileSource) ||
                !options.TryGetProperty("eligibleAbilities", out var eligibleNode) || eligibleNode.ValueKind != JsonValueKind.Array || eligibleNode.GetArrayLength() != 3 ||
                !options.TryGetProperty("allowedPatterns", out var patternsNode) || patternsNode.ValueKind != JsonValueKind.Array) return false;

            var eligible = eligibleNode.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).ToArray();
            if (eligible.Any(string.IsNullOrWhiteSpace) || eligible.Distinct(StringComparer.Ordinal).Count() != 3 ||
                !eligible.SequenceEqual(eligible.OrderBy(ability => Array.IndexOf(AbilityIds, ability), Comparer<int>.Default), StringComparer.Ordinal) ||
                eligible.Any(ability => !AbilityIds.Contains(ability!, StringComparer.Ordinal))) return false;
            var patterns = patternsNode.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).ToArray();
            if (patterns.Length is < 1 or > 2 || patterns.Any(pattern => pattern is not ("plus-2-plus-1" or "plus-1-each")) ||
                patterns.Distinct(StringComparer.Ordinal).Count() != patterns.Length) return false;
            profile = new Profile(eligible!, patterns!);
            return true;
        }
        catch (JsonException) { return false; }
        catch (KeyNotFoundException) { return false; }
    }

    private static bool TrySelection(string? json, Profile profile, out IReadOnlyDictionary<string, int>? selected, out BackgroundAbilityScoreIncreaseProblem? problem)
    {
        selected = null; problem = null;
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail("INVALID_ABILITY_INCREASE_SELECTION", "selection", "Ability increases must be a JSON object.", out problem);
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!profile.Eligible.Contains(property.Name, StringComparer.Ordinal) || !property.Value.TryGetInt32(out var increase) || increase <= 0 || !values.TryAdd(property.Name, increase))
                    return Fail("INVALID_ABILITY_INCREASE_SELECTION", property.Name, "Selections must contain unique eligible abilities with positive integer increases.", out problem);
            }
            var plusTwoPlusOne = values.Count == 2 && values.Values.Order().SequenceEqual([1, 2]);
            var plusOneEach = values.Count == profile.Eligible.Count && values.Keys.Order(StringComparer.Ordinal).SequenceEqual(profile.Eligible.Order(StringComparer.Ordinal)) && values.Values.All(value => value == 1);
            if ((plusTwoPlusOne && profile.Patterns.Contains("plus-2-plus-1", StringComparer.Ordinal)) ||
                (plusOneEach && profile.Patterns.Contains("plus-1-each", StringComparer.Ordinal)))
            {
                selected = values;
                return true;
            }
            return Fail("ABILITY_INCREASE_PATTERN_NOT_ALLOWED", "selection", "Selection does not match a source-declared background ability-increase pattern.", out problem);
        }
        catch (JsonException) { return Fail("INVALID_ABILITY_INCREASE_SELECTION", "selection", "Ability increases must be valid JSON.", out problem); }
    }

    private static bool TryAbilities(string json, out IReadOnlyDictionary<string, int>? scores)
    {
        scores = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, AbilityIds)) return false;
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var ability in AbilityIds)
            {
                if (!Integer(root, ability, out var value) || value is < 1 or > 30) return false;
                values.Add(ability, value);
            }
            scores = values;
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool Source(JsonElement source) => Exact(source, "sourceId", "locator") && String(source, "sourceId", out var sourceId) && sourceId == SourceId && String(source, "locator", out var locator) && Text(locator, 200);
    private static bool SameSource(JsonElement left, JsonElement right) => Source(left) && Source(right) && left.GetProperty("sourceId").GetString() == right.GetProperty("sourceId").GetString() && left.GetProperty("locator").GetString() == right.GetProperty("locator").GetString();
    private static bool Exact(JsonElement element, params string[] names) => element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal));
    private static bool String(JsonElement element, string name, out string value) { value = string.Empty; return element.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value = node.GetString() ?? string.Empty); }
    private static bool Integer(JsonElement element, string name, out int value) { value = default; return element.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out value); }
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
    private static bool Id(string? value, string prefix) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value.StartsWith(prefix, StringComparison.Ordinal) && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static bool Fail(string code, string path, string reason, out BackgroundAbilityScoreIncreaseProblem? problem) { problem = new(code, path, reason, "Correct the source-bound selection and retry the character creation root."); return false; }
    private static BackgroundAbilityScoreIncreasePlan Invalid(string actorId, string backgroundId, string code, string path, string reason) => new("invalid", actorId, backgroundId, null, null, [], [new(code, path, reason, "Correct the source-bound selection and retry the character creation root.")]);
    private sealed record Profile(IReadOnlyList<string> Eligible, IReadOnlyList<string> Patterns);
}
