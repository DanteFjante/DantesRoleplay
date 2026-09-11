using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Mcp;

internal sealed class SystemApplicationObjectHandler
{
    public Task<ToolEnvelope> ReadAsync(
        IApplicationReadModelService? readModels,
        IPublicApplicationCatalogProvider? catalogs,
        IApplicationQueryRoleBindingResolver? roleResolver,
        IApplicationQueryAuthorizedContextProvider? authorizedContext,
        IStateSpaceRegistry? stateSpaces,
        IEntityComponentStore? entities,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string? applicationId,
        string? stateSpaceId,
        string? qualifiedQueryId,
        string? entityId,
        string? request,
        CancellationToken cancellationToken) => ToolRunner.RunAsync(log, "query", async () =>
    {
        var decision = authorization?.Authorize(PrivateOperatorCapability.Read)
            ?? new PrivateOperatorAuthorizationPolicy().Evaluate(new(
                TrustedPrincipalContext.Unauthenticated("MCP_PRIVATE_OPERATOR_REQUIRED"),
                PrivateOperatorCapability.Read,
                PrivateOperatorAuthorizationPolicy.PrivateHostScope,
                "mcp-request"));
        if (!decision.Allowed)
            return Fail(decision, decision.Code, "Private-operator authentication is required.");
        if (readModels is null || catalogs is null || roleResolver is null
            || stateSpaces is null || entities is null)
            return Fail(decision, "APPLICATION_OBJECT_UNAVAILABLE",
                "Registered application object reads are unavailable.");

        try
        {
            var application = ApplicationIdentifier.Parse(applicationId ?? string.Empty);
            if (!Token(stateSpaceId) || !Token(qualifiedQueryId) || !Token(entityId))
                return Fail(decision, "APPLICATION_OBJECT_REQUEST_INVALID", "The object read request is invalid.");
            var stateSpace = stateSpaces.Get(stateSpaceId!);
            if (stateSpace is null || stateSpace.ApplicationRevision.ApplicationId != application)
                return Fail(decision, "APPLICATION_OBJECT_FORBIDDEN", "The application state space is unavailable.");
            var parsed = ParseReadRequest(request);
            if (!catalogs.TryGet(application, out var catalog))
                return Fail(decision, "APPLICATION_OBJECT_UNAVAILABLE", "The application catalog is unavailable.");
            var contract = ApplicationQueryContract.Parse(catalog.Inspect(new(
                application, application.Value, qualifiedQueryId!)).ContentJson, application);
            if (contract.Id != qualifiedQueryId || contract.Status != "active"
                || !contract.IsObjectProjection || contract.RoleBindings is null)
                return Fail(decision, "APPLICATION_OBJECT_UNKNOWN", "The registered object query is unavailable.");

            var needsAuthorizedContext = contract.RoleBindings.Values.Any(
                value => value.Source == "authorized-context");
            if (needsAuthorizedContext && authorizedContext is null)
                return Fail(decision, "APPLICATION_OBJECT_FORBIDDEN", "A trusted role binding is unavailable.");
            var authorizedRoles = needsAuthorizedContext
                ? await authorizedContext!.ResolveAsync(application, stateSpaceId!, cancellationToken)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            if (authorizedRoles is null)
                return Fail(decision, "APPLICATION_OBJECT_FORBIDDEN", "A trusted role binding is unavailable.");
            var roles = roleResolver.Resolve(contract, parsed.InputJson,
                new(entityId!, authorizedRoles));
            foreach (var roleEntityId in roles.Values.Distinct(StringComparer.Ordinal))
                if (await entities.GetEntityAsync(stateSpaceId!, roleEntityId, cancellationToken) is null)
                    return Fail(decision, "APPLICATION_OBJECT_FORBIDDEN", "A declared role entity is unavailable.");
            var result = await readModels.ReadAsync(new(
                stateSpaceId!, application, qualifiedQueryId!, roles,
                Audience: ApplicationObjectHostAccess.PrivilegedReadAudience,
                InputJson: parsed.InputJson, Cursor: parsed.Cursor, PageSize: parsed.PageSize)
            { IncludeObjectReadEvidence = true }, cancellationToken);
            using var data = JsonDocument.Parse(result.DataJson);
            return new(new
            {
                result.ApplicationId,
                result.StateSpaceId,
                result.QualifiedQueryId,
                result.StateSpaceFingerprint,
                result.ResolutionFingerprint,
                result.OutputSchemaHash,
                result.ResultFingerprint,
                result.SourceRevisionFingerprint,
                data = data.RootElement.Clone(),
                sourceEvidence = result.ObjectReadEvidence
            }, "Returned one exact registered application object.", [],
                GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException
            or JsonException or ApplicationReadModelException)
        {
            var code = exception switch
            {
                ApplicationReadModelException read when read.Code is "READ_MODEL_INPUT_INVALID"
                    or "READ_MODEL_REQUEST_INVALID" => "APPLICATION_OBJECT_REQUEST_INVALID",
                ApplicationReadModelException read when read.Code.Contains("FORBIDDEN", StringComparison.Ordinal)
                    || read.Code == "READ_MODEL_ROLES_UNAVAILABLE" => "APPLICATION_OBJECT_FORBIDDEN",
                ApplicationReadModelException read when read.Code.Contains("STALE", StringComparison.Ordinal)
                    => "APPLICATION_OBJECT_STALE",
                KeyNotFoundException => "APPLICATION_OBJECT_UNKNOWN",
                _ => "APPLICATION_OBJECT_UNAVAILABLE"
            };
            return Fail(decision, code, "The registered application object could not be read.");
        }
    });

    private static ReadRequest ParseReadRequest(string? json)
    {
        json = string.IsNullOrWhiteSpace(json) ? "{}" : json;
        if (Encoding.UTF8.GetByteCount(json) > 70_000) throw new JsonException();
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 40
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!names.Add(property.Name) || property.Name is not ("input" or "cursor" or "pageSize"))
                throw new JsonException();
        var input = root.TryGetProperty("input", out var inputElement)
            ? inputElement.ValueKind == JsonValueKind.Object ? inputElement.GetRawText() : throw new JsonException()
            : "{}";
        var cursor = root.TryGetProperty("cursor", out var cursorElement)
            ? cursorElement.ValueKind == JsonValueKind.Null ? null
                : cursorElement.ValueKind == JsonValueKind.String ? cursorElement.GetString() : throw new JsonException()
            : null;
        int? pageSize = root.TryGetProperty("pageSize", out var pageSizeElement)
            ? pageSizeElement.TryGetInt32(out var value) ? value : throw new JsonException()
            : null;
        if (cursor is { Length: > 2_048 } || pageSize is < 1 or > 500) throw new JsonException();
        return new(input, cursor, pageSize);
    }

    private static ToolOutcome Fail(
        PrivateOperatorAuthorizationDecision decision,
        string code,
        string message) => new(null, message, ["query(kind: \"capabilities\")"],
            new(code, message, "Inspect the registered object capability and retry."),
            GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));

    private static bool Token(string? value) => value is { Length: > 0 and <= 200 }
        && value == value.Trim() && !value.Any(char.IsControl) && !value.Any(char.IsWhiteSpace);
    private sealed record ReadRequest(string InputJson, string? Cursor, int? PageSize);
}
