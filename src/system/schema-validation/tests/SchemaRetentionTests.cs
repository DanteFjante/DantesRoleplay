using System.Runtime.CompilerServices;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Tests;

[CollectionDefinition("Schema retention", DisableParallelization = true)]
public sealed class SchemaRetentionCollection;

[Collection("Schema retention")]
public sealed class SchemaRetentionTests
{
    [Fact]
    public void Repeated_compilation_and_validation_do_not_retain_schema_graphs()
    {
        // Run alone so concurrent tests cannot affect the live-heap measurement.
        ExerciseValidator(64);
        var before = CollectRetainedBytes();
        ExerciseValidator(2_000);
        var retained = CollectRetainedBytes() - before;

        Assert.True(retained < 1_048_576,
            $"Repeated validation retained {retained:N0} bytes after full collection.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ExerciseValidator(int count)
    {
        const string schema = """
            {"$schema":"https://json-schema.org/draft/2020-12/schema",
             "$defs":{"name":{"type":"string","minLength":1}},
             "type":"object","additionalProperties":false,"required":["name"],
             "properties":{"name":{"$ref":"#/$defs/name"}}}
            """;
        var validator = new BoundedJsonSchemaValidator();
        for (var index = 0; index < count; index++)
        {
            var compilation = validator.Compile(schema);
            Assert.True(compilation.IsAccepted);
            Assert.Equal(SchemaValueStatus.Valid,
                validator.Validate(compilation.NormalizedSchema, "{\"name\":\"Ada\"}").Status);
            Assert.Equal(SchemaValueStatus.Invalid,
                validator.Validate(compilation.NormalizedSchema, "{\"name\":12}").Status);
        }
    }

    private static long CollectRetainedBytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
