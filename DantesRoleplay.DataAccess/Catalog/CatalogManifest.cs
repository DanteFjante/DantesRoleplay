using System.Text.Json;
using System.Text.Json.Serialization;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>
/// What was in the database the last time it and the catalog agreed.
///
/// This is not a table of contents — the tree is already that. It is the common ancestor that
/// makes three-way drift detection possible in the next slice: with the file's fingerprint, the
/// database row's, and the manifest's, import can tell "the developer edited the file" from "an
/// LLM rewrote the rule live" from "both did". With only two of the three it can tell that
/// something differs and nothing about which side to believe, which is the same as knowing
/// nothing, except that it looks like knowing something.
/// </summary>
public sealed record CatalogManifest
{
    /// <summary>
    /// Bumped when the layout or the file formats change incompatibly, so an old catalog is
    /// refused with an explanation rather than half-read.
    /// </summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public DateTime ExportedAt { get; init; }

    /// <summary>The database this came from. Informational; nothing keys off it.</summary>
    public string SourceDatabase { get; init; } = string.Empty;

    /// <summary>
    /// Whether this catalog was exported with world state, or with --rules-only.
    ///
    /// Without it, a rules-only catalog would make import report every entity in the database as
    /// "authored live and never exported" on every single run — technically true, useless as a
    /// warning, and exactly the kind of noise that trains people to ignore the output.
    /// </summary>
    public bool IncludesWorld { get; init; }

    public IReadOnlyList<CatalogManifestEntry> Records { get; init; } = [];

    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,

        // Kinds as "mechanic" rather than 0. The manifest is read in diffs and in bug reports, and
        // an integer there is a lookup into an enum nobody has open.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },

        // The manifest is written to be read by a person looking at a diff, so no escaping of
        // characters that are perfectly safe in a file nobody serves over HTTP.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json) + "\n";

    public static CatalogManifest FromJson(string json, string sourceName)
    {
        CatalogManifest? manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<CatalogManifest>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{sourceName} is not valid JSON: {ex.Message}", ex);
        }

        if (manifest is null)
        {
            throw new InvalidOperationException($"{sourceName} is empty.");
        }

        if (manifest.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"{sourceName} was written by schema version {manifest.SchemaVersion}; this build "
                + $"reads version {CurrentSchemaVersion}. Re-export the catalog.");
        }

        return manifest;
    }

    /// <summary>Fingerprint recorded for one record at export time, or null if it was not in the manifest.</summary>
    public string? FingerprintOf(CatalogRecordKind kind, string id) => Records
        .FirstOrDefault(r => r.Kind == kind && string.Equals(r.Id, id, StringComparison.Ordinal))
        ?.ContentHash;
}

public enum CatalogRecordKind
{
    Mechanic,
    Procedure,
    ComponentDefinition,
    EventType,
    Subscription,
    Entity,

    /// <summary>
    /// The whole relationship set as one record. Relationships are edges with no identity of their
    /// own — a (from, to, kind) triple is the key — so there is no per-record file to diff, and
    /// treating the set as one thing is more honest than inventing an id for each edge.
    /// </summary>
    Relationships
}

/// <param name="Version">The exported revision. Zero for records that are not versioned.</param>
/// <param name="ContentHash">
/// The record's fingerprint at export time. See <see cref="DantesRoleplay.Content.ContentHash"/>.
/// </param>
/// <param name="Path">Catalog-relative, forward slashes. The markdown or definition file.</param>
public sealed record CatalogManifestEntry(
    CatalogRecordKind Kind,
    string Id,
    int Version,
    string ContentHash,
    string Path);
