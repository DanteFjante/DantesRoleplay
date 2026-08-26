using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.StateSpaceAdministration;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.LegacyStateAdoption;

public sealed class LegacyStateAdoptionService(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    IApplicationComponentTypeRegistry componentTypes,
    IBoundedJsonSchemaValidator schemas,
    IStateSpaceRegistry stateSpaces,
    IOperationLog operations) : ILegacyStateAdoptionService
{
    public const string Kind = "system.state-space.adopt-legacy";

    public async Task<LegacyStateAdoptionPreview> PreviewAsync(
        LegacyStateAdoptionRequest request,
        LegacyStateAdoptionContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(request, context);
        var candidate = BuildCandidate(request);
        var operation = await operations.RecordAsync(
            "commit",
            $"Validated adoption of {candidate.Inventory.EntityCount} legacy entities without changing runtime state.",
            true,
            context.Intent,
            PreviewSubject(context.RequestToken, candidate.RequestFingerprint, candidate.Inventory.EvidenceFingerprint),
            context.ProceduresUsed,
            consumesReadEvidence: false,
            cancellationToken: cancellationToken,
            guardEvidenceJson: JsonSerializer.Serialize(context.AuthorizationEvidence));
        return new(request.StateSpaceId, request.ApplicationId, candidate.Inventory, "would-adopt", operation.Id);
    }

    public async Task<LegacyStateAdoptionReceipt> AdoptAsync(
        LegacyStateAdoptionRequest request,
        LegacyStateAdoptionContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(request, context);
        var requestFingerprint = RequestFingerprint(request);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var replay = await ReplayAsync(request, context.RequestToken, requestFingerprint, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            RequireAnyPreview(context.RequestToken, requestFingerprint);
            var candidate = BuildCandidate(request);
            RequireExactPreview(context.RequestToken, candidate.RequestFingerprint,
                candidate.Inventory.EvidenceFingerprint);

            var stateSpace = stateSpaces.Create(new(
                request.StateSpaceId, candidate.Application, candidate.Active.ActivationFingerprint));
            var now = DateTime.UtcNow;

            db.AddRange(candidate.Entities.Select(value => new ApplicationEcsEntityRecord
            {
                StateSpaceId = request.StateSpaceId,
                Id = value.Id,
                Name = value.Name,
                Revision = 1,
                CreatedAtUtc = Utc(value.CreatedAt),
                DeletedAtUtc = value.DeletedAt is null ? null : Utc(value.DeletedAt.Value)
            }));
            db.AddRange(candidate.Components.Select(value =>
            {
                var mapped = candidate.ComponentMappings[value.DefinitionId];
                return new ApplicationEcsComponentRecord
                {
                    StateSpaceId = request.StateSpaceId,
                    EntityId = value.EntityId,
                    QualifiedTypeId = mapped.QualifiedTypeId,
                    TypeVersion = mapped.TypeVersion,
                    SchemaHash = mapped.SchemaHash,
                    Data = value.Data,
                    Revision = Math.Max(1, value.Revision),
                    CreatedAtUtc = Utc(value.CreatedAt),
                    UpdatedAtUtc = Utc(value.UpdatedAt)
                };
            }));
            db.AddRange(candidate.Containments.Select(value => new ApplicationEcsContainmentRecord
            {
                StateSpaceId = request.StateSpaceId,
                ContainedEntityId = value.ContainedId,
                ContainerEntityId = value.ContainerId,
                Slot = value.Slot,
                Revision = 1,
                CreatedAtUtc = Utc(value.CreatedAt),
                UpdatedAtUtc = Utc(value.CreatedAt)
            }));
            db.AddRange(candidate.Relationships.Select(value => new ApplicationEcsRelationshipRecord
            {
                StateSpaceId = request.StateSpaceId,
                FromEntityId = value.FromEntityId,
                ToEntityId = value.ToEntityId,
                QualifiedKind = candidate.RelationshipMappings[value.Kind],
                Data = value.Data,
                Revision = 1,
                CreatedAtUtc = Utc(value.CreatedAt),
                UpdatedAtUtc = Utc(value.CreatedAt)
            }));

            await operations.RecordAsync(
                "commit",
                $"Adopted {candidate.Inventory.EntityCount} entities, {candidate.Inventory.ComponentCount} components, {candidate.Inventory.ContainmentCount} containments, and {candidate.Inventory.RelationshipCount} relationships into state space '{request.StateSpaceId}'.",
                true,
                context.Intent,
                Subject(candidate.RequestFingerprint),
                context.ProceduresUsed,
                consumesReadEvidence: true,
                cancellationToken: cancellationToken,
                guardEvidenceJson: JsonSerializer.Serialize(context.AuthorizationEvidence),
                id: context.RequestToken);

            var bindingFingerprint = BindingFingerprint(request.StateSpaceId, candidate.Application,
                candidate.Active.ActivationFingerprint, stateSpace.BindingRevision);
            db.Add(new StateSpaceBindingRevisionRecord
            {
                StateSpaceId = request.StateSpaceId,
                BindingRevision = stateSpace.BindingRevision,
                ApplicationId = request.ApplicationId.Value,
                ApplicationRevision = candidate.Application.Revision,
                ApplicationFingerprint = candidate.Application.Fingerprint,
                ActiveFingerprint = candidate.Active.ActivationFingerprint,
                BindingFingerprint = bindingFingerprint,
                PreviousBindingFingerprint = null,
                CompatibilityCode = "adopted-legacy-complete",
                EntityCount = candidate.Inventory.EntityCount,
                ComponentCount = candidate.Inventory.ComponentCount,
                DependencyCoverageVersion = candidate.Active.DependencyCoverageVersion,
                DependencyCoverageComplete = candidate.Active.DependencyCoverageComplete,
                OperationId = context.RequestToken,
                CreatedAtUtc = stateSpace.CreatedAtUtc,
                UpdatedAtUtc = stateSpace.UpdatedAtUtc,
                RecordedAtUtc = now
            });
            db.Add(new LegacyStateAdoptionRecord
            {
                StateSpaceId = request.StateSpaceId,
                ApplicationId = request.ApplicationId.Value,
                RequestFingerprint = candidate.RequestFingerprint,
                SourceFingerprint = candidate.Inventory.SourceFingerprint,
                EvidenceFingerprint = candidate.Inventory.EvidenceFingerprint,
                EntityCount = candidate.Inventory.EntityCount,
                ComponentCount = candidate.Inventory.ComponentCount,
                ContainmentCount = candidate.Inventory.ContainmentCount,
                RelationshipCount = candidate.Inventory.RelationshipCount,
                OperationId = context.RequestToken,
                CreatedAtUtc = now
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(request.StateSpaceId, request.ApplicationId, candidate.Inventory, "adopted", context.RequestToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private Candidate BuildCandidate(LegacyStateAdoptionRequest request)
    {
        if (stateSpaces.Get(request.StateSpaceId) is not null)
            throw Invalid("STATE_SPACE_EXISTS", "stateSpaceId already belongs to an immutable state space.");
        var application = applications.Get(request.ApplicationId)
            ?? throw Invalid("APPLICATION_UNKNOWN", "The requested application is not registered.");
        var active = activations.Current(request.ApplicationId)
            ?? throw Invalid("ACTIVATION_REQUIRED", "The application must have an active overlay.");
        if (active.ActivationFingerprint != request.ActiveFingerprint)
            throw Invalid("ACTIVATION_STALE", "activeFingerprint does not match the current active application overlay.");
        if (active.ApplicationRevision != application.Revision
            || active.ApplicationFingerprint != application.Fingerprint)
            throw Invalid("APPLICATION_STALE", "The active overlay no longer matches the registered application revision.");

        var allEntities = db.Entities.AsNoTracking().OrderBy(value => value.Id).ToArray();
        var allComponents = db.Components.AsNoTracking()
            .OrderBy(value => value.EntityId).ThenBy(value => value.DefinitionId).ToArray();
        var allContainments = db.Containments.AsNoTracking()
            .OrderBy(value => value.ContainedId).ToArray();
        var allRelationships = db.Relationships.AsNoTracking()
            .OrderBy(value => value.FromEntityId).ThenBy(value => value.ToEntityId).ThenBy(value => value.Kind).ToArray();

        var knownEntityIds = allEntities.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        if (allComponents.Any(value => !knownEntityIds.Contains(value.EntityId))
            || allContainments.Any(value => !knownEntityIds.Contains(value.ContainerId)
                || !knownEntityIds.Contains(value.ContainedId))
            || allRelationships.Any(value => !knownEntityIds.Contains(value.FromEntityId)
                || !knownEntityIds.Contains(value.ToEntityId)))
            throw Invalid("LEGACY_REFERENCE_INVALID", "Legacy state contains an edge or component with an unknown entity reference.");

        // Legacy deletion is a tombstone: WorldStore excludes the entity from every runtime read
        // while deliberately retaining its component and edge rows as historical evidence. Adoption
        // must materialize that same active graph, not resurrect tombstones in a new state space.
        var entities = allEntities.Where(value => value.DeletedAt is null).ToArray();
        var entityIds = entities.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        var components = allComponents.Where(value => entityIds.Contains(value.EntityId)).ToArray();
        var containments = allContainments.Where(value => entityIds.Contains(value.ContainerId)
            && entityIds.Contains(value.ContainedId)).ToArray();
        var relationships = allRelationships.Where(value => entityIds.Contains(value.FromEntityId)
            && entityIds.Contains(value.ToEntityId)).ToArray();
        ValidateContainment(containments);

        var componentMappings = ExactComponentMappings(request, components);
        var relationshipMappings = ExactRelationshipMappings(request, relationships);
        foreach (var component in components)
        {
            var mapped = componentMappings[component.DefinitionId];
            var registered = componentTypes.Get(mapped.QualifiedTypeId, mapped.TypeVersion);
            if (registered is null || registered.Owner != request.ApplicationId
                || registered.SchemaHash != mapped.SchemaHash)
                throw Invalid("COMPONENT_TYPE_MISMATCH", $"Mapping for '{component.DefinitionId}' does not identify an exact component type owned by the application.");
            var validation = schemas.Validate(registered.ProfileId, registered.SchemaJson, component.Data);
            if (validation.Status != SchemaValueStatus.Valid)
                throw Invalid("COMPONENT_VALUE_INVALID", $"Legacy component '{component.EntityId}/{component.DefinitionId}' does not satisfy its mapped schema.");
        }
        foreach (var relationship in relationships)
            ValidateJson(relationship.Data, "RELATIONSHIP_DATA_INVALID");

        var requestFingerprint = RequestFingerprint(request);
        var sourceFingerprint = SourceFingerprint(entities, components, containments, relationships);
        var evidenceFingerprint = Hash(new
        {
            requestFingerprint,
            sourceFingerprint,
            applicationId = application.ApplicationId.Value,
            application.Revision,
            application.Fingerprint,
            active.ActivationFingerprint
        });
        var inventory = new LegacyStateInventory(entities.Length, components.Length,
            containments.Length, relationships.Length, sourceFingerprint, evidenceFingerprint);
        return new(application, active, entities, components, containments, relationships,
            componentMappings, relationshipMappings, requestFingerprint, inventory);
    }

    private async Task<LegacyStateAdoptionReceipt?> ReplayAsync(
        LegacyStateAdoptionRequest request,
        string token,
        string requestFingerprint,
        CancellationToken cancellationToken)
    {
        var operation = await operations.GetAsync(token, cancellationToken);
        if (operation is null) return null;
        if (!operation.Success || operation.Tool != "commit" || operation.Subject != Subject(requestFingerprint))
            throw Invalid("REQUEST_TOKEN_CONFLICT", "requestToken already identifies another operation.");
        var evidence = db.Set<LegacyStateAdoptionRecord>().AsNoTracking()
            .SingleOrDefault(value => value.OperationId == token)
            ?? throw Invalid("REPLAY_EVIDENCE_MISSING", "The immutable adoption evidence is unavailable.");
        if (evidence.StateSpaceId != request.StateSpaceId || evidence.ApplicationId != request.ApplicationId.Value
            || evidence.RequestFingerprint != requestFingerprint)
            throw Invalid("REQUEST_TOKEN_CONFLICT", "requestToken already identifies another adoption.");
        var inventory = new LegacyStateInventory(evidence.EntityCount, evidence.ComponentCount,
            evidence.ContainmentCount, evidence.RelationshipCount, evidence.SourceFingerprint,
            evidence.EvidenceFingerprint);
        return new(evidence.StateSpaceId, request.ApplicationId, inventory, "adopted", token);
    }

    private Dictionary<string, EcsComponentReference> ExactComponentMappings(
        LegacyStateAdoptionRequest request,
        IReadOnlyList<DantesRoleplay.World.Component> components)
    {
        Dictionary<string, EcsComponentReference> mappings;
        try
        {
            mappings = request.ComponentMappings.ToDictionary(
                value => value.LegacyDefinitionId, value => value.ComponentType, StringComparer.Ordinal);
        }
        catch (ArgumentException)
        {
            throw Invalid("COMPONENT_MAPPING_DUPLICATE", "Each legacy component definition must be mapped exactly once.");
        }
        var used = components.Select(value => value.DefinitionId).Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!used.SequenceEqual(mappings.Keys.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw Invalid("COMPONENT_MAPPING_INCOMPLETE", "Component mappings must exactly cover the legacy definitions currently in use.");
        foreach (var (legacyId, mapped) in mappings)
        {
            if (string.IsNullOrWhiteSpace(legacyId) || legacyId.Length > 200)
                throw Invalid("INVALID_PAYLOAD", "Legacy component definition IDs must be bounded.");
            try { mapped.Validate(); ComponentTypeIdentifier.Validate(request.ApplicationId, mapped.QualifiedTypeId); }
            catch (ArgumentException exception) { throw Invalid("COMPONENT_MAPPING_INVALID", exception.Message); }
        }
        return mappings;
    }

    private static Dictionary<string, string> ExactRelationshipMappings(
        LegacyStateAdoptionRequest request,
        IReadOnlyList<DantesRoleplay.World.Relationship> relationships)
    {
        Dictionary<string, string> mappings;
        try
        {
            mappings = request.RelationshipMappings.ToDictionary(
                value => value.LegacyKind, value => value.QualifiedKind, StringComparer.Ordinal);
        }
        catch (ArgumentException)
        {
            throw Invalid("RELATIONSHIP_MAPPING_DUPLICATE", "Each legacy relationship kind must be mapped exactly once.");
        }
        var used = relationships.Select(value => value.Kind).Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!used.SequenceEqual(mappings.Keys.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw Invalid("RELATIONSHIP_MAPPING_INCOMPLETE", "Relationship mappings must exactly cover the legacy kinds currently in use.");
        foreach (var (legacyKind, qualifiedKind) in mappings)
        {
            if (string.IsNullOrWhiteSpace(legacyKind) || legacyKind.Length > 100)
                throw Invalid("INVALID_PAYLOAD", "Legacy relationship kinds must be bounded.");
            try { ComponentTypeIdentifier.Validate(request.ApplicationId, qualifiedKind); }
            catch (ArgumentException exception) { throw Invalid("RELATIONSHIP_MAPPING_INVALID", exception.Message); }
        }
        return mappings;
    }

    private static void ValidateContainment(IReadOnlyList<DantesRoleplay.World.Containment> containments)
    {
        var parents = containments.ToDictionary(value => value.ContainedId, value => value.ContainerId,
            StringComparer.Ordinal);
        foreach (var contained in parents.Keys)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { contained };
            var current = contained;
            while (parents.TryGetValue(current, out var parent))
            {
                if (!seen.Add(parent))
                    throw Invalid("CONTAINMENT_CYCLE", "Legacy containment must be acyclic.");
                current = parent;
            }
        }
    }

    private void RequireAnyPreview(string token, string requestFingerprint)
    {
        var prefix = PreviewPrefix(token, requestFingerprint);
        if (!db.Operations.AsNoTracking().Any(value => value.Tool == "commit" && value.Success
                && value.Subject.StartsWith(prefix)))
            throw Invalid("DRY_RUN_REQUIRED", "Commit the exact payload with dryRun: true before applying it.");
    }

    private void RequireExactPreview(string token, string requestFingerprint, string evidenceFingerprint)
    {
        var subject = PreviewSubject(token, requestFingerprint, evidenceFingerprint);
        if (!db.Operations.AsNoTracking().Any(value => value.Tool == "commit" && value.Success
                && value.Subject == subject))
            throw Invalid("DRY_RUN_STALE", "Legacy state or application evidence changed after dry run; dry-run the exact payload again.");
    }

    private static void Validate(LegacyStateAdoptionRequest request, LegacyStateAdoptionContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(request.StateSpaceId) || request.StateSpaceId.Length > 200
            || request.StateSpaceId.Any(char.IsControl))
            throw Invalid("INVALID_PAYLOAD", "stateSpaceId must be a nonblank bounded identifier without control characters.");
        if (!UpperSha256(request.ActiveFingerprint))
            throw Invalid("INVALID_PAYLOAD", "activeFingerprint must be an uppercase SHA-256 value.");
        if (request.ComponentMappings is null || request.ComponentMappings.Count > 256
            || request.RelationshipMappings is null || request.RelationshipMappings.Count > 256)
            throw Invalid("INVALID_PAYLOAD", "Mapping lists are required and may contain at most 256 entries each.");
        if (context.RequestToken is not { Length: 32 }
            || context.RequestToken.Any(value => !(char.IsAsciiDigit(value) || value is >= 'a' and <= 'f')))
            throw Invalid("INVALID_PAYLOAD", "requestToken must contain exactly 32 lowercase hexadecimal characters.");
        if (!context.AuthorizationEvidence.Allowed)
            throw Invalid("PRIVATE_OPERATOR_DENIED", "A successful authorization decision is required.");
        if ((context.Intent?.Length ?? 0) > 2_000 || context.ProceduresUsed is null
            || context.ProceduresUsed.Count > 64
            || context.ProceduresUsed.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 200))
            throw Invalid("INVALID_PAYLOAD", "Intent and procedure evidence must remain bounded.");
    }

    private static string RequestFingerprint(LegacyStateAdoptionRequest request) => Hash(new
    {
        kind = Kind,
        request.StateSpaceId,
        applicationId = request.ApplicationId.Value,
        request.ActiveFingerprint,
        componentMappings = request.ComponentMappings.OrderBy(value => value.LegacyDefinitionId, StringComparer.Ordinal)
            .Select(value => new { value.LegacyDefinitionId, value.ComponentType.QualifiedTypeId,
                value.ComponentType.TypeVersion, value.ComponentType.SchemaHash }).ToArray(),
        relationshipMappings = request.RelationshipMappings.OrderBy(value => value.LegacyKind, StringComparer.Ordinal)
            .Select(value => new { value.LegacyKind, value.QualifiedKind }).ToArray()
    });

    private static string SourceFingerprint(
        IReadOnlyList<DantesRoleplay.World.Entity> entities,
        IReadOnlyList<DantesRoleplay.World.Component> components,
        IReadOnlyList<DantesRoleplay.World.Containment> containments,
        IReadOnlyList<DantesRoleplay.World.Relationship> relationships) => Hash(new
    {
        entities = entities.Select(value => new { value.Id, value.Name, value.CreatedAt, value.DeletedAt }).ToArray(),
        components = components.Select(value => new { value.EntityId, value.DefinitionId, value.Data,
            value.Revision, value.CreatedAt, value.UpdatedAt }).ToArray(),
        containments = containments.Select(value => new { value.ContainerId, value.ContainedId,
            value.Slot, value.CreatedAt }).ToArray(),
        relationships = relationships.Select(value => new { value.FromEntityId, value.ToEntityId,
            value.Kind, value.Data, value.CreatedAt }).ToArray()
    });

    private static string BindingFingerprint(string stateSpaceId, ApplicationRevision application,
        string activeFingerprint, int bindingRevision) => Hash(new
    {
        stateSpaceId,
        applicationId = application.ApplicationId.Value,
        applicationRevision = application.Revision,
        applicationFingerprint = application.Fingerprint,
        activeFingerprint,
        bindingRevision
    });

    private static void ValidateJson(string value, string code)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > SystemJsonSchemaProfile.MaximumValueBytes)
            throw Invalid(code, "Legacy relationship data must be bounded JSON.");
        try { using var _ = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 64 }); }
        catch (JsonException) { throw Invalid(code, "Legacy relationship data must be valid bounded JSON."); }
    }

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static bool UpperSha256(string value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static string Subject(string requestFingerprint) => $"{Kind}|{requestFingerprint}";
    private static string PreviewPrefix(string token, string requestFingerprint) =>
        $"preview|{Kind}|{token}|{requestFingerprint}|";
    private static string PreviewSubject(string token, string requestFingerprint, string evidenceFingerprint) =>
        PreviewPrefix(token, requestFingerprint) + evidenceFingerprint;
    private static LegacyStateAdoptionException Invalid(string code, string message) => new(code, message);

    private sealed record Candidate(
        ApplicationRevision Application,
        ActiveApplicationManifest Active,
        IReadOnlyList<DantesRoleplay.World.Entity> Entities,
        IReadOnlyList<DantesRoleplay.World.Component> Components,
        IReadOnlyList<DantesRoleplay.World.Containment> Containments,
        IReadOnlyList<DantesRoleplay.World.Relationship> Relationships,
        IReadOnlyDictionary<string, EcsComponentReference> ComponentMappings,
        IReadOnlyDictionary<string, string> RelationshipMappings,
        string RequestFingerprint,
        LegacyStateInventory Inventory);
}

internal sealed class LegacyStateAdoptionRecord
{
    public required string StateSpaceId { get; set; }
    public required string ApplicationId { get; set; }
    public required string RequestFingerprint { get; set; }
    public required string SourceFingerprint { get; set; }
    public required string EvidenceFingerprint { get; set; }
    public int EntityCount { get; set; }
    public int ComponentCount { get; set; }
    public int ContainmentCount { get; set; }
    public int RelationshipCount { get; set; }
    public required string OperationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
