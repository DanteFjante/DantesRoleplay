using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DantesRoleplay.CatalogNavigation;

public sealed record CatalogCursorBinding(
    string ManifestFingerprint,
    string ApplicationId,
    string Collection,
    string Branch,
    string FilterFingerprint,
    string SortVersion,
    int PageSize,
    string LastStableKey);

/// <summary>The cursor fields a caller chooses before the navigator supplies its private page key.</summary>
public sealed record CatalogCursorScope(
    string ManifestFingerprint,
    string ApplicationId,
    string Collection,
    string Branch,
    string FilterFingerprint,
    string SortVersion,
    int PageSize)
{
    public CatalogCursorBinding Bind(string lastStableKey) => new(
        ManifestFingerprint, ApplicationId, Collection, Branch, FilterFingerprint, SortVersion,
        PageSize, lastStableKey);
}

public sealed class CatalogCursorCodec
{
    private readonly byte[] _key;
    public CatalogCursorCodec(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32) throw new ArgumentException("Catalog cursor keys must contain at least 256 bits.", nameof(key));
        _key = key.ToArray();
    }

    public string Encode(CatalogCursorBinding binding)
    {
        if (binding.PageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(binding));
        var payload = JsonSerializer.SerializeToUtf8Bytes(binding);
        var signature = HMACSHA256.HashData(_key, payload);
        return Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "." + Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public CatalogCursorBinding Decode(string cursor, CatalogCursorBinding expected)
    {
        var binding = DecodeAuthenticated(cursor);
        if (binding != expected) throw new InvalidOperationException("CURSOR_STALE");
        return binding;
    }

    /// <summary>Validates a continuation against its static scope while retaining its signed page key.</summary>
    public CatalogCursorBinding Decode(string cursor, CatalogCursorScope expected)
    {
        var binding = DecodeAuthenticated(cursor);
        if (binding.ManifestFingerprint != expected.ManifestFingerprint
            || binding.ApplicationId != expected.ApplicationId
            || binding.Collection != expected.Collection
            || binding.Branch != expected.Branch
            || binding.FilterFingerprint != expected.FilterFingerprint
            || binding.SortVersion != expected.SortVersion
            || binding.PageSize != expected.PageSize)
            throw new InvalidOperationException("CURSOR_STALE");
        return binding;
    }

    private CatalogCursorBinding DecodeAuthenticated(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) throw new ArgumentException("CURSOR_INVALID", nameof(cursor));
        var parts = cursor.Split('.');
        if (parts.Length != 2) throw new ArgumentException("CURSOR_INVALID", nameof(cursor));
        byte[] payload;
        byte[] supplied;
        try { payload = DecodeBase64(parts[0]); supplied = DecodeBase64(parts[1]); }
        catch (FormatException) { throw new ArgumentException("CURSOR_INVALID", nameof(cursor)); }
        var actual = HMACSHA256.HashData(_key, payload);
        if (!CryptographicOperations.FixedTimeEquals(supplied, actual)) throw new ArgumentException("CURSOR_INVALID", nameof(cursor));
        try { return JsonSerializer.Deserialize<CatalogCursorBinding>(payload) ?? throw new JsonException(); }
        catch (JsonException) { throw new ArgumentException("CURSOR_INVALID", nameof(cursor)); }
    }

    private static byte[] DecodeBase64(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(value + new string('=', (4 - value.Length % 4) % 4));
    }
}
