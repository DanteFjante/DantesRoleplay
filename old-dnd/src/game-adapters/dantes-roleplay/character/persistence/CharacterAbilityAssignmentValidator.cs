using System.Text.Json;
using DantesRoleplay.Characters;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// CH2's internal, zero-effect ability-allocation validator. The policy is immutable catalog
/// content and is bound by a future composition root rather than selected by an action caller.
/// </summary>
public sealed class CharacterAbilityAssignmentValidator(IWorldStore world) : ICharacterAbilityAssignmentValidator
{
    private const string Definition = "dnd2024.character.ability-assignment-policy";
    private const string SourceId = "source.dnd2024.srd-5.2.1";
    private static readonly string[] AbilityIds = ["str", "dex", "con", "int", "wis", "cha"];
    private readonly IWorldStore _world = world;

    public async Task<CharacterAbilityAssignmentValidationPlan> ValidateAsync(
        CharacterAbilityAssignmentValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        var policyId = request?.BoundPolicyEntityId ?? string.Empty;
        if (!Id(policyId)) return Invalid(policyId, "INVALID_POLICY_ID", "policy", "The bound policy must be a canonical lowercase dotted id.");

        var entity = await _world.GetEntityAsync(policyId, cancellationToken);
        if (entity is null) return Invalid(policyId, "POLICY_NOT_FOUND", "policy", "The bound policy entity does not exist.");
        var component = entity.Components.SingleOrDefault(x => x.DefinitionId == Definition);
        if (component is null) return Invalid(policyId, "POLICY_COMPONENT_REQUIRED", "policy", "The bound entity has no ability-assignment policy component.");
        if (!TryPolicy(component.Data, out var policy, out var policyProblem))
            return Invalid(policyId, policyProblem!.Code, policyProblem.Path, policyProblem.Reason);
        if (!TryScores(request?.ScoresJson, policy!, out var canonical, out var scoresProblem))
            return Invalid(policyId, scoresProblem!.Code, scoresProblem.Path, scoresProblem.Reason, policy!.Version);

        return new("valid", policyId, policy!.Version, canonical, []);
    }

    private static bool TryPolicy(string json, out Policy? policy, out CharacterAbilityAssignmentProblem? problem)
    {
        policy = null;
        problem = null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Exact(root, "policyVersion", "sourceRef", "scoreBounds", "allocation"))
                return Fail("INVALID_POLICY", "policy", "Policy must be a closed object with version, sourceRef, scoreBounds, and allocation.", out problem);
            if (!Integer(root, "policyVersion", out var version) || version < 1)
                return Fail("INVALID_POLICY", "policyVersion", "policyVersion must be a positive integer.", out problem);
            if (!root.TryGetProperty("sourceRef", out var source) || !Source(source))
                return Fail("INVALID_POLICY", "sourceRef", "Policy sourceRef must identify the registered SRD 5.2.1 source with a trimmed locator.", out problem);
            if (!root.TryGetProperty("scoreBounds", out var bounds) || !Exact(bounds, "minimum", "maximum")
                || !Integer(bounds, "minimum", out var minimum) || !Integer(bounds, "maximum", out var maximum)
                || minimum < 1 || maximum > 30 || minimum > maximum)
                return Fail("INVALID_POLICY", "scoreBounds", "scoreBounds must be closed integer bounds within the existing ability-score range.", out problem);
            if (!root.TryGetProperty("allocation", out var allocation) || allocation.ValueKind != JsonValueKind.Object
                || !allocation.TryGetProperty("family", out var familyNode) || familyNode.ValueKind != JsonValueKind.String)
                return Fail("INVALID_POLICY", "allocation", "allocation must declare one supported closed family.", out problem);

            var family = familyNode.GetString();
            if (family == "fixed-multiset")
            {
                if (!Exact(allocation, "family", "values") || !allocation.TryGetProperty("values", out var values)
                    || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != AbilityIds.Length)
                    return Fail("INVALID_POLICY", "allocation.values", "A fixed-multiset policy must declare exactly six values.", out problem);
                var declared = new List<int>();
                foreach (var value in values.EnumerateArray())
                {
                    if (!value.TryGetInt32(out var score) || score < minimum || score > maximum)
                        return Fail("INVALID_POLICY", "allocation.values", "Every declared fixed-multiset score must be an integer inside scoreBounds.", out problem);
                    declared.Add(score);
                }
                policy = new Policy(version, minimum, maximum, family, declared.Order().ToArray(), null, null);
                return true;
            }

