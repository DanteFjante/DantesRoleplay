using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Projections;

/// <summary>
/// Resolves generic registered-object dependencies and stages audience-safe change evidence in the
/// same transaction as the structural writes. Raw entity and component identities are deliberately
/// absent from the durable delivery record.
/// </summary>
public sealed class ApplicationObjectChangeTransactionParticipant(
    DantesRoleplayDbContext db,
    IStateSpaceRegistry stateSpaces) : IApplicationEcsTransactionParticipant
{
    private static readonly string[] AllPerspectives = ["dm", "player"];

    public async Task StageAsync(
        ApplicationEcsEffectBatch batch,
        IReadOnlyList<ApplicationEcsEffectReceipt> receipts,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var stateSpace = stateSpaces.Get(batch.StateSpaceId)
            ?? throw new ApplicationEcsTransactionParticipantException("The change delivery state space is unknown.");
        var applicationId = stateSpace.ApplicationRevision.ApplicationId.Value;

        var definitionIds = await db.Set<ProjectionDefinitionRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == applicationId)
            .Select(value => value.QualifiedId)
            .ToArrayAsync(cancellationToken);
        var versions = await db.Set<ProjectionDefinitionVersionRecord>().AsNoTracking()
            .Where(value => definitionIds.Contains(value.QualifiedId) && value.ObjectContractJson != null)
            .ToArrayAsync(cancellationToken);
        var components = await db.Set<ProjectionComponentInputRecord>().AsNoTracking()
            .Where(value => definitionIds.Contains(value.QualifiedId))
            .ToArrayAsync(cancellationToken);
        var dependencies = await db.Set<ProjectionDependencyInputRecord>().AsNoTracking()
            .Where(value => definitionIds.Contains(value.QualifiedId))
            .ToArrayAsync(cancellationToken);

        var declarations = versions.Select(value => new ObjectDeclaration(
            value.QualifiedId,
            value.Version,
            JsonSerializer.Deserialize<RegisteredApplicationObjectContract>(value.ObjectContractJson!)
                ?? throw new ApplicationEcsTransactionParticipantException("A registered object contract is unreadable.")))
            .ToDictionary(value => value.Key, StringComparer.Ordinal);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        var applicationFallback = versions.Length == 0 && batch.Effects.Count > 0;

        foreach (var effect in batch.Effects)
        {
            switch (effect.Type)
            {
                case ApplicationEcsEffectType.ComponentAdd:
                case ApplicationEcsEffectType.ComponentSet:
                case ApplicationEcsEffectType.ComponentMerge:
                case ApplicationEcsEffectType.ComponentRemove:
                case ApplicationEcsEffectType.ClockAdvance:
                    if (effect.ComponentType is not null)
                        AddComponentConsumers(effect.ComponentType, components, declarations, changed);
                    break;
                case ApplicationEcsEffectType.RelationshipSet:
                case ApplicationEcsEffectType.RelationshipRemove:
                    foreach (var declaration in declarations.Values.Where(value => value.Contract.Relationships.Any(
                                 relationship => relationship.QualifiedKind == effect.QualifiedRelationshipKind)))
                        changed.Add(declaration.Key);
                    break;
                case ApplicationEcsEffectType.EntityCreate:
                case ApplicationEcsEffectType.EntityDelete:
                case ApplicationEcsEffectType.ContainmentMove:
                case ApplicationEcsEffectType.ContainmentRemove:
                    // Registered objects do not yet declare entity-existence or containment dependencies.
                    // Keep this recovery scoped to the owning application and audience.
                    applicationFallback = true;
                    break;
            }
        }

        CloseOverDependencies(changed, dependencies);
        var now = DateTime.UtcNow;
        var rows = changed.Where(declarations.ContainsKey).Order(StringComparer.Ordinal).Select(key =>
        {
            var declaration = declarations[key];
            return new ApplicationObjectChangeRecord
            {
                ContractVersion = ApplicationObjectChangeContract.Version,
                OperationId = operationId,
                ApplicationId = applicationId,
                StateSpaceId = batch.StateSpaceId,
                Scope = ApplicationObjectChangeContract.ObjectScope,
                ObjectQualifiedId = declaration.QualifiedId,
                ObjectVersion = declaration.Version,
                ReadPerspectivesJson = JsonSerializer.Serialize(declaration.Contract.Access.ReadPerspectives
                    .Order(StringComparer.Ordinal).ToArray()),
                Reason = "registered-dependency",
                CreatedAtUtc = now
            };
        }).ToList();

        if (applicationFallback)
            rows.Add(new ApplicationObjectChangeRecord
            {
                ContractVersion = ApplicationObjectChangeContract.Version,
                OperationId = operationId,
                ApplicationId = applicationId,
                StateSpaceId = batch.StateSpaceId,
                Scope = ApplicationObjectChangeContract.ApplicationScope,
                ReadPerspectivesJson = JsonSerializer.Serialize(AllPerspectives),
                Reason = "dependency-fallback",
                CreatedAtUtc = now
            });

        if (rows.Count == 0)
            rows.Add(new ApplicationObjectChangeRecord
            {
                ContractVersion = ApplicationObjectChangeContract.Version,
                OperationId = operationId,
                ApplicationId = applicationId,
                StateSpaceId = batch.StateSpaceId,
                Scope = ApplicationObjectChangeContract.NoChangeScope,
                ReadPerspectivesJson = "[]",
                Reason = "tracked-no-dependency",
                CreatedAtUtc = now
            });
        db.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);

        var cutoff = await db.Set<ApplicationObjectChangeRecord>().AsNoTracking()
            .OrderByDescending(value => value.Cursor)
            .Skip(ApplicationObjectChangeContract.RetainedRows - 1)
            .Select(value => (long?)value.Cursor)
            .FirstOrDefaultAsync(cancellationToken);
        if (cutoff is not null)
        {
            await db.Set<ApplicationObjectChangeRecord>()
                .Where(value => value.Cursor < cutoff.Value)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private static void AddComponentConsumers(
        EcsComponentReference componentType,
        IReadOnlyList<ProjectionComponentInputRecord> components,
        IReadOnlyDictionary<string, ObjectDeclaration> declarations,
        ISet<string> changed)
    {
        foreach (var input in components.Where(value => value.QualifiedTypeId == componentType.QualifiedTypeId
                     && value.TypeVersion == componentType.TypeVersion))
            changed.Add(Key(input.QualifiedId, input.Version));

        foreach (var declaration in declarations.Values.Where(value => value.Contract.Relationships.Any(relationship =>
                     relationship.RequiredEndpointComponents.Concat(relationship.OptionalEndpointComponents)
                         .Any(component => component.Type.QualifiedTypeId == componentType.QualifiedTypeId
                             && component.Type.TypeVersion == componentType.TypeVersion))))
            changed.Add(declaration.Key);
    }

    private static void CloseOverDependencies(
        ISet<string> changed,
        IReadOnlyList<ProjectionDependencyInputRecord> dependencies)
    {
        var pending = new Queue<string>(changed);
        while (pending.TryDequeue(out var dependencyKey))
        {
            foreach (var edge in dependencies.Where(value =>
                         Key(value.DependencyQualifiedId, value.DependencyVersion) == dependencyKey))
            {
                var consumerKey = Key(edge.QualifiedId, edge.Version);
                if (changed.Add(consumerKey)) pending.Enqueue(consumerKey);
            }
        }
    }

    private static string Key(string qualifiedId, int version) => qualifiedId + "@" + version;

    private sealed record ObjectDeclaration(
        string QualifiedId,
        int Version,
        RegisteredApplicationObjectContract Contract)
    {
        public string Key => ApplicationObjectChangeTransactionParticipant.Key(QualifiedId, Version);
    }
}

internal sealed class ApplicationObjectChangeRecord
{
    public long Cursor { get; set; }
    public int ContractVersion { get; set; }
    public required string OperationId { get; set; }
    public required string ApplicationId { get; set; }
    public required string StateSpaceId { get; set; }
    public required string Scope { get; set; }
    public string? ObjectQualifiedId { get; set; }
    public int? ObjectVersion { get; set; }
    public required string ReadPerspectivesJson { get; set; }
    public required string Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
