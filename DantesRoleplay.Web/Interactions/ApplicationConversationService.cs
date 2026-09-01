using System.Collections.Concurrent;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Play;

namespace DantesRoleplay.Web.Interactions;

public static class ApplicationConversationTasks
{
    public const string OuterTurn = InteractionOuterProtocol.OuterTurnTask;
    public const string Narration = InteractionOuterProtocol.NarrationTask;
    public const string TaskAgenda = InteractionOuterProtocol.TaskAgendaTask;
    public const string OuterTurnSchema = InteractionOuterProtocol.OuterTurnSchemaName;
    public const string NarrationSchema = InteractionOuterProtocol.NarrationSchemaName;
    public const string TaskAgendaSchema = InteractionOuterProtocol.TaskAgendaSchemaName;
}

public sealed record ApplicationConversationMessage(
    int Ordinal,
    string Role,
    string Text,
    DateTime CreatedAtUtc,
    string? Code = null,
    string Id = "",
    string? SituationId = null);

public sealed record ApplicationConversationHistoryPage(
    IReadOnlyList<ApplicationConversationMessage> Messages,
    int? NextBeforeOrdinal);

public sealed record ApplicationConversationView(
    string Id,
    ApplicationIdentifier ApplicationId,
    string StateSpaceId,
    string SessionContextId,
    string Status,
    IReadOnlyList<ApplicationConversationMessage> Messages,
    InteractionPlanGatewayResult? PendingPlan,
    InteractionExecutionOutcome? LastExecution,
    InteractionTaskAgendaProgressProjection? ActiveAgenda,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    PlaySituationDocument? CurrentSituation = null,
    int TotalMessageCount = 0,
    bool HasEarlierMessages = false,
    int KnownTruthCount = 0);

public sealed record ApplicationConversationCreateRequest(string StateSpaceId, string? SessionContextId = null);
public sealed record ApplicationConversationTurnRequest(string Text, bool ReplaceActiveAgenda = false);
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

internal sealed class ApplicationTaskBatchProgress(string id, InteractionTaskBatch definition)
{
    public string Id { get; } = id;
    public InteractionTaskBatch Definition { get; } = definition;
    public InteractionTaskBatchStatus Status { get; set; } = InteractionTaskBatchStatus.Pending;
    public string? Code { get; set; }
    public string? ResolutionReceiptId { get; set; }
    public string? ExecutionReceiptId { get; set; }
}

internal sealed class ApplicationTaskProgress(string id, InteractionTaskItem definition,
    IReadOnlyList<ApplicationTaskBatchProgress> batches)
{
    public string Id { get; } = id;
    public InteractionTaskItem Definition { get; } = definition;
    public InteractionTaskStatus Status { get; set; } = InteractionTaskStatus.Pending;
    public IReadOnlyList<ApplicationTaskBatchProgress> Batches { get; } = batches;
}

internal sealed class ApplicationTaskAgendaProgress
{
    private ApplicationTaskAgendaProgress(string id, string idempotencyBase, string delegationId,
        InteractionTaskAgenda definition, IReadOnlyList<ApplicationTaskProgress> tasks)
    {
        Id = id;
        IdempotencyBase = idempotencyBase;
        DelegationId = delegationId;
        Definition = definition;
        Tasks = tasks;
    }

    public string Id { get; }
    public string IdempotencyBase { get; }
    public string DelegationId { get; }
    public InteractionTaskAgenda Definition { get; }
    public InteractionTaskAgendaStatus Status { get; set; } = InteractionTaskAgendaStatus.Planning;
    public IReadOnlyList<ApplicationTaskProgress> Tasks { get; }

    public static ApplicationTaskAgendaProgress Create(
        InteractionTaskAgenda definition, string conversationId, int turnOrdinal)
    {
        var id = "interaction-goal." + Guid.NewGuid().ToString("n");
        var tasks = definition.Tasks.Select(task =>
        {
            var taskId = $"{id}.task.{task.Ordinal}";
            return new ApplicationTaskProgress(taskId, task,
                task.Batches.Select(batch => new ApplicationTaskBatchProgress(
                    $"{taskId}.batch.{batch.Ordinal}", batch)).ToArray());
        }).ToArray();
        return new(id, $"{conversationId}.turn.{turnOrdinal}",
            "delegation." + Guid.NewGuid().ToString("n"), definition, tasks);
    }

