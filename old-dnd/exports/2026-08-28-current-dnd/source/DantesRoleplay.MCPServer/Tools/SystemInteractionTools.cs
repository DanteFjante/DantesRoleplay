using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Tools;

internal sealed class SystemInteractionTools
{
    public Task<ToolEnvelope> SearchAsync(
        IInteractionGateway? gateway,
        IPrivateOperatorRequestAuthorizer? privateOperator,
        IOperationLog log,
        string? applicationId,
        string? query,
        string? qualifiedId,
        int? limit,
        CancellationToken cancellationToken) =>
        RunAsync(log, "system.feature-search", PrivateOperatorCapability.Read, privateOperator,
            async _ =>
            {
                if (gateway is null) return Unavailable("system.feature-search");
                var result = await gateway.SearchFeaturesAsync(ApplicationIdentifier.Parse(applicationId ?? string.Empty),
                    query, qualifiedId, limit ?? 10, cancellationToken);
                return ToolOutcome.Ok(result, "Returned application-scoped interaction features.");
            });

    public Task<ToolEnvelope> PlanAsync(
        IInteractionGateway? gateway,
        IPrivateOperatorRequestAuthorizer? privateOperator,
        IOperationLog log,
        string? applicationId,
        string? request,
        CancellationToken cancellationToken) =>
        RunAsync(log, "system.interaction-plan", PrivateOperatorCapability.Read, privateOperator,
            async principal =>
            {
                if (gateway is null) return Unavailable("system.interaction-plan");
                using var document = ParseObject(request, "interaction plan request");
                var root = document.RootElement;
                Exact(root, "operation", "stateSpaceId", "sessionContextId", "intent", "proposal");
                var operation = RequiredString(root, "operation");
                if (operation is not ("resolve" or "submit"))
                    throw new InteractionContractException("INTERACTION_REQUEST_INVALID",
                        "Field 'operation' must be 'resolve' or 'submit'.");
                var intent = RequiredObject(root, "intent");
                var proposal = OptionalObject(root, "proposal");
                if ((operation == "resolve" && proposal is not null)
                    || (operation == "submit" && proposal is null))
                    throw new InteractionContractException("INTERACTION_REQUEST_INVALID",
                        "Resolve forbids proposal; submit requires proposal.");
                var result = await gateway.PlanAsync(principal,
                    ApplicationIdentifier.Parse(applicationId ?? string.Empty),
                    RequiredString(root, "stateSpaceId"), RequiredString(root, "sessionContextId"),
                    intent.GetRawText(), proposal?.GetRawText(),
                    cancellationToken: cancellationToken);
                return ToolOutcome.Ok(result, "Resolved an inert interaction plan.",
                    result.Proposal is null ? "query(kind: \"system.feature-search\", applicationId: \"...\", query: \"...\")"
                        : "commit(kind: \"system.interaction-execute\", payload: \"{...}\")");
            });

    public Task<ToolEnvelope> ReceiptAsync(
        IInteractionGateway? gateway,
        IPrivateOperatorRequestAuthorizer? privateOperator,
        IOperationLog log,
        string? applicationId,
        string? stateSpaceId,
        string? receiptId,
        CancellationToken cancellationToken) =>
        RunAsync(log, "system.interaction-receipt", PrivateOperatorCapability.Read, privateOperator,
            async principal =>
            {
                if (gateway is null) return Unavailable("system.interaction-receipt");
                var receipt = await gateway.GetReceiptAsync(principal,
                    ApplicationIdentifier.Parse(applicationId ?? string.Empty), stateSpaceId ?? string.Empty,
                    receiptId ?? string.Empty, cancellationToken);
                return receipt is null
                    ? ToolOutcome.Fail("INTERACTION_RECEIPT_NOT_FOUND", "The authorized receipt was not found.",
                        "query(kind: \"system.interaction-plan\", applicationId: \"...\", request: \"{...}\")",
                        "Interaction receipt was unavailable.")
                    : ToolOutcome.Ok(receipt, "Returned an authorized interaction receipt.");
            });

