using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Tools;

internal sealed class SystemKnowledgeStateTools
{
    public Task<ToolEnvelope> SynchronizeAsync(
        IReviewedKnowledgeStateSynchronizer? synchronization,
        IPrivateOperatorRequestAuthorizer? authorization,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        const string kind = "system.knowledge-state.sync";
        return ToolRunner.RunAsync(log, "commit", intent, $"commit:{kind}", procedures, async () =>
        {
            var decision = Authorize(authorization);
            if (!decision.Allowed)
                return Fail(decision, decision.Code,
                    "Private-operator authentication is required before knowledge-state synchronization.",
                    "query(kind: \"capabilities\")",
                    "Denied reviewed knowledge-state synchronization before payload parsing.");
            if (synchronization is null)
                return Fail(decision, "KNOWLEDGE_SYNC_UNAVAILABLE",
                    "Reviewed knowledge-state synchronization is not configured.",
                    "query(kind: \"capabilities\")",
                    "Reviewed knowledge-state synchronization was unavailable.");

            ReviewedKnowledgeStateSyncRequest request;
            try { request = Parse(payload); }
            catch (JsonException exception)
            {
                return Fail(decision, "INVALID_PAYLOAD", exception.Message,
                    VerbSurface.CommitCall(kind, dryRun: true),
                    "Rejected an invalid reviewed knowledge-state payload.");
            }

            var result = await synchronization.SynchronizeAsync(request, dryRun, cancellationToken);
            if (!result.Accepted)
                return Fail(decision, result.ErrorCode,
                    "The reviewed knowledge-state manifest was rejected without changing campaign state.",
                    VerbSurface.CommitCall(kind, dryRun: true),
                    $"Rejected reviewed knowledge-state synchronization: {result.ErrorCode}.");

            return new ToolOutcome(new
            {
                result.DryRun,
                result.Replayed,
                result.ReviewedCount,
                result.ChangedCount,
                EffectOperationId = result.OperationId
            },
            result.DryRun
                ? $"Validated {result.ReviewedCount} reviewed knowledge-state entries; {result.ChangedCount} require change."
                : $"Synchronized {result.ChangedCount} of {result.ReviewedCount} reviewed knowledge-state entries.",
            [result.DryRun ? CommitCall(payload) : "query(kind: \"history\", tool: \"system.ecs.effects\")"],
            GuardEvidenceJson: JsonSerializer.Serialize(decision.Evidence));
        });
    }

    private static ReviewedKnowledgeStateSyncRequest Parse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw Invalid("payload must be a JSON object.");
        RequireProperties(root, ["requestToken", "campaignId", "entries"]);
        var token = Text(root, "requestToken", 200);
        var campaign = Text(root, "campaignId", 200);
        var entriesElement = root.GetProperty("entries");
        if (entriesElement.ValueKind != JsonValueKind.Array) throw Invalid("entries must be an array.");
        var entries = entriesElement.EnumerateArray().Select(entry =>
        {
            if (entry.ValueKind != JsonValueKind.Object) throw Invalid("Every entry must be an object.");
            RequireProperties(entry, ["knowledgeId", "state"]);
            return new ReviewedKnowledgeStateEntry(
                Text(entry, "knowledgeId", 200), Text(entry, "state", 100));
        }).ToArray();
        if (entries.Length is < 1 or > 128 ||
            entries.Select(value => value.KnowledgeId).Distinct(StringComparer.Ordinal).Count() != entries.Length)
            throw Invalid("entries must contain 1 through 128 unique knowledge IDs.");
        return new(token, campaign, entries);
    }

    private static void RequireProperties(JsonElement value, IReadOnlyList<string> required)
    {
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != names.Distinct(StringComparer.Ordinal).Count() ||
            names.Except(required, StringComparer.Ordinal).Any() ||
            required.Except(names, StringComparer.Ordinal).Any())
            throw Invalid($"Object must contain exactly: {string.Join(", ", required)}.");
    }

    private static string Text(JsonElement value, string name, int maximum)
    {
        var element = value.GetProperty(name);
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()) ||
            element.GetString() != element.GetString()!.Trim() || element.GetString()!.Length > maximum ||
            element.GetString()!.Any(char.IsWhiteSpace))
            throw Invalid($"{name} must be one bounded token without whitespace.");
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
        $"commit(kind: \"system.knowledge-state.sync\", payload: {JsonSerializer.Serialize(payload)})";
}
