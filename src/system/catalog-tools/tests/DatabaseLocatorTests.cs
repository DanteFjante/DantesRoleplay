using System.Text.Json;
using DantesRoleplay.Tools;

namespace DantesRoleplay.Tests;

public sealed class DatabaseLocatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"database-selection-{Guid.NewGuid():N}");

    [Fact]
    public void Saved_selection_resolves_the_installed_database_without_a_legacy_copy()
    {
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "installed.db");
        File.WriteAllText(database, "selected database");
        WriteProfile(new { schemaVersion = 1, database });
        Assert.Equal(database, DatabaseLocator.ResolveSavedDatabase(root));
    }

    [Fact]
    public void Missing_or_malformed_selection_cannot_fall_back_to_a_different_game()
    {
        Assert.Null(DatabaseLocator.ResolveSavedDatabase(root));
        var legacy = Path.Combine(root, "DantesRoleplay.MCPServer", "data", "dantesroleplay.db");
        WriteProfile(new { schemaVersion = 1, database = Path.Combine(root, "missing.db") });
        File.WriteAllText(legacy, "preserve legacy game");
        Assert.Throws<InvalidOperationException>(() => DatabaseLocator.ResolveSavedDatabase(root));
        WriteProfile(new { database = legacy });
        Assert.Throws<InvalidOperationException>(() => DatabaseLocator.ResolveSavedDatabase(root));
        Assert.Equal("preserve legacy game", File.ReadAllText(legacy));
    }

    private void WriteProfile(object value)
    {
        var path = Path.Combine(root, "DantesRoleplay.MCPServer", "data", "runtime-launch.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
