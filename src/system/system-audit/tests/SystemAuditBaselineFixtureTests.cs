using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace DantesRoleplay.SystemAudit.Tests;

[Collection("System audit performance")]
public sealed class SystemAuditBaselineFixtureTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, 2_638)]
    [InlineData(true, 5_276)]
    public async Task Disposable_fixture_preserves_observed_collections_while_unrelated_population_doubles(
        bool includeUnrelatedPopulation,
        int expectedEntities)
    {
        using var fixture = await SystemAuditBaselineFixture.CreateAsync(includeUnrelatedPopulation);
        await using var db = fixture.CreateContext();

        Assert.Equal(expectedEntities, await db.Set<ApplicationEcsEntityRecord>().CountAsync());
        Assert.Equal(259, await CountKindAsync(db, "location"));
        Assert.Equal(124, await CountKindAsync(db, "person"));
        Assert.Equal(35, await CountKindAsync(db, "faction"));
        Assert.Equal(1, await CountKindAsync(db, "campaign"));
        Assert.Equal(includeUnrelatedPopulation ? 2_638 : 0,
            await db.Set<ApplicationEcsEntityRecord>().CountAsync(value =>
                EF.Functions.Like(value.Id, "unrelated.%")));
    }

    [Fact]
    public async Task Fixture_measurement_reports_cold_and_warm_sql_duration_and_allocations_without_source_reads()
    {
        using var fixture = await SystemAuditBaselineFixture.CreateAsync(includeUnrelatedPopulation: true);
        var counter = new CommandCounter();
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite(fixture.Connection)
            .AddInterceptors(counter)
            .Options;

        await using var db = new DantesRoleplayDbContext(options);
        var cold = await MeasureAsync(db, counter);
        var warm = await MeasureAsync(db, counter);

        Assert.Equal(1, cold.SqlCommands);
        Assert.Equal(1, warm.SqlCommands);
        Assert.Equal(419, cold.Rows);
        Assert.Equal(cold.Rows, warm.Rows);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "dantesroleplay.system-audit-fixture-measurement.v1",
            profile = "observed-plus-unrelated-1x",
            entities = 5_276,
            sourceFileReads = 0,
            sourceFileReadReason = "The disposable scaling fixture is database-only by construction.",
            cold,
            warm
        }));
    }

    [Fact]
    public async Task Component_selected_pages_are_constant_sql_and_unrelated_catalog_growth_stays_bounded()
    {
        using var observed = await SystemAuditBaselineFixture.CreateAsync(includeUnrelatedPopulation: false);
        using var expanded = await SystemAuditBaselineFixture.CreateAsync(includeUnrelatedPopulation: true);
        var observedProfile = CreateSelectionProfile(observed);
        var expandedProfile = CreateSelectionProfile(expanded);
        await using var observedDb = observedProfile.Db;
        await using var expandedDb = expandedProfile.Db;

        var observedSamples = new List<Measurement>();
        var expandedSamples = new List<Measurement>();
        for (var sample = 0; sample < 25; sample++)
        {
            observedSamples.Add(await MeasureSelectionAsync(observedProfile));
            expandedSamples.Add(await MeasureSelectionAsync(expandedProfile));
        }
        var observedMedian = Median(observedSamples.Skip(5).ToArray());
        var expandedMedian = Median(expandedSamples.Skip(5).ToArray());
        var legacySamples = new List<Measurement>();
        for (var sample = 0; sample < 20; sample++)
            legacySamples.Add(await MeasureLegacyEntityScanAsync(observedProfile));
        var legacyMedian = Median(legacySamples);

        Assert.All(observedSamples, value => Assert.InRange(value.SqlCommands, 1, 2));
        Assert.All(expandedSamples, value => Assert.InRange(value.SqlCommands, 1, 2));
        Assert.All(observedSamples, value => Assert.Equal(419, value.Rows));
        Assert.All(expandedSamples, value => Assert.Equal(419, value.Rows));
        Assert.True(expandedMedian.DurationMs <= observedMedian.DurationMs * 1.10,
            $"Expanded median {expandedMedian.DurationMs:F3} ms exceeded observed median {observedMedian.DurationMs:F3} ms by more than 10%.");
        Assert.True(expandedMedian.AllocatedBytes <= observedMedian.AllocatedBytes * 1.10,
            $"Expanded median allocation {expandedMedian.AllocatedBytes} exceeded observed {observedMedian.AllocatedBytes} by more than 10%.");
        Assert.True(observedMedian.DurationMs <= legacyMedian.DurationMs * 0.50,
            $"Selected median {observedMedian.DurationMs:F3} ms did not improve the legacy scan median {legacyMedian.DurationMs:F3} ms by at least 50%.");
        output.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "dantesroleplay.system-audit-component-selection.v1",
            samplesPerProfile = 20,
            sourceFileReads = 0,
            legacyEntityScan = legacyMedian,
            observed = observedMedian,
            expanded = expandedMedian
        }));
    }

    private static Task<int> CountKindAsync(DantesRoleplayDbContext db, string kind) =>
        db.Set<ApplicationEcsComponentRecord>().CountAsync(value =>
            value.QualifiedTypeId == SystemAuditBaselineFixture.KindTypeId &&
            value.Data == $"{{\"kind\":\"{kind}\"}}");

    private static async Task<Measurement> MeasureAsync(DantesRoleplayDbContext db, CommandCounter counter)
    {
        db.ChangeTracker.Clear();
        counter.Reset();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        var rows = await db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
            .Where(value => value.QualifiedTypeId == SystemAuditBaselineFixture.KindTypeId)
            .OrderBy(value => value.EntityId)
            .Select(value => new { value.EntityId, value.Data })
            .ToArrayAsync();
        timer.Stop();
        return new(rows.Length, counter.Count, timer.Elapsed.TotalMilliseconds,
            GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore);
    }

    private static SelectionProfile CreateSelectionProfile(SqliteFixture fixture)
    {
        var counter = new CommandCounter();
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite(fixture.Connection).AddInterceptors(counter).Options;
        var db = new DantesRoleplayDbContext(options);
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        return new(db, counter, new SqliteEntityComponentStore(db, types, schemas));
    }

    private static async Task<Measurement> MeasureSelectionAsync(SelectionProfile profile)
    {
        profile.Db.ChangeTracker.Clear();
        profile.Counter.Reset();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        var page = await profile.Store.SelectAsync(SystemAuditBaselineFixture.StateSpaceId,
            new([SystemAuditBaselineFixture.KindTypeId], [SystemAuditBaselineFixture.KindTypeId], null, 1_000));
        timer.Stop();
        return new(page.Entities.Count, profile.Counter.Count, timer.Elapsed.TotalMilliseconds,
            GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore);
    }

    private static async Task<Measurement> MeasureLegacyEntityScanAsync(SelectionProfile profile)
    {
        profile.Db.ChangeTracker.Clear();
        profile.Counter.Reset();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        var rows = 0;
        string? cursor = null;
        do
        {
            var page = await profile.Store.ListEntitiesAsync(
                SystemAuditBaselineFixture.StateSpaceId, cursor, 100);
            foreach (var entity in page.Entities)
                if (await profile.Store.GetComponentAsync(SystemAuditBaselineFixture.StateSpaceId,
                        entity.EntityId, SystemAuditBaselineFixture.KindTypeId) is not null) rows++;
            cursor = page.NextEntityId;
        } while (cursor is not null);
        timer.Stop();
        return new(rows, profile.Counter.Count, timer.Elapsed.TotalMilliseconds,
            GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore);
    }

    private static Measurement Median(IReadOnlyList<Measurement> values)
    {
        var duration = values.Select(value => value.DurationMs).Order().ToArray();
        var allocation = values.Select(value => value.AllocatedBytes).Order().ToArray();
        return new(values[0].Rows, values.Max(value => value.SqlCommands),
            duration[duration.Length / 2], allocation[allocation.Length / 2]);
    }

    private sealed record Measurement(int Rows, int SqlCommands, double DurationMs, long AllocatedBytes);
    private sealed record SelectionProfile(
        DantesRoleplayDbContext Db,
        CommandCounter Counter,
        SqliteEntityComponentStore Store);

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public void Reset() => Count = 0;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Count++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}

