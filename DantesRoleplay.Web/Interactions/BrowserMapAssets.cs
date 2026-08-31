namespace DantesRoleplay.Web.Interactions;

/// <summary>Loads reviewed map images from the browser-component output directory.</summary>
public static class BrowserMapAssets
{
    public static async Task<byte[]?> ReadAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        if (!BrowserComponentAssets.IsValidName(name)) return null;
        var root = Path.Combine(AppContext.BaseDirectory, "BrowserComponents", "MapImages");
        var path = Path.Combine(root, name + ".png");
        if (!File.Exists(path)) return null;
        return await File.ReadAllBytesAsync(path, cancellationToken);
    }
}