    public Task<ToolEnvelope> RecipesAsync(
        IInteractionRecipeStore? recipes,
        IPrivateOperatorRequestAuthorizer? privateOperator,
        IOperationLog log,
        string? applicationId,
        string? recipeId,
        string? query,
        string? status,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken) =>
        RunAsync(log, "system.interaction-recipes", PrivateOperatorCapability.Read, privateOperator,
            async _ =>
            {
                if (recipes is null) return Unavailable("system.interaction-recipes");
                var app = ApplicationIdentifier.Parse(applicationId ?? string.Empty);
                var hasId = !string.IsNullOrWhiteSpace(recipeId);
                var hasQuery = !string.IsNullOrWhiteSpace(query);
                if (hasId == hasQuery)
                    throw new InteractionContractException("RECIPE_LOOKUP_SELECTOR_REQUIRED", "Specify exactly one of id or query.");
                InteractionRecipeStatus? parsedStatus = string.IsNullOrWhiteSpace(status)
                    ? null : InteractionRecipeStatusNames.Parse(status.Trim().ToLowerInvariant());
                if (hasId)
                {
                    if (!string.IsNullOrWhiteSpace(cursor))
                        throw new InteractionContractException("RECIPE_CURSOR_INVALID", "An exact recipe lookup does not accept a cursor.");
                    var item = await recipes.GetAsync(app, recipeId!, cancellationToken);
                    if (item is null || parsedStatus is not null && item.Status != parsedStatus)
                        return ToolOutcome.Fail("RECIPE_NOT_FOUND", "The recipe was not found.",
                            "query(kind: \"system.interaction-recipes\", applicationId: \"...\", query: \"...\")",
                            "Recipe was unavailable.");
                    return ToolOutcome.Ok(new { items = new[] { item }, nextCursor = (string?)null }, "Returned one private recipe projection.");
                }
                var pageSize = limit ?? 20;
                if (pageSize is < 1 or > 50)
                    throw new InteractionContractException("INVALID_RECIPE_LIMIT", "The recipe page size is outside the closed range.");
                var offset = DecodeRecipeCursor(cursor, app, query!, parsedStatus);
                var page = await recipes.SearchPageAsync(app, query!, parsedStatus, offset, pageSize, cancellationToken);
                if (offset > page.Total)
                    throw new InteractionContractException("RECIPE_CURSOR_STALE", "The recipe cursor is stale.");
                var items = page.Items;
                var nextOffset = offset + items.Count;
                var nextCursor = nextOffset < page.Total
                    ? EncodeRecipeCursor(app, query!, parsedStatus, nextOffset) : null;
                return ToolOutcome.Ok(new { items, nextCursor }, "Returned private application-scoped recipes.");
            });

    public Task<ToolEnvelope> ReviewRecipeAsync(
        IInteractionRecipeReviewService? reviews,
        IPrivateOperatorRequestAuthorizer? privateOperator,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        CancellationToken cancellationToken) =>
        ToolRunner.RunAsync(log, "commit", intent, "commit:system.interaction-recipe-review", procedures,
            async () =>
            {
                try
                {
                    var authorization = Authorize(privateOperator, PrivateOperatorCapability.Modify);
                    if (authorization.Problem is not null) return authorization.Problem;
                    if (reviews is null) return Unavailable("system.interaction-recipe-review");
                    using var document = ParseObject(payload, "interaction recipe review");
                    var root = document.RootElement;
                    Exact(root, "requestToken", "applicationId", "recipeId", "expectedVersion", "decision", "reason");
                    if (!root.TryGetProperty("expectedVersion", out var version) || !version.TryGetInt32(out var expectedVersion))
                        throw new InteractionContractException("INTERACTION_REQUEST_INVALID", "Field 'expectedVersion' must be an integer.");
                    var result = await reviews.ReviewAsync(new(
                        RequiredString(root, "requestToken"),
                        ApplicationIdentifier.Parse(RequiredString(root, "applicationId")),
                        RequiredString(root, "recipeId"), expectedVersion,
                        RequiredString(root, "decision"), RequiredString(root, "reason"),
                        authorization.Principal!.PrincipalId), cancellationToken);
                    return result.Disposition == InteractionRecipeWriteDisposition.Conflict
                        ? ToolOutcome.Fail(result.Code, "The recipe review conflicted with current state.",
                            "query(kind: \"system.interaction-recipes\", applicationId: \"...\", id: \"...\")",
                            "Recipe review was not applied.")
                        : ToolOutcome.Ok(result, "The recipe review was recorded.");
                }
                catch (Exception exception) when (IsRequestException(exception))
                { return ContractFailure(exception); }
            }, consumesReadEvidence: true);

