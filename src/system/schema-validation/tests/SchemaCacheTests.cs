using System.Runtime.CompilerServices;
using System.Text.Json;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

public sealed class SchemaCacheTests
{
    [Fact]
    public void Identical_schemas_reuse_compilation_but_values_and_changed_schemas_are_rechecked()
    {
        var validator = new BoundedJsonSchemaValidator();
        const string first = "{\"type\":\"integer\",\"minimum\":2}";
        const string changed = "{\"type\":\"integer\",\"minimum\":3}";
        var compiled = validator.Compile(first);

        Assert.Same(compiled, validator.Compile(first));
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(first, "2").Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(first, "1").Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(changed, "2").Status);
        Assert.NotEqual(compiled.SchemaHash, validator.Compile(changed).SchemaHash);
        Assert.Equal(2, validator.CacheUsage.Count);
    }

    [Fact]
    public void Profile_requirements_and_failure_results_are_never_reused_as_success()
    {
        var validator = new BoundedJsonSchemaValidator();
        const string schema = "{\"type\":\"string\",\"format\":\"date-time\"}";
        const string value = "\"2026-09-05T10:00:00Z\"";
        Assert.Equal(SchemaValueStatus.Valid,
            validator.Validate(SystemJsonSchemaProfile.Version2Id, schema, value).Status);
        Assert.Equal(SchemaValueStatus.Rejected,
            validator.Validate(SystemJsonSchemaProfile.Version1Id, schema, value).Status);
        Assert.Equal(SchemaValueStatus.Invalid,
            validator.Validate(SystemJsonSchemaProfile.Version2Id, schema, "\"yesterday\"").Status);
        var usage = validator.CacheUsage;
        Assert.False(validator.Compile("not-json").IsAccepted);
        Assert.False(validator.Compile("{\"$ref\":\"https://example.invalid/remote\"}").IsAccepted);
        Assert.Equal(SchemaValueStatus.Rejected,
            validator.Validate(schema, new string(' ', SystemJsonSchemaProfile.MaximumValueBytes + 1)).Status);
        // The default profile is a separate compilation; invalid schemas never enter the cache.
        Assert.Equal(usage.Count + 1, validator.CacheUsage.Count);
    }

    [Fact]
    public async Task Concurrent_reads_share_compilation_and_keep_validation_results_independent()
    {
        var validator = new BoundedJsonSchemaValidator();
        const string schema = "{\"$defs\":{\"value\":{\"type\":\"integer\"}},\"$ref\":\"#/$defs/value\"}";
        var tasks = Enumerable.Range(0, 64).Select(index => Task.Run(() =>
        {
            var compilation = validator.Compile(schema);
            Assert.Equal(index % 2 == 0 ? SchemaValueStatus.Valid : SchemaValueStatus.Invalid,
                validator.Validate(schema, index % 2 == 0 ? "2" : "\"2\"").Status);
            return compilation;
        }));
        var results = await Task.WhenAll(tasks);
        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.Equal(1, validator.CacheUsage.Count);
    }

    [Fact]
    public async Task Concurrent_distinct_misses_compile_safely_and_remain_within_the_shared_bounds()
    {
        var validator = new BoundedJsonSchemaValidator();
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 64).Select(index => Task.Run(() =>
        {
            start.Wait();
            var schema = JsonSerializer.Serialize(new
            {
                type = "object",
                properties = Enumerable.Range(0, 32).ToDictionary(
                    property => $"p{property}", _ => new { type = "integer", minimum = index })
            });
            return validator.Compile(schema);
        })).ToArray();

        start.Set();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.True(result.IsAccepted));
        Assert.Equal(64, results.Select(value => value.SchemaHash).Distinct(StringComparer.Ordinal).Count());
        Assert.InRange(validator.CacheUsage.Count, 1, BoundedJsonSchemaValidator.MaximumCachedSchemas);
        Assert.InRange(validator.CacheUsage.TextBytes, 1, BoundedJsonSchemaValidator.MaximumCachedTextBytes);
        Assert.InRange(validator.CacheUsage.Nodes, 1, BoundedJsonSchemaValidator.MaximumCachedSchemaNodes);
    }

    [Fact]
    public void Cache_evicts_least_recently_used_entries_and_releases_them()
    {
        var validator = new BoundedJsonSchemaValidator();
        var evicted = FillCacheAndEvictFirst(validator);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(evicted.IsAlive);
        Assert.Equal(BoundedJsonSchemaValidator.MaximumCachedSchemas, validator.CacheUsage.Count);
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(Schema(0), "0").Status);
        Assert.InRange(validator.CacheUsage.Count, 1, BoundedJsonSchemaValidator.MaximumCachedSchemas);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference FillCacheAndEvictFirst(BoundedJsonSchemaValidator validator)
    {
        var first = new WeakReference(validator.Compile(Schema(0)));
        var hot = validator.Compile(Schema(1));
        for (var index = 2; index <= BoundedJsonSchemaValidator.MaximumCachedSchemas; index++)
        {
            Assert.Same(hot, validator.Compile(Schema(1)));
            Assert.True(validator.Compile(Schema(index)).IsAccepted);
        }
        Assert.Same(hot, validator.Compile(Schema(1)));
        return first;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cache_also_bounds_large_schema_text_and_structural_complexity(bool manyNodes)
    {
        var validator = new BoundedJsonSchemaValidator();
        for (var index = 0; index < 64; index++)
        {
            var schema = manyNodes
                ? JsonSerializer.Serialize(new { type = "object", title = index.ToString(),
                    properties = Enumerable.Range(0, 500).ToDictionary(value => "p" + value,
                        _ => new { type = "integer" }) })
                : JsonSerializer.Serialize(new { type = "integer", minimum = index,
                    description = new string('x', 60_000) });
            Assert.True(validator.Compile(schema).IsAccepted);
        }
        var usage = validator.CacheUsage;
        Assert.InRange(usage.Count, 1, 63);
        Assert.InRange(usage.TextBytes, 1, BoundedJsonSchemaValidator.MaximumCachedTextBytes);
        Assert.InRange(usage.Nodes, 1, BoundedJsonSchemaValidator.MaximumCachedSchemaNodes);
    }

    private static string Schema(int minimum) => $"{{\"type\":\"integer\",\"minimum\":{minimum}}}";
}
