using System.Diagnostics;
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
    IStateSpaceRegistry stateSpaces,
    ApplicationObjectDependencyIndexCache dependencyIndices) : IApplicationEcsTransactionParticipant
{
    private const string AllPerspectivesJson = "[\"dm\",\"player\"]";

    public async Task StageAsync(
        ApplicationEcsEffectBatch batch,
        IReadOnlyList<ApplicationEcsEffectReceipt> receipts,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var stageStarted = Stopwatch.GetTimestamp();
        try
        {
            var stateSpace = stateSpaces.Get(batch.StateSpaceId)
                ?? throw new ApplicationEcsTransactionParticipantException("The change delivery state space is unknown.");
            var applicationId = stateSpace.ApplicationRevision.ApplicationId.Value;
            var registryGeneration = await db.Set<ProjectionRegistryGenerationRecord>().AsNoTracking()
                .Where(value => value.ApplicationId == applicationId)
                .Select(value => (long?)value.Generation)
                .SingleOrDefaultAsync(cancellationToken) ?? 0;

            var index = await dependencyIndices.GetOrPrepareAsync(db.Database.GetDbConnection(), applicationId,
                registryGeneration,
                async token =>
                {
                    var definitionIds = await db.Set<ProjectionDefinitionRecord>().AsNoTracking()
                        .Where(value => value.ApplicationId == applicationId)
                        .Select(value => value.QualifiedId)
                        .ToArrayAsync(token);
                    var versions = await db.Set<ProjectionDefinitionVersionRecord>().AsNoTracking()
                        .Where(value => definitionIds.Contains(value.QualifiedId) && value.ObjectContractJson != null)
                        .ToArrayAsync(token);
                    var components = await db.Set<ProjectionComponentInputRecord>().AsNoTracking()
                        .Where(value => definitionIds.Contains(value.QualifiedId))
                        .ToArrayAsync(token);
                    var dependencies = await db.Set<ProjectionDependencyInputRecord>().AsNoTracking()
                        .Where(value => definitionIds.Contains(value.QualifiedId))
                        .ToArrayAsync(token);
                    return PreparedApplicationObjectDependencyIndex.Create(versions, components, dependencies);
                }, cancellationToken);
            var declarations = index.Declarations;
            var changed = new HashSet<string>(StringComparer.Ordinal);
            var applicationFallback = declarations.Count == 0 && batch.Effects.Count > 0;

            foreach (var effect in batch.Effects)
            {
                var consumers = new HashSet<string>(StringComparer.Ordinal);
                switch (effect.Type)
                {
                    case ApplicationEcsEffectType.ComponentAdd:
                    case ApplicationEcsEffectType.ComponentSet:
                    case ApplicationEcsEffectType.ComponentMerge:
                    case ApplicationEcsEffectType.ComponentRemove:
                    case ApplicationEcsEffectType.ClockAdvance:
                        if (effect.ComponentType is not null)
                            AddConsumers(index.ComponentConsumers,
                                new(effect.ComponentType.QualifiedTypeId, effect.ComponentType.TypeVersion), consumers);
                        break;
                    case ApplicationEcsEffectType.RelationshipSet:
                    case ApplicationEcsEffectType.RelationshipRemove:
                        AddConsumers(index.RelationshipConsumers, effect.QualifiedRelationshipKind, consumers);
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
                CloseOverDependencies(consumers, index.DependencyConsumers);
                // Registered objects do not prove coverage of all still-active legacy queries.
                // An unmatched effect therefore needs scoped compatibility recovery, even beside
                // another effect which has a precise registered consumer in this same transaction.
                if (!consumers.Any(declarations.ContainsKey)) applicationFallback = true;
                changed.UnionWith(consumers);
            }

            CloseOverDependencies(changed, index.DependencyConsumers);
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
                    ReadPerspectivesJson = declaration.ReadPerspectivesJson,
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
                    ReadPerspectivesJson = AllPerspectivesJson,
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
        finally
        {
            dependencyIndices.RecordWriterHeldStage(Stopwatch.GetTimestamp() - stageStarted);
        }
    }

    private static void AddConsumers<TKey>(
        IReadOnlyDictionary<TKey, IReadOnlyList<string>> indexed,
        TKey key,
        ISet<string> changed) where TKey : notnull
    {
        if (!indexed.TryGetValue(key, out var consumers)) return;
        foreach (var consumer in consumers) changed.Add(consumer);
    }

    private static void CloseOverDependencies(
        ISet<string> changed,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencyConsumers)
    {
        var pending = new Queue<string>(changed);
        while (pending.TryDequeue(out var dependencyKey))
        {
            if (!dependencyConsumers.TryGetValue(dependencyKey, out var consumers)) continue;
            foreach (var consumerKey in consumers)
            {
                if (changed.Add(consumerKey)) pending.Enqueue(consumerKey);
            }
        }
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
