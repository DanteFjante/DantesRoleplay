using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;

namespace DantesRoleplay.MCPServer.Mcp;

internal sealed class SystemApplicationObjectSubmitHandler
{
    public Task<ToolEnvelope> SubmitAsync(
        IApplicationObjectWriteService? writes,
        IPublicApplicationCatalogProvider? catalogs,
        IApplicationQueryRoleBindingResolver? roleResolver,
        IApplicationQueryAuthorizedContextProvider? authorizedContext,
        IStateSpaceRegistry? stateSpaces,
        IEntityComponentStore? entities,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        CancellationToken cancellationToken) => ToolRunner.RunAsync(
            log, "commit", intent, "commit:system.application-object.submit", procedures, async () =>
    {
        var decision = authorization?.Authorize(PrivateOperatorCapability.Modify)
            ?? new PrivateOperatorAuthorizationPolicy().Evaluate(new(
                TrustedPrincipalContext.Unauthenticated("MCP_PRIVATE_OPERATOR_REQUIRED"),
                PrivateOperatorCapability.Modify,
                PrivateOperatorAuthorizationPolicy.PrivateHostScope,
                "mcp-request"));
        if (!decision.Allowed)
            return Fail(decision, decision.Code, "Private-operator authorization is required.");
        if (writes is null || catalogs is null || roleResolver is null
            || stateSpaces is null || entities is null)
            return Fail(decision, "APPLICATION_OBJECT_UNAVAILABLE",
                "Registered application object submission is unavailable.");

        try
        {
            var submitted = Parse(payload);
            var application = ApplicationIdentifier.Parse(submitted.ApplicationId);
            var stateSpace = stateSpaces.Get(submitted.StateSpaceId);
            if (stateSpace is null || stateSpace.ApplicationRevision.ApplicationId != application)
                return Fail(decision, "APPLICATION_OBJECT_FORBIDDEN", "The application state space is unavailable.");
            if (!catalogs.TryGet(application, out var catalog))
                return Fail(decision, "APPLICATION_OBJECT_UNAVAILABLE", "The application catalog is unavailable.");
            var contract = ApplicationQueryContract.Parse(catalog.Inspect(new(
                application, application.Value, submitted.QualifiedQueryId)).ContentJson, application);
            if (contract.Id != submitted.QualifiedQueryId || contract.Status != "active"
                || !contract.IsObjectProjection || contract.ObjectCollectionId is null
                || contract.RoleBindings is null)
                return Fail(decision, "APPLICATION_OBJECT_UNKNOWN", "The registered object query is unavailable.");

            var needsAuthorizedContext = contract.RoleBindings.Values.Any(
                value => value.Source == "authorized-context");
            if (needsAuthorizedContext && authorizedContext is null)
                return Fail(decision, "APPLICATION_OBJECT_FORBIDDEN", "A trusted role binding is unavailable.");
            var authorizedRoles = needsAuthorizedContext
                ? await authorizedContext!.ResolveAsync(application, submitted.StateSpaceId, cancellationToken)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            if (authorizedRoles is null)
                return Fail(decision, "APPLICATION_OBJECT_FORBIDDEN", "A trusted role binding is unavailable.");
            var roles = roleResolver.Resolve(contract, submitted.InputJson,
                new(submitted.EntityId, authorizedRoles));
            foreach (var entityId in roles.Values.Distinct(StringComparer.Ordinal))
                if (await entities.GetEntityAsync(submitted.StateSpaceId, entityId, cancellationToken) is null)
                    return Fail(decision, "APPLICATION_OBJECT_FORBIDDEN", "A declared role entity is unavailable.");

            var request = new ApplicationObjectWriteRequest(
                submitted.StateSpaceId,
                application,
                new(contract.ProjectionQualifiedId, contract.ProjectionVersion,
                    contract.ProjectionContentHash),
                roles,
                contract.ObjectCollectionId,
                ApplicationObjectHostAccess.PrivilegedWritePerspective,
                submitted.IdempotencyKey,
                submitted.ExpectedSourceRevisionFingerprint,
                submitted.Mode == "changes" ? submitted.ValueJson : "{}",
                submitted.RelationshipEdits)
            {
                SubmissionMode = submitted.Mode,
                SubmittedObjectJson = submitted.Mode == "object" ? submitted.ValueJson : "{}"
            };
            var result = await writes.WriteAsync(request, cancellationToken);
            using var data = JsonDocument.Parse(result.OutputJson);
            return new(new
            {
                applicationId = application.Value,
                submitted.StateSpaceId,
                submitted.QualifiedQueryId,
                result.Applied,
                result.Replayed,
                result.NoOp,
                result.OperationId,
                result.SourceRevisionFingerprint,
                data = data.RootElement.Clone()
            }, result.Replayed ? "Replayed the registered application object submission."
                : result.NoOp ? "The registered application object was unchanged."
                : "Applied the registered application object submission.", [],
                GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException
            or JsonException or ApplicationReadModelException or ApplicationObjectWriteException)
        {
            var code = exception switch
            {
                ApplicationObjectWriteException write when write.Code == "OBJECT_WRITE_SOURCE_STALE"
                    => "APPLICATION_OBJECT_STALE",
                ApplicationObjectWriteException write when write.Code == "OBJECT_WRITE_IDEMPOTENCY_CONFLICT"
                    => "APPLICATION_OBJECT_IDEMPOTENCY_CONFLICT",
                ApplicationObjectWriteException write when write.Code == "OBJECT_WRITE_FORBIDDEN"
                    => "APPLICATION_OBJECT_FORBIDDEN",
                ApplicationObjectWriteException write when write.Code == "OBJECT_WRITE_READ_ONLY_CHANGED"
                    => "APPLICATION_OBJECT_READ_ONLY_CHANGED",
                ApplicationReadModelException read when read.Code is "READ_MODEL_INPUT_INVALID"
                    or "READ_MODEL_REQUEST_INVALID" => "APPLICATION_OBJECT_REQUEST_INVALID",
                ApplicationReadModelException read when read.Code.Contains("FORBIDDEN", StringComparison.Ordinal)
                    || read.Code == "READ_MODEL_ROLES_UNAVAILABLE" => "APPLICATION_OBJECT_FORBIDDEN",
                KeyNotFoundException => "APPLICATION_OBJECT_UNKNOWN",
                _ => "APPLICATION_OBJECT_REQUEST_INVALID"
            };
            return Fail(decision, code, "The registered application object submission was rejected.");
        }
    }, consumesReadEvidence: false);

    private static SubmittedObject Parse(string json)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > 131_072) throw new JsonException();
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "applicationId", "stateSpaceId", "qualifiedQueryId", "entityId", "idempotencyKey",
            "expectedSourceRevisionFingerprint", "input", "mode", "object", "changes", "relationshipEdits"
        };
        var properties = root.EnumerateObject().ToArray();
        if (properties.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length
            || properties.Any(value => !allowed.Contains(value.Name))) throw new JsonException();
        var applicationId = String(root, "applicationId");
        var stateSpaceId = String(root, "stateSpaceId");
        var queryId = String(root, "qualifiedQueryId");
        var entityId = String(root, "entityId");
        var key = String(root, "idempotencyKey", 128);
        var expected = String(root, "expectedSourceRevisionFingerprint", 64);
        if (expected.Length != 64 || expected.Any(value => !char.IsAsciiDigit(value)
                && value is not (>= 'A' and <= 'F'))) throw new JsonException();
        var mode = String(root, "mode", 16);
        var valueName = mode switch { "object" => "object", "changes" => "changes", _ => throw new JsonException() };
        var otherName = mode == "object" ? "changes" : "object";
        if (!root.TryGetProperty(valueName, out var value) || value.ValueKind != JsonValueKind.Object
            || root.TryGetProperty(otherName, out _)) throw new JsonException();
        var input = root.TryGetProperty("input", out var inputValue)
            ? inputValue.ValueKind == JsonValueKind.Object ? inputValue.GetRawText() : throw new JsonException()
            : "{}";
        var relationships = new List<ApplicationObjectRelationshipEdit>();
        if (root.TryGetProperty("relationshipEdits", out var edits))
        {
            if (edits.ValueKind != JsonValueKind.Array || edits.GetArrayLength() > 32) throw new JsonException();
            foreach (var edit in edits.EnumerateArray())
            {
                if (edit.ValueKind != JsonValueKind.Object) throw new JsonException();
                var names = edit.EnumerateObject().Select(item => item.Name).ToArray();
                if (names.Length != 4 || names.Distinct(StringComparer.Ordinal).Count() != 4
                    || names.Any(name => name is not ("path" or "operation" or "targetEntityId" or "expectedRevision")))
                    throw new JsonException();
                if (!edit.TryGetProperty("expectedRevision", out var revision) || !revision.TryGetInt32(out var expectedRevision))
                    throw new JsonException();
                relationships.Add(new(String(edit, "path", 1_000), String(edit, "operation", 32),
                    String(edit, "targetEntityId"), expectedRevision));
            }
        }
        return new(applicationId, stateSpaceId, queryId, entityId, key, expected,
            input, mode, value.GetRawText(), relationships);
    }

    private static string String(JsonElement value, string name, int maximum = 200)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            throw new JsonException();
        var result = property.GetString()!;
        if (string.IsNullOrWhiteSpace(result) || result != result.Trim() || result.Length > maximum
            || result.Any(char.IsControl)) throw new JsonException();
        return result;
    }

    private static ToolOutcome Fail(
        PrivateOperatorAuthorizationDecision decision,
        string code,
        string message) => new(null, message, ["query(kind: \"capabilities\")"],
            new(code, message, "Inspect the registered object capability and retry."),
            GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));

    private sealed record SubmittedObject(
        string ApplicationId,
        string StateSpaceId,
        string QualifiedQueryId,
        string EntityId,
        string IdempotencyKey,
        string ExpectedSourceRevisionFingerprint,
        string InputJson,
        string Mode,
        string ValueJson,
        IReadOnlyList<ApplicationObjectRelationshipEdit> RelationshipEdits);
}

/// <summary>Compatibility translation for the existing application-object access contract.</summary>
internal static class ApplicationObjectHostAccess
{
    internal static bool CanWrite(LocalKnowledgeSeatSnapshot seat) =>
        seat.Enabled && seat.Role == KnowledgeAudienceRole.GameMaster;

    internal static bool RequiresAuthorizedEntityGrant(LocalKnowledgeSeatSnapshot seat) =>
        !seat.Enabled || seat.Role != KnowledgeAudienceRole.GameMaster;

    internal static bool CanReadRoleEntity(
        LocalKnowledgeSeatSnapshot seat,
        string entityId,
        IReadOnlyDictionary<string, string> authorizedRoleEntityIds) =>
        seat.Enabled && (seat.Role == KnowledgeAudienceRole.GameMaster
            || seat.Role == KnowledgeAudienceRole.Actor
            && (entityId == seat.ActorId
                || authorizedRoleEntityIds.Values.Contains(entityId, StringComparer.Ordinal)));

    internal static MechanicAudienceContext PrivilegedReadAudience => MechanicAudienceContext.GameMaster;
    internal static string PrivilegedWritePerspective => MechanicAudienceContext.GameMaster.Perspective;
}