    public Task<ToolEnvelope> ExecuteAsync(
        IInteractionGateway? gateway,
        IPrivateOperatorRequestAuthorizer? privateOperator,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        CancellationToken cancellationToken) =>
        ToolRunner.RunAsync(log, "commit", intent, "commit:system.interaction-execute", procedures,
            async () =>
            {
                try
                {
                    var authorization = Authorize(privateOperator, PrivateOperatorCapability.Modify);
                    if (authorization.Problem is not null) return authorization.Problem;
                    if (gateway is null) return Unavailable("system.interaction-execute");
                    using var document = ParseObject(payload, "interaction execution request");
                    var root = document.RootElement;
                    Exact(root, "applicationId", "stateSpaceId", "resolutionReceiptId", "proposalFingerprint",
                        "idempotencyKey", "proposal", "stopOnFailure", "learn", "learningIntent");
                    var proposal = RequiredObject(root, "proposal");
                    var learn = OptionalBoolean(root, "learn") ?? false;
                    var learningIntent = OptionalObject(root, "learningIntent");
                    if (learn != (learningIntent is not null))
                        throw new InteractionContractException(learn ? "LEARNING_INTENT_REQUIRED" : "LEARNING_INTENT_FORBIDDEN",
                            learn ? "learningIntent is required when learn is true." : "learningIntent is forbidden when learn is false.");
                    var resolutionReceiptId = RequiredString(root, "resolutionReceiptId");
                    var proposalFingerprint = RequiredString(root, "proposalFingerprint");
                    var idempotencyKey = RequiredString(root, "idempotencyKey");
                    var stopOnFailure = OptionalBoolean(root, "stopOnFailure") ?? true;
                    var closedRequest = learn
                        ? JsonSerializer.Serialize(new { resolutionReceiptId, proposalFingerprint, idempotencyKey,
                            proposal, stopOnFailure, learn = true, learningIntent })
                        : JsonSerializer.Serialize(new { resolutionReceiptId, proposalFingerprint, idempotencyKey,
                            proposal, stopOnFailure, learn = false });
                    var result = await gateway.ExecuteAsync(authorization.Principal!,
                        ApplicationIdentifier.Parse(RequiredString(root, "applicationId")),
                        RequiredString(root, "stateSpaceId"), closedRequest, cancellationToken);
                    return result.Successful
                        ? ToolOutcome.Ok(result, result.SafeSummary,
                            $"query(kind: \"system.interaction-receipt\", applicationId: \"{RequiredString(root, "applicationId")}\", stateSpaceId: \"{RequiredString(root, "stateSpaceId")}\", id: \"{result.Receipt!.Receipt!.Id}\")")
                        : ToolOutcome.Fail(result.Code, result.SafeSummary,
                            "query(kind: \"system.interaction-receipt\", applicationId: \"...\", stateSpaceId: \"...\", id: \"...\")",
                            result.SafeSummary);
                }
                catch (Exception exception) when (IsRequestException(exception))
                { return ContractFailure(exception); }
            }, consumesReadEvidence: true);

    private static Task<ToolEnvelope> RunAsync(
        IOperationLog log,
        string kind,
        PrivateOperatorCapability capability,
        IPrivateOperatorRequestAuthorizer? privateOperator,
        Func<TrustedPrincipalContext, Task<ToolOutcome>> body) =>
        ToolRunner.RunAsync(log, "query", async () =>
        {
            try
            {
                var authorization = Authorize(privateOperator, capability);
                return authorization.Problem ?? await body(authorization.Principal!);
            }
            catch (Exception exception) when (IsRequestException(exception))
            { return ContractFailure(exception); }
        });

    private static bool IsRequestException(Exception exception) =>
        exception is InteractionContractException or ArgumentException or JsonException;

    private static ToolOutcome ContractFailure(Exception exception) => ToolOutcome.Fail(
        exception is InteractionContractException contract ? contract.Code : "INTERACTION_REQUEST_INVALID",
        exception.Message,
        "query(kind: \"capabilities\")",
        "Rejected an invalid interaction request.");

