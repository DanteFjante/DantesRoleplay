using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>
/// One-call adapter for an already selected application mechanic. Selection and ambiguity remain
/// with interaction planning; this handler only translates an exact authorized request into the
/// existing application action owner.
/// </summary>
internal sealed class ApplicationActionExecutionHandler
{
    public Task<ToolEnvelope> ExecuteAsync(
        IApplicationActionRunner? actions,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        CancellationToken cancellationToken) =>
        ToolRunner.RunAsync(log, "commit", intent, "commit:application.action.execute", procedures,
            async () =>
            {
                var decision = authorization?.Authorize(PrivateOperatorCapability.Modify);
                if (decision is null || !decision.Allowed)
                    return ToolOutcome.Fail(
                        decision?.Code ?? "PRIVATE_OPERATOR_AUTHORIZATION_UNAVAILABLE",
                        "Private-operator authorization is required before an application action can execute.",
                        "query(kind: \"system.audience-context\")",
                        "Denied direct application action before payload parsing.");
                if (actions is null)
                    return ToolOutcome.Fail("APPLICATION_ACTION_UNAVAILABLE",
                        "Exact application action execution is not configured.",
                        "query(kind: \"capabilities\")",
                        "Direct application action execution was unavailable.");

                DirectApplicationActionRequest request;
                try { request = Parse(payload); }
                catch (Exception exception) when (exception is JsonException or ArgumentException
                    or InteractionContractException)
                {
                    return ToolOutcome.Fail("APPLICATION_ACTION_INPUT_INVALID",
                        exception.Message,
                        McpVerbCatalog.CommitCall("application.action.execute"),
                        "Rejected an invalid exact application action request.");
                }

                var requestFingerprint = Fingerprint(request, decision.Evidence.PrincipalReference);
                var operationId = OperationId(decision.Evidence.PrincipalReference, request.IdempotencyKey);
                var seed = BinaryPrimitives.ReadInt64BigEndian(Convert.FromHexString(requestFingerprint[..16]));
                var result = await actions.RunAsync(new(
                    request.StateSpaceId,
                    request.ApplicationId,
                    request.QualifiedMechanicId,
                    request.MechanicVersion,
                    request.ContentFingerprint,
                    request.RoleEntityIds,
                    request.InputJson,
                    seed,
                    new ApplicationEcsExecutionIdentity(operationId, requestFingerprint)), cancellationToken);

                if (!result.Successful)
                {
                    var problem = result.Problems.FirstOrDefault();
                    var rediscover = McpNextActionFactory.Create(
                        result.Disposition == ApplicationActionExecutionDisposition.Stale
                            ? "refresh-stale-mechanic" : "inspect-action-contract",
                        result.Disposition == ApplicationActionExecutionDisposition.Stale
                            ? "Rediscover the current mechanic version and content fingerprint before retrying."
                            : "Read the exact mechanic roles and input schema before retrying.",
                        "mcp.query.system.feature-search", new JsonObject
                        {
                            ["applicationId"] = request.ApplicationId.Value,
                            ["id"] = request.QualifiedMechanicId
                        }, [], "applicationId", "id");
                    var recoverFromContract = result.Disposition == ApplicationActionExecutionDisposition.Stale
                        || problem?.Code is "MISSING_REQUIRED_ROLE" or "APPLICATION_ACTION_PROJECTION_FAILED"
                        || problem?.SafeMessage.StartsWith("MISSING_REQUIRED_ROLE", StringComparison.Ordinal) == true;
                    var fix = recoverFromContract
                        ? McpNextActionFactory.Advice(rediscover)
                        : "query(kind: \"history\", tool: \"system.ecs.effects\", subject: \"interaction-step:" + requestFingerprint + "\")";
                    return new ToolOutcome(null,
                        $"Exact application action failed: {problem?.Code ?? "APPLICATION_ACTION_FAILED"}.",
                        [fix],
                        new(problem?.Code ?? "APPLICATION_ACTION_FAILED",
                            problem?.SafeMessage ?? "The exact application action failed.", fix),
                        GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence),
                        NextActions: recoverFromContract ? [rediscover] : []);
                }

                var entityRead = EntityRead(request, result.AffectedEntityIds);
                var nextActions = result.AffectedEntityIds.Count == 0
                    ? new[]
                    {
                        McpNextActionFactory.Create("inspect-receipt", "Inspect the durable execution audit.",
                            "mcp.query.history", new JsonObject
                            {
                                ["tool"] = ApplicationEcsExecutionIdentity.AuditTool,
                                ["subject"] = "interaction-step:" + requestFingerprint
                            }, [], "tool", "subject")
                    }
                    : new[]
                    {
                        McpNextActionFactory.Create("read-affected-entities", "Read the affected entities from current state.",
                            "mcp.query.entities", new JsonObject
                            {
                                ["applicationId"] = request.ApplicationId.Value,
                                ["stateSpaceId"] = request.StateSpaceId,
                                ["ids"] = new JsonArray(result.AffectedEntityIds
                                    .Select(value => (JsonNode?)JsonValue.Create(value)).ToArray())
                            }, [], "applicationId", "stateSpaceId", "ids")
                    };
                var receipt = new
                {
                    result.OperationId,
                    Disposition = result.Disposition.ToString().ToLowerInvariant(),
                    result.QualifiedMechanicId,
                    result.MechanicVersion,
                    result.ContentFingerprint,
                    result.Seed,
                    result.AppliedEffectCount,
                    Effects = result.EffectReceipts
                };
                return new ToolOutcome(new
                {
                    result.AffectedEntityIds,
                    result.Narration,
                    Receipt = receipt,
                    NextActions = nextActions
                },
                result.Disposition == ApplicationActionExecutionDisposition.Replayed
                    ? "Replayed the already committed exact application action without a second write."
                    : $"Executed one exact application mechanic and applied {result.AppliedEffectCount} effect(s).",
                [result.AffectedEntityIds.Count == 0
                    ? "query(kind: \"history\", tool: \"system.ecs.effects\")"
                    : entityRead],
                Subject: request.QualifiedMechanicId,
                GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence),
                NextActions: nextActions);
            });

    private static DirectApplicationActionRequest Parse(string payload)
    {
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });
        var root = document.RootElement;
        var names = new[] { "idempotencyKey", "applicationId", "stateSpaceId", "qualifiedMechanicId",
            "mechanicVersion", "contentFingerprint", "roleEntityIds", "input" };
        var properties = root.ValueKind == JsonValueKind.Object
            ? root.EnumerateObject().Select(value => value.Name).ToArray() : [];
        if (root.ValueKind != JsonValueKind.Object || properties.Length != names.Length
            || properties.Distinct(StringComparer.Ordinal).Count() != properties.Length
            || properties.Any(value => !names.Contains(value, StringComparer.Ordinal))
            || names.Any(value => !properties.Contains(value, StringComparer.Ordinal)))
            throw new JsonException("The exact application action requires only its eight declared fields.");
        var idempotencyKey = Text(root, "idempotencyKey", 200);
        var applicationId = ApplicationIdentifier.Parse(Text(root, "applicationId", 63));
        var stateSpaceId = Text(root, "stateSpaceId", 200);
        var mechanicId = Text(root, "qualifiedMechanicId", 200);
        if (!root.GetProperty("mechanicVersion").TryGetInt32(out var version) || version < 1)
            throw new JsonException("mechanicVersion must be a positive integer.");
        var fingerprint = Text(root, "contentFingerprint", 64);
        if (!UpperSha256(fingerprint)) throw new JsonException("contentFingerprint must be an uppercase SHA-256 value.");
        var roleElement = root.GetProperty("roleEntityIds");
        if (roleElement.ValueKind != JsonValueKind.Object) throw new JsonException("roleEntityIds must be an object.");
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in roleElement.EnumerateObject())
        {
            if (roles.Count >= 32 || property.Value.ValueKind != JsonValueKind.String
                || !roles.TryAdd(Bounded(property.Name, 200), Bounded(property.Value.GetString(), 200)))
                throw new JsonException("roleEntityIds contains an invalid or duplicate role binding.");
        }
        var input = root.GetProperty("input");
        if (input.ValueKind != JsonValueKind.Object) throw new JsonException("input must be one JSON object.");
        var inputJson = InteractionCanonicalJson.CanonicalizeObject(input.GetRawText());
        return new(idempotencyKey, applicationId, stateSpaceId, mechanicId, version,
            fingerprint, roles, inputJson);
    }

    private static string Fingerprint(DirectApplicationActionRequest request, string principal) => Hash(
        JsonSerializer.Serialize(new
        {
            principal,
            request.IdempotencyKey,
            applicationId = request.ApplicationId.Value,
            request.StateSpaceId,
            request.QualifiedMechanicId,
            request.MechanicVersion,
            request.ContentFingerprint,
            roles = request.RoleEntityIds.OrderBy(value => value.Key, StringComparer.Ordinal),
            request.InputJson
        }));

    private static string OperationId(string principal, string idempotencyKey) =>
        Hash("application.action.execute\n" + principal + "\n" + idempotencyKey)[..32].ToLowerInvariant();

    private static string EntityRead(DirectApplicationActionRequest request, IReadOnlyList<string> ids) =>
        $"query(kind: \"entities\", applicationId: {JsonSerializer.Serialize(request.ApplicationId.Value)}, "
        + $"stateSpaceId: {JsonSerializer.Serialize(request.StateSpaceId)}, ids: {JsonSerializer.Serialize(ids)})";

    private static string Text(JsonElement root, string name, int maximum) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? Bounded(value.GetString(), maximum)
            : throw new JsonException($"{name} must be a string.");

    private static string Bounded(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && value == value.Trim()
            && !value.Any(char.IsControl)
            ? value
            : throw new JsonException("A direct application action identifier is invalid or unbounded.");

    private static bool UpperSha256(string value) => value.Length == 64
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record DirectApplicationActionRequest(
        string IdempotencyKey,
        ApplicationIdentifier ApplicationId,
        string StateSpaceId,
        string QualifiedMechanicId,
        int MechanicVersion,
        string ContentFingerprint,
        IReadOnlyDictionary<string, string> RoleEntityIds,
        string InputJson);

}
