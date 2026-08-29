using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Characters;
using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Resolves the SRD's universal Common-plus-two-standard-languages origin rule into one staged
/// language component fragment. It has no selection, randomization, direct-write, or grant authority.
/// </summary>
public sealed class CharacterOriginLanguageResolver(
    IWorldStore world,
    ICampaignCharacterParticipationVerifier participation) : ICharacterOriginLanguageResolver
{
    private const string Definition = "dnd2024.language-proficiencies";
    private const string SourceId = "source.dnd2024.srd-5.2.1";
    private const string Locator = "Character Creation > Step 2: Character Origin > Choose Languages";
    private static readonly string[] Vocabulary =
    [
        "abyssal", "celestial", "common", "common-sign-language", "deep-speech", "draconic",
        "druidic", "dwarvish", "elvish", "giant", "gnomish", "goblin", "halfling", "infernal",
        "orc", "primordial", "sylvan", "thieves-cant", "undercommon"
    ];
    private static readonly string[] StandardSelections =
    ["common-sign-language", "draconic", "dwarvish", "elvish", "giant", "gnomish", "goblin", "halfling", "orc"];
    private readonly IWorldStore _world = world;
    private readonly ICampaignCharacterParticipationVerifier _participation = participation;

    public async Task<CharacterOriginLanguagePlan> PlanAsync(
        CharacterOriginLanguageRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = request?.ActorId ?? string.Empty;
        if (!Id(actorId))
            return Invalid(actorId, "INVALID_ACTOR_ID", "actorId", "actorId must be a canonical actor.* id.");

        var actor = await _world.GetEntityAsync(actorId, cancellationToken);
        if (actor is null)
            return Invalid(actorId, "ACTOR_NOT_FOUND", "actorId", "actorId must name an existing or staged actor.");

        var existing = actor.Components.SingleOrDefault(component => component.DefinitionId == Definition);
        if (existing is not null)
        {
            if (!ValidLanguageState(existing.Data))
                return Invalid(actorId, "INVALID_EXISTING_LANGUAGE_STATE", "actorId", "Existing language state is corrupt and cannot be replaced by the origin resolver.");
            return Invalid(actorId, "LANGUAGE_PROFICIENCIES_ALREADY_EXIST", "actorId", "Initial origin languages can be recorded only when language state is absent.");
        }

        var scope = await _participation.ResolveActiveScopeAsync(actorId, cancellationToken);
        if (!scope.Valid)
            return Invalid(actorId, "CAMPAIGN_SCOPE_REQUIRED", "actorId", "A valid active campaign participation is required before resolving origin languages.");

        if (!TrySelection(request?.SelectionJson, out var selected, out var problem))
            return Invalid(actorId, problem!.Code, problem.Path, problem.Reason);

        var languages = new[] { "common", selected![0], selected[1] }
            .OrderBy(language => Array.IndexOf(Vocabulary, language))
            .ToArray();
        var canonical = JsonSerializer.Serialize(new
        {
            languages,
            sourceRef = new { sourceId = SourceId, locator = Locator }
        });
        return new("valid", actorId, scope.CampaignId, canonical,
            [new Effect { Type = EffectType.ComponentAdd, EntityId = actorId, DefinitionId = Definition, Data = canonical }], []);
    }

    private static bool TrySelection(string? json, out string[]? selected, out CharacterOriginLanguageProblem? problem)
    {
        selected = null;
        problem = null;
        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Exact(root, "languages") ||
                !root.TryGetProperty("languages", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != 2)
                return Fail("INVALID_ORIGIN_LANGUAGE_SELECTION", "selection", "Selection must be exactly {\"languages\":[<two standard language ids>]}", out problem);

            var choices = values.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null).ToArray();
            if (choices.Any(string.IsNullOrWhiteSpace) || choices.Distinct(StringComparer.Ordinal).Count() != 2)
                return Fail("INVALID_ORIGIN_LANGUAGE_SELECTION", "languages", "Select exactly two distinct canonical standard language ids.", out problem);
            if (choices.Any(choice => !StandardSelections.Contains(choice!, StringComparer.Ordinal)))
                return Fail("ORIGIN_LANGUAGE_NOT_STANDARD", "languages", "Each origin choice must be one of the source table's non-Common standard languages.", out problem);

            selected = choices!;
            return true;
        }
        catch (JsonException)
        {
            return Fail("INVALID_ORIGIN_LANGUAGE_SELECTION", "selection", "Selection must be valid JSON.", out problem);
        }
    }

    private static bool ValidLanguageState(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "languages", "sourceRef") || !root.TryGetProperty("languages", out var languages) || languages.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("sourceRef", out var source) || !Exact(source, "sourceId", "locator") ||
                !String(source, "sourceId", out var sourceId) || sourceId != SourceId || !String(source, "locator", out var locator) || locator != Locator) return false;
            var prior = languages.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null).ToArray();
            return prior.All(value => !string.IsNullOrWhiteSpace(value) && Vocabulary.Contains(value!, StringComparer.Ordinal)) &&
                   prior.Distinct(StringComparer.Ordinal).Count() == prior.Length &&
                   prior.SequenceEqual(prior.OrderBy(value => Array.IndexOf(Vocabulary, value), Comparer<int>.Default), StringComparer.Ordinal);
        }
        catch (JsonException) { return false; }
    }

    private static bool Exact(JsonElement element, params string[] names) =>
        element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal));
    private static bool String(JsonElement element, string name, out string value) { value = string.Empty; return element.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value = node.GetString() ?? string.Empty); }
    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value.StartsWith("actor.", StringComparison.Ordinal) && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static bool Fail(string code, string path, string reason, out CharacterOriginLanguageProblem? problem) { problem = new(code, path, reason, "Correct the source-bound selection and retry the character creation root."); return false; }
    private static CharacterOriginLanguagePlan Invalid(string actorId, string code, string path, string reason) => new("invalid", actorId, null, null, [], [new(code, path, reason, "Correct the source-bound selection and retry the character creation root.")]);
}
