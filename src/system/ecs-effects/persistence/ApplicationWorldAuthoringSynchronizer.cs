using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;

namespace DantesRoleplay.EcsEffects;

public sealed class ApplicationWorldAuthoringSynchronizer(
    IStateSpaceRegistry stateSpaces,
    IApplicationComponentTypeRegistry componentTypes,
    IEntityComponentStore entities,
    IStateSpaceEdgeStore edges,
    IApplicationEcsEffectApplier effects,
    IOperationLog operations) : IApplicationWorldAuthoringSynchronizer
{
    private const int MaximumEntities = 64;
    private const int MaximumComponentsPerEntity = 32;
    private const int MaximumRelationships = 64;
    private const int MaximumJsonLength = 1_000_000;

    public async Task<ApplicationWorldAuthoringResult> SynchronizeAsync(
        ApplicationWorldAuthoringRequest request,
        ApplicationWorldAuthoringContext context,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var shapeProblem = ValidateShape(request, context);
        if (shapeProblem is not null) return Rejected(dryRun, shapeProblem.Code, shapeProblem.Message);

        ApplicationIdentifier applicationId;
        try { applicationId = ApplicationIdentifier.Parse(request.ApplicationId); }
        catch (ArgumentException exception) { return Rejected(dryRun, "APPLICATION_INVALID", exception.Message); }

        var stateSpace = stateSpaces.Get(request.StateSpaceId);
        if (stateSpace is null) return Rejected(dryRun, "STATE_SPACE_UNKNOWN", "The state space is unknown.");
        if (stateSpace.ApplicationRevision.ApplicationId != applicationId)
            return Rejected(dryRun, "STATE_SPACE_APPLICATION_MISMATCH",
                "The state space is not bound to the requested application.");

        var manifestEntities = request.Entities.OrderBy(value => value.EntityId, StringComparer.Ordinal).ToArray();
        var canonical = new
        {
            Version = "application-world-authoring-v1",
            request.RequestToken,
            request.ApplicationId,
            request.StateSpaceId,
            request.RootEntityId,
            ApplicationRevision = stateSpace.ApplicationRevision.Revision,
            stateSpace.ApplicationRevision.Fingerprint,
            stateSpace.ManifestFingerprint,
            stateSpace.BindingRevision,
            DryRun = dryRun,
            Entities = manifestEntities.Select(value => new
            {
                value.EntityId,
                value.Name,
                value.ExpectedRevision,
                Components = value.Components.OrderBy(component => component.QualifiedTypeId, StringComparer.Ordinal),
                value.Containment
            }),
            Relationships = request.Relationships
                .OrderBy(value => value.FromEntityId, StringComparer.Ordinal)
                .ThenBy(value => value.QualifiedKind, StringComparer.Ordinal)
                .ThenBy(value => value.ToEntityId, StringComparer.Ordinal)
        };
        var requestFingerprint = Fingerprint(JsonSerializer.SerializeToUtf8Bytes(canonical));
        var operationId = Fingerprint(Encoding.UTF8.GetBytes(
            $"application-world-authoring-v1:{request.RequestToken}:{(dryRun ? "dry-run" : "commit")}"))
            [..32].ToLowerInvariant();
        var existingOperation = await operations.GetAsync(operationId, cancellationToken);
        if (existingOperation is not null)
        {
            if (existingOperation.Tool != ApplicationEcsExecutionIdentity.AuditTool
                || existingOperation.Subject != "interaction-step:" + requestFingerprint)
                return Rejected(dryRun, "OPERATION_ID_CONFLICT",
                    "The request token is already bound to another world-authoring manifest.");
            if (!existingOperation.Success)
                return Rejected(dryRun,
                    string.IsNullOrWhiteSpace(existingOperation.Error) ? "REPLAYED_FAILURE" : existingOperation.Error,
                    "The same world-authoring manifest previously failed.");
            var replayEffectCount = manifestEntities.Count(value => value.ExpectedRevision == 0)
                + manifestEntities.Sum(value => value.Components.Count)
                + manifestEntities.Count(value => value.Containment is not null)
                + request.Relationships.Count;
            return new(true, dryRun, true, manifestEntities.Length, replayEffectCount,
                operationId, Receipts: []);
        }

        try
        {
            var root = await entities.GetEntityAsync(request.StateSpaceId, request.RootEntityId, cancellationToken);
            if (root is null) return Rejected(dryRun, "ROOT_UNKNOWN", "The selected world root is unknown.");

            var currentContainments = await edges.ListContainmentsAsync(request.StateSpaceId, cancellationToken);
            var containmentByChild = new Dictionary<string, EcsContainmentView>(StringComparer.Ordinal);
            foreach (var containment in currentContainments)
            {
                if (!containmentByChild.TryAdd(containment.ContainedEntityId, containment))
                    return Rejected(dryRun, "WORLD_SCOPE_INVALID", "The current containment graph is ambiguous.");
            }

            var manifestById = manifestEntities.ToDictionary(value => value.EntityId, StringComparer.Ordinal);
            var currentEntities = new Dictionary<string, EcsEntityView?>(StringComparer.Ordinal);
            foreach (var entry in manifestEntities)
            {
                var current = await entities.GetEntityAsync(request.StateSpaceId, entry.EntityId, cancellationToken);
                currentEntities.Add(entry.EntityId, current);
                if (entry.ExpectedRevision == 0)
                {
                    if (current is not null)
                        return Rejected(dryRun, "ENTITY_ALREADY_EXISTS", $"Entity '{entry.EntityId}' already exists.");
                    if (entry.Containment is null)
                        return Rejected(dryRun, "WORLD_SCOPE_INVALID",
                            $"New entity '{entry.EntityId}' requires containment beneath the selected root.");
                }
                else
                {
                    if (current is null || current.Revision != entry.ExpectedRevision)
                        return Rejected(dryRun, "ENTITY_REVISION_STALE",
                            $"Entity '{entry.EntityId}' does not match the expected revision.");
                    if (!string.Equals(current.Name, entry.Name, StringComparison.Ordinal))
                        return Rejected(dryRun, "ENTITY_RENAME_UNSUPPORTED",
                            $"Entity '{entry.EntityId}' cannot be renamed through world-state synchronization.");
                    if (!InsideRoot(entry.EntityId, request.RootEntityId, containmentByChild))
                        return Rejected(dryRun, "WORLD_SCOPE_INVALID",
                            $"Entity '{entry.EntityId}' is outside the selected root.");
                }
            }

            foreach (var entry in manifestEntities)
            {
                if (entry.Containment is null) continue;
                var target = entry.Containment.ContainerEntityId;
                if (target == entry.EntityId)
                    return Rejected(dryRun, "WORLD_SCOPE_INVALID", "An entity cannot contain itself.");
                if (!manifestById.ContainsKey(target)
                    && target != request.RootEntityId
                    && !InsideRoot(target, request.RootEntityId, containmentByChild))
                    return Rejected(dryRun, "WORLD_SCOPE_INVALID",
                        $"Containment target '{target}' is outside the selected root.");

                containmentByChild.TryGetValue(entry.EntityId, out var current);
                if (entry.Containment.ExpectedRevision == 0)
                {
                    if (current is not null)
                        return Rejected(dryRun, "EDGE_REVISION_STALE",
                            $"Entity '{entry.EntityId}' already has containment.");
                }
                else if (current is null || current.Revision != entry.Containment.ExpectedRevision)
                    return Rejected(dryRun, "EDGE_REVISION_STALE",
                        $"Containment for '{entry.EntityId}' does not match the expected revision.");
            }

            foreach (var entry in manifestEntities.Where(value => value.ExpectedRevision == 0))
                if (!NewEntityReachesRoot(entry.EntityId, request.RootEntityId, manifestById, containmentByChild))
                    return Rejected(dryRun, "WORLD_SCOPE_INVALID",
                        $"New entity '{entry.EntityId}' does not terminate beneath the selected root.");

            var allowedOwners = stateSpace.ApplicationRevision.BaseApplications
                .Append(stateSpace.ApplicationRevision.ApplicationId)
                .ToHashSet();
            var typedEffects = new List<ApplicationEcsEffect>();
            foreach (var entry in manifestEntities.Where(value => value.ExpectedRevision == 0))
                typedEffects.Add(new()
                {
                    Type = ApplicationEcsEffectType.EntityCreate,
                    EntityId = entry.EntityId,
                    Name = entry.Name
                });

            foreach (var entry in manifestEntities)
            {
                foreach (var component in entry.Components.OrderBy(value => value.QualifiedTypeId, StringComparer.Ordinal))
                {
                    var registered = componentTypes.GetLatest(component.QualifiedTypeId);
                    if (registered is null || !allowedOwners.Contains(registered.Owner))
                        return Rejected(dryRun, "COMPONENT_TYPE_UNKNOWN",
                            $"Component type '{component.QualifiedTypeId}' is not available to this state space.");
                    var current = await entities.GetComponentAsync(
                        request.StateSpaceId, entry.EntityId, component.QualifiedTypeId, cancellationToken);
                    if (component.ExpectedRevision == 0)
                    {
                        if (current is not null)
                            return Rejected(dryRun, "COMPONENT_REVISION_STALE",
                                $"Component '{component.QualifiedTypeId}' already exists on '{entry.EntityId}'.");
                    }
                    else if (current is null || current.Revision != component.ExpectedRevision)
                        return Rejected(dryRun, "COMPONENT_REVISION_STALE",
                            $"Component '{component.QualifiedTypeId}' on '{entry.EntityId}' is stale.");

                    typedEffects.Add(new()
                    {
                        Type = component.ExpectedRevision == 0
                            ? ApplicationEcsEffectType.ComponentAdd
                            : ApplicationEcsEffectType.ComponentSet,
                        EntityId = entry.EntityId,
                        ComponentType = new(
                            registered.QualifiedId, registered.Version, registered.SchemaHash),
                        DataJson = component.ValueJson,
                        ExpectedRevision = component.ExpectedRevision
                    });
                }
            }

            foreach (var entry in manifestEntities.Where(value => value.Containment is not null))
                typedEffects.Add(new()
                {
                    Type = ApplicationEcsEffectType.ContainmentMove,
                    EntityId = entry.EntityId,
                    TargetEntityId = entry.Containment!.ContainerEntityId,
                    Slot = entry.Containment.Slot,
                    ExpectedRevision = entry.Containment.ExpectedRevision
                });

            var relationshipKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var relationship in request.Relationships
                         .OrderBy(value => value.FromEntityId, StringComparer.Ordinal)
                         .ThenBy(value => value.QualifiedKind, StringComparer.Ordinal)
                         .ThenBy(value => value.ToEntityId, StringComparer.Ordinal))
            {
                var key = relationship.FromEntityId + "\n" + relationship.QualifiedKind + "\n" + relationship.ToEntityId;
                if (!relationshipKeys.Add(key))
                    return Rejected(dryRun, "DUPLICATE_RELATIONSHIP", "The relationship manifest contains a duplicate edge.");
                if (relationship.FromEntityId == relationship.ToEntityId)
                    return Rejected(dryRun, "WORLD_SCOPE_INVALID", "A world-authoring relationship cannot be a self link.");
                if (!EndpointInsideRoot(relationship.FromEntityId, request.RootEntityId, manifestById, containmentByChild)
                    || !EndpointInsideRoot(relationship.ToEntityId, request.RootEntityId, manifestById, containmentByChild))
                    return Rejected(dryRun, "WORLD_SCOPE_INVALID", "A relationship endpoint is outside the selected root.");
                if (!OwnedQualifiedId(relationship.QualifiedKind, allowedOwners))
                    return Rejected(dryRun, "RELATIONSHIP_KIND_UNKNOWN",
                        $"Relationship kind '{relationship.QualifiedKind}' is not owned by this application profile.");
                var current = await edges.GetRelationshipAsync(
                    request.StateSpaceId, relationship.FromEntityId, relationship.ToEntityId,
                    relationship.QualifiedKind, cancellationToken);
                if (relationship.ExpectedRevision == 0)
                {
                    if (current is not null)
                        return Rejected(dryRun, "EDGE_REVISION_STALE", "The relationship already exists.");
                }
                else if (current is null || current.Revision != relationship.ExpectedRevision)
                    return Rejected(dryRun, "EDGE_REVISION_STALE", "The relationship revision is stale.");

                typedEffects.Add(new()
                {
                    Type = ApplicationEcsEffectType.RelationshipSet,
                    EntityId = relationship.FromEntityId,
                    TargetEntityId = relationship.ToEntityId,
                    QualifiedRelationshipKind = relationship.QualifiedKind,
                    DataJson = relationship.ValueJson,
                    ExpectedRevision = relationship.ExpectedRevision
                });
            }

            if (typedEffects.Count == 0)
                return Rejected(dryRun, "EMPTY_WORLD_MANIFEST", "The manifest derives no state changes.");
            if (typedEffects.Count > ApplicationEcsEffectValidation.MaximumEffects)
                return Rejected(dryRun, "EFFECT_LIMIT",
                    $"The manifest derives more than {ApplicationEcsEffectValidation.MaximumEffects} effects.");

            var relevantExisting = manifestEntities.Where(value => value.ExpectedRevision > 0)
                .Select(value => value.EntityId)
                .Concat(request.Relationships.SelectMany(value => new[] { value.FromEntityId, value.ToEntityId }))
                .Concat(manifestEntities.Where(value => value.Containment is not null)
                    .Select(value => value.Containment!.ContainerEntityId))
                .Where(value => !manifestById.TryGetValue(value, out var authored) || authored.ExpectedRevision > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var expectations = BuildContainmentExpectations(
                relevantExisting, request.RootEntityId, containmentByChild, currentContainments);
            if (expectations is null)
                return Rejected(dryRun, "WORLD_SCOPE_TOO_LARGE",
                    "The relevant containment ancestry exceeds the bounded transaction snapshot.");

            var batch = new ApplicationEcsEffectBatch
            {
                StateSpaceId = request.StateSpaceId,
                Effects = typedEffects.AsReadOnly(),
                Intent = context.Intent,
                ProceduresUsed = context.ProceduresUsed.Count == 0
                    ? ["procedure.system.use"]
                    : context.ProceduresUsed,
                ExecutionIdentity = new(operationId, requestFingerprint),
                ContainmentExpectations = expectations
            };
            var applied = await effects.ApplyAsync(batch, dryRun, cancellationToken);
            return new(applied.Valid, dryRun, applied.Replayed, manifestEntities.Length,
                typedEffects.Count, applied.OperationId,
                applied.Valid ? "" : applied.Problems.FirstOrDefault()?.Code ?? "WORLD_AUTHORING_FAILED",
                applied.Problems, applied.Receipts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            return Rejected(dryRun, "WORLD_AUTHORING_FAILED", exception.Message);
        }
    }

    private static ApplicationEcsEffectProblem? ValidateShape(
        ApplicationWorldAuthoringRequest? request,
        ApplicationWorldAuthoringContext? context)
    {
        if (request is null || context is null) return Problem("INVALID_WORLD_MANIFEST", "A request and authoring context are required.");
        if (!LowerHexToken(request.RequestToken)) return Problem("INVALID_WORLD_MANIFEST", "requestToken must be 32 lowercase hexadecimal characters.");
        if (!Token(request.ApplicationId, 63) || !Token(request.StateSpaceId, 200) || !Token(request.RootEntityId, 200))
            return Problem("INVALID_WORLD_MANIFEST", "Application, state-space, and root IDs must be bounded tokens.");
        if (request.Entities is not { Count: >= 1 and <= MaximumEntities }
            || request.Relationships is null || request.Relationships.Count > MaximumRelationships)
            return Problem("INVALID_WORLD_MANIFEST", "The manifest requires 1–64 entities and at most 64 relationships.");
        if (context.ProceduresUsed is null || context.Intent is null)
            return Problem("INVALID_WORLD_MANIFEST", "Intent and procedure evidence are required.");
        if (request.Entities.Any(value => value is null || !Token(value.EntityId, 200)
                || !Text(value.Name, 400) || value.ExpectedRevision < 0
                || value.Components is null || value.Components.Count > MaximumComponentsPerEntity
                || value.Components.Any(component => component is null || !Token(component.QualifiedTypeId, 200)
                    || component.ExpectedRevision < 0 || !JsonObject(component.ValueJson))))
            return Problem("INVALID_WORLD_MANIFEST", "One or more entity or component records are invalid.");
        if (request.Entities.Select(value => value.EntityId).Distinct(StringComparer.Ordinal).Count() != request.Entities.Count
            || request.Entities.Any(value => value.Components.Select(component => component.QualifiedTypeId)
                .Distinct(StringComparer.Ordinal).Count() != value.Components.Count))
            return Problem("INVALID_WORLD_MANIFEST", "Entity and per-entity component IDs must be unique.");
        if (request.Entities.Any(value => value.Containment is not null
                && (!Token(value.Containment.ContainerEntityId, 200) || !Text(value.Containment.Slot, 100)
                    || value.Containment.ExpectedRevision < 0)))
            return Problem("INVALID_WORLD_MANIFEST", "One or more containment records are invalid.");
        if (request.Relationships.Any(value => value is null || !Token(value.FromEntityId, 200)
                || !Token(value.ToEntityId, 200) || !Token(value.QualifiedKind, 200)
                || value.ExpectedRevision < 0 || !JsonObject(value.ValueJson)))
            return Problem("INVALID_WORLD_MANIFEST", "One or more relationship records are invalid.");
        return null;
    }

    private static IReadOnlyList<ApplicationEcsContainmentExpectation>? BuildContainmentExpectations(
        IEnumerable<string> entityIds,
        string rootId,
        IReadOnlyDictionary<string, EcsContainmentView> containmentByChild,
        IReadOnlyList<EcsContainmentView> containments)
    {
        var containers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entityId in entityIds)
        {
            var current = entityId;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (current != rootId && containmentByChild.TryGetValue(current, out var edge))
            {
                if (!seen.Add(current)) return null;
                containers.Add(edge.ContainerEntityId);
                current = edge.ContainerEntityId;
            }
            if (current != rootId) return null;
        }
        if (containers.Count > ApplicationEcsEffectValidation.MaximumContainmentExpectations) return null;
        var result = new List<ApplicationEcsContainmentExpectation>(containers.Count);
        foreach (var container in containers.Order(StringComparer.Ordinal))
        {
            var contents = containments.Where(value => value.ContainerEntityId == container)
                .OrderBy(value => value.ContainedEntityId, StringComparer.Ordinal)
                .Select(value => new EcsContainmentExpectationItem(
                    value.ContainedEntityId, value.Slot, value.Revision))
                .ToArray();
            if (contents.Length > ApplicationEcsEffectValidation.MaximumContentsPerExpectation) return null;
            result.Add(new(container, contents));
        }
        return result.AsReadOnly();
    }

    private static bool NewEntityReachesRoot(
        string entityId,
        string rootId,
        IReadOnlyDictionary<string, ApplicationWorldAuthoringEntity> manifest,
        IReadOnlyDictionary<string, EcsContainmentView> containments)
    {
        var current = entityId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (current != rootId)
        {
            if (!seen.Add(current)) return false;
            if (manifest.TryGetValue(current, out var authored) && authored.ExpectedRevision == 0)
            {
                if (authored.Containment is null) return false;
                current = authored.Containment.ContainerEntityId;
                continue;
            }
            return InsideRoot(current, rootId, containments);
        }
        return true;
    }

    private static bool EndpointInsideRoot(
        string entityId,
        string rootId,
        IReadOnlyDictionary<string, ApplicationWorldAuthoringEntity> manifest,
        IReadOnlyDictionary<string, EcsContainmentView> containments) =>
        entityId == rootId
        || (manifest.TryGetValue(entityId, out var authored) && authored.ExpectedRevision == 0
            ? NewEntityReachesRoot(entityId, rootId, manifest, containments)
            : InsideRoot(entityId, rootId, containments));

    private static bool InsideRoot(
        string entityId,
        string rootId,
        IReadOnlyDictionary<string, EcsContainmentView> containments)
    {
        var current = entityId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (current != rootId)
        {
            if (!seen.Add(current) || !containments.TryGetValue(current, out var edge)) return false;
            current = edge.ContainerEntityId;
        }
        return true;
    }

    private static bool OwnedQualifiedId(string value, IReadOnlySet<ApplicationIdentifier> owners)
    {
        var separator = value.IndexOf('.');
        if (separator <= 0) return false;
        try
        {
            var owner = ApplicationIdentifier.Parse(value[..separator]);
            ComponentTypeIdentifier.Validate(owner, value);
            return owners.Contains(owner);
        }
        catch (ArgumentException) { return false; }
    }

    private static bool JsonObject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumJsonLength) return false;
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }

    private static bool LowerHexToken(string? value) => value is { Length: 32 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    private static bool Token(string? value, int maximum) => Text(value, maximum)
        && !value!.Any(char.IsWhiteSpace);

    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value == value.Trim() && value.Length <= maximum;

    private static string Fingerprint(byte[] value) => Convert.ToHexString(SHA256.HashData(value));

    private static ApplicationEcsEffectProblem Problem(string code, string message) => new(-1, code, message);

    private static ApplicationWorldAuthoringResult Rejected(bool dryRun, string code, string message) =>
        new(false, dryRun, false, 0, 0, "", code, [Problem(code, message)], []);
}