[CollectionDefinition("System audit performance", DisableParallelization = true)]
public sealed class SystemAuditPerformanceCollection;

internal static class SystemAuditBaselineFixture
{
    public const string KindTypeId = "system-audit-fixture.kind";
    public const string StateSpaceId = "system-audit-fixture-main";

    public static async Task<SqliteFixture> CreateAsync(bool includeUnrelatedPopulation)
    {
        var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var application = ApplicationIdentifier.Parse("system-audit-fixture");
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(application, "System audit fixture", "Disposable scaling evidence.", []));
        new SqliteStateSpaceRegistry(db, applications).Create(new(StateSpaceId, revision, new('A', 64)));
        var type = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator()).Define(new(
            application,
            KindTypeId,
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"kind\"],\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"campaign\",\"location\",\"person\",\"faction\"]}}}"));

        var now = DateTime.UtcNow;
        var entities = new List<ApplicationEcsEntityRecord>(includeUnrelatedPopulation ? 5_276 : 2_638);
        var components = new List<ApplicationEcsComponentRecord>(419);
        AddKind("campaign", 1);
        AddKind("location", 259);
        AddKind("person", 124);
        AddKind("faction", 35);
        AddEntities("observed.other", 2_219);
        if (includeUnrelatedPopulation) AddEntities("unrelated", 2_638);

        db.Set<ApplicationEcsEntityRecord>().AddRange(entities);
        db.Set<ApplicationEcsComponentRecord>().AddRange(components);
        await db.SaveChangesAsync();
        return fixture;

        void AddKind(string kind, int count)
        {
            for (var index = 1; index <= count; index++)
            {
                var id = $"{kind}.{index:D4}";
                entities.Add(Entity(id, $"{kind} {index}"));
                components.Add(new()
                {
                    StateSpaceId = StateSpaceId,
                    EntityId = id,
                    QualifiedTypeId = type.QualifiedId,
                    TypeVersion = type.Version,
                    SchemaHash = type.SchemaHash,
                    Data = $"{{\"kind\":\"{kind}\"}}",
                    Revision = 1,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
        }

        void AddEntities(string prefix, int count)
        {
            for (var index = 1; index <= count; index++)
                entities.Add(Entity($"{prefix}.{index:D4}", $"{prefix} {index}"));
        }

        ApplicationEcsEntityRecord Entity(string id, string name) => new()
        {
            StateSpaceId = StateSpaceId,
            Id = id,
            Name = name,
            Revision = 1,
            CreatedAtUtc = now
        };
    }
}
