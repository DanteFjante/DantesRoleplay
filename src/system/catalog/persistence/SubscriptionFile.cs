using System.Text.Json;
using DantesRoleplay.Content;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Events;

namespace DantesRoleplay.DataAccess.Catalog;

/// <summary>One file-first middleware registration. Runtime routing is deliberately elsewhere.</summary>
public sealed record SubscriptionFile(string Id, string Category, string EventTypeId, string EventMechanicId, SubscriptionMode Mode, int Order, string FixedRoleEntityIdsJson, string TrackedEntityIdsJson, string PayloadEqualsJson, int MaxExecutionsPerChain, string Scope, SubscriptionStatus Status, string RoleFromEventPayloadJson = "{}", string FanoutSelectorJson = "{}", string CreatedBy = "", string ChangeNote = "")
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public string ContentHash => DantesRoleplay.Content.ContentHash.Of(Category, EventTypeId, EventMechanicId, Mode.ToString(), Order.ToString(), SubscriptionCanonicalJson.Object(FixedRoleEntityIdsJson), SubscriptionCanonicalJson.Object(RoleFromEventPayloadJson), SubscriptionCanonicalJson.Object(FanoutSelectorJson), SubscriptionCanonicalJson.Ids(TrackedEntityIdsJson), SubscriptionCanonicalJson.Object(PayloadEqualsJson), MaxExecutionsPerChain.ToString(), Scope, Status.ToString());
    public string ToJson()
    {
        using var fixedRoles = JsonDocument.Parse(FixedRoleEntityIdsJson);
        using var roleFromEventPayload = JsonDocument.Parse(RoleFromEventPayloadJson);
        using var fanoutSelector = JsonDocument.Parse(FanoutSelectorJson);
        using var tracked = JsonDocument.Parse(TrackedEntityIdsJson);
        using var payload = JsonDocument.Parse(PayloadEqualsJson);
        return JsonSerializer.Serialize(
                   new Payload(Id, Category, EventTypeId, EventMechanicId, Mode.ToString(), Order,
                       fixedRoles.RootElement.Clone(), roleFromEventPayload.RootElement.Clone(), fanoutSelector.RootElement.Clone(), tracked.RootElement.Clone(), payload.RootElement.Clone(),
                       MaxExecutionsPerChain, Scope, Status.ToString(), CreatedBy, ChangeNote),
                   Json)
               + "\n";
    }
    public static SubscriptionFile Parse(string json, string source)
    {
        Payload? p; try { p = JsonSerializer.Deserialize<Payload>(json, Json); } catch (JsonException ex) { throw new InvalidOperationException($"{source} is not valid JSON: {ex.Message}", ex); }
        if (p is null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Category) || string.IsNullOrWhiteSpace(p.EventTypeId) || string.IsNullOrWhiteSpace(p.EventMechanicId)) throw new InvalidOperationException($"{source} requires id, category, eventTypeId, and eventMechanicId.");
        if (p.FixedRoleEntityIds.ValueKind != JsonValueKind.Object || (p.RoleFromEventPayload.ValueKind is not JsonValueKind.Object and not JsonValueKind.Undefined) || (p.FanoutSelector.ValueKind is not JsonValueKind.Object and not JsonValueKind.Undefined) || p.TrackedEntityIds.ValueKind != JsonValueKind.Array || p.PayloadEquals.ValueKind != JsonValueKind.Object) throw new InvalidOperationException($"{source} requires fixedRoleEntityIds, roleFromEventPayload, fanoutSelector, and payloadEquals objects plus a trackedEntityIds array.");
        if (!Enum.TryParse<SubscriptionMode>(p.Mode, true, out var mode) || !Enum.TryParse<SubscriptionStatus>(p.Status, true, out var status)) throw new InvalidOperationException($"{source} has invalid mode or status.");
        return new(p.Id.Trim(), p.Category.Trim(), p.EventTypeId.Trim(), p.EventMechanicId.Trim(), mode, p.Order, SubscriptionCanonicalJson.Object(p.FixedRoleEntityIds), SubscriptionCanonicalJson.Ids(p.TrackedEntityIds), SubscriptionCanonicalJson.Object(p.PayloadEquals), p.MaxExecutionsPerChain, p.Scope ?? string.Empty, status, p.RoleFromEventPayload.ValueKind == JsonValueKind.Undefined ? "{}" : SubscriptionCanonicalJson.Object(p.RoleFromEventPayload), p.FanoutSelector.ValueKind == JsonValueKind.Undefined ? "{}" : SubscriptionCanonicalJson.Object(p.FanoutSelector), p.CreatedBy ?? string.Empty, p.ChangeNote ?? string.Empty);
    }
    private sealed record Payload(string? Id, string? Category, string? EventTypeId, string? EventMechanicId, string? Mode, int Order, JsonElement FixedRoleEntityIds, JsonElement RoleFromEventPayload, JsonElement FanoutSelector, JsonElement TrackedEntityIds, JsonElement PayloadEquals, int MaxExecutionsPerChain, string? Scope, string? Status, string? CreatedBy = null, string? ChangeNote = null);
}
