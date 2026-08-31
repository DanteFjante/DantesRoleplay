namespace DantesRoleplay.Web.Interactions;

/// <summary>Loads reviewed browser-component assets without compiling application vocabulary into the host.</summary>
public static class BrowserComponentAssets
{
    public const int MaximumNameLength = 80;

    public static async Task<string?> ReadAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidName(name)) return null;
        var root = Path.Combine(AppContext.BaseDirectory, "BrowserComponents");
        var path = Path.Combine(root, name + ".js");
        if (!File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= MaximumNameLength &&
        name.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
