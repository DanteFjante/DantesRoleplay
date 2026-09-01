using System.Text.Json;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.Tools.Commands;

public sealed record EcsComponentTypeMigrationPlanEntry(
    string SourceQualifiedTypeId,
    string TargetQualifiedTypeId,
    IReadOnlyList<EcsComponentMigrationValue>? RewrittenValues = null);

public sealed record EcsRelationshipKindMigrationPlanEntry(
    string SourceQualifiedKind,
    string TargetQualifiedKind);

public sealed record EcsIdentityMigrationPlan(
    IReadOnlyList<EcsComponentTypeMigrationPlanEntry> ComponentTypes,
    IReadOnlyList<EcsRelationshipKindMigrationPlanEntry> RelationshipKinds);

/// <summary>
/// Operator boundary for reviewed live ECS identity migrations. All state changes are delegated
/// to the generic lifecycle store; the tool only parses the reviewed plan and creates a
/// consistent backup before the first mutation.
/// </summary>
public sealed class MigrateEcsIdentitiesTool : ITool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public string Name => "migrate-ecs-identities";
    public string Summary => "Inspect or apply reviewed live ECS component and relationship identity migrations.";
    public string Usage => """
        roleplay migrate-ecs-identities <plan.json> [--apply] [--database <path>]

        The plan contains componentTypes and relationshipKinds. Component entries identify an
        existing source type, an existing canonical target type, and optional exact revision-bound
        rewrittenValues. Relationship entries rename one qualified kind everywhere it is used.

        Without --apply, reports source references and target availability without changing the
        database. With --apply, creates a consistent sibling backups/ database copy, then delegates
        every mutation to the transactional ECS lifecycle store. A failed entry stops the run and
        leaves the backup available for operator recovery.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Count != 1)
        {
            context.Error.WriteLine("migrate-ecs-identities needs one reviewed plan file.");
            return 2;
        }

        var path = Path.GetFullPath(context.Arguments[0]);
        if (!File.Exists(path)) throw new FileNotFoundException("ECS identity migration plan not found.", path);
        var plan = JsonSerializer.Deserialize<EcsIdentityMigrationPlan>(
            await File.ReadAllTextAsync(path, cancellationToken), Json)
            ?? throw new InvalidOperationException("The ECS identity migration plan is empty.");
        plan = plan with
        {
            ComponentTypes = plan.ComponentTypes ?? [],
            RelationshipKinds = plan.RelationshipKinds ?? []
        };
        Validate(plan);

        await using var db = context.OpenDatabase();
        var lifecycle = new SqliteEcsLifecycleStore(db, schemas: new BoundedJsonSchemaValidator());
        if (!context.HasFlag("apply"))
        {
            foreach (var entry in plan.ComponentTypes)
            {
                var source = await lifecycle.GetComponentTypeAsync(entry.SourceQualifiedTypeId, cancellationToken);
                var target = await lifecycle.GetComponentTypeAsync(entry.TargetQualifiedTypeId, cancellationToken);
                var references = source?.References.SingleOrDefault(value => value.Kind == "components")?.Count ?? 0;
                context.Out.WriteLine(
                    $"component {entry.SourceQualifiedTypeId} -> {entry.TargetQualifiedTypeId}: "
                    + $"{references} live value(s), target {(target is { IsEnabled: true } ? "ready" : "missing or disabled")}");
            }
            foreach (var entry in plan.RelationshipKinds)
            {
                var source = await lifecycle.GetRelationshipKindAsync(entry.SourceQualifiedKind, cancellationToken);
                var target = await lifecycle.GetRelationshipKindAsync(entry.TargetQualifiedKind, cancellationToken);
                context.Out.WriteLine(
                    $"relationship {entry.SourceQualifiedKind} -> {entry.TargetQualifiedKind}: "
                    + $"{source.References} live edge(s), {target.References} target collision candidate(s)");
            }
            context.Out.WriteLine("Dry run only; add --apply to create a backup and run the lifecycle migrations.");
            return 0;
        }

        await db.DisposeAsync();
        var backup = Backup(context.DatabasePath);
        context.Out.WriteLine($"Backup: {backup}");

        await using var writable = context.OpenDatabase();
        lifecycle = new SqliteEcsLifecycleStore(writable, schemas: new BoundedJsonSchemaValidator());
        foreach (var entry in plan.ComponentTypes)
        {
            var source = await lifecycle.GetComponentTypeAsync(entry.SourceQualifiedTypeId, cancellationToken);
            var sourceComponents = source?.References.SingleOrDefault(
                value => value.Kind == "components")?.Count ?? 0;
            if ((source is null || !source.IsEnabled) && sourceComponents == 0)
            {
                var target = await lifecycle.GetComponentTypeAsync(entry.TargetQualifiedTypeId, cancellationToken);
                if (target is not { IsEnabled: true })
                    throw new InvalidOperationException(
                        $"Previously retired source '{entry.SourceQualifiedTypeId}' has no enabled target.");
                context.Out.WriteLine(
                    $"Already migrated: {entry.SourceQualifiedTypeId} -> {entry.TargetQualifiedTypeId}.");
                continue;
            }
            var result = await lifecycle.MigrateComponentTypeAsync(
                entry.SourceQualifiedTypeId, entry.TargetQualifiedTypeId,
                entry.RewrittenValues, cancellationToken);
            context.Out.WriteLine(
                $"Migrated {result.MigratedComponents} component(s): "
                + $"{entry.SourceQualifiedTypeId} -> {entry.TargetQualifiedTypeId}.");
        }
        foreach (var entry in plan.RelationshipKinds)
        {
            var result = await lifecycle.MigrateRelationshipKindAsync(
                entry.SourceQualifiedKind, entry.TargetQualifiedKind, cancellationToken);
            context.Out.WriteLine(
                $"Migrated {result.MigratedRelationships} relationship(s): "
                + $"{entry.SourceQualifiedKind} -> {entry.TargetQualifiedKind}.");
        }
        return 0;
    }

    private static void Validate(EcsIdentityMigrationPlan plan)
    {
        if (plan.ComponentTypes.Count + plan.RelationshipKinds.Count is < 1 or > 512)
            throw new InvalidOperationException("An ECS identity migration needs 1 through 512 entries.");
        if (plan.ComponentTypes.Any(value => value.SourceQualifiedTypeId == value.TargetQualifiedTypeId)
            || plan.RelationshipKinds.Any(value => value.SourceQualifiedKind == value.TargetQualifiedKind))
            throw new InvalidOperationException("Every ECS identity migration must change the qualified ID.");
        if (plan.ComponentTypes.GroupBy(value => value.SourceQualifiedTypeId, StringComparer.Ordinal).Any(group => group.Count() > 1)
            || plan.RelationshipKinds.GroupBy(value => value.SourceQualifiedKind, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Each source ECS identity may appear only once.");
        if (plan.ComponentTypes.Sum(value => value.RewrittenValues?.Count ?? 0) > 10_000)
            throw new InvalidOperationException("The migration contains too many rewritten component values.");
    }

    private static string Backup(string databasePath)
    {
        var directory = Path.Combine(Path.GetDirectoryName(databasePath)!, "backups");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,
            $"{Path.GetFileNameWithoutExtension(databasePath)}-before-ecs-identity-migration-"
            + $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.db");
        using var source = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        using var target = new SqliteConnection($"Data Source={path};Mode=ReadWriteCreate");
        source.Open();
        target.Open();
        source.BackupDatabase(target);
        return path;
    }
}
