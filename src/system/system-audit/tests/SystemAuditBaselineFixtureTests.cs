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

    private sealed record Measurement(int Rows, int SqlCommands, double DurationMs, long AllocatedBytes);

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

internal static class SystemAuditBaselineFixture
{
    public const string KindTypeId = "system-audit-fixture.kind";
    private const string StateSpaceId = "system-audit-fixture-main";

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
