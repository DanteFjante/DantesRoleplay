namespace DantesRoleplay.Web.Interactions;

public sealed record BrowserMediaAsset(byte[] Content, string ContentType);

/// <summary>Loads reviewed, content-addressed visual media without application vocabulary.</summary>
public static class BrowserMediaAssets
{
    private const string Prefix = "sha256.";

    public static async Task<BrowserMediaAsset?> ReadAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        if (!TryContentType(name, out var contentType)) return null;
        var root = Path.Combine(AppContext.BaseDirectory, "BrowserComponents", "Media");
        var path = Path.Combine(root, name!);
        if (!File.Exists(path)) return null;
        return new BrowserMediaAsset(
            await File.ReadAllBytesAsync(path, cancellationToken), contentType!);
    }

    public static bool TryContentType(string? name, out string? contentType)
    {
        contentType = null;
        if (name is null || !name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length < 72)
            return false;
        var separator = name.IndexOf('.', Prefix.Length);
        if (separator != Prefix.Length + 64) return false;
        var digest = name.AsSpan(Prefix.Length, 64);
        foreach (var character in digest)
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) return false;
        contentType = name[(separator + 1)..] switch
        {
            "png" => "image/png",
            "jpg" => "image/jpeg",
            "webp" => "image/webp",
            _ => null
        };
        return contentType is not null;
    }
}
