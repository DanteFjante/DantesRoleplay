using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using DantesRoleplay.EcsEffects;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.Projections;

/// <summary>
/// Process-wide bounded cache of immutable application-object dependency indices. The persisted
/// registry generation is part of every key, so definitions committed by this or another process
/// cannot alias an index prepared from older registry contents.
/// </summary>
public sealed class ApplicationObjectDependencyIndexCache : IApplicationObjectDependencyIndexCacheDiagnostics
{
    internal const int MaximumIndices = 64;
    internal const int MaximumDeclarationBytes = 4 * 1024 * 1024;
    internal const int MaximumNodes = 100_000;

    private readonly object gate = new();
    private readonly Dictionary<IndexKey, Entry> entries = [];
    private readonly LinkedList<IndexKey> recency = [];
    private readonly ConditionalWeakTable<DbConnection, PrivateMemoryIdentity> privateMemoryIdentities = new();
    private long nextPrivateMemoryIdentity;
    private int declarationBytes;
    private int nodes;
    private long preparations;
    private long hits;
    private long evictions;
    private long generationAdvances;
    private long preparationElapsedTicks;
    private long preparationAllocatedBytes;
    private long writerHeldStageElapsedTicks;
    private long stageCalls;

    public ApplicationObjectDependencyIndexCacheSnapshot Snapshot
    {
        get
        {
            lock (gate)
                return new(entries.Values.Count(value => value.Index is not null), declarationBytes, nodes,
                    preparations, hits, evictions, generationAdvances, preparationElapsedTicks,
                    preparationAllocatedBytes, writerHeldStageElapsedTicks, stageCalls);
        }
    }

    internal async Task<PreparedApplicationObjectDependencyIndex> GetOrPrepareAsync(
        DbConnection connection,
        string applicationId,
        long registryGeneration,
        Func<CancellationToken, Task<PreparedApplicationObjectDependencyIndex>> prepare,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentOutOfRangeException.ThrowIfNegative(registryGeneration);
        ArgumentNullException.ThrowIfNull(prepare);
        var application = new DatabaseApplication(DatabaseIdentity(connection), applicationId);
        Entry entry;
        var ownsPreparation = false;
        var admitted = true;
        lock (gate)
        {
            var key = new IndexKey(application, registryGeneration);
            if (entries.TryGetValue(key, out entry!))
            {
                hits++;
                Touch(entry);
            }
            else
            {
                EvictForAdmission();
                if (entries.Count < MaximumIndices)
                {
                    entry = new(key, recency.AddFirst(key));
                    entries.Add(key, entry);
                }
                else
                {
                    entry = new(key, new LinkedListNode<IndexKey>(key));
                    admitted = false;
                }
                ownsPreparation = true;
            }
        }

        if (!ownsPreparation)
            return await entry.Completion.Task.WaitAsync(cancellationToken);

        var started = Stopwatch.GetTimestamp();
        var allocatedBefore = GC.GetTotalAllocatedBytes(false);
        PreparedApplicationObjectDependencyIndex index;
        try
        {
            index = await prepare(cancellationToken);
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                if (admitted)
                {
                    entries.Remove(entry.Key);
                    recency.Remove(entry.Recency);
                    entry.Completion.TrySetException(exception);
                    _ = entry.Completion.Task.Exception;
                }
            }
            throw;
        }

