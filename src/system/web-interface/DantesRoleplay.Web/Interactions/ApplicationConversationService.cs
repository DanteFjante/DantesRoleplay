using System.Collections.Concurrent;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.Web.Interactions;

public static class ApplicationConversationTasks
{
    public const string OuterTurn = InteractionOuterProtocol.OuterTurnTask;
    public const string Narration = InteractionOuterProtocol.NarrationTask;
    public const string OuterTurnSchema = InteractionOuterProtocol.OuterTurnSchemaName;
    public const string NarrationSchema = InteractionOuterProtocol.NarrationSchemaName;
}

public sealed record ApplicationConversationMessage(
    int Ordinal, string Role, string Text, DateTime CreatedAtUtc, string? Code = null);

public sealed record ApplicationConversationView(
    string Id,
    ApplicationIdentifier ApplicationId,
    string StateSpaceId,
    string SessionContextId,
    string Status,
    IReadOnlyList<ApplicationConversationMessage> Messages,
    InteractionPlanGatewayResult? PendingPlan,
    InteractionExecutionOutcome? LastExecution,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ApplicationConversationCreateRequest(string StateSpaceId, string? SessionContextId = null);
public sealed record ApplicationConversationTurnRequest(string Text);
public sealed record ApplicationConversationExecuteRequest(string? IdempotencyKey = null, bool Learn = false);

public sealed class ApplicationConversationStore
{
    public const int MaximumConversations = 128;
    public const int MaximumMessages = 64;
    public const int MaximumConversationBytes = 64 * 1024;
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromHours(2);
    internal ConcurrentDictionary<string, ApplicationConversationEntry> Entries { get; } = new(StringComparer.Ordinal);
    private readonly object sync = new();

    internal void RemoveExpired(DateTime nowUtc)
    {
        lock (sync)
        {
            foreach (var value in Entries)
                if (nowUtc - value.Value.UpdatedAtUtc > IdleLifetime)
                    Entries.TryRemove(value.Key, out _);
        }
    }

    internal bool TryAdd(ApplicationConversationEntry entry)
    {
        lock (sync)
        {
            foreach (var value in Entries)
                if (DateTime.UtcNow - value.Value.UpdatedAtUtc > IdleLifetime)
                    Entries.TryRemove(value.Key, out _);
            return Entries.Count < MaximumConversations && Entries.TryAdd(entry.Id, entry);
        }
    }

    internal ApplicationConversationEntry? GetCurrent(
        string id, string principalId, ApplicationIdentifier applicationId)
    {
        lock (sync)
        {
            if (!Entries.TryGetValue(id, out var entry)
                || entry.PrincipalId != principalId || entry.ApplicationId != applicationId)
                return null;
            var now = DateTime.UtcNow;
            if (now - entry.UpdatedAtUtc > IdleLifetime)
            {
                Entries.TryRemove(id, out _);
                return null;
            }
            entry.UpdatedAtUtc = now;
            return entry;
        }
    }
}

internal sealed class ApplicationConversationEntry(
    string id,
    string principalId,
    ApplicationIdentifier applicationId,
    string stateSpaceId,
    string sessionContextId)
{
    public string Id { get; } = id;
    public string PrincipalId { get; } = principalId;
    public ApplicationIdentifier ApplicationId { get; } = applicationId;
    public string StateSpaceId { get; } = stateSpaceId;
    public string SessionContextId { get; } = sessionContextId;
    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "ready";
    public List<ApplicationConversationMessage> Messages { get; } = [];
    public InteractionPlanGatewayResult? PendingPlan { get; set; }
    public string? PendingProposalJson { get; set; }
    public string? PendingIntentJson { get; set; }
    public InteractionExecutionOutcome? LastExecution { get; set; }
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public ApplicationConversationView View() => new(Id, ApplicationId, StateSpaceId, SessionContextId,
        Status, Messages.ToArray(), PendingPlan, LastExecution, CreatedAtUtc, UpdatedAtUtc);
}

public sealed class ApplicationConversationService(
    ApplicationConversationStore store,
    IStateSpaceRegistry stateSpaces,
    IInteractionGateway gateway,
    IInteractionOuterTurnProvider outer,
    IInteractionNarrationProvider narrator)
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ApplicationConversationView Create(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        ApplicationConversationCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(request);
        if (!principal.Verified) throw new InteractionContractException("VERIFIED_OPERATOR_REQUIRED", "A verified operator is required.");
        var stateSpace = stateSpaces.Get(request.StateSpaceId)
            ?? throw new InteractionContractException("STATE_SPACE_UNKNOWN", "The state space is unavailable.");
        if (stateSpace.ApplicationRevision.ApplicationId != applicationId)
            throw new InteractionContractException("STATE_SPACE_APPLICATION_MISMATCH", "The state space belongs to another application.");
        var id = "application-conversation." + Guid.NewGuid().ToString("n");
        var session = string.IsNullOrWhiteSpace(request.SessionContextId)
            ? "application-session." + Guid.NewGuid().ToString("n")
            : request.SessionContextId.Trim();
        var entry = new ApplicationConversationEntry(id, principal.PrincipalId, applicationId,
            request.StateSpaceId, session);
        if (!store.TryAdd(entry))
            throw new InteractionContractException("CONVERSATION_CAPACITY_REACHED", "The ephemeral conversation capacity is full.");
        return entry.View();
    }

    public ApplicationConversationView? Get(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string conversationId) =>
        Current(principal, applicationId, conversationId)?.View();

    public async Task<ApplicationConversationView?> TurnAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string conversationId,
        ApplicationConversationTurnRequest request,
        CancellationToken cancellationToken)
    {
        var entry = Current(principal, applicationId, conversationId);
        if (entry is null) return null;
        var text = request?.Text?.Trim() ?? string.Empty;
        if (text.Length is 0 or > 4_000 || text.Any(char.IsControl))
            throw new InteractionContractException("INVALID_CONVERSATION_TURN", "A turn requires bounded plain text.");
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            if (entry.PendingPlan?.Proposal is not null)
                throw new InteractionContractException("INTERACTION_CONFIRMATION_REQUIRED",
                    "Execute or replace the pending proposal before sending another turn.");
            var ordinal = entry.Messages.Count + 1;
            if (entry.Messages.Count + 3 > ApplicationConversationStore.MaximumMessages
                || ConversationBytes(entry) + JsonBytes(text) + 8_192 > ApplicationConversationStore.MaximumConversationBytes)
                throw new InteractionContractException("CONVERSATION_LIMIT_REACHED", "The ephemeral conversation reached its closed limit.");
            entry.Messages.Add(new(ordinal, "player", text, DateTime.UtcNow));
            var outerTurn = await outer.DecideAsync(new(text), cancellationToken);
            if (!outerTurn.Available || outerTurn.Decision is null)
            {
                entry.Status = "unavailable";
                entry.Messages.Add(new(entry.Messages.Count + 1, "assistant",
                    "The outer conversation model is unavailable.", DateTime.UtcNow, outerTurn.Code));
                entry.UpdatedAtUtc = DateTime.UtcNow;
                return entry.View();
            }
            if (outerTurn.Decision == InteractionOuterDecision.Respond)
            {
                entry.Status = "ready";
                entry.Messages.Add(new(entry.Messages.Count + 1, "assistant", outerTurn.Text,
                    DateTime.UtcNow, outerTurn.Code));
                entry.UpdatedAtUtc = DateTime.UtcNow;
                return entry.View();
            }
            var delegation = "delegation." + Guid.NewGuid().ToString("n");
            var delegated = outerTurn.Decision == InteractionOuterDecision.Delegate;
            var intent = JsonSerializer.Serialize(new
            {
                idempotencyKey = $"{entry.Id}.turn.{ordinal}",
                intentText = outerTurn.Text,
                roleHints = new Dictionary<string, string>(),
                conversationFactReferences = Array.Empty<string>(),
                maximumPlanSteps = InteractionContractLimits.ProposalSteps,
                plannerPreference = delegated ? "local" : "remote"
            });
            var planned = await gateway.PlanAsync(principal, applicationId, entry.StateSpaceId,
                entry.SessionContextId, intent, conversationId: entry.Id,
                role: delegated ? InteractionAiRole.Inner : InteractionAiRole.Outer,
                parentDelegationId: delegated ? delegation : null,
                cancellationToken: cancellationToken);
            entry.PendingPlan = planned.Proposal is null ? null : planned;
            entry.PendingProposalJson = planned.Proposal is null ? null : ProposalJson(planned.Proposal);
            entry.PendingIntentJson = planned.Proposal is null ? null : intent;
            entry.Status = planned.Proposal is null ? "needs-attention" : "awaiting-confirmation";
            entry.Messages.Add(new(entry.Messages.Count + 1, "assistant", planned.SafeSummary,
                DateTime.UtcNow, planned.Code));
            entry.UpdatedAtUtc = DateTime.UtcNow;
            return entry.View();
        }
        finally { entry.Gate.Release(); }
    }

    public async Task<ApplicationConversationView?> ExecuteAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string conversationId,
        ApplicationConversationExecuteRequest request,
        CancellationToken cancellationToken)
    {
        var entry = Current(principal, applicationId, conversationId);
        if (entry is null) return null;
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            var plan = entry.PendingPlan;
            var receipt = plan?.Receipt.Receipt;
            if (plan?.Proposal is null || plan.ProposalFingerprint is null || receipt is null
                || entry.PendingProposalJson is null || entry.PendingIntentJson is null)
                throw new InteractionContractException("NO_PENDING_INTERACTION", "There is no resolved proposal awaiting confirmation.");
            var idempotency = string.IsNullOrWhiteSpace(request?.IdempotencyKey)
                ? $"{entry.Id}.execute.{entry.Messages.Count + 1}"
                : request.IdempotencyKey!;
            using var proposal = JsonDocument.Parse(entry.PendingProposalJson);
            using var learningIntent = JsonDocument.Parse(entry.PendingIntentJson);
            var executionJson = request?.Learn == true
                ? JsonSerializer.Serialize(new
                {
                    resolutionReceiptId = receipt.Id, proposalFingerprint = plan.ProposalFingerprint,
                    idempotencyKey = idempotency, proposal = proposal.RootElement, stopOnFailure = true,
                    learn = true, learningIntent = learningIntent.RootElement
                })
                : JsonSerializer.Serialize(new
                {
                    resolutionReceiptId = receipt.Id, proposalFingerprint = plan.ProposalFingerprint,
                    idempotencyKey = idempotency, proposal = proposal.RootElement, stopOnFailure = true,
                    learn = false
                });
            var result = await gateway.ExecuteAsync(principal, applicationId, entry.StateSpaceId,
                executionJson, cancellationToken);
            entry.LastExecution = result;
            entry.PendingPlan = null;
            entry.PendingProposalJson = null;
            entry.PendingIntentJson = null;
            entry.Status = result.Successful ? "ready" : "needs-attention";
            var playerText = entry.Messages.LastOrDefault(message => message.Role == "player")?.Text ?? "";
            var narration = await narrator.NarrateAsync(new(playerText,
                result.Disposition.ToString().ToLowerInvariant(), result.Code,
                result.ActionResults.Select(action => action.Narration).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                new[] { result.Receipt?.Receipt?.Id }.Concat(result.ActionResults.Select(action => action.OperationId))
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray()), cancellationToken);
            entry.Messages.Add(new(entry.Messages.Count + 1, "assistant",
                narration.Available ? narration.Narration : result.SafeSummary,
                DateTime.UtcNow, narration.Available ? narration.Code : result.Code));
            entry.UpdatedAtUtc = DateTime.UtcNow;
            return entry.View();
        }
        finally { entry.Gate.Release(); }
    }

    private ApplicationConversationEntry? Current(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string conversationId)
    {
        return store.GetCurrent(conversationId, principal.PrincipalId, applicationId);
    }

    private static int ConversationBytes(ApplicationConversationEntry entry) =>
        entry.Messages.Sum(message => JsonBytes(message.Text));

    private static int JsonBytes(string value) => System.Text.Encoding.UTF8.GetByteCount(value);

    private static string ProposalJson(InteractionProposalProjection proposal) =>
        JsonSerializer.Serialize(new
        {
            command = proposal.Command,
            steps = proposal.Steps.Select(step => new
            {
                stepId = step.StepId,
                kind = step.Kind,
                qualifiedId = step.QualifiedId,
                version = step.Version,
                fingerprint = step.Fingerprint,
                dependsOn = step.DependsOn,
                roleBindings = step.RoleBindings,
                input = step.Input
            })
        }, CamelCase);
}
