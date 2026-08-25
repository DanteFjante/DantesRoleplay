using DantesRoleplay.Ecs;

namespace DantesRoleplay.EcsEffects;

public static class ApplicationEcsEffectType
{
    public const string EntityCreate = "entity.create";
    public const string EntityDelete = "entity.delete";
    public const string ComponentAdd = "component.add";
    public const string ComponentSet = "component.set";
    public const string ComponentMerge = "component.merge";
    public const string ComponentRemove = "component.remove";
    public const string ContainmentMove = "containment.move";
    public const string ContainmentRemove = "containment.remove";
    public const string RelationshipSet = "relationship.set";
    public const string RelationshipRemove = "relationship.remove";

    public static readonly IReadOnlyList<string> All =
    [
        EntityCreate, EntityDelete,
        ComponentAdd, ComponentSet, ComponentMerge, ComponentRemove,
        ContainmentMove, ContainmentRemove, RelationshipSet, RelationshipRemove
    ];
}

public sealed record ApplicationEcsEffect
{
    public required string Type { get; init; }
    public string EntityId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TargetEntityId { get; init; } = string.Empty;
    public string Slot { get; init; } = string.Empty;
    public string QualifiedRelationshipKind { get; init; } = string.Empty;
    public EcsComponentReference? ComponentType { get; init; }
    public string DataJson { get; init; } = string.Empty;
    public int ExpectedRevision { get; init; }
}

public sealed record ApplicationEcsEffectBatch
{
    public required string StateSpaceId { get; init; }
    public required IReadOnlyList<ApplicationEcsEffect> Effects { get; init; }
    public string Intent { get; init; } = string.Empty;
    public IReadOnlyList<string> ProceduresUsed { get; init; } = [];
    public ApplicationEcsExecutionIdentity? ExecutionIdentity { get; init; }
}

/// <summary>
/// Optional host-owned identity for at-most-once execution. Ordinary callers leave this absent;
/// an orchestration coordinator derives both values and never accepts either from a model.
/// </summary>
public sealed record ApplicationEcsExecutionIdentity(string OperationId, string RequestFingerprint)
{
    public const string AuditTool = "system.ecs.effects";
    public string AuditSubject => "interaction-step:" + RequestFingerprint;
}

public sealed record ApplicationEcsEffectProblem(int Index, string Code, string Message);

public sealed record ApplicationEcsEffectReceipt(
    int Index,
    string Type,
    string EntityId,
    string QualifiedTypeId,
    int? Revision,
    int? RemovedRevision = null,
    string TargetEntityId = "",
    string QualifiedRelationshipKind = "");

public sealed record ApplicationEcsEffectResult(
    bool Applied,
    bool DryRun,
    string OperationId,
    IReadOnlyList<ApplicationEcsEffectReceipt> Receipts,
    IReadOnlyList<ApplicationEcsEffectProblem> Problems,
    bool Replayed = false)
{
    public bool Valid => Problems.Count == 0;
}

public interface IApplicationEcsEffectApplier
{
    Task<ApplicationEcsEffectResult> ApplyAsync(
        ApplicationEcsEffectBatch batch,
        bool dryRun = false,
        CancellationToken cancellationToken = default);
}

public static class ApplicationEcsEffectValidation
{
    public const int MaximumEffects = 128;
    public const int MaximumIntentLength = 2_000;
    public const int MaximumProcedures = 64;
    public const int MaximumProcedureIdLength = 200;

