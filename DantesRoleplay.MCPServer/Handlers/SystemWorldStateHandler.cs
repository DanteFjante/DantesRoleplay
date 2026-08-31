using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Mcp;

internal sealed class SystemWorldStateHandler
{
    public Task<ToolEnvelope> SynchronizeAsync(
        IApplicationWorldAuthoringSynchronizer? synchronization,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        const string kind = "system.world-state.sync";
        return ToolRunner.RunAsync(log, "commit", intent, $"commit:{kind}", procedures, async () =>
        {
            var decision = Authorize(authorization);
            if (!decision.Allowed)
                return Fail(decision, decision.Code,
                    "Private-operator authentication is required before application-world authoring.",
                    "query(kind: \"capabilities\")",
                    "Denied application-world authoring before payload parsing.");
            if (synchronization is null)
                return Fail(decision, "WORLD_AUTHORING_UNAVAILABLE",
                    "Application-world authoring is not configured.",
                    "query(kind: \"capabilities\")",
                    "Application-world authoring was unavailable.");

            ApplicationWorldAuthoringRequest request;
            try { request = Parse(payload); }
            catch (JsonException exception)
            {
                return Fail(decision, "INVALID_PAYLOAD", exception.Message,
                    McpVerbCatalog.CommitCall(kind, dryRun: true),
                    "Rejected an invalid application-world manifest.");
            }

            var result = await synchronization.SynchronizeAsync(
                request,
                new(intent ?? string.Empty, procedures ?? []),
                dryRun,
                cancellationToken);
            if (!result.Accepted)
                return Fail(decision, result.ErrorCode,
                    "The application-world manifest was rejected without changing state. "
                    + string.Join(" ", (result.Problems ?? []).Select(value => value.Message)),
                    McpVerbCatalog.CommitCall(kind, dryRun: true),
                    $"Rejected application-world synchronization: {result.ErrorCode}.");

            return new ToolOutcome(new
            {
                result.DryRun,
                result.Replayed,
                result.ReviewedEntityCount,
                result.AppliedEffectCount,
                EffectOperationId = result.OperationId,
                Receipts = (result.Receipts ?? []).Select(value => new
                {
                    value.Index,
                    value.Type,
                    value.EntityId,
                    value.QualifiedTypeId,
                    value.Revision,
                    value.RemovedRevision,
                    value.TargetEntityId,
                    value.QualifiedRelationshipKind
                }).ToArray()
            },
            result.DryRun
                ? $"Validated {result.ReviewedEntityCount} world entities as {result.AppliedEffectCount} atomic effects; nothing was written."
                : result.Replayed
                    ? "Replayed the already committed application-world manifest without a second write."
                    : $"Authored {result.ReviewedEntityCount} world entities through {result.AppliedEffectCount} atomic effects.",
            [result.DryRun ? CommitCall(payload) : "query(kind: \"history\", tool: \"system.ecs.effects\")"],
            GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
        }, consumesReadEvidence: !dryRun);
    }

    private static ApplicationWorldAuthoringRequest Parse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw Invalid("payload must be a JSON object.");
        RequireProperties(root,
            ["requestToken", "applicationId", "stateSpaceId", "rootEntityId", "entities", "relationships"]);
        var entities = Array(root, "entities", 1, 64).Select(ReadEntity).ToArray();
        var relationships = Array(root, "relationships", 0, 64).Select(ReadRelationship).ToArray();
        return new(
            Token(root, "requestToken", 32),
            Token(root, "applicationId", 63),
            Token(root, "stateSpaceId", 200),
            Token(root, "rootEntityId", 200),
            entities,
            relationships);
    }

