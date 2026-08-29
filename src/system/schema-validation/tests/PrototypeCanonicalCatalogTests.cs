using DantesRoleplay.SchemaValidation;
using System.Runtime.CompilerServices;

namespace DantesRoleplay.SchemaValidation.Tests;

public sealed class PrototypeCanonicalCatalogTests
{
    [Fact]
    public async Task Every_active_prototype_component_schema_compiles_in_the_generic_bounded_profile()
    {
        var root = Path.Combine(RepositoryRoot(), "catalog", "applications", "dnd2024");
        var components = Path.Combine(root, "components");
        Assert.True(Directory.Exists(components), "The prototype canonical D&D source is missing.");

        var schemas = Directory.EnumerateFiles(components, "*.schema.json", SearchOption.TopDirectoryOnly)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        Assert.Equal(154, schemas.Length);

        var validator = new BoundedJsonSchemaValidator();
        foreach (var schema in schemas)
        {
            var result = validator.Compile(await File.ReadAllTextAsync(schema));
            Assert.True(result.IsAccepted, $"{Path.GetFileName(schema)}: " +
                string.Join("; ", result.Diagnostics.Select(value => value.Code + " " + value.Pointer + " " + value.Message)));
        }
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        var starts = new[]
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
            Path.GetDirectoryName(sourceFile) ?? string.Empty
        };

        foreach (var start in starts)
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
