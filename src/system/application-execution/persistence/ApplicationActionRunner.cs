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
    IApplicationMechanicProjectionMappingResolver mappings,
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

        if (!ValidId(request.StateSpaceId) || !ValidId(request.QualifiedMechanicId)
            || request.MechanicVersion < 1 || !UpperSha256(request.ContentFingerprint)
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
            || record.Summary.Version != request.MechanicVersion
            || record.Summary.ContentFingerprint != request.ContentFingerprint)
            return Failed(request, ApplicationActionExecutionDisposition.Stale,
                "MECHANIC_STALE", "The exact application mechanic changed or is inactive.");

        var replay = await ReplayAsync(request, cancellationToken);
        if (replay is not null) return replay;

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

        var mapping = await mappings.ResolveAsync(request.StateSpaceId, request.ApplicationId,
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
            request.Seed,
            new MechanicExecutionContext(
                request.ExecutionIdentity.OperationId,
                request.ExecutionIdentity.OperationId,
                null,
                0)), cancellationToken);
        if (!evaluation.Evaluated || evaluation.Run is null)
            return Failed(request, ApplicationActionExecutionDisposition.Failed,
                "APPLICATION_ACTION_PROJECTION_FAILED", SafeProblem(evaluation.Problems));
        if (!evaluation.Run.Ok)
            return Failed(request, ApplicationActionExecutionDisposition.Failed,
                string.IsNullOrWhiteSpace(evaluation.Run.LimitHit) ? "MECHANIC_FAILED" : "MECHANIC_LIMIT",
                SafeMechanicError(evaluation.Run.Error, evaluation.Run.LimitHit));
        var proposal = evaluation.Proposal.Append(evaluation.Run.Output);
        if (proposal.Notifications.Count > 0)
            return Failed(request, ApplicationActionExecutionDisposition.Unsupported,
                "MECHANIC_OUTPUT_UNSUPPORTED", "Application notification output is not enabled for direct execution.");

        var translated = await TranslateAsync(
            stateSpace, mapping.Mapping!, evaluation.Projection!, proposal.Effects, cancellationToken);
        if (translated.Problems.Count > 0)
            return Failed(request, translated.Stale
                    ? ApplicationActionExecutionDisposition.Stale
                    : ApplicationActionExecutionDisposition.Unsupported,
                translated.Problems[0].Code, translated.Problems[0].SafeMessage);
        var clockAdvanceCount = translated.Effects.Count(effect =>
            effect.Type == ApplicationEcsEffectType.ClockAdvance);
        var elapsedMode = requirements.ElapsedTime?.Mode?.Trim();
        if (clockAdvanceCount > 1)
            return Failed(request, ApplicationActionExecutionDisposition.Unsupported,
                "CLOCK_ADVANCE_MULTIPLE", "One action may advance the authoritative clock only once.");
        if (clockAdvanceCount == 1 && elapsedMode is not ("fixed" or "derived" or "supplied"))
            return Failed(request, ApplicationActionExecutionDisposition.Unsupported,
                "ELAPSED_TIME_CONTRACT_MISSING",
                "A time-coupled action must declare how its elapsed time is obtained.");
        if (clockAdvanceCount == 0 && elapsedMode is "fixed" or "derived" or "supplied")
            return Failed(request, ApplicationActionExecutionDisposition.Unsupported,
                "CLOCK_ADVANCE_MISSING",
                "A non-zero elapsed-time declaration must produce one authoritative clock advance.");

        var applied = await effects.ApplyAsync(new ApplicationEcsEffectBatch
        {
            StateSpaceId = request.StateSpaceId,
            Effects = translated.Effects,
            Intent = "Execute one verified application interaction step.",
            ProceduresUsed = [],
            ExecutionIdentity = request.ExecutionIdentity,
            ComponentExpectations = evaluation.Projection!.ObservedComponents
                .Select(value => new ApplicationEcsComponentExpectation(value.EntityId,
                    new EcsComponentReference(value.QualifiedTypeId, value.TypeVersion, value.SchemaHash),
                    value.Revision))
                .ToArray(),
            ContainmentExpectations = evaluation.Projection!.ContainmentRevisions
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ApplicationEcsContainmentExpectation(pair.Key,
                    pair.Value.Select(value => new EcsContainmentExpectationItem(value.EntityId, value.Slot, value.Revision)).ToArray()))
                .ToArray(),
            DeclaredEvents = proposal.Events,
            MechanicId = record.Summary.QualifiedId,
            MechanicVersion = record.Summary.Version,
            Seed = request.Seed,
            ProjectionJson = JsonSerializer.Serialize(evaluation.Projection, AuditProjectionJson)
        }, cancellationToken: cancellationToken);
        if (applied.Replayed)
            return Result(request, ApplicationActionExecutionDisposition.Replayed, applied.OperationId,
                "The exact application action was already committed.", 0, [], applied.Receipts);
        if (!applied.Applied || applied.Problems.Count > 0)
        {
            var problem = applied.Problems.FirstOrDefault();
            var stale = problem?.Code.Contains("STALE", StringComparison.Ordinal) == true
                || problem?.Code is "REFERENCE_UNKNOWN";
            return Result(request, stale ? ApplicationActionExecutionDisposition.Stale
                    : ApplicationActionExecutionDisposition.Failed,
                applied.OperationId, "", 0,
                [new(problem?.Code ?? "APPLICATION_EFFECTS_REJECTED",
                    "The application effect transaction was rejected.")], applied.Receipts);
        }
        return Result(request, ApplicationActionExecutionDisposition.Succeeded, applied.OperationId,
            evaluation.Run.Output.Narration, applied.Receipts.Count, [], applied.Receipts);
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

    private EcsComponentReference? ResolveComponent(
        IReadOnlyList<ApplicationIdentifier> owners,
        string localOrQualifiedId)
    {
        if (string.IsNullOrWhiteSpace(localOrQualifiedId) || owners.Count == 0) return null;
        var application = owners[0];
        if (localOrQualifiedId.StartsWith(application.Value + ".", StringComparison.Ordinal))
        {
            var installedId = localOrQualifiedId[(application.Value.Length + 1)..];
            var installedBase = owners.Skip(1).FirstOrDefault(owner =>
                installedId.StartsWith(owner.Value + ".", StringComparison.Ordinal));
            if (installedBase is not null)
            {
                var installedValue = componentTypes.GetLatest(installedId);
                return installedValue is not null && installedValue.Owner == installedBase
                    ? new(installedValue.QualifiedId, installedValue.Version, installedValue.SchemaHash)
                    : null;
            }
        }
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
                case EffectType.ClockAdvance:
                {
                    var type = mapping.Components.TryGetValue(effect.DefinitionId, out var declared)
                        ? declared : ResolveComponent(owners, effect.DefinitionId);
                    if (type is null)
                        return TranslationResult.Failed("COMPONENT_MAPPING_MISSING",
                            "The authoritative clock has no exact component mapping.");
                    var localId = mapping.Components.ContainsKey(effect.DefinitionId)
                        ? effect.DefinitionId
                        : mapping.Components.FirstOrDefault(pair => pair.Value == type).Key;
                    if (string.IsNullOrWhiteSpace(localId)
                        || !projection.ComponentRevisions.TryGetValue(effect.EntityId, out var revisions)
                        || !revisions.TryGetValue(localId, out var observedRevision)
                        || observedRevision is null)
                        return TranslationResult.Failed("COMPONENT_SNAPSHOT_MISSING",
                            "The authoritative clock was not declared in the evaluated projection.");
                    result.Add(new()
                    {
                        Type = ApplicationEcsEffectType.ClockAdvance,
                        EntityId = effect.EntityId,
                        ComponentType = type,
                        DataJson = effect.Data,
                        ExpectedRevision = observedRevision.Value,
                        CalendarId = effect.CalendarId,
                        PreviousMinute = effect.PreviousMinute,
                        DeltaMinutes = effect.DeltaMinutes,
                        ResultingMinute = effect.ResultingMinute,
                        PreviousClockRevision = effect.PreviousClockRevision,
                        ResultingClockRevision = effect.ResultingClockRevision,
                        EventTypeId = effect.EventTypeId,
                        SubjectEntityId = effect.SubjectEntityId,
                        ActivityId = effect.ActivityId
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
                    var snapshot = projection.RelationshipCollections.SingleOrDefault(value =>
                        value.QualifiedKind == qualified &&
                        ((!value.Incoming && value.AnchorEntityId == effect.EntityId) ||
                         (value.Incoming && value.AnchorEntityId == effect.ToEntityId)));
                    var observed = snapshot?.Relationships.SingleOrDefault(value =>
                        value.FromEntityId == effect.EntityId && value.ToEntityId == effect.ToEntityId &&
                        value.QualifiedKind == qualified);
                    var current = snapshot is null
                        ? await edges.GetRelationshipAsync(stateSpace.StateSpaceId, effect.EntityId,
                            effect.ToEntityId, qualified, cancellationToken)
                        : null;
                    var remove = effect.Type == EffectType.RelationshipRemove;
                    if (remove && observed is null && current is null)
                        return TranslationResult.StaleFailure("RELATIONSHIP_STALE", "The affected relationship is unavailable.");
                    result.Add(new()
                    {
                        Type = remove ? ApplicationEcsEffectType.RelationshipRemove : ApplicationEcsEffectType.RelationshipSet,
                        EntityId = effect.EntityId,
                        TargetEntityId = effect.ToEntityId,
                        QualifiedRelationshipKind = qualified,
                        DataJson = remove ? "" : effect.Data,
                        ExpectedRevision = observed?.Revision ?? current?.Revision ?? 0
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
        IReadOnlyList<ApplicationActionExecutionProblem> problems,
        IReadOnlyList<ApplicationEcsEffectReceipt>? receipts = null) => new(
            disposition, operationId, request.QualifiedMechanicId, request.ContentFingerprint,
            request.Seed, narration, applied, problems)
        {
            MechanicVersion = request.MechanicVersion,
            EffectReceipts = receipts ?? [],
            AffectedEntityIds = (receipts ?? [])
                .SelectMany(value => string.IsNullOrWhiteSpace(value.TargetEntityId)
                    ? new[] { value.EntityId }
                    : new[] { value.EntityId, value.TargetEntityId })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
        };

    /// <summary>
    /// The message a refusing mechanic wrote for its caller. A mechanic's own `throw` is authored
    /// text about the caller's request -- "The destination is outside the campaign World." -- and
    /// withholding it leaves the caller with nothing to act on. It is bounded and passed through as
    /// written; it is never combined with host state.
    /// </summary>
    private static string SafeMechanicError(string? error, string? limitHit)
    {
        if (!string.IsNullOrWhiteSpace(limitHit))
            return $"The exact mechanic hit the '{limitHit}' execution limit.";
        var text = (error ?? string.Empty).Trim();
        if (text.Length == 0) return "The exact mechanic could not produce an accepted result.";
        if (text.Length > 300) text = text[..300];
        return $"The exact mechanic refused the request: {text}";
    }

    private static string SafeProblem(IReadOnlyList<string> problems) => problems.Count == 0
        ? "The exact application mechanic could not be evaluated."
        : problems[0].Split(':', 2)[0] + ": The exact application mechanic could not be evaluated.";
    private static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200;
    private static bool UpperSha256(string value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');

    private sealed record TranslationResult(
        IReadOnlyList<ApplicationEcsEffect> Effects,
        IReadOnlyList<ApplicationActionExecutionProblem> Problems,
        bool Stale)
    {
        public static TranslationResult Failed(string code, string message) => new([], [new(code, message)], false);
        public static TranslationResult StaleFailure(string code, string message) => new([], [new(code, message)], true);
    }
}