    private static (TrustedPrincipalContext? Principal, ToolOutcome? Problem) Authorize(
        IPrivateOperatorRequestAuthorizer? privateOperator,
        PrivateOperatorCapability capability)
    {
        var decision = privateOperator?.Authorize(capability);
        if (decision is null || !decision.Allowed)
            return (null, ToolOutcome.Fail(decision?.Code ?? "PRIVATE_OPERATOR_REQUIRED",
                "A verified private operator is required.", "Call this operation through the local private host.",
                "Rejected unauthorized interaction operation."));
        try
        {
            return (TrustedPrincipalContext.VerifiedPrincipal(decision.Evidence.PrincipalReference,
                decision.Evidence.AuthenticationMethod), null);
        }
        catch (ArgumentException)
        {
            return (null, ToolOutcome.Fail("PRIVATE_OPERATOR_EVIDENCE_INVALID",
                "The private operator evidence is invalid.", "Retry through the local private host.",
                "Rejected invalid private operator evidence."));
        }
    }

    private static ToolOutcome Unavailable(string kind) => ToolOutcome.Fail("INTERACTION_COMPONENT_UNAVAILABLE",
        "The interaction component is unavailable.", "query(kind: \"capabilities\")", $"{kind} is unavailable.");

    private static JsonDocument ParseObject(string? json, string label)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InteractionContractException("INTERACTION_REQUEST_REQUIRED", $"The {label} is required.");
        var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new InteractionContractException("INTERACTION_REQUEST_INVALID", $"The {label} must be an object.");
        }
        return document;
    }

    private static void Exact(JsonElement root, params string[] names)
    {
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        var extra = root.EnumerateObject().Select(property => property.Name).FirstOrDefault(name => !allowed.Contains(name));
        if (extra is not null)
            throw new InteractionContractException("INTERACTION_REQUEST_INVALID", $"Field '{extra}' is not supported.");
    }

    private static string RequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InteractionContractException("INTERACTION_REQUEST_INVALID", $"Field '{name}' is required.");

    private static JsonElement RequiredObject(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InteractionContractException("INTERACTION_REQUEST_INVALID", $"Field '{name}' must be an object.");

    private static JsonElement? OptionalObject(JsonElement root, string name) =>
        !root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? null
            : value.ValueKind == JsonValueKind.Object ? value
            : throw new InteractionContractException("INTERACTION_REQUEST_INVALID", $"Field '{name}' must be an object.");

    private static bool? OptionalBoolean(JsonElement root, string name) =>
        !root.TryGetProperty(name, out var value) ? null
            : value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean()
            : throw new InteractionContractException("INTERACTION_REQUEST_INVALID", $"Field '{name}' must be boolean.");

    private static string EncodeRecipeCursor(
        ApplicationIdentifier applicationId,
        string query,
        InteractionRecipeStatus? status,
        int offset)
    {
        var payload = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            applicationId = applicationId.Value,
            query,
            status = status is null ? null : InteractionRecipeStatusNames.Get(status.Value),
            offset
        }));
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "dantes-roleplay/interaction-recipe-cursor/v1\0" + payload)));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { payload, signature })))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static int DecodeRecipeCursor(
        string? cursor,
        ApplicationIdentifier applicationId,
        string query,
        InteractionRecipeStatus? status)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return 0;
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(base64));
            var root = document.RootElement;
            Exact(root, "payload", "signature");
            var payload = RequiredString(root, "payload");
            var signature = RequiredString(root, "signature");
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                "dantes-roleplay/interaction-recipe-cursor/v1\0" + payload)));
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(signature), Encoding.ASCII.GetBytes(expected)))
                throw new FormatException();
            using var value = JsonDocument.Parse(payload);
            Exact(value.RootElement, "applicationId", "offset", "query", "status");
            var expectedStatus = status is null ? null : InteractionRecipeStatusNames.Get(status.Value);
            var actualStatus = value.RootElement.GetProperty("status").ValueKind == JsonValueKind.Null
                ? null : value.RootElement.GetProperty("status").GetString();
            if (RequiredString(value.RootElement, "applicationId") != applicationId.Value
                || RequiredString(value.RootElement, "query") != query
                || actualStatus != expectedStatus
                || !value.RootElement.GetProperty("offset").TryGetInt32(out var offset) || offset < 0 || offset > 10_000)
                throw new FormatException();
            return offset;
        }
        catch (Exception exception) when (exception is not InteractionContractException)
        {
            throw new InteractionContractException("RECIPE_CURSOR_INVALID", "The recipe cursor is invalid.");
        }
    }
}
