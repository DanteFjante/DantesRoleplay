using DantesRoleplay.DataAccess;
using DantesRoleplay.Information;
using Microsoft.EntityFrameworkCore;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.SchemaValidation;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class InformationStoreMetadataValidationTests
{
    [Fact]
    public void Production_registration_exposes_the_conditional_schema_bound_owner()
    {
        var services = new ServiceCollection();
        services.AddDantesRoleplayDataAccess("Filename=:memory:");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<SqliteConditionalInformationStore>(
            scope.ServiceProvider.GetRequiredService<IConditionalInformationStore>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<InformationStore>(),
            scope.ServiceProvider.GetRequiredService<IInformationStore>());
    }

    [Fact]
    public async Task Production_registration_reuses_the_singleton_schema_graph_across_store_scopes()
    {
        using var fixture = new SqliteFixture();
        var services = new ServiceCollection();
        services.AddScoped(_ => fixture.CreateContext());
        services.AddSchemaValidationComponent();
        services.AddInformationComponent();
        await using var provider = services.BuildServiceProvider();
        var validator = Assert.IsType<BoundedJsonSchemaValidator>(provider.GetRequiredService<IBoundedJsonSchemaValidator>());
        const string schema = "{\"type\":\"object\",\"required\":[\"rank\"]}";
        Assert.Equal(0, validator.CacheUsage.Count);
        using (var scope = provider.CreateScope())
        {
            var source = await scope.ServiceProvider.GetRequiredService<IInformationStore>()
                .WriteSourceAsync(new("source.one", "local.rules", "One", MetadataSchemaJson: schema));
            Assert.Equal("created", source.Status);
        }
        Assert.Equal(1, validator.CacheUsage.Count);
        var graph = validator.ObserveCachedSchemaGraph(schema).Target;
        using (var scope = provider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IInformationStore>();
            Assert.Same(validator, scope.ServiceProvider.GetRequiredService<IBoundedJsonSchemaValidator>());
            Assert.Equal("created", (await store.WriteSourceAsync(
                new("source.two", "local.rules", "Two", MetadataSchemaJson: schema))).Status);
            Assert.Equal("INFORMATION_RECORD_METADATA_INVALID", (await store.WriteRecordAsync(
                new("record.invalid", "source.two", "Invalid", "Content", "{}"))).ErrorCode);
        }
        Assert.Same(graph, validator.ObserveCachedSchemaGraph(schema).Target);
    }

    [Fact]
    public async Task Default_metadata_schema_accepts_an_empty_object()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new InformationStore(db);

        var source = await store.WriteSourceAsync(new("source.rules", "local.rules", "Rules"));
        var record = await store.WriteRecordAsync(new("record.empty", "source.rules", "Empty", "Content"));

        Assert.Equal("created", source.Status);
        Assert.Equal("created", record.Status);
        Assert.Equal("{}", record.Record!.MetadataJson);
    }

    [Fact]
    public async Task Unchanged_source_write_releases_its_writer_reservation()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new InformationStore(db);
        var request = new InformationSourceWriteRequest("source.rules", "local.rules", "Rules");

        await store.WriteSourceAsync(request);
        var unchanged = await store.WriteSourceAsync(request);
        var record = await store.WriteRecordAsync(new("record.after-unchanged", "source.rules", "After", "Content"));

        Assert.Equal("unchanged", unchanged.Status);
        Assert.Equal("created", record.Status);
    }

    [Fact]
    public async Task Record_metadata_must_satisfy_the_source_schema()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new InformationStore(db);
        const string schema = """
            {"type":"object","properties":{"rank":{"type":"integer","minimum":1}},"required":["rank"],"additionalProperties":false}
            """;
        await store.WriteSourceAsync(new("source.rules", "local.rules", "Rules", MetadataSchemaJson: schema));

        var rejected = await store.WriteRecordAsync(new("record.invalid", "source.rules", "Invalid", "Content", "{}"));
        var accepted = await store.WriteRecordAsync(new("record.valid", "source.rules", "Valid", "Content", "{\"rank\":1}"));

        Assert.Equal("rejected", rejected.Status);
        Assert.Equal("INFORMATION_RECORD_METADATA_INVALID", rejected.ErrorCode);
        Assert.Contains("VALUE_INVALID", rejected.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal("created", accepted.Status);
    }

    [Fact]
    public async Task Invalid_and_duplicate_metadata_json_are_rejected_before_write()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new InformationStore(db);
        await store.WriteSourceAsync(new("source.rules", "local.rules", "Rules"));

        var duplicate = await store.WriteRecordAsync(new("record.duplicate", "source.rules", "Duplicate", "Content", "{\"key\":1,\"key\":2}"));
        var deep = await store.WriteRecordAsync(new("record.deep", "source.rules", "Deep", "Content", DeepObject(65)));

        Assert.Equal("INVALID_INFORMATION_RECORD", duplicate.ErrorCode);
        Assert.Equal("INVALID_INFORMATION_RECORD", deep.ErrorCode);
        Assert.Empty(await db.Set<InformationRecord>().ToListAsync());
    }

    [Fact]
    public async Task Incompatible_source_schema_change_preserves_the_existing_source_and_records()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new InformationStore(db);
        var original = await store.WriteSourceAsync(new("source.rules", "local.rules", "Rules"));
        var record = await store.WriteRecordAsync(new("record.legacy", "source.rules", "Legacy", "Content", "{\"note\":\"kept\"}"));
        const string incompatibleSchema = """
            {"type":"object","properties":{"rank":{"type":"integer"}},"required":["rank"],"additionalProperties":false}
            """;

        var rejected = await store.WriteSourceAsync(new("source.rules", "local.rules", "Rules", MetadataSchemaJson: incompatibleSchema));
        db.ChangeTracker.Clear();
        var persistedSource = await db.Set<InformationSource>().SingleAsync(value => value.Id == "source.rules");
        var persistedRecord = await db.Set<InformationRecord>().SingleAsync(value => value.Id == "record.legacy");

        Assert.Equal("created", original.Status);
        Assert.Equal("created", record.Status);
        Assert.Equal("rejected", rejected.Status);
        Assert.Equal("INFORMATION_SOURCE_SCHEMA_INCOMPATIBLE", rejected.ErrorCode);
        Assert.Contains("record.legacy", rejected.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal("{}", persistedSource.MetadataSchemaJson);
        Assert.Equal(1, persistedSource.Revision);
        Assert.Equal("{\"note\":\"kept\"}", persistedRecord.MetadataJson);
        Assert.Equal(1, persistedRecord.Revision);
    }

    [Fact]
    public async Task Compatible_source_schema_change_is_published_and_becomes_the_current_record_constraint()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new InformationStore(db);
        await store.WriteSourceAsync(new("source.rules", "local.rules", "Rules"));
        await store.WriteRecordAsync(new("record.rank", "source.rules", "Rank", "Content", "{\"rank\":1}"));
        const string rankSchema = """
            {"type":"object","properties":{"rank":{"type":"integer","minimum":1}},"required":["rank"],"additionalProperties":false}
            """;

        var revised = await store.WriteSourceAsync(new("source.rules", "local.rules", "Rules", MetadataSchemaJson: rankSchema));
        var rejected = await store.WriteRecordAsync(new("record.after-change", "source.rules", "After", "Content", "{}"));

        Assert.Equal("revised", revised.Status);
        Assert.Equal(2, revised.Source!.Revision);
        Assert.Equal("INFORMATION_RECORD_METADATA_INVALID", rejected.ErrorCode);
    }

    [Fact]
    public async Task Record_write_uses_the_current_source_schema_when_another_context_changed_it()
    {
        using var fixture = new SqliteFixture();
        await using var staleDb = fixture.CreateContext();
        var staleStore = new InformationStore(staleDb);
        await staleStore.WriteSourceAsync(new("source.rules", "local.rules", "Rules"));

        await using (var currentDb = fixture.CreateContext())
        {
            var currentStore = new InformationStore(currentDb);
            var revised = await currentStore.WriteSourceAsync(new("source.rules", "local.rules", "Rules",
                MetadataSchemaJson: "{\"type\":\"object\",\"required\":[\"rank\"]}"));
            Assert.Equal("revised", revised.Status);
        }

        var rejected = await staleStore.WriteRecordAsync(new("record.stale", "source.rules", "Stale", "Content", "{}"));

        Assert.Equal("INFORMATION_RECORD_METADATA_INVALID", rejected.ErrorCode);
        Assert.Empty(await staleDb.Set<InformationRecord>().ToListAsync());
    }

    [Fact]
    public async Task Source_schema_is_validated_with_the_bounded_schema_validator()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new InformationStore(db);

        var unsupportedKeyword = await store.WriteSourceAsync(new("source.invalid", "local.rules", "Invalid",
            MetadataSchemaJson: "{\"type\":\"object\",\"contains\":{}}"));
        var duplicate = await store.WriteSourceAsync(new("source.duplicate", "local.rules", "Duplicate",
            MetadataSchemaJson: "{\"type\":\"object\",\"type\":\"string\"}"));

        Assert.Equal("INVALID_INFORMATION_SOURCE_SCHEMA", unsupportedKeyword.ErrorCode);
        Assert.Equal("INVALID_INFORMATION_SOURCE", duplicate.ErrorCode);
        Assert.Empty(await db.Set<InformationSource>().ToListAsync());
    }

    private static string DeepObject(int depth) => Enumerable.Range(0, depth)
        .Aggregate("0", (json, _) => "{\"next\":" + json + "}");

    [Fact]
    public async Task Compatibility_budget_exhaustion_preserves_source_and_all_records()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var store = new InformationStore(db);
        await store.WriteSourceAsync(new("source.rules", "local.rules", "Rules"));
        db.AddRange(Enumerable.Range(0, 501).Select(index => new InformationRecord
        {
            Id = $"record.{index:D3}", SourceId = "source.rules", Title = "Preserved",
            Content = "Original", MetadataJson = "{}", ContentHash = new string('A', 64), Revision = 1
        }));
        await db.SaveChangesAsync();

        var rejected = await store.WriteSourceAsync(new("source.rules", "local.rules", "Rules",
            MetadataSchemaJson: "{\"type\":\"object\"}"));

        Assert.Equal("INFORMATION_SOURCE_SCHEMA_VALIDATION_BUDGET_EXCEEDED", rejected.ErrorCode);
        Assert.Equal("{}", (await db.Set<InformationSource>().AsNoTracking().SingleAsync()).MetadataSchemaJson);
        Assert.Equal(501, await db.Set<InformationRecord>().CountAsync());
        Assert.All(await db.Set<InformationRecord>().AsNoTracking().ToArrayAsync(), record => Assert.Equal(1, record.Revision));
        Assert.Equal("created", (await store.WriteRecordAsync(new("record.after-budget", "source.rules", "After", "Content"))).Status);
    }
}