        var elapsed = Stopwatch.GetTimestamp() - started;
        var allocated = Math.Max(0, GC.GetTotalAllocatedBytes(false) - allocatedBefore);
        lock (gate)
        {
            preparations++;
            preparationElapsedTicks += elapsed;
            preparationAllocatedBytes += allocated;
            var retain = admitted && entries.ContainsKey(entry.Key)
                && index.DeclarationBytes <= MaximumDeclarationBytes
                && index.Nodes <= MaximumNodes;
            if (retain)
            {
                EvictFor(index.DeclarationBytes, index.Nodes, entry);
                entry.Index = index;
                declarationBytes += index.DeclarationBytes;
                nodes += index.Nodes;
            }
            else
            {
                if (admitted)
                {
                    entries.Remove(entry.Key);
                    recency.Remove(entry.Recency);
                }
            }
            entry.Completion.TrySetResult(index);
        }
        return index;
    }

    internal void ObserveGeneration(DbConnection connection, string applicationId, long registryGeneration)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentOutOfRangeException.ThrowIfNegative(registryGeneration);
        var application = new DatabaseApplication(DatabaseIdentity(connection), applicationId);
        lock (gate)
        {
            generationAdvances++;
            foreach (var entry in entries.Values.Where(value => value.Key.Application == application
                         && value.Key.Generation != registryGeneration).ToArray())
                Remove(entry);
        }
    }

    internal void RecordWriterHeldStage(long elapsedTicks)
    {
        lock (gate)
        {
            writerHeldStageElapsedTicks += elapsedTicks;
            stageCalls++;
        }
    }

    private string DatabaseIdentity(DbConnection connection)
    {
        if (connection is not SqliteConnection sqlite)
            return connection.GetType().FullName + ":" + connection.Database;
        var builder = new SqliteConnectionStringBuilder(sqlite.ConnectionString);
        if (builder.Mode == SqliteOpenMode.Memory || builder.DataSource == ":memory:")
        {
            if (builder.Cache == SqliteCacheMode.Shared && builder.DataSource != ":memory:")
                return "sqlite-memory-shared:" + builder.DataSource;
            return "sqlite-memory-private:" + privateMemoryIdentities.GetValue(connection,
                _ => new(Interlocked.Increment(ref nextPrivateMemoryIdentity))).Value;
        }
        var dataSource = string.IsNullOrWhiteSpace(builder.DataSource) ? sqlite.DataSource : builder.DataSource;
        return "sqlite-file:" + Path.GetFullPath(dataSource).ToUpperInvariant();
    }

    private void EvictFor(int bytes, int nodeCount, Entry incoming)
    {
        while (entries.Values.Count(value => value.Index is not null) >= MaximumIndices
               || declarationBytes + bytes > MaximumDeclarationBytes
               || nodes + nodeCount > MaximumNodes)
        {
            var candidateNode = recency.Last;
            while (candidateNode is not null
                   && (candidateNode.Value == incoming.Key || entries[candidateNode.Value].Index is null))
                candidateNode = candidateNode.Previous;
            if (candidateNode is null) break;
            Remove(entries[candidateNode.Value]);
            evictions++;
        }
    }

    private void EvictForAdmission()
    {
        while (entries.Count >= MaximumIndices)
        {
            var candidateNode = recency.Last;
            while (candidateNode is not null && entries[candidateNode.Value].Index is null)
                candidateNode = candidateNode.Previous;
            if (candidateNode is null) return;
            Remove(entries[candidateNode.Value]);
            evictions++;
        }
    }

    private void Remove(Entry entry)
    {
        if (!entries.Remove(entry.Key)) return;
        recency.Remove(entry.Recency);
        if (entry.Index is null) return;
        declarationBytes -= entry.Index.DeclarationBytes;
        nodes -= entry.Index.Nodes;
    }

    private void Touch(Entry entry)
    {
        recency.Remove(entry.Recency);
        recency.AddFirst(entry.Recency);
    }

    private sealed class Entry(IndexKey key, LinkedListNode<IndexKey> recency)
    {
        public IndexKey Key { get; } = key;
        public LinkedListNode<IndexKey> Recency { get; } = recency;
        public TaskCompletionSource<PreparedApplicationObjectDependencyIndex> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PreparedApplicationObjectDependencyIndex? Index { get; set; }
    }

    private sealed record PrivateMemoryIdentity(long Value);
    private sealed record DatabaseApplication(string Database, string ApplicationId);
    private sealed record IndexKey(DatabaseApplication Application, long Generation);
}

public sealed record ApplicationObjectDependencyIndexCacheSnapshot(
    int RetainedIndices,
    int DeclarationBytes,
    int Nodes,
    long Preparations,
    long Hits,
    long Evictions,
    long GenerationAdvances,
    long PreparationElapsedTicks,
    long PreparationAllocatedBytes,
    long WriterHeldStageElapsedTicks,
    long StageCalls);

public interface IApplicationObjectDependencyIndexCacheDiagnostics
{
    ApplicationObjectDependencyIndexCacheSnapshot Snapshot { get; }
}

