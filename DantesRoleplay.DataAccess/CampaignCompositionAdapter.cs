using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>R5's C2-equivalent, zero-write campaign child for a valid W17 virtual World.</summary>
public sealed class CampaignCompositionAdapter : ICampaignCompositionAdapter
{
    private static readonly (string LocalKey, string Role, string Audience)[] References =
    [
        ("location.gate", "start", "party"), ("actor.one", "npc", "party"), ("actor.two", "npc", "party"), ("faction", "faction-stake", "party"),
        ("knowledge.fact", "knowledge", "party"), ("knowledge.rumour", "knowledge", "party"), ("knowledge.clue.one", "knowledge", "gm"),
        ("knowledge.clue.three", "knowledge", "gm"), ("knowledge.clue.two", "knowledge", "gm"), ("knowledge.secret", "knowledge", "gm")
    ];

    public async Task<CampaignCompositionResult> ComposeAsync(NewWorldCampaignBlueprint request, SmallWorldCompositionResult world, CancellationToken cancellationToken = default)
    {
        if (request is null) return Invalid("INVALID_BLUEPRINT", "blueprint", "A closed new-world campaign blueprint is required.");
        if (!ValidCampaignId(request.CampaignId)) return Invalid("INVALID_CAMPAIGN_ID", "campaignId", "campaignId must be a trimmed lowercase campaign.* id.");
        if (!world.Valid || world.WorldRootId is null || world.World is null) return Invalid("INVALID_STAGED_WORLD", "world", "A valid staged World result is required before campaign composition.");

        var suffix = request.CampaignId["campaign.".Length..];
        var expectedRoot = $"world.c10.{suffix}.world";
        if (world.WorldRootId != expectedRoot) return Invalid("INVALID_STAGED_WORLD", "world.worldRootId", "The staged World root does not match the ratified campaign namespace.");
        var map = world.LocalKeyMap.ToDictionary(entry => entry.LocalKey, entry => entry.EntityId, StringComparer.Ordinal);
        if (map.Count != world.LocalKeyMap.Count || !References.All(reference => map.TryGetValue(reference.LocalKey, out var id) && id == $"world.c10.{suffix}.{reference.LocalKey}"))
            return Invalid("INVALID_STAGED_WORLD", "world.localKeyMap", "The staged World lacks the ratified canonical local-key mapping.");

        var blueprint = new CampaignBlueprint(
            request.CampaignId, request.Title, request.Premise, request.PartyGoals, request.ToneAndBoundaries, request.RulesetScope,
            world.WorldRootId, map["location.gate"], References.Select(reference => new CampaignReference(map[reference.LocalKey], reference.Role, reference.Audience)).ToArray(),
            request.InitialChapter, request.InitialArc);
        var review = await new CampaignBlueprintValidator(world.World).ValidateAsync(blueprint, cancellationToken);
        if (!review.Valid) return new("invalid", null, review.ResolvedReferences, null, [], review.Problems);

        return new("valid", blueprint, review.ResolvedReferences, review.CreationCounts, Effects(blueprint, review), []);
    }

    private static IReadOnlyList<Effect> Effects(CampaignBlueprint blueprint, CampaignValidationResult review)
    {
        var root = JsonSerializer.Serialize(new
        {
            status = "active", title = blueprint.Title, premise = blueprint.Premise,
            partyGoals = blueprint.PartyGoals, toneAndBoundaries = blueprint.ToneAndBoundaries,
            rulesetScope = "dnd2024", creationMethod = "manual", reviewFingerprint = review.ReviewFingerprint
        });
        var effects = new List<Effect>
        {
            new() { Type = EffectType.EntityCreate, EntityId = blueprint.CampaignId, Name = blueprint.Title },
            new() { Type = EffectType.ComponentAdd, EntityId = blueprint.CampaignId, DefinitionId = "game.core.campaign.root", Data = root },
            new() { Type = EffectType.RelationshipCreate, EntityId = blueprint.CampaignId, ToEntityId = blueprint.ExistingWorldId, Kind = "game.core.campaign.in-world", Data = "{}" }
        };
        effects.AddRange(review.ResolvedReferences.Select(reference => new Effect
        {
            Type = EffectType.RelationshipCreate, EntityId = blueprint.CampaignId, ToEntityId = reference.EntityId,
            Kind = "game.core.campaign.references", Data = JsonSerializer.Serialize(new { role = reference.Role, audience = reference.Audience })
        }));
        return effects;
    }

    private static CampaignCompositionResult Invalid(string code, string path, string reason) =>
        new("invalid", null, [], null, [], [new CampaignProblem(code, path, reason, "Correct the closed composition request and validate again.")]);
    private static bool ValidCampaignId(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.StartsWith("campaign.", StringComparison.Ordinal) && value.Length is > 9 and <= 100 && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
}