    private static ApplicationWorldAuthoringEntity ReadEntity(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("Every entity must be an object.");
        RequireProperties(value, ["entityId", "name", "expectedRevision", "components"], ["containment"]);
        ApplicationWorldAuthoringContainment? containment = null;
        if (value.TryGetProperty("containment", out var containmentElement))
            containment = containmentElement.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.Object => ReadContainment(containmentElement),
                _ => throw Invalid("containment must be an object or null.")
            };
        return new(
            Token(value, "entityId", 200),
            Text(value, "name", 400),
            Revision(value, "expectedRevision"),
            Array(value, "components", 0, 32).Select(ReadComponent).ToArray(),
            containment);
    }

    private static ApplicationWorldAuthoringComponent ReadComponent(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("Every component must be an object.");
        RequireProperties(value, ["qualifiedTypeId", "expectedRevision", "value"]);
        return new(
            Token(value, "qualifiedTypeId", 200),
            Revision(value, "expectedRevision"),
            ObjectJson(value, "value"));
    }

    private static ApplicationWorldAuthoringContainment ReadContainment(JsonElement value)
    {
        RequireProperties(value, ["containerEntityId", "slot", "expectedRevision"]);
        return new(
            Token(value, "containerEntityId", 200),
            Text(value, "slot", 100),
            Revision(value, "expectedRevision"));
    }

    private static ApplicationWorldAuthoringRelationship ReadRelationship(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("Every relationship must be an object.");
        RequireProperties(value,
            ["fromEntityId", "toEntityId", "qualifiedKind", "expectedRevision", "value"]);
        return new(
            Token(value, "fromEntityId", 200),
            Token(value, "toEntityId", 200),
            Token(value, "qualifiedKind", 200),
            Revision(value, "expectedRevision"),
            ObjectJson(value, "value"));
    }

    private static JsonElement.ArrayEnumerator Array(
        JsonElement parent,
        string name,
        int minimum,
        int maximum)
    {
        var value = parent.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < minimum
            || value.GetArrayLength() > maximum)
            throw Invalid($"{name} must contain {minimum} through {maximum} entries.");
        return value.EnumerateArray();
    }

    private static string ObjectJson(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Object) throw Invalid($"{name} must be a JSON object.");
        return JsonSerializer.Serialize(value);
    }

    private static int Revision(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var revision) || revision < 0)
            throw Invalid($"{name} must be a nonnegative integer.");
        return revision;
    }

    private static void RequireProperties(
        JsonElement value,
        IReadOnlyList<string> required,
        IReadOnlyList<string>? optional = null)
    {
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        var allowed = optional is null ? required : required.Concat(optional);
        if (names.Length != names.Distinct(StringComparer.Ordinal).Count()
            || names.Except(allowed, StringComparer.Ordinal).Any()
            || required.Except(names, StringComparer.Ordinal).Any())
            throw Invalid(optional is null
                ? $"Object must contain exactly: {string.Join(", ", required)}."
                : $"Object requires {string.Join(", ", required)} and permits {string.Join(", ", optional)}.");
    }

    private static string Token(JsonElement value, string name, int maximum)
    {
        var result = Text(value, name, maximum);
        if (result.Any(char.IsWhiteSpace)) throw Invalid($"{name} must not contain whitespace.");
        return result;
    }

    private static string Text(JsonElement value, string name, int maximum)
    {
        var element = value.GetProperty(name);
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString())
            || element.GetString() != element.GetString()!.Trim() || element.GetString()!.Length > maximum)
            throw Invalid($"{name} must be bounded, nonempty, and trimmed.");
        return element.GetString()!;
    }

    private static PrivateOperatorAuthorizationDecision Authorize(IPrivateOperatorRequestAuthorizer? authorization) =>
        authorization?.Authorize(PrivateOperatorCapability.Modify)
        ?? new PrivateOperatorAuthorizationPolicy().Evaluate(new(
            TrustedPrincipalContext.Unauthenticated("MCP_PRIVATE_OPERATOR_REQUIRED"),
            PrivateOperatorCapability.Modify,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope,
            "mcp-request"));

    private static ToolOutcome Fail(
        PrivateOperatorAuthorizationDecision decision,
        string code,
        string why,
        string fix,
        string summary) =>
        new(null, summary, [fix], new(code, why, fix),
            GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));

    private static JsonException Invalid(string message) => new(message);
    private static string CommitCall(string payload) =>
        $"commit(kind: \"system.world-state.sync\", payload: {JsonSerializer.Serialize(payload)})";
}