internal sealed record PreparedApplicationObjectDependencyIndex(
    IReadOnlyDictionary<string, PreparedApplicationObjectDeclaration> Declarations,
    IReadOnlyDictionary<ApplicationObjectComponentKey, IReadOnlyList<string>> ComponentConsumers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> RelationshipConsumers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> DependencyConsumers,
    int DeclarationBytes,
    int Nodes)
{
    public static PreparedApplicationObjectDependencyIndex Create(
        IReadOnlyList<ProjectionDefinitionVersionRecord> versions,
        IReadOnlyList<ProjectionComponentInputRecord> components,
        IReadOnlyList<ProjectionDependencyInputRecord> dependencies)
    {
        var declarations = versions.Select(value =>
        {
            var contract = System.Text.Json.JsonSerializer.Deserialize<RegisteredApplicationObjectContract>(
                               value.ObjectContractJson!)
                           ?? throw new ApplicationEcsTransactionParticipantException(
                               "A registered object contract is unreadable.");
            return new PreparedApplicationObjectDeclaration(value.QualifiedId, value.Version,
                System.Text.Json.JsonSerializer.Serialize(contract.Access.ReadPerspectives
                    .Order(StringComparer.Ordinal).ToArray()), contract);
        }).ToDictionary(value => value.Key, StringComparer.Ordinal);

        var componentConsumers = new Dictionary<ApplicationObjectComponentKey, HashSet<string>>();
        foreach (var component in components)
            Add(componentConsumers, new(component.QualifiedTypeId, component.TypeVersion),
                Key(component.QualifiedId, component.Version));
        var relationshipConsumers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var declaration in declarations.Values)
            foreach (var relationship in declaration.Contract.Relationships)
            {
                Add(relationshipConsumers, relationship.QualifiedKind, declaration.Key);
                foreach (var component in relationship.RequiredEndpointComponents
                             .Concat(relationship.OptionalEndpointComponents))
                    Add(componentConsumers,
                        new(component.Type.QualifiedTypeId, component.Type.TypeVersion), declaration.Key);
            }
        var dependencyConsumers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var dependency in dependencies)
            Add(dependencyConsumers, Key(dependency.DependencyQualifiedId, dependency.DependencyVersion),
                Key(dependency.QualifiedId, dependency.Version));

        var declarationBytes = versions.Sum(value => Encoding.UTF8.GetByteCount(value.QualifiedId)
            + Encoding.UTF8.GetByteCount(value.ObjectContractJson!) + 16);
        var nodeCount = declarations.Count + components.Count + dependencies.Count
            + relationshipConsumers.Sum(value => value.Value.Count)
            + componentConsumers.Sum(value => value.Value.Count);
        return new(ReadOnly(declarations), ReadOnly(componentConsumers), ReadOnly(relationshipConsumers),
            ReadOnly(dependencyConsumers), declarationBytes, nodeCount);
    }

    private static void Add<TKey>(Dictionary<TKey, HashSet<string>> values, TKey key, string consumer)
        where TKey : notnull
    {
        if (!values.TryGetValue(key, out var consumers)) values.Add(key, consumers = new(StringComparer.Ordinal));
        consumers.Add(consumer);
    }

    private static IReadOnlyDictionary<TKey, IReadOnlyList<string>> ReadOnly<TKey>(
        Dictionary<TKey, HashSet<string>> values) where TKey : notnull =>
        new ReadOnlyDictionary<TKey, IReadOnlyList<string>>(values.ToDictionary(value => value.Key,
            value => (IReadOnlyList<string>)Array.AsReadOnly(value.Value.Order(StringComparer.Ordinal).ToArray())));

    private static IReadOnlyDictionary<string, PreparedApplicationObjectDeclaration> ReadOnly(
        Dictionary<string, PreparedApplicationObjectDeclaration> values) =>
        new ReadOnlyDictionary<string, PreparedApplicationObjectDeclaration>(values);

    internal static string Key(string qualifiedId, int version) => qualifiedId + "@" + version;
}

internal readonly record struct ApplicationObjectComponentKey(string QualifiedTypeId, int TypeVersion);

internal sealed record PreparedApplicationObjectDeclaration(
    string QualifiedId,
    int Version,
    string ReadPerspectivesJson,
    RegisteredApplicationObjectContract Contract)
{
    public string Key => PreparedApplicationObjectDependencyIndex.Key(QualifiedId, Version);
}
