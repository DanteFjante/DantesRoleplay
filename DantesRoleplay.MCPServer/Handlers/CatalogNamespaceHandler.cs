using System.Text.Json;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>
/// Reads the namespace registry, and registers a new namespace for review.
///
/// Every authored identity — a mechanic, a procedure, a component definition — is placed by its
/// prefix, and the prefix has to be a registered namespace or the write is refused inside
/// SaveChanges. That rule was enforced without being readable: no verb listed what existed, and no
/// verb created what was missing, so a session that wanted to author `mechanic.game.core.world.quest.*`
/// could neither discover the constraint nor satisfy it. A constraint a caller cannot see is
/// indistinguishable, from the caller's side, from a broken system.
///
/// Registration deliberately does NOT accept a review status. A namespace arrives needing review
/// and nothing may be written into it until a person reviews it: the point of the gate is that new
/// identity space is granted rather than taken, and a caller that could review its own request
/// would be granting it.
/// </summary>
public sealed class CatalogNamespaceHandler
{
    public async Task<ToolEnvelope> ListAsync(
        ICatalogNamespaceRegistry? namespaces,
        IOperationLog log,
        string? query,
        string? id,
        bool includeInactive,
        int? limit) =>
        await ToolRunner.RunAsync(log, "read_namespaces", async () =>
        {
            await Task.CompletedTask;
            if (namespaces is null)
                return ToolOutcome.Fail("NAMESPACE_REGISTRY_UNAVAILABLE",
                    "The catalog namespace registry is not configured on this host.",
                    "query(kind: \"capabilities\")",
                    "Namespace registry unavailable.");

            if (!string.IsNullOrWhiteSpace(id))
            {
                var one = namespaces.Get(id, includeDisabled: includeInactive);
                return one is null
                    ? ToolOutcome.Fail("NAMESPACE_UNKNOWN",
                        $"'{id}' is not a registered namespace.",
                        "query(kind: \"namespaces\") — list what is registered.",
                        $"Namespace '{id}' not found.")
                    : ToolOutcome.OkAbout(id, one, $"Namespace '{id}'.",
                        $"commit(kind: \"mechanic\", payload: {{\"id\": \"{id}.<name>\", ...}}, dryRun: true)");
            }

            var bounded = limit is > 0 and <= 200 ? limit.Value : 100;
            var matches = string.IsNullOrWhiteSpace(query)
                ? namespaces.List(includeDisabled: includeInactive).Take(bounded).ToArray()
                : namespaces.Search(query, Math.Min(bounded, 100), includeDisabled: includeInactive)
                    .Select(hit => hit.Namespace).ToArray();

            return ToolOutcome.Ok(
                new { Count = matches.Length, Namespaces = matches },
                $"{matches.Length} registered namespace(s).",
                "commit(kind: \"system.namespace.register\", payload: {...}, dryRun: true) — only when none of these fits.");
        });

    public async Task<ToolEnvelope> RegisterAsync(
        ICatalogNamespaceRegistry? namespaces,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        bool dryRun,
        CancellationToken cancellationToken) =>
        await ToolRunner.RunAsync(log, "register_namespace", intent, string.Empty, procedures, async () =>
        {
            await Task.CompletedTask;
            if (namespaces is null)
                return ToolOutcome.Fail("NAMESPACE_REGISTRY_UNAVAILABLE",
                    "The catalog namespace registry is not configured on this host.",
                    "query(kind: \"capabilities\")",
                    "Namespace registry unavailable.");

            string id, owner, description;
            string[] kinds;
            try
            {
                using var document = JsonDocument.Parse(payload);
                var value = document.RootElement;
                id = Text(value, "id");
                owner = Text(value, "owner");
                description = Text(value, "description");
                kinds = value.TryGetProperty("allowedKinds", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Select(entry => entry.GetString() ?? string.Empty).ToArray()
                    : [];
            }
            catch (JsonException)
            {
                return ToolOutcome.Fail("INVALID_PAYLOAD",
                    "The namespace payload is not valid JSON.",
                    "commit(kind: \"system.namespace.register\", payload: \"{\\\"id\\\":\\\"...\\\",\\\"owner\\\":\\\"...\\\",\\\"description\\\":\\\"...\\\",\\\"allowedKinds\\\":[\\\"mechanic\\\"]}\", dryRun: true)",
                    "Rejected malformed namespace payload.");
            }

            if (id.Length == 0 || owner.Length == 0 || description.Length == 0 || kinds.Length == 0)
                return ToolOutcome.Fail("INVALID_PAYLOAD",
                    "A namespace needs id, owner, description, and at least one allowedKinds entry.",
                    "commit(kind: \"system.namespace.register\", payload: \"{\\\"id\\\":\\\"...\\\",\\\"owner\\\":\\\"...\\\",\\\"description\\\":\\\"...\\\",\\\"allowedKinds\\\":[\\\"mechanic\\\"]}\", dryRun: true)",
                    "Rejected incomplete namespace payload.");

            var unknownKind = kinds.FirstOrDefault(value => !CatalogNamespaceKinds.All.Contains(value, StringComparer.Ordinal));
            if (unknownKind is not null)
                return ToolOutcome.Fail("NAMESPACE_KIND_UNKNOWN",
                    $"'{unknownKind}' is not a record kind. Use one of: {string.Join(", ", CatalogNamespaceKinds.All)}.",
                    "commit(kind: \"system.namespace.register\", payload: {...}, dryRun: true)",
                    $"Rejected namespace '{id}': unknown record kind.");

            var existing = namespaces.Get(id, includeDisabled: true);
            if (existing is not null)
                return ToolOutcome.OkAbout(id, existing,
                    $"Namespace '{id}' is already registered ({existing.ReviewStatus}).",
                    $"query(kind: \"namespaces\", id: \"{id}\")");

            if (dryRun)
                return ToolOutcome.OkAbout(id,
                    new { Id = id, Owner = owner, Description = description, AllowedKinds = kinds, WouldRegister = true },
                    $"Dry run for namespace '{id}': nothing written.",
                    "commit(kind: \"system.namespace.register\", payload: {...}) — the identical payload without dryRun registers it.");

            try
            {
                // Review status is not accepted from the caller: a namespace is granted, not taken.
                var registered = namespaces.Register(new CatalogNamespaceRegistration(id, owner, description, kinds));
                return ToolOutcome.OkAbout(id, registered,
                    $"Registered namespace '{id}'. It needs review before any record may be written into it.",
                    $"query(kind: \"namespaces\", id: \"{id}\") — check its review status.",
                    "Ask the operator to review it: `roleplay namespaces review " + id + " --note \"...\"`.");
            }
            catch (Exception error) when (error is CatalogNamespaceException or ArgumentException or InvalidOperationException)
            {
                var code = error is CatalogNamespaceException typed ? typed.Code : "NAMESPACE_INVALID";
                return ToolOutcome.Fail(code, error.Message,
                    "query(kind: \"namespaces\") — list what is registered, then retry under a valid parent.",
                    $"Rejected namespace '{id}': {code}.");
            }
        }, consumesReadEvidence: !dryRun);

    private static string Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()!.Trim()
            : string.Empty;
}