            if (family == "point-budget")
            {
                if (!Exact(allocation, "family", "budget", "costs") || !Integer(allocation, "budget", out var budget) || budget < 0
                    || !allocation.TryGetProperty("costs", out var costs) || costs.ValueKind != JsonValueKind.Array || costs.GetArrayLength() == 0)
                    return Fail("INVALID_POLICY", "allocation", "A point-budget policy needs a nonnegative budget and a nonempty cost table.", out problem);
                var table = new Dictionary<int, int>();
                var prior = minimum - 1;
                foreach (var entry in costs.EnumerateArray())
                {
                    if (!Exact(entry, "score", "cost") || !Integer(entry, "score", out var score) || !Integer(entry, "cost", out var cost)
                        || score < minimum || score > maximum || cost < 0 || score <= prior)
                        return Fail("INVALID_POLICY", "allocation.costs", "Point-budget costs must be unique, ascending in-range scores with nonnegative integer costs.", out problem);
                    table.Add(score, cost);
                    prior = score;
                }
                policy = new Policy(version, minimum, maximum, family, null, budget, table);
                return true;
            }

            return Fail("INVALID_POLICY", "allocation.family", "allocation.family must be fixed-multiset or point-budget.", out problem);
        }
        catch (JsonException)
        {
            return Fail("INVALID_POLICY", "policy", "Policy component data must be valid JSON.", out problem);
        }
    }

    private static bool TryScores(string? json, Policy policy, out string? canonical, out CharacterAbilityAssignmentProblem? problem)
    {
        canonical = null;
        problem = null;

        try
        {
            using var document = JsonDocument.Parse(json ?? string.Empty);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Exact(root, AbilityIds))
                return Fail("INVALID_SCORES", "scores", "Scores must be an object with exactly str, dex, con, int, wis, and cha.", out problem);
            var scores = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var ability in AbilityIds)
            {
                if (!Integer(root, ability, out var score) || score < policy.Minimum || score > policy.Maximum)
                    return Fail("INVALID_SCORES", ability, "Every score must be an integer within the bound policy's scoreBounds.", out problem);
                scores.Add(ability, score);
            }
            var values = scores.Values.Order().ToArray();
            if (policy.Family == "fixed-multiset" && !values.SequenceEqual(policy.Values!))
                return Fail("ASSIGNMENT_NOT_ALLOWED", "scores", "Scores do not match the bound policy's fixed multiset.", out problem);
            if (policy.Family == "point-budget" && (!values.All(score => policy.Costs!.ContainsKey(score)) || values.Sum(score => policy.Costs![score]) != policy.Budget))
                return Fail("ASSIGNMENT_NOT_ALLOWED", "scores", "Scores do not use exactly the bound policy's point budget.", out problem);
            canonical = JsonSerializer.Serialize(new { str = scores["str"], dex = scores["dex"], con = scores["con"], @int = scores["int"], wis = scores["wis"], cha = scores["cha"] });
            return true;
        }
        catch (JsonException)
        {
            return Fail("INVALID_SCORES", "scores", "Scores must be valid JSON.", out problem);
        }
    }

    private static bool Exact(JsonElement element, params string[] names) =>
        element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal));

    private static bool Integer(JsonElement element, string name, out int value)
    {
        value = default;
        return element.TryGetProperty(name, out var node)
            && node.ValueKind == JsonValueKind.Number
            && node.TryGetInt32(out value);
    }

    private static bool Source(JsonElement source) =>
        Exact(source, "sourceId", "locator")
        && source.TryGetProperty("sourceId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == SourceId
        && source.TryGetProperty("locator", out var locator) && locator.ValueKind == JsonValueKind.String
        && Text(locator.GetString(), 300);

    private static bool Fail(string code, string path, string reason, out CharacterAbilityAssignmentProblem? problem)
    {
        problem = new(code, path, reason, "Correct the bound policy or submitted scores and retry the character creation root.");
        return false;
    }

    private static CharacterAbilityAssignmentValidationPlan Invalid(string policyId, string code, string path, string reason, int? version = null) =>
        new("invalid", policyId, version, null, [new(code, path, reason, "Correct the bound policy or submitted scores and retry the character creation root.")]);

    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim() && value == value.ToLowerInvariant() && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;

    private sealed record Policy(int Version, int Minimum, int Maximum, string Family, IReadOnlyList<int>? Values, int? Budget, IReadOnlyDictionary<int, int>? Costs);
}