    public (ApplicationTaskProgress Task, ApplicationTaskBatchProgress Batch)? Next()
    {
        foreach (var task in Tasks)
        {
            if (task.Status is InteractionTaskStatus.Completed or InteractionTaskStatus.Blocked
                or InteractionTaskStatus.Cancelled) continue;
            if (task.Definition.DependsOn.Any(dependency =>
                    Tasks[dependency - 1].Status != InteractionTaskStatus.Completed))
                continue;
            var batch = task.Batches.FirstOrDefault(value => value.Status == InteractionTaskBatchStatus.Pending);
            if (batch is not null) return (task, batch);
        }
        return null;
    }

    public void PauseDependants()
    {
        foreach (var task in Tasks.Where(value => value.Status == InteractionTaskStatus.Pending))
            if (DependsOnStopped(task)) task.Status = InteractionTaskStatus.Blocked;
    }

    private bool DependsOnStopped(ApplicationTaskProgress task) => task.Definition.DependsOn.Any(dependency =>
        Tasks[dependency - 1].Status is InteractionTaskStatus.Blocked or InteractionTaskStatus.Cancelled
            or InteractionTaskStatus.Active);

    public void Cancel()
    {
        Status = InteractionTaskAgendaStatus.Cancelled;
        foreach (var task in Tasks.Where(value => value.Status != InteractionTaskStatus.Completed))
        {
            task.Status = InteractionTaskStatus.Cancelled;
            foreach (var batch in task.Batches.Where(value => value.Status != InteractionTaskBatchStatus.Completed))
                batch.Status = InteractionTaskBatchStatus.Cancelled;
        }
    }

    public InteractionTaskAgendaProgressProjection Projection()
    {
        var current = Tasks.SelectMany(task => task.Batches.Select(batch => (Task: task, Batch: batch)))
            .FirstOrDefault(value => value.Batch.Status is InteractionTaskBatchStatus.Planning
                or InteractionTaskBatchStatus.AwaitingConfirmation);
        return new(Id, Definition.Fingerprint, Name(Status),
            current.Task?.Definition.Ordinal, current.Batch?.Definition.Ordinal,
            Tasks.Select(task => new InteractionTaskProgressProjection(task.Definition.Ordinal, task.Id,
                task.Definition.IntentText, task.Definition.DependsOn, Name(task.Status),
                task.Batches.Select(batch => new InteractionTaskBatchProgressProjection(
                    batch.Definition.Ordinal, batch.Id, batch.Definition.IntentText, Name(batch.Status),
                    batch.Code, batch.ResolutionReceiptId, batch.ExecutionReceiptId)).ToArray())).ToArray());
    }

    private static string Name<T>(T value) where T : struct, Enum => value.ToString() switch
    {
        "AwaitingConfirmation" => "awaiting-confirmation",
        "NeedsAttention" => "needs-attention",
        _ => value.ToString().ToLowerInvariant()
    };
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
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "ready";
    public List<ApplicationConversationMessage> Messages { get; } = [];
    public List<PlayTruthDocument> KnownTruths { get; } = [];
    public PlaySituationDocument? CurrentSituation { get; set; }
    public int TotalMessageCount { get; set; }
    public InteractionPlanGatewayResult? PendingPlan { get; set; }
    public string? PendingProposalJson { get; set; }
    public string? PendingIntentJson { get; set; }
    public InteractionExecutionOutcome? LastExecution { get; set; }
    public ApplicationTaskAgendaProgress? ActiveAgenda { get; set; }
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public ApplicationConversationView View() => new(Id, ApplicationId, StateSpaceId, SessionContextId,
        Status, Messages.ToArray(), PendingPlan, LastExecution, ActiveAgenda?.Projection(), CreatedAtUtc, UpdatedAtUtc,
        CurrentSituation, TotalMessageCount, TotalMessageCount > Messages.Count, KnownTruths.Count);

