using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;

namespace DantesRoleplay.ApplicationExecution;

public sealed class ApplicationActionRunner(
    IPublicApplicationCatalogProvider catalogs,
    IApplicationActivationReader activations,
    IStateSpaceRegistry stateSpaces,
    IApplicationComponentTypeRegistry componentTypes,
    IEntityComponentStore entities,
    IStateSpaceEdgeStore edges,
    IApplicationMechanicEvaluator evaluator,
    IApplicationEcsEffectApplier effects,
    IOperationLog operations) : IApplicationActionRunner
{
    public async Task<ApplicationActionExecutionResult> RunAsync(
        ApplicationActionExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ApplicationId);
        ArgumentNullException.ThrowIfNull(request.ExecutionIdentity);

        var replay = await ReplayAsync(request, cancellationToken);
        if (replay is not null) return replay;

        if (!ValidId(request.StateSpaceId) || !ValidId(request.QualifiedMechanicId)
            || !UpperSha256(request.ContentFingerprint)
            || request.RoleEntityIds is null || request.RoleEntityIds.Count > 32
            || request.RoleEntityIds.Any(value => !ValidId(value.Key) || !ValidId(value.Value)))
            return Failed(request, ApplicationActionExecutionDisposition.Failed,
                "APPLICATION_ACTION_INVALID", "The exact application action request is invalid.");

        var stateSpace = stateSpaces.Get(request.StateSpaceId);
        if (stateSpace is null || stateSpace.ApplicationRevision.ApplicationId != request.ApplicationId)
            return Failed(request, ApplicationActionExecutionDisposition.Stale,
                "STATE_SPACE_APPLICATION_MISMATCH", "The state space is unavailable for this application.");
        var activation = activations.Current(request.ApplicationId);
        if (activation is null || activation.ActivationFingerprint != stateSpace.ManifestFingerprint)
            return Failed(request, ApplicationActionExecutionDisposition.Stale,
                "STATE_SPACE_ACTIVATION_STALE", "The state space is not bound to the current application activation.");
        if (!catalogs.TryGet(request.ApplicationId, out var catalog))
            return Failed(request, ApplicationActionExecutionDisposition.Stale,
                "APPLICATION_CATALOG_UNAVAILABLE", "The current application catalog is unavailable.");

        CatalogRecordView record;
        try
        {
            record = catalog.Inspect(new(request.ApplicationId, request.ApplicationId.Value,
                request.QualifiedMechanicId));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return Failed(request, ApplicationActionExecutionDisposition.Stale,
                "MECHANIC_UNKNOWN", "The exact application mechanic is unavailable.");
        }
        if (record.Summary.Kind != "mechanic" || record.Summary.Status != "active"
            || record.Summary.ContentFingerprint != request.ContentFingerprint)
            return Failed(request, ApplicationActionExecutionDisposition.Stale,
                "MECHANIC_STALE", "The exact application mechanic changed or is inactive.");

        MechanicRequirements requirements;
        try
        {
            using var document = JsonDocument.Parse(record.ContentJson);
            if (!document.RootElement.TryGetProperty("requirements", out var value)
                || value.ValueKind != JsonValueKind.String)
                throw new JsonException();
            requirements = MechanicRequirements.Parse(value.GetString()!);
        }
        catch (JsonException)
        {
            return Failed(request, ApplicationActionExecutionDisposition.Unsupported,
                "MECHANIC_CONTRACT_INVALID", "The exact mechanic requirements are invalid.");
        }
        if (requirements.Event is not null || requirements.Children.Count > 0
            || requirements.ProjectionProblems().Count > 0 || requirements.CompositionProblems().Count > 0)
            return Failed(request, ApplicationActionExecutionDisposition.Unsupported,
                "MECHANIC_EXECUTION_UNSUPPORTED", "This mechanic requires an execution feature not enabled by this action owner.");

        var mapping = await BuildMappingAsync(stateSpace, requirements, cancellationToken);
        if (mapping.Problems.Count > 0)
            return Failed(request, ApplicationActionExecutionDisposition.Unsupported,
                mapping.Problems[0].Code, mapping.Problems[0].SafeMessage);

        var evaluation = await evaluator.EvaluateAsync(new(
            request.StateSpaceId,
            request.ApplicationId,
            request.QualifiedMechanicId,
            request.ContentFingerprint,
            mapping.Mapping!,
            request.RoleEntityIds,
            request.InputJson,
            request.Seed), cancellationToken);
        if (!evaluation.Evaluated || evaluation.Run is null)
            return Failed(request, ApplicationActionExecutionDisposition.Failed,
                "APPLICATION_ACTION_PROJECTION_FAILED", SafeProblem(evaluation.Problems));
        if (!evaluation.Run.Ok)
            return Failed(request, ApplicationActionExecutionDisposition.Failed,
                string.IsNullOrWhiteSpace(evaluation.Run.LimitHit) ? "MECHANIC_FAILED" : "MECHANIC_LIMIT",
                "The exact mechanic could not produce an accepted result.");
        if (evaluation.Run.Output.Events.Count > 0 || evaluation.Run.Output.Notifications.Count > 0)
            return Failed(request, ApplicationActionExecutionDisposition.Unsupported,
                "MECHANIC_OUTPUT_UNSUPPORTED", "Application event or notification output is not enabled for direct execution.");

        var translated = await TranslateAsync(
            stateSpace, mapping.Mapping!, evaluation.Run.Output.Effects, cancellationToken);
        if (translated.Problems.Count > 0)
            return Failed(request, translated.Stale
                    ? ApplicationActionExecutionDisposition.Stale
                    : ApplicationActionExecutionDisposition.Unsupported,
                translated.Problems[0].Code, translated.Problems[0].SafeMessage);

        var applied = await effects.ApplyAsync(new ApplicationEcsEffectBatch
        {
            StateSpaceId = request.StateSpaceId,
            Effects = translated.Effects,
            Intent = "Execute one verified application interaction step.",
            ProceduresUsed = [],
            ExecutionIdentity = request.ExecutionIdentity
        }, cancellationToken: cancellationToken);
        if (applied.Replayed)
            return Result(request, ApplicationActionExecutionDisposition.Replayed, applied.OperationId,
                "The exact application action was already committed.", 0, []);
        if (!applied.Applied || applied.Problems.Count > 0)
        {
            var problem = applied.Problems.FirstOrDefault();
            var stale = problem?.Code.Contains("STALE", StringComparison.Ordinal) == true
                || problem?.Code is "REFERENCE_UNKNOWN";
            return Result(request, stale ? ApplicationActionExecutionDisposition.Stale
                    : ApplicationActionExecutionDisposition.Failed,
                applied.OperationId, "", 0,
                [new(problem?.Code ?? "APPLICATION_EFFECTS_REJECTED",
                    "The application effect transaction was rejected.")]);
        }
        return Result(request, ApplicationActionExecutionDisposition.Succeeded, applied.OperationId,
            evaluation.Run.Output.Narration, applied.Receipts.Count, []);
    }

    private async Task<ApplicationActionExecutionResult?> ReplayAsync(
        ApplicationActionExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await operations.GetAsync(request.ExecutionIdentity.OperationId, cancellationToken);
        if (existing is null) return null;
        if (existing.Tool != ApplicationEcsExecutionIdentity.AuditTool
            || existing.Subject != request.ExecutionIdentity.AuditSubject)
            return Failed(request, ApplicationActionExecutionDisposition.Failed,
                "OPERATION_ID_CONFLICT", "The execution operation ID is bound to another request.");
        return existing.Success
            ? Result(request, ApplicationActionExecutionDisposition.Replayed, existing.Id,
                "The exact application action was already committed.", 0, [])
            : Result(request, ApplicationActionExecutionDisposition.Failed, existing.Id, "", 0,
                [new(string.IsNullOrWhiteSpace(existing.Error) ? "REPLAYED_FAILURE" : existing.Error,
                    "The same exact application action previously failed.")]);
    }

    private async Task<MappingResult> BuildMappingAsync(
        StateSpaceView stateSpace,
        MechanicRequirements requirements,
        CancellationToken cancellationToken)
    {
        var owners = stateSpace.ApplicationRevision.BaseApplications
            .Prepend(stateSpace.ApplicationRevision.ApplicationId).ToArray();
        var localIds = requirements.Roles.Values.SelectMany(value =>
                value.Components.Concat(value.ContentComponentIds ?? [])
                    .Concat((value.ComponentReferences ?? []).SelectMany(reference =>
                        new[] { reference.SourceComponentId }.Concat(reference.TargetComponentIds))))
            .Distinct(StringComparer.Ordinal).ToArray();
        var components = new Dictionary<string, EcsComponentReference>(StringComparer.Ordinal);
        foreach (var localId in localIds)
        {
            var resolved = ResolveComponent(owners, localId);
            if (resolved is null)
                return MappingResult.Failed("COMPONENT_MAPPING_MISSING",
                    "A declared component has no exact current application mapping.");
            components[localId] = resolved;
        }

        var relationships = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relationship in await edges.ListRelationshipsAsync(stateSpace.StateSpaceId, cancellationToken))
        {
            var owner = owners.FirstOrDefault(value => relationship.QualifiedKind.StartsWith(value.Value + ".", StringComparison.Ordinal));
            if (owner is null)
                return MappingResult.Failed("RELATIONSHIP_OWNER_INVALID",
                    "A projected relationship belongs to an unrelated application.");
            var local = relationship.QualifiedKind[(owner.Value.Length + 1)..];
            if (!relationships.TryAdd(local, relationship.QualifiedKind)
                && relationships[local] != relationship.QualifiedKind)
                return MappingResult.Failed("RELATIONSHIP_MAPPING_AMBIGUOUS",
                    "An application relationship mapping is ambiguous.");
        }
        return new(new(components, relationships), []);
    }

    private EcsComponentReference? ResolveComponent(
        IReadOnlyList<ApplicationIdentifier> owners,
        string localOrQualifiedId)
    {
        if (string.IsNullOrWhiteSpace(localOrQualifiedId)) return null;
        foreach (var owner in owners)
        {
            var qualified = localOrQualifiedId.StartsWith(owner.Value + ".", StringComparison.Ordinal)
                ? localOrQualifiedId
                : owner.Value + "." + localOrQualifiedId;
            var value = componentTypes.GetLatest(qualified);
            if (value is not null) return new(value.QualifiedId, value.Version, value.SchemaHash);
            if (localOrQualifiedId.Contains('.', StringComparison.Ordinal)
                && owners.Any(candidate => localOrQualifiedId.StartsWith(candidate.Value + ".", StringComparison.Ordinal)))
                return null;
        }
        return null;
    }

    private async Task<TranslationResult> TranslateAsync(
        StateSpaceView stateSpace,
        ApplicationMechanicProjectionMapping mapping,
        IReadOnlyList<Effect> proposed,
        CancellationToken cancellationToken)
    {
        if (proposed.Count > ApplicationEcsEffectValidation.MaximumEffects)
            return TranslationResult.Failed("EFFECT_LIMIT", "The mechanic proposed too many effects.");
        var owners = stateSpace.ApplicationRevision.BaseApplications
            .Prepend(stateSpace.ApplicationRevision.ApplicationId).ToArray();
        var result = new List<ApplicationEcsEffect>(proposed.Count);
        foreach (var effect in proposed)
        {
            if (effect is null) return TranslationResult.Failed("EFFECT_REQUIRED", "The mechanic proposed an empty effect.");
            switch (effect.Type)
            {
                case EffectType.EntityCreate:
                    result.Add(new() { Type = ApplicationEcsEffectType.EntityCreate, EntityId = effect.EntityId, Name = effect.Name });
                    break;
                case EffectType.EntityDelete:
                {
                    var entity = await entities.GetEntityAsync(stateSpace.StateSpaceId, effect.EntityId, cancellationToken);
                    if (entity is null) return TranslationResult.StaleFailure("ENTITY_STALE", "An affected entity is unavailable.");
                    result.Add(new() { Type = ApplicationEcsEffectType.EntityDelete, EntityId = effect.EntityId, ExpectedRevision = entity.Revision });
                    break;
                }
                case EffectType.ComponentAdd:
                case EffectType.ComponentSet:
                case EffectType.ComponentMerge:
                case EffectType.ComponentRemove:
                {
                    var type = mapping.Components.TryGetValue(effect.DefinitionId, out var declared)
                        ? declared : ResolveComponent(owners, effect.DefinitionId);
                    if (type is null) return TranslationResult.Failed("COMPONENT_MAPPING_MISSING", "An affected component has no exact mapping.");
                    var current = await entities.GetComponentAsync(stateSpace.StateSpaceId, effect.EntityId, type.QualifiedTypeId, cancellationToken);
                    var add = effect.Type == EffectType.ComponentAdd;
                    if ((!add && current is null) || (current is not null && current.Type != type))
                        return TranslationResult.StaleFailure("COMPONENT_STALE", "An affected component changed or is unavailable.");
                    result.Add(new()
                    {
                        Type = effect.Type,
                        EntityId = effect.EntityId,
                        ComponentType = type,
                        DataJson = effect.Type == EffectType.ComponentRemove ? "" : effect.Data,
                        ExpectedRevision = add ? 0 : current!.Revision
                    });
                    break;
                }
                case EffectType.ContainmentMove:
                {
                    var current = await edges.GetContainmentAsync(stateSpace.StateSpaceId, effect.EntityId, cancellationToken);
                    if (string.IsNullOrWhiteSpace(effect.ToEntityId))
                    {
                        if (current is null) return TranslationResult.StaleFailure("CONTAINMENT_STALE", "The affected containment is unavailable.");
                        result.Add(new() { Type = ApplicationEcsEffectType.ContainmentRemove, EntityId = effect.EntityId, ExpectedRevision = current.Revision });
                    }
                    else result.Add(new()
                    {
                        Type = ApplicationEcsEffectType.ContainmentMove,
                        EntityId = effect.EntityId,
                        TargetEntityId = effect.ToEntityId,
                        Slot = effect.Slot,
                        ExpectedRevision = current?.Revision ?? 0
                    });
                    break;
                }
                case EffectType.RelationshipCreate:
                case EffectType.RelationshipRemove:
                {
                    var qualified = QualifiedRelationship(owners, mapping, effect.Kind);
                    if (qualified is null) return TranslationResult.Failed("RELATIONSHIP_MAPPING_MISSING", "An affected relationship has no exact mapping.");
                    var current = await edges.GetRelationshipAsync(stateSpace.StateSpaceId, effect.EntityId,
                        effect.ToEntityId, qualified, cancellationToken);
                    var remove = effect.Type == EffectType.RelationshipRemove;
                    if (remove && current is null) return TranslationResult.StaleFailure("RELATIONSHIP_STALE", "The affected relationship is unavailable.");
                    result.Add(new()
                    {
                        Type = remove ? ApplicationEcsEffectType.RelationshipRemove : ApplicationEcsEffectType.RelationshipSet,
                        EntityId = effect.EntityId,
                        TargetEntityId = effect.ToEntityId,
                        QualifiedRelationshipKind = qualified,
                        DataJson = remove ? "" : effect.Data,
                        ExpectedRevision = current?.Revision ?? 0
                    });
                    break;
                }
                default:
                    return TranslationResult.Failed("EFFECT_TYPE_UNSUPPORTED", "The mechanic proposed an unsupported effect type.");
            }
        }
        return new(result.AsReadOnly(), [], false);
    }

    private static string? QualifiedRelationship(
        IReadOnlyList<ApplicationIdentifier> owners,
        ApplicationMechanicProjectionMapping mapping,
        string localOrQualified)
    {
        if (mapping.Relationships.TryGetValue(localOrQualified, out var mapped)) return mapped;
        if (owners.Any(owner => localOrQualified.StartsWith(owner.Value + ".", StringComparison.Ordinal)))
            return localOrQualified;
        return string.IsNullOrWhiteSpace(localOrQualified)
            ? null
            : owners[0].Value + "." + localOrQualified;
    }

    private static ApplicationActionExecutionResult Failed(
        ApplicationActionExecutionRequest request,
        ApplicationActionExecutionDisposition disposition,
        string code,
        string message) => Result(request, disposition, request.ExecutionIdentity.OperationId,
            "", 0, [new(code, message)]);

    private static ApplicationActionExecutionResult Result(
        ApplicationActionExecutionRequest request,
        ApplicationActionExecutionDisposition disposition,
        string operationId,
        string narration,
        int applied,
        IReadOnlyList<ApplicationActionExecutionProblem> problems) => new(
            disposition, operationId, request.QualifiedMechanicId, request.ContentFingerprint,
            request.Seed, narration, applied, problems);

    private static string SafeProblem(IReadOnlyList<string> problems) => problems.Count == 0
        ? "The exact application mechanic could not be evaluated."
        : problems[0].Split(':', 2)[0] + ": The exact application mechanic could not be evaluated.";
    private static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200;
    private static bool UpperSha256(string value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');

    private sealed record MappingResult(
        ApplicationMechanicProjectionMapping? Mapping,
        IReadOnlyList<ApplicationActionExecutionProblem> Problems)
    {
        public static MappingResult Failed(string code, string message) => new(null, [new(code, message)]);
    }

    private sealed record TranslationResult(
        IReadOnlyList<ApplicationEcsEffect> Effects,
        IReadOnlyList<ApplicationActionExecutionProblem> Problems,
        bool Stale)
    {
        public static TranslationResult Failed(string code, string message) => new([], [new(code, message)], false);
        public static TranslationResult StaleFailure(string code, string message) => new([], [new(code, message)], true);
    }
}
