using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: CaldrisBootstrapTool <database> <manifest> <backup>");
    return 2;
}

var databasePath = Path.GetFullPath(args[0]);
var manifestPath = Path.GetFullPath(args[1]);
var backupPath = Path.GetFullPath(args[2]);
if (!File.Exists(databasePath) || !File.Exists(manifestPath))
{
    Console.Error.WriteLine("The database and manifest must already exist.");
    return 2;
}
if (File.Exists(backupPath))
{
    Console.Error.WriteLine("The backup target already exists; refusing to overwrite it.");
    return 2;
}

var manifestBytes = await File.ReadAllBytesAsync(manifestPath);
var manifest = JsonSerializer.Deserialize<BootstrapManifest>(manifestBytes,
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException("The bootstrap manifest is empty.");
ValidateManifest(manifest);

Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
await using (var source = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False"))
await using (var destination = new SqliteConnection($"Data Source={backupPath};Mode=ReadWriteCreate;Pooling=False"))
{
    await source.OpenAsync();
    await destination.OpenAsync();
    source.BackupDatabase(destination);
    await using var check = destination.CreateCommand();
    check.CommandText = "PRAGMA quick_check;";
    if (!string.Equals((string?)await check.ExecuteScalarAsync(), "ok", StringComparison.Ordinal))
        throw new InvalidOperationException("The completed SQLite backup did not pass quick_check.");
}
Console.WriteLine($"Backup: {backupPath} (quick_check=ok)");

var services = new ServiceCollection();
services.AddLogging();
services.AddDantesRoleplayDataAccess(databasePath, DatabaseProvider.Sqlite);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var stateSpaces = scope.ServiceProvider.GetRequiredService<IStateSpaceRegistry>();
var stateSpace = stateSpaces.Get(manifest.StateSpaceId)
    ?? throw new InvalidOperationException("The target state space is unknown.");
if (stateSpace.ApplicationRevision.ApplicationId != ApplicationIdentifier.Parse(manifest.ApplicationId))
    throw new InvalidOperationException("The state space belongs to another application.");

var types = scope.ServiceProvider.GetRequiredService<IApplicationComponentTypeRegistry>();
var effects = new List<ApplicationEcsEffect>();
effects.AddRange(manifest.Entities.Select(entity => new ApplicationEcsEffect
{
    Type = ApplicationEcsEffectType.EntityCreate,
    EntityId = entity.EntityId,
    Name = entity.Name
}));
foreach (var entity in manifest.Entities)
foreach (var component in entity.Components)
{
    var installed = types.GetLatest(component.QualifiedTypeId)
        ?? throw new InvalidOperationException($"Unknown component type: {component.QualifiedTypeId}");
    effects.Add(new ApplicationEcsEffect
    {
        Type = ApplicationEcsEffectType.ComponentAdd,
        EntityId = entity.EntityId,
        ComponentType = new EcsComponentReference(installed.QualifiedId, installed.Version, installed.SchemaHash),
        DataJson = component.Value.GetRawText(),
        ExpectedRevision = 0
    });
}
effects.AddRange(manifest.Entities.Where(entity => entity.ContainerEntityId is not null).Select(entity =>
    new ApplicationEcsEffect
    {
        Type = ApplicationEcsEffectType.ContainmentMove,
        EntityId = entity.EntityId,
        TargetEntityId = entity.ContainerEntityId!,
        Slot = entity.Slot!,
        ExpectedRevision = 0
    }));
effects.AddRange(manifest.Relationships.Select(relationship => new ApplicationEcsEffect
{
    Type = ApplicationEcsEffectType.RelationshipSet,
    EntityId = relationship.FromEntityId,
    TargetEntityId = relationship.ToEntityId,
    QualifiedRelationshipKind = relationship.QualifiedKind,
    DataJson = relationship.Value.GetRawText(),
    ExpectedRevision = 0
}));

var fingerprint = Convert.ToHexString(SHA256.HashData(manifestBytes));
var applier = scope.ServiceProvider.GetRequiredService<IApplicationEcsEffectApplier>();
var preview = await applier.ApplyAsync(Batch("preview"), dryRun: true);
Print("Preview", preview);
if (!preview.Valid) return 1;
var commit = await applier.ApplyAsync(Batch("commit"));
Print("Commit", commit);
return commit.Applied || commit.Replayed ? 0 : 1;

ApplicationEcsEffectBatch Batch(string phase)
{
    var phaseHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{fingerprint}:{phase}")));
    return new ApplicationEcsEffectBatch
    {
        StateSpaceId = manifest.StateSpaceId,
        Effects = effects,
        Intent = manifest.Intent,
        ProceduresUsed = manifest.ProceduresUsed,
        ExecutionIdentity = new ApplicationEcsExecutionIdentity(phaseHash[..32].ToLowerInvariant(), fingerprint)
    };
}

static void Print(string label, ApplicationEcsEffectResult result)
{
    Console.WriteLine($"{label}: valid={result.Valid} applied={result.Applied} replayed={result.Replayed} effects={result.Receipts.Count} operation={result.OperationId}");
    foreach (var problem in result.Problems)
        Console.Error.WriteLine($"  {problem.Index}: {problem.Code} — {problem.Message}");
}

static void ValidateManifest(BootstrapManifest manifest)
{
    if (string.IsNullOrWhiteSpace(manifest.ApplicationId) || string.IsNullOrWhiteSpace(manifest.StateSpaceId)
        || manifest.Entities.Count is < 2 or > 64 || manifest.Relationships.Count > 64)
        throw new InvalidOperationException("The manifest boundary is invalid.");
    var ids = manifest.Entities.Select(entity => entity.EntityId).ToHashSet(StringComparer.Ordinal);
    if (ids.Count != manifest.Entities.Count || manifest.Entities.Any(entity =>
            string.IsNullOrWhiteSpace(entity.EntityId) || string.IsNullOrWhiteSpace(entity.Name)
            || (entity.ContainerEntityId is null) != (entity.Slot is null)
            || entity.Components.Count == 0))
        throw new InvalidOperationException("The entity manifest is invalid.");
    if (manifest.Entities.Any(entity => entity.ContainerEntityId is not null && !ids.Contains(entity.ContainerEntityId)))
        throw new InvalidOperationException("Every containment target must be part of this closed bootstrap.");
    var prefix = manifest.ApplicationId + ".";
    if (manifest.Entities.SelectMany(entity => entity.Components).Any(component =>
            !component.QualifiedTypeId.StartsWith(prefix, StringComparison.Ordinal))
        || manifest.Relationships.Any(relationship =>
            !ids.Contains(relationship.FromEntityId) || !ids.Contains(relationship.ToEntityId)
            || !relationship.QualifiedKind.StartsWith(prefix, StringComparison.Ordinal)))
        throw new InvalidOperationException("Components and relationships must belong to this application and closed graph.");
}

internal sealed record BootstrapManifest(
    string ApplicationId,
    string StateSpaceId,
    string Intent,
    IReadOnlyList<string> ProceduresUsed,
    IReadOnlyList<BootstrapEntity> Entities,
    IReadOnlyList<BootstrapRelationship> Relationships);
internal sealed record BootstrapEntity(
    string EntityId,
    string Name,
    string? ContainerEntityId,
    string? Slot,
    IReadOnlyList<BootstrapComponent> Components);
internal sealed record BootstrapComponent(string QualifiedTypeId, JsonElement Value);
internal sealed record BootstrapRelationship(
    string FromEntityId,
    string ToEntityId,
    string QualifiedKind,
    JsonElement Value);
