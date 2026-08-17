namespace DantesRoleplay.Effects;

/// <summary>
/// The single doorway through which world state changes.
///
/// Every effect in a list is validated before any of them is applied, and the whole list runs in
/// one transaction. This is ARCHITECTURE.md §3.8: TravelRoleplay applied changes as it walked the
/// list, so a five-step action that failed on step four left three steps of damage behind and no
/// record of what half-happened. Partial application is worse than outright failure, because the
/// failure is at least visible.
///
/// Validation is also the reason a mechanic can be a pure function (§3.6). It returns a list of
/// proposed changes and never touches the database; the applier decides whether they are coherent.
/// </summary>
public interface IEffectApplier
{
    /// <summary>
    /// Validate a list of effects and, unless <paramref name="dryRun"/>, apply it atomically.
    /// </summary>
    /// <param name="dryRun">
    /// True validates and reports without writing. The same code path, so a clean dry run is real
    /// evidence rather than an optimistic guess.
    /// </param>
    /// <returns>
    /// <see cref="EffectResult.Valid"/> when the list is coherent; <see cref="EffectResult.Problems"/>
    /// lists <em>every</em> fault found, not just the first, so one round trip is enough to fix them.
    /// </returns>
    Task<EffectResult> ApplyAsync(
        IReadOnlyList<Effect> effects,
        bool dryRun = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Structural checks that need no database. Split out from the applier so the sandbox host (§P8)
/// can reject a malformed effect list the moment a mechanic returns it, without a round trip.
/// </summary>
public static class EffectValidation
{
    /// <summary>
    /// Field-level validation of one effect: is the verb known, and are the fields that verb needs
    /// present? Says nothing about whether the entities exist — that is the applier's job.
    /// </summary>
    /// <returns>Null when the effect is well-formed, otherwise what is wrong and how to fix it.</returns>
    public static string? Check(Effect effect)
    {
        if (effect is null)
        {
            return "Effect is null.";
        }

        var type = effect.Type?.Trim() ?? string.Empty;

        if (type.Length == 0)
        {
            return $"Missing 'type'. One of: {string.Join(", ", EffectType.All)}.";
        }

        if (!EffectType.All.Contains(type))
        {
            return $"Unknown effect type '{type}'. One of: {string.Join(", ", EffectType.All)}.";
        }

        return type switch
        {
            EffectType.EntityCreate => CheckEntityCreate(effect),
            EffectType.EntityDelete => Require(effect.EntityId, "entityId", type),

            EffectType.ComponentAdd or
            EffectType.ComponentSet or
            EffectType.ComponentMerge =>
                Require(effect.EntityId, "entityId", type)
                ?? Require(effect.DefinitionId, "definitionId", type)
                ?? CheckJsonObject(effect.Data, type),

            EffectType.ComponentRemove =>
                Require(effect.EntityId, "entityId", type)
                ?? Require(effect.DefinitionId, "definitionId", type),

            EffectType.ContainmentMove => CheckMove(effect),

            EffectType.RelationshipCreate =>
                Require(effect.EntityId, "entityId", type)
                ?? Require(effect.ToEntityId, "toEntityId", type)
                ?? Require(effect.Kind, "kind", type)
                ?? CheckJsonObject(effect.Data, type),

            EffectType.RelationshipRemove =>
                Require(effect.EntityId, "entityId", type)
                ?? Require(effect.ToEntityId, "toEntityId", type)
                ?? Require(effect.Kind, "kind", type),

            _ => null
        };
    }

    private static string? CheckEntityCreate(Effect effect)
    {
        var missingName = Require(effect.Name, "name", EffectType.EntityCreate);

        if (missingName is not null)
        {
            return missingName;
        }

        // An explicit id is mandatory, and this is the one place the rule may look fussy.
        //
        // The whole list is validated before any of it is applied, so an id the applier would have
        // generated does not exist at validation time — a later `component.set` in the same list
        // could not name the entity it belongs to. Requiring the caller to choose the id also keeps
        // an effect list replayable, which a Guid generated inside the applier would not be.
        return string.IsNullOrWhiteSpace(effect.EntityId)
            ? "entity.create needs 'entityId'. Choose a stable id — the list is validated before " +
              "anything is applied, so a generated id could not be referenced by later effects."
            : null;
    }

    private static string? CheckMove(Effect effect)
    {
        var missing = Require(effect.EntityId, "entityId", EffectType.ContainmentMove);

        if (missing is not null)
        {
            return missing;
        }

        // Empty toEntityId is meaningful here: "take it out of whatever holds it".
        return effect.EntityId.Trim() == effect.ToEntityId.Trim()
            ? "containment.move cannot put an entity inside itself."
            : null;
    }

    private static string? Require(string? value, string field, string type) =>
        string.IsNullOrWhiteSpace(value) ? $"{type} needs '{field}'." : null;

    private static string? CheckJsonObject(string? data, string type)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        try
        {
            using var parsed = System.Text.Json.JsonDocument.Parse(data);

            return parsed.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                ? null
                : $"{type} needs 'data' to be a JSON object, got {parsed.RootElement.ValueKind}.";
        }
        catch (System.Text.Json.JsonException ex)
        {
            return $"{type} has invalid JSON in 'data': {ex.Message}";
        }
    }
}
