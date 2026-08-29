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
    private static readonly JsonSerializerOptions AuditProjectionJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
        // The evaluator owns projection and child-declaration validation so action execution and
        // read-only evaluation share one interpretation of composed mechanics. Event middleware
        // remains a distinct execution surface.
        if (requirements.Event is not null)
            return Failed(request, ApplicationActionExecutionDisposition.Unsupported,
                "MECHANIC_EXECUTION_UNSUPPORTED", "This mechanic requires an execution feature not enabled by this action owner.");

        var mapping = await BuildMappingAsync(stateSpace, catalog, request.ApplicationId,
            request.QualifiedMechanicId, requirements, cancellationToken);
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
            stateSpace, mapping.Mapping!, evaluation.Projection!, evaluation.Run.Output.Effects, cancellationToken);
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
            ExecutionIdentity = request.ExecutionIdentity,
            ContainmentExpectations = evaluation.Projection!.ContainmentRevisions
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ApplicationEcsContainmentExpectation(pair.Key,
                    pair.Value.Select(value => new EcsContainmentExpectationItem(value.EntityId, value.Slot, value.Revision)).ToArray()))
                .ToArray(),
            MechanicId = record.Summary.QualifiedId,
            MechanicVersion = record.Summary.Version,
            Seed = request.Seed,
            ProjectionJson = JsonSerializer.Serialize(evaluation.Projection, AuditProjectionJson)
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
        ICatalogNavigator catalog,
        ApplicationIdentifier applicationId,
        string mechanicId,
        MechanicRequirements requirements,
        CancellationToken cancellationToken)
    {
        var owners = stateSpace.ApplicationRevision.BaseApplications
            .Prepend(stateSpace.ApplicationRevision.ApplicationId).ToArray();
        var localIds = new HashSet<string>(StringComparer.Ordinal);
        var localRelationshipKinds = new HashSet<string>(StringComparer.Ordinal);
        var dependencyVisits = 0;
        var dependencyProblem = await CollectComponentIdsAsync(
            requirements, mechanicId, depth: 0, new HashSet<string>(StringComparer.Ordinal));
        if (dependencyProblem is not null)
            return MappingResult.Failed("CHILD_DEPENDENCY_INVALID", dependencyProblem);
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
        foreach (var local in localRelationshipKinds)
            relationships.TryAdd(local, applicationId.Value + "." + local);
        return new(new(components, relationships), []);

        async Task<string?> CollectComponentIdsAsync(
            MechanicRequirements declared,
            string currentMechanicId,
            int depth,
            HashSet<string> lineage)
        {
            foreach (var localId in declared.Roles.Values.SelectMany(value =>
                         value.Components.Concat(value.ContentComponentIds ?? [])
                             .Concat((value.ComponentReferences ?? []).SelectMany(reference =>
                                 new[] { reference.SourceComponentId }.Concat(reference.TargetComponentIds)))
                             .Concat((value.RelationshipComponents ?? []).SelectMany(reference =>
                                 reference.TargetComponentIds))))
                localIds.Add(localId);
            foreach (var kind in declared.Roles.Values
                         .SelectMany(value => value.RelationshipComponents ?? [])
                         .Select(value => value.Kind))
                localRelationshipKinds.Add(kind);

            if (declared.Children.Count == 0) return null;
            if (declared.Children.Count > 64) return "The declared child-mechanic count exceeds the supported limit.";
            if (depth >= 8) return "The declared child-mechanic depth exceeds the supported limit.";
            if (!lineage.Add(currentMechanicId))
                return "The declared child-mechanic graph contains a cycle.";

            foreach (var child in declared.Children.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (++dependencyVisits > 256) return "The declared child-mechanic graph exceeds the supported traversal limit.";
                var childMechanicId = QualifyMechanicId(applicationId, child.Value.MechanicId);
                if (lineage.Contains(childMechanicId))
                    return "The declared child-mechanic graph contains a cycle.";
                CatalogRecordView childRecord;
                try { childRecord = catalog.Inspect(new(applicationId, applicationId.Value, childMechanicId)); }
                catch (Exception) { return $"Declared child '{child.Key}' is unavailable."; }
                if (childRecord.Summary.Kind != "mechanic" || childRecord.Summary.Status != "active")
                    return $"Declared child '{child.Key}' is inactive.";
                try
                {
                    using var document = JsonDocument.Parse(childRecord.ContentJson);
                    if (!document.RootElement.TryGetProperty("requirements", out var value)
                        || value.ValueKind != JsonValueKind.String)
                        return $"Declared child '{child.Key}' has invalid requirements.";
                    var childRequirements = MechanicRequirements.Parse(value.GetString()!);
                    if (childRequirements.ProjectionProblems().Count > 0 || childRequirements.CompositionProblems().Count > 0)
                        return $"Declared child '{child.Key}' has invalid requirements.";
                    var nested = await CollectComponentIdsAsync(childRequirements, childRecord.Summary.QualifiedId, depth + 1,
                        new HashSet<string>(lineage, StringComparer.Ordinal));
                    if (nested is not null) return nested;
                }
                catch (JsonException) { return $"Declared child '{child.Key}' has invalid requirements."; }
            }
            return null;
        }
    }

    private EcsComponentReference? ResolveComponent(
        IReadOnlyList<ApplicationIdentifier> owners,
        string localOrQualifiedId)
    {
        if (string.IsNullOrWhiteSpace(localOrQualifiedId)) return null;
        var explicitOwner = owners.FirstOrDefault(owner =>
            localOrQualifiedId.StartsWith(owner.Value + ".", StringComparison.Ordinal));
        if (explicitOwner is not null)
        {
            var explicitValue = componentTypes.GetLatest(localOrQualifiedId);
            return explicitValue is not null && explicitValue.Owner == explicitOwner
                ? new(explicitValue.QualifiedId, explicitValue.Version, explicitValue.SchemaHash)
                : null;
        }
        foreach (var owner in owners)
        {
            var qualified = owner.Value + "." + localOrQualifiedId;
            var value = componentTypes.GetLatest(qualified);
            if (value is not null) return new(value.QualifiedId, value.Version, value.SchemaHash);
        }
        return null;
    }

    private static string QualifyMechanicId(ApplicationIdentifier applicationId, string mechanicId) =>
        mechanicId.StartsWith(applicationId.Value + ".", StringComparison.Ordinal)
            ? mechanicId : applicationId.Value + "." + mechanicId;

    private async Task<TranslationResult> TranslateAsync(
        StateSpaceView stateSpace,
        ApplicationMechanicProjectionMapping mapping,
        MechanicProjection projection,
        IReadOnlyList<Effect> proposed,
        CancellationToken cancellationToken)
    {
        if (proposed.Count > ApplicationEcsEffectValidation.MaximumEffects)
            return TranslationResult.Failed("EFFECT_LIMIT", "The mechanic proposed too many effects.");
        var owners = stateSpace.ApplicationRevision.BaseApplications
            .Prepend(stateSpace.ApplicationRevision.ApplicationId).ToArray();
        var result = new List<ApplicationEcsEffect>(proposed.Count);
        var createdEntityIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in proposed)
        {
            if (effect is null) return TranslationResult.Failed("EFFECT_REQUIRED", "The mechanic proposed an empty effect.");
            switch (effect.Type)
            {
                case EffectType.EntityCreate:
                    result.Add(new() { Type = ApplicationEcsEffectType.EntityCreate, EntityId = effect.EntityId, Name = effect.Name });
                    createdEntityIds.Add(effect.EntityId);
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
                    var localId = mapping.Components.ContainsKey(effect.DefinitionId)
                        ? effect.DefinitionId
                        : mapping.Components.FirstOrDefault(pair => pair.Value == type).Key;
                    if (string.IsNullOrWhiteSpace(localId))
                        return TranslationResult.Failed("COMPONENT_SNAPSHOT_MISSING",
                            "An affected component was not declared in the evaluated projection.");
                    var add = effect.Type == EffectType.ComponentAdd;
                    int? observedRevision = null;
                    var hasSnapshot = projection.ComponentRevisions.TryGetValue(effect.EntityId, out var revisions)
                        && revisions.TryGetValue(localId, out observedRevision);
                    if (!hasSnapshot && (!add || !createdEntityIds.Contains(effect.EntityId)))
                        return TranslationResult.Failed("COMPONENT_SNAPSHOT_MISSING",
                            "An affected component was not declared in the evaluated projection.");
                    if ((add && observedRevision is not null) || (!add && observedRevision is null))
                        return TranslationResult.StaleFailure("COMPONENT_STALE", "An affected component changed or is unavailable.");
                    result.Add(new()
                    {
                        Type = effect.Type,
                        EntityId = effect.EntityId,
                        ComponentType = type,
                        DataJson = effect.Type == EffectType.ComponentRemove ? "" : effect.Data,
                        ExpectedRevision = observedRevision ?? 0
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