    public static IReadOnlyList<ApplicationEcsEffectProblem> Validate(ApplicationEcsEffectBatch? batch)
    {
        if (batch is null) return [new(-1, "BATCH_REQUIRED", "An ECS effect batch is required.")];
        if (string.IsNullOrWhiteSpace(batch.StateSpaceId) || batch.StateSpaceId.Length > 200)
            return [new(-1, "STATE_SPACE_REQUIRED", "A bounded state-space ID is required.")];
        if (batch.Effects is null) return [new(-1, "EFFECTS_REQUIRED", "The effect list is required.")];
        if (batch.Effects.Count > MaximumEffects)
            return [new(-1, "EFFECT_LIMIT", $"At most {MaximumEffects} effects may be applied in one batch.")];

        var problems = new List<ApplicationEcsEffectProblem>();
        if ((batch.Intent?.Length ?? 0) > MaximumIntentLength)
            problems.Add(new(-1, "INTENT_LIMIT", $"Intent may not exceed {MaximumIntentLength} characters."));
        if (batch.ProceduresUsed is null)
            problems.Add(new(-1, "PROCEDURES_REQUIRED", "The procedure list is required."));
        else
        {
            if (batch.ProceduresUsed.Count > MaximumProcedures)
                problems.Add(new(-1, "PROCEDURE_LIMIT", $"At most {MaximumProcedures} procedure IDs may be cited."));
            else for (var procedureIndex = 0; procedureIndex < batch.ProceduresUsed.Count; procedureIndex++)
            {
                var procedure = batch.ProceduresUsed[procedureIndex];
                if (string.IsNullOrWhiteSpace(procedure) || procedure.Length > MaximumProcedureIdLength)
                    problems.Add(new(-1, "PROCEDURE_INVALID", $"Procedure ID {procedureIndex} is missing or exceeds {MaximumProcedureIdLength} characters."));
            }
        }
        if (batch.ExecutionIdentity is not null
            && (!IsOperationId(batch.ExecutionIdentity.OperationId)
                || !IsUpperSha256(batch.ExecutionIdentity.RequestFingerprint)))
            problems.Add(new(-1, "EXECUTION_IDENTITY_INVALID",
                "Execution identity requires a 32-character lowercase operation ID and uppercase SHA-256 request fingerprint."));
        for (var index = 0; index < batch.Effects.Count; index++)
        {
            var effect = batch.Effects[index];
            if (effect is null) { problems.Add(new(index, "EFFECT_REQUIRED", "The effect is null.")); continue; }
            if (!ApplicationEcsEffectType.All.Contains(effect.Type, StringComparer.Ordinal))
            {
                problems.Add(new(index, "EFFECT_TYPE", $"Unknown ECS effect type '{effect.Type}'."));
                continue;
            }
            if (string.IsNullOrWhiteSpace(effect.EntityId) || effect.EntityId.Length > 200)
                problems.Add(new(index, "ENTITY_REQUIRED", "A bounded entity ID is required."));

            switch (effect.Type)
            {
                case ApplicationEcsEffectType.EntityCreate:
                    if (string.IsNullOrWhiteSpace(effect.Name) || effect.Name.Length > 400)
                        problems.Add(new(index, "NAME_REQUIRED", "Entity creation requires a bounded name."));
                    if (effect.ExpectedRevision != 0) problems.Add(new(index, "REVISION_INVALID", "Entity creation requires expected revision zero."));
                    if (effect.ComponentType is not null || !string.IsNullOrEmpty(effect.DataJson)
                        || !string.IsNullOrEmpty(effect.TargetEntityId) || !string.IsNullOrEmpty(effect.Slot)
                        || !string.IsNullOrEmpty(effect.QualifiedRelationshipKind))
                        problems.Add(new(index, "FIELDS_INVALID", "Entity creation cannot carry component or edge fields."));
                    break;
                case ApplicationEcsEffectType.EntityDelete:
                    if (effect.ExpectedRevision < 1) problems.Add(new(index, "REVISION_REQUIRED", "Entity deletion requires a positive expected revision."));
                    if (effect.ComponentType is not null || !string.IsNullOrEmpty(effect.DataJson)
                        || !string.IsNullOrEmpty(effect.Name) || !string.IsNullOrEmpty(effect.TargetEntityId)
                        || !string.IsNullOrEmpty(effect.Slot) || !string.IsNullOrEmpty(effect.QualifiedRelationshipKind))
                        problems.Add(new(index, "FIELDS_INVALID", "Entity deletion cannot carry create, component, or edge fields."));
                    break;
                case ApplicationEcsEffectType.ComponentAdd:
                case ApplicationEcsEffectType.ComponentSet:
                case ApplicationEcsEffectType.ComponentMerge:
                case ApplicationEcsEffectType.ComponentRemove:
                    ValidateComponent(effect, index, problems);
                    break;
                case ApplicationEcsEffectType.ContainmentMove:
                    if (string.IsNullOrWhiteSpace(effect.TargetEntityId) || effect.TargetEntityId.Length > 200)
                        problems.Add(new(index, "TARGET_ENTITY_REQUIRED", "Containment move requires a bounded container entity ID."));
                    if (effect.Slot is null || effect.Slot.Length > 100)
                        problems.Add(new(index, "SLOT_INVALID", "A containment slot may not exceed 100 characters."));
                    if (effect.ExpectedRevision < 0)
                        problems.Add(new(index, "REVISION_INVALID", "Containment move requires a non-negative expected revision."));
                    ValidateNoComponentOrRelationshipFields(effect, index, problems, allowSlot: true);
                    break;
                case ApplicationEcsEffectType.ContainmentRemove:
                    if (effect.ExpectedRevision < 1)
                        problems.Add(new(index, "REVISION_REQUIRED", "Containment removal requires a positive expected revision."));
                    ValidateNoComponentOrRelationshipFields(effect, index, problems);
                    break;
                case ApplicationEcsEffectType.RelationshipSet:
                case ApplicationEcsEffectType.RelationshipRemove:
                    if (string.IsNullOrWhiteSpace(effect.TargetEntityId) || effect.TargetEntityId.Length > 200)
                        problems.Add(new(index, "TARGET_ENTITY_REQUIRED", "A relationship effect requires a bounded target entity ID."));
                    if (string.IsNullOrWhiteSpace(effect.QualifiedRelationshipKind) || effect.QualifiedRelationshipKind.Length > 200)
                        problems.Add(new(index, "RELATIONSHIP_KIND_REQUIRED", "A relationship effect requires a bounded qualified kind."));
                    var set = effect.Type == ApplicationEcsEffectType.RelationshipSet;
                    if ((set && effect.ExpectedRevision < 0) || (!set && effect.ExpectedRevision < 1))
                        problems.Add(new(index, "REVISION_INVALID", set
                            ? "Relationship set requires a non-negative expected revision."
                            : "Relationship removal requires a positive expected revision."));
                    if (set && string.IsNullOrWhiteSpace(effect.DataJson))
                        problems.Add(new(index, "DATA_REQUIRED", "Relationship set requires JSON data."));
                    if (!set && !string.IsNullOrEmpty(effect.DataJson))
                        problems.Add(new(index, "FIELDS_INVALID", "Relationship removal cannot carry data."));
                    if (effect.ComponentType is not null || !string.IsNullOrEmpty(effect.Name) || !string.IsNullOrEmpty(effect.Slot))
                        problems.Add(new(index, "FIELDS_INVALID", "Relationship effects cannot carry entity, component, or containment fields."));
                    break;
            }
        }
        return problems.AsReadOnly();
    }

