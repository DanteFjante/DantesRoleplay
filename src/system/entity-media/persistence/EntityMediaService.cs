using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Blobs;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.Media;

public sealed class EntityMediaService(
    IStateSpaceRegistry stateSpaces,
    IEntityComponentStore entities,
    IBlobTransferService blobs) : IEntityMediaService
{
    public const string VisualComponentTypeId = "game.core.media.visual";
    public const string MapVisualComponentTypeId = "game.core.world.map.visual";
    private const int MaximumAttachments = 64;

    public async Task<EntityMediaDiscoveryResult> DiscoverAsync(
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string entityId,
        EntityMediaAudience audience,
        bool diagnostics = false,
        CancellationToken cancellationToken = default)
    {
        ValidateId(stateSpaceId, nameof(stateSpaceId));
        ValidateId(entityId, nameof(entityId));
        var stateSpace = stateSpaces.Get(stateSpaceId)
            ?? throw new EntityMediaException("MEDIA_STATE_SPACE_UNKNOWN", "The media state space is unknown.");
        if (stateSpace.ApplicationRevision.ApplicationId != applicationId)
            throw new EntityMediaException("MEDIA_STATE_SPACE_WRONG_APPLICATION",
                "The media state space does not belong to the requested application.");
        if (await entities.GetEntityAsync(stateSpaceId, entityId, cancellationToken) is null)
            throw new EntityMediaException("MEDIA_ENTITY_UNKNOWN", "The media owner is unknown or deleted.");

        var locators = new[]
        {
            new EcsComponentLocator(entityId, VisualComponentTypeId),
            new EcsComponentLocator(entityId, MapVisualComponentTypeId)
        };
        var components = await entities.GetComponentsAsync(stateSpaceId, locators, cancellationToken);
        var found = new List<EntityMediaAttachment>();
        var evidence = new List<EntityMediaDiagnostic>();
        foreach (var component in components.OrderBy(value => value.Type.QualifiedTypeId, StringComparer.Ordinal))
        {
            try
            {
                var parsed = component.Type.QualifiedTypeId switch
                {
                    VisualComponentTypeId => ParseVisual(component.ValueJson),
                    MapVisualComponentTypeId => ParseMap(component.ValueJson),
                    _ => []
                };
                foreach (var attachment in parsed)
                {
                    if (!attachment.Visibility.Contains(audience)) continue;
                    var blob = await blobs.FindAsync(attachment.Sha256, cancellationToken);
                    if (blob is null)
                    {
                        evidence.Add(new("MEDIA_BLOB_MISSING",
                            "The attachment references a blob that has not been finalized.",
                            component.Type.QualifiedTypeId, attachment.MediaId));
                        continue;
                    }
                    if (!string.Equals(blob.MediaType, attachment.MediaType, StringComparison.Ordinal))
                    {
                        evidence.Add(new("MEDIA_BLOB_METADATA_MISMATCH",
                            "The attachment MIME type does not match the finalized blob metadata.",
                            component.Type.QualifiedTypeId, attachment.MediaId));
                        continue;
                    }
                    found.Add(attachment);
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                evidence.Add(new("MEDIA_COMPONENT_MALFORMED", exception.Message,
                    component.Type.QualifiedTypeId));
            }
        }

        var ordered = found
            .OrderBy(value => value.Order)
            .ThenBy(value => value.Role, StringComparer.Ordinal)
            .ThenBy(value => value.MediaId, StringComparer.Ordinal)
            .Take(MaximumAttachments)
            .ToArray();
        return new(applicationId.Value, stateSpaceId, entityId, stateSpace.ResolutionFingerprint,
            ordered, diagnostics ? evidence.ToArray() : []);
    }

    public async Task<EntityMediaReadResult?> OpenReadAsync(
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        string entityId,
        string mediaId,
        EntityMediaAudience audience,
        CancellationToken cancellationToken = default)
    {
        ValidateId(mediaId, nameof(mediaId));
        var discovered = await DiscoverAsync(
            applicationId, stateSpaceId, entityId, audience, diagnostics: false, cancellationToken);
        var attachment = discovered.Attachments.SingleOrDefault(value => value.MediaId == mediaId);
        if (attachment is null) return null;
        var blob = await blobs.OpenReadAsync(attachment.Sha256, cancellationToken);
        return blob is null ? null : new(attachment, blob);
    }

    private static IReadOnlyList<EntityMediaAttachment> ParseVisual(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Exact(root, "status", "attachments");
        if (root.GetProperty("status").GetString() != "active") return [];
        var attachments = root.GetProperty("attachments");
        if (attachments.ValueKind != JsonValueKind.Array || attachments.GetArrayLength() is < 1 or > MaximumAttachments)
            throw Invalid("attachments must contain between 1 and 64 entries.");
        var result = new List<EntityMediaAttachment>();
        var orders = new HashSet<int>();
        foreach (var value in attachments.EnumerateArray())
        {
            Exact(value, "role", "visibility", "sha256", "mimeType", "width", "height",
                "alt", "caption", "order", "provenance");
            var order = value.GetProperty("order").GetInt32();
            if (!orders.Add(order)) throw Invalid("attachment order values must be unique.");
            result.Add(new(
                $"visual-{order}", Required(value, "role"), Visibility(value.GetProperty("visibility")),
                Required(value, "sha256"), Required(value, "mimeType"),
                value.GetProperty("width").GetInt32(), value.GetProperty("height").GetInt32(),
                Required(value, "alt"), value.GetProperty("caption").GetString() ?? "", order,
                Provenance(value.GetProperty("provenance"))));
        }
        return result;
    }

    private static IReadOnlyList<EntityMediaAttachment> ParseMap(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Exact(root, "status", "variants");
        if (root.GetProperty("status").GetString() != "active") return [];
        var variants = root.GetProperty("variants");
        if (variants.ValueKind != JsonValueKind.Object ||
            variants.EnumerateObject().Any(value => value.Name is not ("player" or "dm")))
            throw Invalid("map variants must be a closed player/dm object.");
        var result = new List<EntityMediaAttachment>();
        foreach (var variant in variants.EnumerateObject())
        {
            var value = variant.Value;
            Exact(value, "sha256", "mimeType", "width", "height", "alt", "caption", "order", "provenance");
            result.Add(new(
                $"map-{variant.Name}", "map",
                [variant.Name == "player" ? EntityMediaAudience.Player : EntityMediaAudience.GameMaster],
                Required(value, "sha256"), Required(value, "mimeType"),
                value.GetProperty("width").GetInt32(), value.GetProperty("height").GetInt32(),
                Required(value, "alt"), value.GetProperty("caption").GetString() ?? "",
                value.GetProperty("order").GetInt32(), Provenance(value.GetProperty("provenance"))));
        }
        return result;
    }

    private static EntityMediaProvenance Provenance(JsonElement value)
    {
        Exact(value, "kind", "credit", "source", "reviewedOn", "version");
        return new(Required(value, "kind"), Required(value, "credit"), Required(value, "source"),
            Required(value, "reviewedOn"), value.GetProperty("version").GetInt32());
    }

    private static IReadOnlyList<EntityMediaAudience> Visibility(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 1 or > 2)
            throw Invalid("attachment visibility must contain player, dm, or both.");
        var result = value.EnumerateArray().Select(item => item.GetString() switch
        {
            "player" => EntityMediaAudience.Player,
            "dm" => EntityMediaAudience.GameMaster,
            _ => throw Invalid("attachment visibility contains an unknown audience.")
        }).Distinct().ToArray();
        if (result.Length != value.GetArrayLength()) throw Invalid("attachment visibility contains duplicates.");
        return result;
    }

    private static string Required(JsonElement value, string property)
    {
        var result = value.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(result) ? throw Invalid($"{property} is required.") : result;
    }

    private static void Exact(JsonElement value, params string[] properties)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(properties.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw Invalid("media data does not match its closed contract.");
    }

    private static InvalidDataException Invalid(string message) => new(message);

    private static void ValidateId(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(char.IsControl))
            throw new EntityMediaException("MEDIA_IDENTIFIER_INVALID", $"{name} is invalid.");
    }
}
