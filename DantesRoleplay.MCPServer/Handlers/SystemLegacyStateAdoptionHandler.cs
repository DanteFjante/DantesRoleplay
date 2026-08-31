using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Ecs;
using DantesRoleplay.LegacyStateAdoption;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Mcp;

internal sealed class SystemLegacyStateAdoptionHandler
{
    public async Task<ToolEnvelope> AdoptAsync(
        ILegacyStateAdoptionService? adoption,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        const string kind = LegacyStateAdoptionService.Kind;
        var decision = Authorize(authorization);
        if (!decision.Allowed)
            return await FailureAsync(log, decision, kind, decision.Code,
                "Private-operator authentication is required before legacy state adoption.",
                "query(kind: \"capabilities\")", intent, procedures,
                "Denied legacy state adoption before payload parsing or state access.");
        if (adoption is null)
            return await FailureAsync(log, decision, kind, "LEGACY_STATE_ADOPTION_UNAVAILABLE",
                "Legacy state adoption is not configured.", "query(kind: \"capabilities\")",
                intent, procedures, "Legacy state adoption was unavailable.");

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Invalid("payload must be a JSON object.");
            RequireRootProperties(root, ["requestToken", "stateSpaceId", "applicationId",
                "activeFingerprint", "componentMappings", "relationshipMappings"], ["entityIds"]);
            var token = String(root, "requestToken", 32);
            var app = ApplicationIdentifier.Parse(String(root, "applicationId", 63));
            var request = new LegacyStateAdoptionRequest(
                String(root, "stateSpaceId", 200),
                app,
                String(root, "activeFingerprint", 64),
                ComponentMappings(root.GetProperty("componentMappings")),
                RelationshipMappings(root.GetProperty("relationshipMappings")),
                root.TryGetProperty("entityIds", out var entityIds) ? EntityIds(entityIds) : null);
            var context = new LegacyStateAdoptionContext(token, intent,
                Array.AsReadOnly((procedures ?? []).ToArray()), decision.Evidence);

            if (dryRun)
            {
                var preview = await adoption.PreviewAsync(request, context, cancellationToken);
                return ToolEnvelope.Success(Data(true, token, preview.Outcome, preview.Inventory),
                    preview.OperationId, Call(payload));
            }

            var receipt = await adoption.AdoptAsync(request, context, cancellationToken);
            return ToolEnvelope.Success(Data(false, token, receipt.Outcome, receipt.Inventory),
                receipt.OperationId,
                $"query(kind: \"system.applications\", applicationId: {JsonSerializer.Serialize(app.Value)})");
        }
        catch (JsonException)
        {
            return await FailureAsync(log, decision, kind, "INVALID_PAYLOAD",
                "payload must be valid JSON with the documented closed legacy-adoption shape.",
                McpVerbCatalog.CommitCall(kind, dryRun: true), intent, procedures,
                "Rejected malformed legacy-adoption payload.");
        }
        catch (LegacyStateAdoptionException exception)
        {
            var fix = exception.Code switch
            {
                "DRY_RUN_REQUIRED" or "DRY_RUN_STALE" => Call(payload, true),
                "ACTIVATION_REQUIRED" or "ACTIVATION_STALE" or "APPLICATION_STALE" =>
                    $"query(kind: \"system.applications\", applicationId: {JsonSerializer.Serialize(TryApplicationId(payload))})",
                _ => McpVerbCatalog.CommitCall(kind, dryRun: true)
            };
            return await FailureAsync(log, decision, kind, exception.Code, exception.Message,
                fix, intent, procedures, $"Rejected legacy state adoption: {exception.Code}.");
        }
        catch (ArgumentException exception)
        {
            return await FailureAsync(log, decision, kind, "INVALID_PAYLOAD", exception.Message,
                McpVerbCatalog.CommitCall(kind, dryRun: true), intent, procedures,
                "Rejected invalid legacy-adoption input.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await FailureAsync(log, decision, kind, "LEGACY_STATE_ADOPTION_FAILED",
                "The adoption transaction failed without changing runtime state.",
                McpVerbCatalog.CommitCall(kind, dryRun: true), intent, procedures,
                "Legacy state adoption failed without disclosing internal details.");
        }
    }

    private static IReadOnlyList<LegacyComponentTypeMapping> ComponentMappings(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > 256)
            throw Invalid("componentMappings must be an array with at most 256 entries.");
        var values = new List<LegacyComponentTypeMapping>();
        foreach (var item in element.EnumerateArray())
        {
            RequireProperties(item, "legacyDefinitionId", "qualifiedTypeId", "typeVersion", "schemaHash");
            var version = item.GetProperty("typeVersion");
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number < 1)
                throw Invalid("typeVersion must be a positive integer.");
            values.Add(new(String(item, "legacyDefinitionId", 200), new EcsComponentReference(
                String(item, "qualifiedTypeId", 200), number, String(item, "schemaHash", 64))));
        }
        return values.AsReadOnly();
    }

    private static IReadOnlyList<LegacyRelationshipKindMapping> RelationshipMappings(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > 256)
            throw Invalid("relationshipMappings must be an array with at most 256 entries.");
        var values = new List<LegacyRelationshipKindMapping>();
        foreach (var item in element.EnumerateArray())
        {
            RequireProperties(item, "legacyKind", "qualifiedKind");
            values.Add(new(String(item, "legacyKind", 100), String(item, "qualifiedKind", 200)));
        }
        return values.AsReadOnly();
    }

    private static IReadOnlyList<string> EntityIds(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() is < 1 or > 1_000)
            throw Invalid("entityIds must be an array with 1 through 1,000 entries.");
        var values = element.EnumerateArray().Select(value =>
        {
            var entityId = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (string.IsNullOrWhiteSpace(entityId) || entityId.Length > 200 || entityId.Any(char.IsControl))
                throw Invalid("Every entityIds entry must be a bounded identifier without control characters.");
            return entityId;
        }).ToArray();
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw Invalid("entityIds must not contain duplicates.");
        return Array.AsReadOnly(values);
    }

    private static object Data(bool dryRun, string token, string outcome, LegacyStateInventory inventory) => new
    {
        DryRun = dryRun,
        RequestToken = token,
        Outcome = outcome,
        Inventory = inventory
    };

    private static void RequireProperties(JsonElement payload, params string[] required)
    {
        if (payload.ValueKind != JsonValueKind.Object) throw Invalid("mapping entries must be JSON objects.");
        var names = payload.EnumerateObject().Select(value => value.Name).ToArray();
        if (names.Length != names.Distinct(StringComparer.Ordinal).Count()
            || names.Length != required.Length
            || names.Except(required, StringComparer.Ordinal).Any()
            || required.Except(names, StringComparer.Ordinal).Any())
            throw Invalid($"payload object must contain exactly: {string.Join(", ", required)}.");
    }

    private static void RequireRootProperties(JsonElement payload, IReadOnlyList<string> required,
        IReadOnlyList<string> optional)
    {
        if (payload.ValueKind != JsonValueKind.Object) throw Invalid("payload must be a JSON object.");
        var names = payload.EnumerateObject().Select(value => value.Name).ToArray();
        if (names.Length != names.Distinct(StringComparer.Ordinal).Count()
            || names.Except(required.Concat(optional), StringComparer.Ordinal).Any()
            || required.Except(names, StringComparer.Ordinal).Any())
            throw Invalid($"payload must contain {string.Join(", ", required)} and only optional fields: {string.Join(", ", optional)}.");
    }

    private static string String(JsonElement payload, string name, int maximum)
    {
        var value = payload.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())
            || value.GetString()!.Length > maximum)
            throw Invalid($"{name} must be a nonblank string of at most {maximum} characters.");
        return value.GetString()!;
    }

    private static string TryApplicationId(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("applicationId", out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "..." : "...";
        }
        catch (JsonException) { return "..."; }
    }

    private static PrivateOperatorAuthorizationDecision Authorize(IPrivateOperatorRequestAuthorizer? authorization) =>
        authorization?.Authorize(PrivateOperatorCapability.Modify)
        ?? new PrivateOperatorAuthorizationPolicy().Evaluate(new(
            TrustedPrincipalContext.Unauthenticated("MCP_PRIVATE_OPERATOR_REQUIRED"),
            PrivateOperatorCapability.Modify,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "mcp-request"));

    private static Task<ToolEnvelope> FailureAsync(
        IOperationLog log, PrivateOperatorAuthorizationDecision decision, string kind, string code,
        string why, string fix, string intent, string[]? procedures, string summary) =>
        ToolRunner.RunAsync(log, "commit", intent, $"commit:{kind}", procedures,
            () => Task.FromResult(new ToolOutcome(null, summary, [fix], new(code, why, fix),
                GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence))),
            consumesReadEvidence: false);

    private static LegacyStateAdoptionException Invalid(string message) => new("INVALID_PAYLOAD", message);
    private static string Call(string payload, bool dryRun = false) =>
        $"commit(kind: \"{LegacyStateAdoptionService.Kind}\", payload: {JsonSerializer.Serialize(payload)}"
        + (dryRun ? ", dryRun: true)" : ")");
}