    private static bool IsOperationId(string value) => value is { Length: 32 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    private static bool IsUpperSha256(string value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');

    private static void ValidateComponent(ApplicationEcsEffect effect, int index, List<ApplicationEcsEffectProblem> problems)
    {
        if (effect.ComponentType is null) problems.Add(new(index, "COMPONENT_TYPE_REQUIRED", "A component effect requires an exact component type reference."));
        else
        {
            try { effect.ComponentType.Validate(); }
            catch (ArgumentException exception) { problems.Add(new(index, "COMPONENT_TYPE_INVALID", exception.Message)); }
        }
        var isAdd = effect.Type == ApplicationEcsEffectType.ComponentAdd;
        if ((isAdd && effect.ExpectedRevision != 0) || (!isAdd && effect.ExpectedRevision < 1))
            problems.Add(new(index, "REVISION_INVALID", isAdd ? "Component add requires expected revision zero." : "Component update/removal requires a positive expected revision."));
        var carriesData = effect.Type != ApplicationEcsEffectType.ComponentRemove;
        if (carriesData && string.IsNullOrWhiteSpace(effect.DataJson)) problems.Add(new(index, "DATA_REQUIRED", "This component effect requires JSON data."));
        if (!carriesData && !string.IsNullOrEmpty(effect.DataJson)) problems.Add(new(index, "FIELDS_INVALID", "Component removal cannot carry data."));
        if (!string.IsNullOrEmpty(effect.Name)) problems.Add(new(index, "FIELDS_INVALID", "Component effects cannot carry an entity name."));
        if (!string.IsNullOrEmpty(effect.TargetEntityId) || !string.IsNullOrEmpty(effect.Slot)
            || !string.IsNullOrEmpty(effect.QualifiedRelationshipKind))
            problems.Add(new(index, "FIELDS_INVALID", "Component effects cannot carry edge fields."));
    }

    private static void ValidateNoComponentOrRelationshipFields(
        ApplicationEcsEffect effect,
        int index,
        List<ApplicationEcsEffectProblem> problems,
        bool allowSlot = false)
    {
        if (effect.ComponentType is not null || !string.IsNullOrEmpty(effect.DataJson)
            || !string.IsNullOrEmpty(effect.Name) || !string.IsNullOrEmpty(effect.QualifiedRelationshipKind)
            || (!allowSlot && !string.IsNullOrEmpty(effect.Slot))
            || (!allowSlot && !string.IsNullOrEmpty(effect.TargetEntityId)))
            problems.Add(new(index, "FIELDS_INVALID", "The containment effect carries fields owned by another effect type."));
    }
}