    public void Apply(PlayConversationDocument document)
    {
        if (document.Id != Id || document.PrincipalId != PrincipalId
            || document.ApplicationId != ApplicationId.Value
            || document.StateSpaceId != StateSpaceId
            || document.SessionContextId != SessionContextId)
            throw new InvalidOperationException("The durable play conversation binding changed.");
        Messages.Clear();
        Messages.AddRange(document.RecentMessages.Select(value => new ApplicationConversationMessage(
            value.Ordinal, value.Role, value.Text, value.CreatedAtUtc, value.Code, value.Id, value.SituationId)));
        KnownTruths.Clear();
        KnownTruths.AddRange(document.KnownTruths);
        CurrentSituation = document.CurrentSituation;
        TotalMessageCount = document.TotalMessageCount;
        CreatedAtUtc = document.CreatedAtUtc;
        UpdatedAtUtc = document.UpdatedAtUtc;
        Status = document.Status;
    }
}

public sealed class ApplicationConversationService(
    ApplicationConversationStore store,
    IStateSpaceRegistry stateSpaces,
    IInteractionGateway gateway,
    IInteractionOuterTurnProvider outer,
    IInteractionNarrationProvider narrator,
    IApplicationPlayRecordStore? records = null)
{
    private const int MaximumOuterTranscriptMessages = 12;
    private const int MaximumOuterTranscriptCharacters = 12_000;
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
        var session = string.IsNullOrWhiteSpace(request.SessionContextId)
            ? "application-session." + Guid.NewGuid().ToString("n")
            : request.SessionContextId.Trim();
        if (records is not null)
        {
            var document = records.ResumeOrCreate(new(
                principal.PrincipalId, applicationId.Value, request.StateSpaceId, session));
            var current = store.GetCurrent(document.Id, principal.PrincipalId, applicationId);
            if (current is not null)
            {
                current.Apply(document);
                return current.View();
            }
            var restored = new ApplicationConversationEntry(document.Id, principal.PrincipalId, applicationId,
                request.StateSpaceId, session);
            restored.Apply(document);
            if (!store.TryAdd(restored))
            {
                current = store.GetCurrent(document.Id, principal.PrincipalId, applicationId);
                if (current is not null) return current.View();
                throw new InteractionContractException("CONVERSATION_CAPACITY_REACHED",
                    "The active conversation cache is full.");
            }
            return restored.View();
        }
        var id = "application-conversation." + Guid.NewGuid().ToString("n");
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

    public ApplicationConversationHistoryPage? History(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string conversationId,
        int? beforeOrdinal,
        int limit)
    {
        if (limit is < 1 or > 100) throw new InteractionContractException(
            "INVALID_HISTORY_LIMIT", "History pages contain between 1 and 100 messages.");
        if (records is null)
        {
            var entry = Current(principal, applicationId, conversationId);
            if (entry is null) return null;
            var values = entry.Messages.Where(value => beforeOrdinal is null || value.Ordinal < beforeOrdinal)
                .OrderByDescending(value => value.Ordinal).Take(limit).OrderBy(value => value.Ordinal).ToArray();
            return new(values, null);
        }
        try
        {
            var page = records.GetMessages(principal.PrincipalId, applicationId.Value,
                conversationId, beforeOrdinal, limit);
            return new(page.Messages.Select(value => new ApplicationConversationMessage(
                value.Ordinal, value.Role, value.Text, value.CreatedAtUtc,
                value.Code, value.Id, value.SituationId)).ToArray(), page.NextBeforeOrdinal);
        }
        catch (KeyNotFoundException) { return null; }
    }

    public async Task<ApplicationConversationView?> TurnAsync(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string conversationId,
        ApplicationConversationTurnRequest request,
        CancellationToken cancellationToken)
    {
        var entry = Current(principal, applicationId, conversationId);
        if (entry is null) return null;
        var text = request?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4_000 ||
            text.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
            throw new InteractionContractException("INVALID_CONVERSATION_TURN", "A turn requires bounded plain text.");
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            if (entry.ActiveAgenda is { Status: not InteractionTaskAgendaStatus.Completed
                    and not InteractionTaskAgendaStatus.Cancelled })
            {
                if (request?.ReplaceActiveAgenda != true)
                    throw new InteractionContractException(entry.PendingPlan?.Proposal is null
                            ? "TASK_AGENDA_ACTIVE" : "INTERACTION_CONFIRMATION_REQUIRED",
                        "Complete or explicitly replace the active task agenda before sending another turn.");
                entry.ActiveAgenda.Cancel();
                ClearPending(entry);
            }
            else if (entry.PendingPlan?.Proposal is not null)
                throw new InteractionContractException("INTERACTION_CONFIRMATION_REQUIRED",
                    "Execute the pending proposal before sending another turn.");
            entry.ActiveAgenda = null;
            var ordinal = entry.TotalMessageCount + 1;
            AppendMessage(entry, "player", text, null);
            var outerTurn = await outer.DecideAsync(OuterRequest(entry, text), cancellationToken);
            if (!outerTurn.Available || outerTurn.Decision is null)
            {
                entry.Status = "unavailable";
                AppendMessage(entry, "assistant", "The outer conversation model is unavailable.", outerTurn.Code);
                entry.UpdatedAtUtc = DateTime.UtcNow;
                return entry.View();
            }
            if (outerTurn.Decision == InteractionOuterDecision.Respond)
            {
                entry.Status = "ready";
                AppendMessage(entry, "assistant", outerTurn.Text, outerTurn.Code,
                    outerTurn.Situation, outerTurn.Truths ?? []);
                entry.UpdatedAtUtc = DateTime.UtcNow;
                return entry.View();
            }

            InteractionTaskAgenda? agenda;
            if (IsSingleTaskRequest(text))
            {
                agenda = InteractionTaskAgenda.Single(outerTurn.Text);
            }
            else if (outer is not IInteractionTaskAgendaProvider taskAgendaProvider)
            {
                entry.Status = "unavailable";
                AppendMessage(entry, "assistant",
                    "The selected outer model cannot create a bounded task agenda.", "TASK_AGENDA_UNAVAILABLE");
                return entry.View();
            }
            else
            {
                var agendaResult = await taskAgendaProvider.CreateAgendaAsync(new(outerTurn.Text), cancellationToken);
                if (!agendaResult.Available || agendaResult.Agenda is null)
                {
                    entry.Status = "needs-attention";
                    AppendMessage(entry, "assistant",
                        "The outer model could not produce a valid bounded task agenda.", agendaResult.Code);
                    return entry.View();
                }
                agenda = agendaResult.Agenda;
            }
            var agendaBytes = JsonBytes(JsonSerializer.Serialize(agenda, CamelCase));
            if (agendaBytes + 8_192 > ApplicationConversationStore.MaximumConversationBytes)
                throw new InteractionContractException("CONVERSATION_LIMIT_REACHED",
                    "The bounded task agenda exceeds the active conversation capacity.");
            entry.ActiveAgenda = ApplicationTaskAgendaProgress.Create(agenda, entry.Id, ordinal);
            await PlanNextBatchAsync(entry, principal, applicationId, cancellationToken);
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
            var active = entry.ActiveAgenda?.Tasks
                .SelectMany(task => task.Batches.Select(batch => (Task: task, Batch: batch)))
                .SingleOrDefault(value => value.Batch.Status == InteractionTaskBatchStatus.AwaitingConfirmation);
            var idempotency = string.IsNullOrWhiteSpace(request?.IdempotencyKey)
                ? active?.Batch is null ? $"{entry.Id}.execute.{entry.TotalMessageCount + 1}"
                    : $"{active.Value.Batch.Id}.execute"
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
            var durableOutcome = result.Receipt?.Receipt is not null;
            if (durableOutcome) ClearPending(entry);
            var playerText = active?.Batch.Definition.IntentText
                ?? entry.Messages.LastOrDefault(message => message.Role == "player")?.Text ?? "";
            var narration = await narrator.NarrateAsync(new(playerText,
                result.Disposition.ToString().ToLowerInvariant(), result.Code,
                result.ActionResults.Select(action => action.Narration).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                new[] { result.Receipt?.Receipt?.Id }.Concat(result.ActionResults.Select(action => action.OperationId))
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray(),
                (result.QueryResults ?? []).Where(value => value.Output is not null).ToArray(),
                PlayContext(entry)), cancellationToken);
            AppendMessage(entry, "assistant", narration.Available ? narration.Narration : result.SafeSummary,
                narration.Available ? narration.Code : result.Code,
                narration.Available ? narration.Situation : null,
                narration.Available ? narration.Truths ?? [] : []);

            if (entry.ActiveAgenda is not null && active?.Batch is not null && active?.Task is not null)
            {
                active.Value.Batch.ExecutionReceiptId = result.Receipt?.Receipt?.Id;
                active.Value.Batch.Code = result.Code;
                if (result.Successful && durableOutcome)
                {
                    active.Value.Batch.Status = InteractionTaskBatchStatus.Completed;
                    if (active.Value.Task.Batches.All(value => value.Status == InteractionTaskBatchStatus.Completed))
                        active.Value.Task.Status = InteractionTaskStatus.Completed;
                    entry.ActiveAgenda.Status = InteractionTaskAgendaStatus.Planning;
                    await PlanNextBatchAsync(entry, principal, applicationId, cancellationToken);
                }
                else if (durableOutcome)
                {
                    active.Value.Batch.Status = InteractionTaskBatchStatus.Failed;
                    entry.ActiveAgenda.Status = InteractionTaskAgendaStatus.NeedsAttention;
                    entry.ActiveAgenda.PauseDependants();
                    entry.Status = "needs-attention";
                }
                else
                {
                    active.Value.Batch.Status = InteractionTaskBatchStatus.AwaitingConfirmation;
                    entry.ActiveAgenda.Status = InteractionTaskAgendaStatus.AwaitingConfirmation;
                    entry.Status = "awaiting-confirmation";
                }
            }
            else entry.Status = result.Successful ? "ready"
                : durableOutcome ? "needs-attention" : "awaiting-confirmation";
            if (records is not null) entry.Apply(records.SetStatus(entry.Id, entry.Status));
            entry.UpdatedAtUtc = DateTime.UtcNow;
            return entry.View();
        }
        finally { entry.Gate.Release(); }
    }

    private async Task PlanNextBatchAsync(
        ApplicationConversationEntry entry,
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        CancellationToken cancellationToken)
    {
        var agenda = entry.ActiveAgenda
            ?? throw new InvalidOperationException("Task agenda progress is required.");
        var next = agenda.Next();
        if (next is null)
        {
            if (agenda.Tasks.All(task => task.Status == InteractionTaskStatus.Completed))
            {
                agenda.Status = InteractionTaskAgendaStatus.Completed;
                entry.Status = "ready";
            }
            else
            {
                agenda.Status = InteractionTaskAgendaStatus.NeedsAttention;
                entry.Status = "needs-attention";
            }
            ClearPending(entry);
            return;
        }

        var (task, batch) = next.Value;
        task.Status = InteractionTaskStatus.Active;
        batch.Status = InteractionTaskBatchStatus.Planning;
        agenda.Status = InteractionTaskAgendaStatus.Planning;
        entry.Status = "planning";
        var keyBase = $"{agenda.IdempotencyBase}.task.{task.Definition.Ordinal}.batch.{batch.Definition.Ordinal}";
        var innerIntent = IntentJson(keyBase, "inner", batch.Definition.IntentText, "local",
            entry.KnownTruths.Select(value => value.Id).ToArray());
        var inner = await gateway.PlanAsync(principal, applicationId, entry.StateSpaceId,
            entry.SessionContextId, innerIntent, conversationId: entry.Id,
            role: InteractionAiRole.Inner, parentDelegationId: agenda.DelegationId,
            cancellationToken: cancellationToken);
        batch.ResolutionReceiptId = inner.Receipt.Receipt?.Id;
        batch.Code = inner.Code;
        if (inner.Proposal is not null)
        {
            RetainPlan(entry, inner, innerIntent);
            batch.Status = InteractionTaskBatchStatus.AwaitingConfirmation;
            agenda.Status = InteractionTaskAgendaStatus.AwaitingConfirmation;
            AddResolutionMessage(entry, inner);
            return;
        }

        ClearPending(entry);
        AddResolutionMessage(entry, inner);
        if (!FallbackEligible(inner.Status))
        {
            PauseAgenda(entry, batch, inner.Code);
            return;
        }

        var innerReceipt = inner.Receipt.Receipt;
        var reconsidered = await outer.DecideAsync(OuterRequest(entry, batch.Definition.IntentText,
            inner.Code, new(InteractionResolutionStatusNames.Get(inner.Status), inner.Code,
                inner.SafeSummary, inner.Evidence, innerReceipt?.Id)), cancellationToken);
        if (!reconsidered.Available || reconsidered.Decision is null)
        {
            AppendMessage(entry, "assistant", "The outer model could not continue the unresolved batch.",
                reconsidered.Code);
            PauseAgenda(entry, batch, reconsidered.Code);
            return;
        }
        if (reconsidered.Decision != InteractionOuterDecision.DirectPlan)
        {
            if (reconsidered.Decision != InteractionOuterDecision.Respond
                || !string.Equals(reconsidered.Text, ResolutionText(inner), StringComparison.Ordinal))
                AppendMessage(entry, "assistant", reconsidered.Decision == InteractionOuterDecision.Respond
                    ? reconsidered.Text
                    : "The batch remains unresolved after the bounded fallback decision.",
                    reconsidered.Decision == InteractionOuterDecision.Respond ? reconsidered.Code : inner.Code);
            PauseAgenda(entry, batch, inner.Code);
            return;
        }

        var outerIntent = IntentJson(keyBase, "outer", reconsidered.Text, OuterPlannerPreference(),
            entry.KnownTruths.Select(value => value.Id).ToArray());
        var fallback = await gateway.PlanAsync(principal, applicationId, entry.StateSpaceId,
            entry.SessionContextId, outerIntent, conversationId: entry.Id,
            role: InteractionAiRole.Outer, parentDelegationId: agenda.DelegationId,
            cancellationToken: cancellationToken);
        batch.ResolutionReceiptId = fallback.Receipt.Receipt?.Id;
        batch.Code = fallback.Code;
        if (fallback.Proposal is not null)
        {
            RetainPlan(entry, fallback, outerIntent);
            batch.Status = InteractionTaskBatchStatus.AwaitingConfirmation;
            agenda.Status = InteractionTaskAgendaStatus.AwaitingConfirmation;
        }
        else PauseAgenda(entry, batch, fallback.Code);
        AddResolutionMessage(entry, fallback);
    }

    private static void PauseAgenda(
        ApplicationConversationEntry entry,
        ApplicationTaskBatchProgress batch,
        string code)
    {
        batch.Status = InteractionTaskBatchStatus.Unresolved;
        batch.Code = code;
        entry.ActiveAgenda!.Status = InteractionTaskAgendaStatus.NeedsAttention;
        entry.ActiveAgenda.PauseDependants();
        entry.Status = "needs-attention";
        ClearPending(entry);
    }

    private ApplicationConversationEntry? Current(
        TrustedPrincipalContext principal,
        ApplicationIdentifier applicationId,
        string conversationId)
    {
        var current = store.GetCurrent(conversationId, principal.PrincipalId, applicationId);
        if (current is not null || records is null) return current;
        var document = records.Get(principal.PrincipalId, applicationId.Value, conversationId);
        if (document is null) return null;
        var restored = new ApplicationConversationEntry(document.Id, document.PrincipalId, applicationId,
            document.StateSpaceId, document.SessionContextId);
        restored.Apply(document);
        if (store.TryAdd(restored)) return restored;
        return store.GetCurrent(conversationId, principal.PrincipalId, applicationId);
    }

    private InteractionOuterTurnRequest OuterRequest(
        ApplicationConversationEntry entry,
        string playerText,
        string? priorSafeResultCode = null,
        InteractionOuterPriorResolution? priorSafeResolution = null)
    {
        var stateSpace = stateSpaces.Get(entry.StateSpaceId)
            ?? throw new InteractionContractException("STATE_SPACE_UNKNOWN", "The state space is unavailable.");
        if (stateSpace.ApplicationRevision.ApplicationId != entry.ApplicationId)
            throw new InteractionContractException("STATE_SPACE_APPLICATION_MISMATCH",
                "The state space belongs to another application.");
        var selected = entry.Messages.TakeLast(MaximumOuterTranscriptMessages)
            .Select(message => new InteractionOuterVisibleMessage(message.Role, message.Text))
            .ToList();
        while (selected.Count > 1
               && selected.Sum(message => message.Role.Length + message.Text.Length + 2)
                   > MaximumOuterTranscriptCharacters)
            selected.RemoveAt(0);
        return new(
            playerText,
            priorSafeResultCode,
            priorSafeResolution,
            new(
                entry.ApplicationId.Value,
                entry.StateSpaceId,
                stateSpace.ApplicationRevision.Revision,
                stateSpace.ApplicationRevision.Fingerprint,
                stateSpace.ManifestFingerprint),
            selected.ToArray(),
            PlayContext(entry));
    }

    private static InteractionOuterPlayContext PlayContext(ApplicationConversationEntry entry) => new(
        entry.CurrentSituation is null ? null : new(
            entry.CurrentSituation.Kind,
            entry.CurrentSituation.Summary,
            entry.CurrentSituation.Participants,
            entry.CurrentSituation.Location),
        entry.KnownTruths.TakeLast(64)
            .Select(value => new InteractionOuterKnownTruth(value.Statement, value.SubjectEntityIds)).ToArray());

    private static bool IsSingleTaskRequest(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 1) return false;
        return System.Text.RegularExpressions.Regex.Matches(text, @"(?:^|\s)\d{1,2}[.)]\s").Count < 2;
    }

    private static int JsonBytes(string value) => System.Text.Encoding.UTF8.GetByteCount(value);

    private string OuterPlannerPreference() =>
        outer is IInteractionOuterProviderAdapter { Kind: InteractionOuterProviderKind.Remote }
            ? "remote" : "local";

    private static bool FallbackEligible(InteractionResolutionStatus status) => status is
        InteractionResolutionStatus.Unknown or InteractionResolutionStatus.Unsupported
        or InteractionResolutionStatus.Unavailable;

    private static string IntentJson(
        string idempotencyBase,
        string attempt,
        string intentText,
        string plannerPreference,
        IReadOnlyList<string> conversationFactReferences) => JsonSerializer.Serialize(new
        {
            idempotencyKey = $"{idempotencyBase}.{attempt}",
            intentText,
            roleHints = new Dictionary<string, string>(),
            conversationFactReferences,
            maximumPlanSteps = InteractionContractLimits.ProposalSteps,
            plannerPreference
        });

    private static void RetainPlan(
        ApplicationConversationEntry entry,
        InteractionPlanGatewayResult plan,
        string intentJson)
    {
        entry.PendingPlan = plan;
        entry.PendingProposalJson = ProposalJson(plan.Proposal!);
        entry.PendingIntentJson = intentJson;
        entry.Status = "awaiting-confirmation";
    }

    private static void ClearPending(ApplicationConversationEntry entry)
    {
        entry.PendingPlan = null;
        entry.PendingProposalJson = null;
        entry.PendingIntentJson = null;
    }

    private void AddResolutionMessage(
        ApplicationConversationEntry entry,
        InteractionPlanGatewayResult plan)
    {
        AppendMessage(entry, "assistant", ResolutionText(plan), plan.Code);
    }

    private static string ResolutionText(InteractionPlanGatewayResult plan)
    {
        var receipt = plan.Receipt.Receipt?.Id;
        return receipt is null ? plan.SafeSummary : $"{plan.SafeSummary} Receipt: {receipt}.";
    }

    private void AppendMessage(
        ApplicationConversationEntry entry,
        string role,
        string text,
        string? code,
        PlaySituationUpdate? situation = null,
        IReadOnlyList<PlayTruthAssertion>? truths = null)
    {
        if (JsonBytes(text) > 8_000)
            throw new InteractionContractException("CONVERSATION_LIMIT_REACHED",
                "A single conversation message exceeded its durable limit.");
        var now = DateTime.UtcNow;
        if (records is not null)
        {
            var message = new PlayMessageAppend(role, text, code, now);
            var document = role == "assistant" && (situation is not null || truths is { Count: > 0 })
                ? records.AppendNarrative(entry.Id, new(message, situation, truths ?? []), entry.Status)
                : records.AppendMessage(entry.Id, message, entry.Status);
            entry.Apply(document);
            return;
        }

        var ordinal = entry.TotalMessageCount + 1;
        var situationId = ApplyLocalSituation(entry, situation, now);
        var messageId = "play-message." + Guid.NewGuid().ToString("n");
        entry.Messages.Add(new(ordinal, role, text, now, code, messageId, situationId));
        entry.TotalMessageCount++;
        if (entry.Messages.Count > ApplicationConversationStore.MaximumMessages)
            entry.Messages.RemoveRange(0, entry.Messages.Count - ApplicationConversationStore.MaximumMessages);
        AppendLocalTruths(entry, truths ?? [], messageId, situationId, now);
        entry.UpdatedAtUtc = now;
    }

    private static string? ApplyLocalSituation(
        ApplicationConversationEntry entry,
        PlaySituationUpdate? update,
        DateTime now)
    {
        if (update is null) return entry.CurrentSituation?.Id;
        if (update.Transition == PlaySituationTransitions.Complete)
        {
            var completed = entry.CurrentSituation;
            if (completed is not null)
                entry.CurrentSituation = completed with
                {
                    Revision = completed.Revision + 1,
                    Status = PlaySituationStatuses.Completed,
                    UpdatedAtUtc = now,
                    CompletedAtUtc = now
                };
            var completedId = entry.CurrentSituation?.Id;
            entry.CurrentSituation = null;
            return completedId;
        }
        var current = entry.CurrentSituation;
        if (update.Transition == PlaySituationTransitions.Replace || current is null
            || current.Status != PlaySituationStatuses.Active || current.Kind != update.Kind)
        {
            entry.CurrentSituation = new(
                "play-situation." + Guid.NewGuid().ToString("n"),
                1,
                update.Kind,
                PlaySituationStatuses.Active,
                update.Summary,
                update.Participants,
                update.Location,
                now,
                now,
                null);
            return entry.CurrentSituation.Id;
        }
        entry.CurrentSituation = current with
        {
            Revision = current.Revision + 1,
            Summary = update.Summary,
            Participants = update.Participants,
            Location = update.Location,
            UpdatedAtUtc = now
        };
        return entry.CurrentSituation.Id;
    }

    private static void AppendLocalTruths(
        ApplicationConversationEntry entry,
        IReadOnlyList<PlayTruthAssertion> truths,
        string messageId,
        string? situationId,
        DateTime now)
    {
        foreach (var truth in truths)
        {
            if (entry.KnownTruths.Any(value =>
                    string.Equals(value.Statement, truth.Statement, StringComparison.OrdinalIgnoreCase)))
                continue;
            entry.KnownTruths.Add(new(
                "play-truth." + Guid.NewGuid().ToString("n"),
                entry.KnownTruths.Count + 1,
                truth.Statement,
                truth.SubjectEntityIds,
                messageId,
                situationId,
                now));
        }
    }

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
                input = step.Input,
                resultBindings = step.ResultBindings.Select(binding => binding.ToRole is not null
                    ? (object)new
                    {
                        fromStepId = binding.FromStepId,
                        fromPointer = binding.FromPointer,
                        toRole = binding.ToRole
                    }
                    : new
                    {
                        fromStepId = binding.FromStepId,
                        fromPointer = binding.FromPointer,
                        toInputPointer = binding.ToInputPointer
                    })
            })
        }, CamelCase);
}
