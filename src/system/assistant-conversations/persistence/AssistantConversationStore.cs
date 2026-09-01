using DantesRoleplay.Assistants;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DantesRoleplay.DataAccess;

public sealed class AssistantConversationStore(
    DantesRoleplayDbContext db,
    IOperationLog operations) : IAssistantConversationStore
{
    private static readonly SemaphoreSlim BeginGate = new(1, 1);
    private static readonly SemaphoreSlim ApprovalDecisionGate = new(1, 1);

    public async Task<AssistantTurnBeginResult> BeginTurnAsync(
        AssistantTurnBegin request, CancellationToken cancellationToken = default)
    {
        // The durable claim must be selected before any provider call. Serializing this short
        // transaction inside the single supported host makes simultaneous same-key requests
        // deterministically converge on replay instead of leaking a SQLite busy/unique error.
        await BeginGate.WaitAsync(cancellationToken);
        try { return await BeginTurnCoreAsync(request, cancellationToken); }
        finally { BeginGate.Release(); }
    }

    private async Task<AssistantTurnBeginResult> BeginTurnCoreAsync(
        AssistantTurnBegin request, CancellationToken cancellationToken)
    {
        if (!AssistantConversationScopes.IsKnown(request.Scope))
            throw new ArgumentException("The assistant conversation scope is invalid.", nameof(request));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var replay = await db.AssistantTurns.AsNoTracking().Include(turn => turn.Conversation)
                .SingleOrDefaultAsync(turn =>
                turn.OperatorId == request.OperatorId && turn.Provider == request.Provider &&
                turn.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (replay is not null)
            {
                var sameRequestTarget = request.ConversationId is null
                    ? replay.TurnNumber == 1
                    : replay.ConversationId == request.ConversationId && replay.TurnNumber > 1;
                if (replay.RequestHash != request.RequestHash ||
                    !sameRequestTarget || replay.Conversation?.Scope != request.Scope)
                    throw Conflict("ASSISTANT_IDEMPOTENCY_CONFLICT", "The idempotency key was already used for another request.");
                await transaction.CommitAsync(cancellationToken);
                return new(replay.ConversationId, replay.Id, true);
            }

            var now = DateTime.UtcNow;
            AssistantConversation conversation;
            if (request.ConversationId is null)
            {
                if (request.ExpectedRevision is not null)
                    throw Conflict("ASSISTANT_REVISION_STALE", "A new conversation cannot have an expected revision.");
                conversation = new()
                {
                    Id = NewId("conversation."), OperatorId = request.OperatorId, Provider = request.Provider,
                    Scope = request.Scope,
                    Title = Title(request.Message), Revision = 0, Status = AssistantConversationStatuses.Pending,
                    CreatedAtUtc = now, UpdatedAtUtc = now
                };
                db.AssistantConversations.Add(conversation);
            }
            else
            {
                conversation = await db.AssistantConversations
                    .Include(item => item.Turns).Include(item => item.Messages)
                    .SingleOrDefaultAsync(item => item.Id == request.ConversationId &&
                        item.OperatorId == request.OperatorId && item.Scope == request.Scope,
                        cancellationToken)
                    ?? throw Conflict("ASSISTANT_CONVERSATION_UNKNOWN", "The conversation was not found.");
                if (conversation.Provider != request.Provider)
                    throw Conflict("ASSISTANT_PROVIDER_INVALID", "The conversation belongs to another provider.");
                if (conversation.Revision != request.ExpectedRevision)
                    throw Conflict("ASSISTANT_REVISION_STALE", $"The current conversation revision is {conversation.Revision}.");
                if (conversation.Turns.Any(turn => turn.Status is AssistantConversationStatuses.Pending or
                        AssistantConversationStatuses.Running or AssistantConversationStatuses.AwaitingApproval))
                    throw Conflict("ASSISTANT_TURN_ACTIVE", "The conversation already has an active turn.");
            }

            var turnNumber = conversation.Turns.Count == 0 ? 1 : conversation.Turns.Max(turn => turn.TurnNumber) + 1;
            var ordinal = conversation.Messages.Count == 0 ? 1 : conversation.Messages.Max(message => message.Ordinal) + 1;
            var turn = new AssistantTurn
            {
                Id = NewId("turn."), ConversationId = conversation.Id, OperatorId = request.OperatorId,
                Provider = request.Provider, TurnNumber = turnNumber, IdempotencyKey = request.IdempotencyKey,
                RequestHash = request.RequestHash, Status = AssistantConversationStatuses.Pending,
                CreatedAtUtc = now
            };
            conversation.Turns.Add(turn);
            conversation.Messages.Add(new AssistantMessage
            {
                Id = NewId("message."), ConversationId = conversation.Id, TurnId = turn.Id,
                Ordinal = ordinal, Role = "user", Content = request.Message, CreatedAtUtc = now
            });
            conversation.Revision = checked(conversation.Revision + 1);
            conversation.Status = AssistantConversationStatuses.Pending;
            conversation.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(conversation.Id, turn.Id, false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task MarkRunningAsync(string turnId, CancellationToken cancellationToken = default)
    {
        var turn = await db.AssistantTurns.Include(item => item.Conversation)
            .SingleAsync(item => item.Id == turnId, cancellationToken);
        if (turn.Status != AssistantConversationStatuses.Pending) return;
        turn.Status = AssistantConversationStatuses.Running;
        turn.StartedAtUtc = DateTime.UtcNow;
        turn.Conversation!.Status = AssistantConversationStatuses.Running;
        turn.Conversation.UpdatedAtUtc = turn.StartedAtUtc.Value;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task BindCodexTurnAsync(
        CodexTurnBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (string.IsNullOrWhiteSpace(binding.ExternalThreadId) || binding.ExternalThreadId.Length > 200 ||
            string.IsNullOrWhiteSpace(binding.ExternalTurnId) || binding.ExternalTurnId.Length > 200 ||
            string.IsNullOrWhiteSpace(binding.ExternalStatus) || binding.ExternalStatus.Length > 30)
            throw new ArgumentException("The Codex turn binding is invalid.", nameof(binding));

        var turn = await db.AssistantTurns.Include(item => item.Conversation)
            .SingleAsync(item => item.Id == binding.TurnId, cancellationToken);
        if (turn.Provider != "codex") throw Conflict(
            "ASSISTANT_PROVIDER_INVALID", "Only Codex turns can bind app-server identifiers.");
        if (turn.Status is not (AssistantConversationStatuses.Pending or AssistantConversationStatuses.Running))
            throw Conflict("ASSISTANT_TURN_NOT_ACTIVE", "The Codex turn is no longer active.");
        if (turn.Conversation!.ExternalThreadId is not null &&
            turn.Conversation.ExternalThreadId != binding.ExternalThreadId)
            throw Conflict("CODEX_THREAD_MISMATCH", "The Codex thread identifier does not match this conversation.");
        if (turn.ExternalTurnId is not null && turn.ExternalTurnId != binding.ExternalTurnId)
            throw Conflict("CODEX_TURN_MISMATCH", "The Codex turn identifier is already bound differently.");

        turn.Conversation.ExternalThreadId ??= binding.ExternalThreadId;
        turn.ExternalTurnId ??= binding.ExternalTurnId;
        turn.ExternalStatus = binding.ExternalStatus;
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<AssistantTurnActivityDocument> AppendCodexActivityAsync(
        CodexTurnActivityAppend activity, CancellationToken cancellationToken = default) =>
        AppendActivityCoreAsync(activity, codexOnly: true, cancellationToken);

    public Task<AssistantTurnActivityDocument> AppendActivityAsync(
        CodexTurnActivityAppend activity, CancellationToken cancellationToken = default) =>
        AppendActivityCoreAsync(activity, codexOnly: false, cancellationToken);

    private async Task<AssistantTurnActivityDocument> AppendActivityCoreAsync(
        CodexTurnActivityAppend activity, bool codexOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (string.IsNullOrWhiteSpace(activity.ExternalItemId) || activity.ExternalItemId.Length > 200 ||
            activity.Sequence < 1 || activity.Kind is not ("command" or "file-change" or "mcp-tool" or
                "dynamic-tool" or "web-search" or "warning" or "error" or "tool-call" or
                "validation" or "request" or "result" or "reasoning" or "task" or "recipe") ||
            string.IsNullOrWhiteSpace(activity.Status) || activity.Status.Length > 30 ||
            string.IsNullOrWhiteSpace(activity.Summary) || activity.Summary.Length > 500)
            throw new ArgumentException("The Codex activity is invalid.", nameof(activity));

        var existing = await db.AssistantTurnActivities.AsNoTracking().SingleOrDefaultAsync(
            item => item.TurnId == activity.TurnId && item.ExternalItemId == activity.ExternalItemId,
            cancellationToken);
        if (existing is not null) return Activity(existing);

        var turn = await db.AssistantTurns.AsNoTracking().SingleAsync(
            item => item.Id == activity.TurnId, cancellationToken);
        if (codexOnly && turn.Provider != "codex") throw Conflict(
            "ASSISTANT_PROVIDER_INVALID", "Only Codex turns can append app-server activity.");
        var row = new AssistantTurnActivity
        {
            Id = NewId("activity."), ConversationId = turn.ConversationId, TurnId = turn.Id,
            ExternalItemId = activity.ExternalItemId, Sequence = activity.Sequence,
            Kind = activity.Kind, Status = activity.Status, Summary = activity.Summary,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.AssistantTurnActivities.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return Activity(row);
    }

    public async Task<AssistantTurnApprovalDocument> AppendCodexApprovalAsync(
        CodexApprovalAppend approval, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ValidateApprovalAppend(approval);
        var detailsJson = JsonSerializer.Serialize(approval.Details);
        if (detailsJson.Length is < 2 or > 8_192)
            throw new ArgumentException("The Codex approval details are invalid.", nameof(approval));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await db.AssistantTurnApprovals.AsNoTracking().SingleOrDefaultAsync(
                item => item.TurnId == approval.TurnId && item.ExternalRequestId == approval.ExternalRequestId,
                cancellationToken);
            if (existing is not null)
            {
                if (existing.RequestFingerprint != approval.RequestFingerprint)
                    throw Conflict("CODEX_APPROVAL_REQUEST_MISMATCH",
                        "Codex reused an approval request identity with different content.");
                await transaction.CommitAsync(cancellationToken);
                return Approval(existing);
            }

            var turn = await db.AssistantTurns.Include(item => item.Conversation)
                .SingleAsync(item => item.Id == approval.TurnId, cancellationToken);
            if (turn.Provider != "codex" || turn.Status is not (
                    AssistantConversationStatuses.Running or AssistantConversationStatuses.AwaitingApproval))
                throw Conflict("CODEX_APPROVAL_TURN_INACTIVE", "The Codex turn is not accepting approvals.");
            var openCount = await db.AssistantTurnApprovals.CountAsync(item => item.TurnId == turn.Id &&
                (item.Status == CodexApprovalStatuses.Pending || item.Status == CodexApprovalStatuses.Decided ||
                 item.Status == CodexApprovalStatuses.Dispatched), cancellationToken);
            if (openCount >= 4)
                throw Conflict("CODEX_APPROVAL_LIMIT", "The Codex turn already has four open approval requests.");

            var now = DateTime.UtcNow;
            var row = new AssistantTurnApproval
            {
                Id = NewId("approval."), ConversationId = turn.ConversationId, TurnId = turn.Id,
                OperatorId = turn.OperatorId, ExternalRequestId = approval.ExternalRequestId,
                ExternalItemId = approval.ExternalItemId, ExternalApprovalId = approval.ExternalApprovalId,
                Kind = approval.Kind, RequestFingerprint = approval.RequestFingerprint,
                Summary = approval.Summary, DetailsJson = detailsJson, CanAccept = approval.CanAccept,
                Status = CodexApprovalStatuses.Pending, Revision = 1,
                RequestedAtUtc = now, ExpiresAtUtc = approval.ExpiresAtUtc
            };
            db.AssistantTurnApprovals.Add(row);
            turn.Status = AssistantConversationStatuses.AwaitingApproval;
            turn.Conversation!.Status = AssistantConversationStatuses.AwaitingApproval;
            BumpConversation(turn.Conversation, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Approval(row);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<CodexApprovalDispatch> DecideCodexApprovalAsync(
        CodexApprovalDecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateApprovalDecision(request);
        await ApprovalDecisionGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var approval = await db.AssistantTurnApprovals
                    .Include(item => item.Conversation).Include(item => item.Turn)
                    .SingleOrDefaultAsync(item => item.Id == request.ApprovalId &&
                        item.ConversationId == request.ConversationId && item.TurnId == request.TurnId &&
                        item.OperatorId == request.OperatorId, cancellationToken)
                    ?? throw Conflict("CODEX_APPROVAL_UNKNOWN", "The Codex approval was not found.");
                if (approval.Status != CodexApprovalStatuses.Pending)
                    throw Conflict("CODEX_APPROVAL_NOT_PENDING", "The Codex approval is no longer pending.");
                if (approval.Revision != request.ExpectedRevision)
                    throw Conflict("CODEX_APPROVAL_REVISION_STALE",
                        $"The current approval revision is {approval.Revision}.");
                if (approval.ExpiresAtUtc <= DateTime.UtcNow)
                    throw Conflict("CODEX_APPROVAL_EXPIRED", "The Codex approval has expired.");
                if (request.Decision == CodexApprovalDecisions.Accept && !approval.CanAccept)
                    throw Conflict("CODEX_APPROVAL_NOT_ACCEPTABLE",
                        "This request cannot be accepted within the repository safety boundary.");
                if (approval.Turn!.Status != AssistantConversationStatuses.AwaitingApproval)
                    throw Conflict("CODEX_APPROVAL_TURN_INACTIVE", "The Codex turn is no longer awaiting approval.");

                var now = DateTime.UtcNow;
                approval.Decision = request.Decision;
                approval.Status = CodexApprovalStatuses.Decided;
                approval.Revision = checked(approval.Revision + 1);
                approval.DecidedAtUtc = now;
                BumpConversation(approval.Conversation!, now);
                await operations.RecordAsync(
                    "control.assistant.codex-approval",
                    $"Recorded {request.Decision} for {approval.Kind} Codex approval.",
                    true,
                    intent: "Resolve one visible, turn-scoped Codex side-effect request.",
                    subject: approval.Id,
                    consumesReadEvidence: false,
                    cancellationToken: cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(Approval(approval), approval.ExternalRequestId, approval.Kind, request.Decision);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        finally { ApprovalDecisionGate.Release(); }
    }

    public async Task MarkCodexApprovalDispatchedAsync(
        string approvalId, CancellationToken cancellationToken = default)
    {
        var approval = await db.AssistantTurnApprovals.Include(item => item.Conversation)
            .SingleAsync(item => item.Id == approvalId, cancellationToken);
        if (approval.Status != CodexApprovalStatuses.Decided) return;
        var now = DateTime.UtcNow;
        approval.Status = CodexApprovalStatuses.Dispatched;
        approval.Revision = checked(approval.Revision + 1);
        approval.DispatchedAtUtc = now;
        BumpConversation(approval.Conversation!, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResolveCodexApprovalAsync(
        string turnId, string externalRequestId, CancellationToken cancellationToken = default)
    {
        // The operator decision is written by a separate HTTP request/DbContext. Detach the
        // stream request's earlier pending snapshot so resolution cannot overwrite that durable
        // decision with stale tracked state.
        db.ChangeTracker.Clear();
        var approval = await db.AssistantTurnApprovals
            .Include(item => item.Conversation).Include(item => item.Turn)
            .SingleOrDefaultAsync(item => item.TurnId == turnId && item.ExternalRequestId == externalRequestId,
                cancellationToken);
        if (approval is null || approval.Status is CodexApprovalStatuses.Resolved or
            CodexApprovalStatuses.Expired or CodexApprovalStatuses.Cancelled or CodexApprovalStatuses.Failed)
            return;
        var now = DateTime.UtcNow;
        if (approval.Status == CodexApprovalStatuses.Pending)
        {
            approval.Status = CodexApprovalStatuses.Cancelled;
            approval.Decision = CodexApprovalDecisions.Cancel;
        }
        else approval.Status = CodexApprovalStatuses.Resolved;
        approval.Revision = checked(approval.Revision + 1);
        approval.ResolvedAtUtc = now;
        await RestoreRunningWhenNoOtherOpenApproval(approval, now, cancellationToken);
        BumpConversation(approval.Conversation!, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CodexApprovalDispatch?> ExpireCodexApprovalAsync(
        string approvalId, CancellationToken cancellationToken = default)
    {
        await ApprovalDecisionGate.WaitAsync(cancellationToken);
        try
        {
            var approval = await db.AssistantTurnApprovals
                .Include(item => item.Conversation).Include(item => item.Turn)
                .SingleOrDefaultAsync(item => item.Id == approvalId, cancellationToken);
            if (approval is null || approval.Status != CodexApprovalStatuses.Pending ||
                approval.ExpiresAtUtc > DateTime.UtcNow)
                return null;
            var now = DateTime.UtcNow;
            approval.Status = CodexApprovalStatuses.Expired;
            approval.Decision = CodexApprovalDecisions.Cancel;
            approval.DecidedAtUtc = now;
            approval.ResolvedAtUtc = now;
            approval.Revision = checked(approval.Revision + 1);
            BumpConversation(approval.Conversation!, now);
            await operations.RecordAsync(
                "control.assistant.codex-approval-expire",
                $"Expired {approval.Kind} Codex approval.", false,
                intent: "Fail closed when a Codex approval receives no timely operator decision.",
                subject: approval.Id, error: "CODEX_APPROVAL_EXPIRED",
                consumesReadEvidence: false, cancellationToken: cancellationToken);
            return new(Approval(approval), approval.ExternalRequestId, approval.Kind,
                CodexApprovalDecisions.Cancel);
        }
        finally { ApprovalDecisionGate.Release(); }
    }

    public async Task CloseOpenCodexApprovalsAsync(
        string turnId, string status, CancellationToken cancellationToken = default)
    {
        if (status is not (CodexApprovalStatuses.Cancelled or CodexApprovalStatuses.Failed))
            throw new ArgumentException("The terminal Codex approval status is invalid.", nameof(status));
        var rows = await db.AssistantTurnApprovals.Include(item => item.Conversation)
            .Where(item => item.TurnId == turnId && (item.Status == CodexApprovalStatuses.Pending ||
                item.Status == CodexApprovalStatuses.Decided || item.Status == CodexApprovalStatuses.Dispatched))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.Status = status;
            row.Decision ??= CodexApprovalDecisions.Cancel;
            row.Revision = checked(row.Revision + 1);
            row.ResolvedAtUtc = now;
        }
        BumpConversation(rows[0].Conversation!, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteTurnAsync(
        AssistantTurnCompletion completion, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var turn = await db.AssistantTurns.Include(item => item.Conversation)
                .ThenInclude(item => item!.Messages).Include(item => item.Approvals)
                .SingleAsync(item => item.Id == completion.TurnId, cancellationToken);
            if (turn.Status is AssistantConversationStatuses.Completed or AssistantConversationStatuses.Failed or AssistantConversationStatuses.Cancelled)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            if (completion.Status is not (AssistantConversationStatuses.Completed or AssistantConversationStatuses.Failed or AssistantConversationStatuses.Cancelled))
                throw new ArgumentException("The assistant completion status is invalid.", nameof(completion));

            var now = DateTime.UtcNow;
            if (completion.Status == AssistantConversationStatuses.Completed)
            {
                if (string.IsNullOrWhiteSpace(completion.Reply) || completion.Reply.Length > 8_000)
                    throw new ArgumentException("A completed assistant turn requires a bounded reply.", nameof(completion));
                var ordinal = turn.Conversation!.Messages.Max(message => message.Ordinal) + 1;
                db.AssistantMessages.Add(new()
                {
                    Id = NewId("message."), ConversationId = turn.ConversationId, TurnId = turn.Id,
                    Ordinal = ordinal, Role = "assistant", Content = completion.Reply, CreatedAtUtc = now
                });
            }
            ApplyContext(turn, completion.Context, completion.Status);
            turn.Status = completion.Status;
            if (completion.ExternalStatus is not null)
                turn.ExternalStatus = Bound(completion.ExternalStatus, 30);
            turn.ErrorCode = Bound(completion.ErrorCode, 100);
            turn.ErrorMessage = Bound(completion.ErrorMessage, 500);
            turn.ModelProvider = Bound(completion.ModelProvider, 50);
            turn.Model = Bound(completion.Model, 200);
            turn.ModelRevision = Bound(completion.ModelRevision, 200);
            turn.ModelProfile = Bound(completion.ModelProfile, 100);
            turn.ElapsedMilliseconds = Math.Max(0, completion.ElapsedMilliseconds);
            turn.PromptTokens = Math.Max(0, completion.PromptTokens);
            turn.OutputTokens = Math.Max(0, completion.OutputTokens);
            turn.CompletedAtUtc = now;
            turn.Conversation!.Status = completion.Status;
            turn.Conversation.UpdatedAtUtc = now;
            foreach (var approval in turn.Approvals.Where(item => item.Status is
                         CodexApprovalStatuses.Pending or CodexApprovalStatuses.Decided or
                         CodexApprovalStatuses.Dispatched))
            {
                approval.Status = completion.Status == AssistantConversationStatuses.Cancelled
                    ? CodexApprovalStatuses.Cancelled : CodexApprovalStatuses.Failed;
                approval.Decision ??= CodexApprovalDecisions.Cancel;
                approval.Revision = checked(approval.Revision + 1);
                approval.ResolvedAtUtc = now;
            }
            await operations.RecordAsync(
                turn.Provider == "codex" ? "control.assistant.codex-message" : "control.assistant.local-message",
                completion.Status == AssistantConversationStatuses.Completed
                    ? $"Completed {turn.Provider} assistant turn {turn.TurnNumber}."
                    : $"{turn.Provider} assistant turn {turn.TurnNumber} ended as {completion.Status}.",
                completion.Status == AssistantConversationStatuses.Completed,
                intent: turn.Provider == "codex"
                    ? "Send a repository Codex message with explicit one-request side-effect approvals."
                    : turn.ContextProfile == AssistantTurnContextProfiles.ApplicationAiV1
                        ? "Send a provider-neutral AI request bound to registered direct capabilities and application context."
                    : turn.Conversation.Scope == AssistantConversationScopes.System
                        ? "Send a read-only message to the local system assistant."
                        : "Send a message to the local advisory assistant.",
                subject: turn.ConversationId,
                error: turn.ErrorCode,
                consumesReadEvidence: false,
                cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var turns = await db.AssistantTurns.Include(item => item.Conversation).Include(item => item.Approvals)
                .Where(item => item.Status == AssistantConversationStatuses.Pending ||
                    item.Status == AssistantConversationStatuses.Running ||
                    item.Status == AssistantConversationStatuses.AwaitingApproval)
                .ToListAsync(cancellationToken);
            if (turns.Count == 0) return 0;
            var now = DateTime.UtcNow;
            foreach (var turn in turns)
            {
                turn.Status = AssistantConversationStatuses.Failed;
                var codex = turn.Provider == "codex";
                turn.ErrorCode = codex ? "CODEX_PROCESS_INTERRUPTED" : "ASSISTANT_PROCESS_INTERRUPTED";
                turn.ErrorMessage = codex
                    ? "The host stopped before the Codex turn completed. Send a new turn to resume explicitly."
                    : "The host stopped before the local assistant turn completed.";
                turn.CompletedAtUtc = now;
                turn.Conversation!.Status = AssistantConversationStatuses.Failed;
                turn.Conversation.UpdatedAtUtc = now;
                foreach (var approval in turn.Approvals.Where(item => item.Status is
                             CodexApprovalStatuses.Pending or CodexApprovalStatuses.Decided or
                             CodexApprovalStatuses.Dispatched))
                {
                    approval.Status = CodexApprovalStatuses.Failed;
                    approval.Decision ??= CodexApprovalDecisions.Cancel;
                    approval.Revision = checked(approval.Revision + 1);
                    approval.ResolvedAtUtc = now;
                }
                await operations.RecordAsync(
                    codex ? "control.assistant.codex-recover" : "control.assistant.recover",
                    $"Recovered interrupted {turn.Provider} assistant turn {turn.TurnNumber} as failed.",
                    false, intent: "Reconcile interrupted assistant work at startup.", subject: turn.ConversationId,
                    error: turn.ErrorCode, consumesReadEvidence: false, cancellationToken: cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return turns.Count;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<AssistantConversationDocument?> GetAsync(
        string operatorId, string conversationId, CancellationToken cancellationToken = default,
        string scope = AssistantConversationScopes.Advisory)
    {
        if (!AssistantConversationScopes.IsKnown(scope))
            throw new ArgumentException("The assistant conversation scope is invalid.", nameof(scope));
        var conversation = await db.AssistantConversations.AsNoTracking()
            .Include(item => item.Turns).Include(item => item.Messages).Include(item => item.Activities)
            .Include(item => item.Approvals)
            .SingleOrDefaultAsync(item => item.Id == conversationId && item.OperatorId == operatorId &&
                item.Scope == scope, cancellationToken);
        return conversation is null ? null : Document(conversation);
    }

    public async Task<IReadOnlyList<AssistantConversationSummary>> ListAsync(
        string operatorId, string provider, DateTime? beforeUpdatedAtUtc, string? beforeId, int limit,
        CancellationToken cancellationToken = default,
        string scope = AssistantConversationScopes.Advisory)
    {
        if (!AssistantConversationScopes.IsKnown(scope))
            throw new ArgumentException("The assistant conversation scope is invalid.", nameof(scope));
        var query = db.AssistantConversations.AsNoTracking()
            .Where(item => item.OperatorId == operatorId && item.Provider == provider && item.Scope == scope);
        if (beforeUpdatedAtUtc.HasValue)
            query = query.Where(item => item.UpdatedAtUtc < beforeUpdatedAtUtc.Value ||
                item.UpdatedAtUtc == beforeUpdatedAtUtc.Value && string.Compare(item.Id, beforeId) < 0);
        return await query.OrderByDescending(item => item.UpdatedAtUtc).ThenByDescending(item => item.Id)
            .Take(limit).Select(item => new AssistantConversationSummary(
                item.Id, item.Provider, item.Scope, item.Title, item.Revision, item.Status,
                item.CreatedAtUtc, item.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        string operatorId, string conversationId, int expectedRevision,
        CancellationToken cancellationToken = default,
        string scope = AssistantConversationScopes.Advisory)
    {
        if (!AssistantConversationScopes.IsKnown(scope))
            throw new ArgumentException("The assistant conversation scope is invalid.", nameof(scope));
        if (expectedRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var conversation = await db.AssistantConversations.SingleOrDefaultAsync(item =>
                item.Id == conversationId && item.OperatorId == operatorId && item.Scope == scope,
                cancellationToken);
            if (conversation is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }
            if (conversation.Revision != expectedRevision)
                throw Conflict("ASSISTANT_REVISION_STALE",
                    $"The current conversation revision is {conversation.Revision}.");
            if (conversation.Status is AssistantConversationStatuses.Pending or
                AssistantConversationStatuses.Running or AssistantConversationStatuses.AwaitingApproval)
                throw Conflict("ASSISTANT_TURN_ACTIVE",
                    "An active conversation cannot be removed. Cancel or finish its current turn first.");

            db.AssistantConversations.Remove(conversation);
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException)
            {
                throw Conflict("ASSISTANT_CONVERSATION_IN_USE",
                    "This conversation is retained by system work and cannot be removed yet.");
            }
            await operations.RecordAsync(
                "control.assistant.remove-conversation",
                "Removed an assistant conversation and its retained chat history.",
                true,
                intent: "Remove a selected assistant conversation.",
                subject: conversationId,
                consumesReadEvidence: false,
                cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static AssistantConversationDocument Document(AssistantConversation item) => new(
        new(item.Id, item.Provider, item.Scope, item.Title, item.Revision, item.Status,
            item.CreatedAtUtc, item.UpdatedAtUtc),
        item.ExternalThreadId,
        item.Turns.OrderBy(turn => turn.TurnNumber).Select(turn => new AssistantTurnDocument(
            turn.Id, turn.TurnNumber, turn.Status, turn.ExternalTurnId, turn.ExternalStatus,
            turn.ErrorCode, turn.ErrorMessage,
            turn.ModelProvider, turn.Model, turn.ModelRevision, turn.ModelProfile,
            turn.ElapsedMilliseconds, turn.PromptTokens, turn.OutputTokens,
            turn.CreatedAtUtc, turn.StartedAtUtc, turn.CompletedAtUtc,
            Context(turn))).ToArray(),
        item.Messages.OrderBy(message => message.Ordinal).Select(message => new AssistantMessageDocument(
            message.Id, message.TurnId, message.Ordinal, message.Role, message.Content, message.CreatedAtUtc)).ToArray(),
        item.Activities.OrderBy(activity => activity.Sequence).Select(Activity).ToArray(),
        item.Approvals.OrderBy(approval => approval.RequestedAtUtc).ThenBy(approval => approval.Id)
            .Select(Approval).ToArray());

    private static AssistantTurnContextDocument? Context(AssistantTurn turn)
    {
        if (string.IsNullOrEmpty(turn.ContextProfile)) return null;
        var references = JsonSerializer.Deserialize<string[]>(turn.ContextSourceReferencesJson)
            ?? throw new InvalidOperationException("The stored assistant context references are invalid.");
        return new(turn.ContextProfile, turn.ContextFingerprint, references, turn.ResponseDisposition);
    }

    private static void ApplyContext(
        AssistantTurn turn,
        AssistantTurnContextCompletion? context,
        string completionStatus)
    {
        var system = turn.Conversation?.Scope == AssistantConversationScopes.System;
        if (!system)
        {
            if (context is not null)
                throw new ArgumentException("Advisory assistant turns cannot store system context.", nameof(context));
            return;
        }
        if (completionStatus != AssistantConversationStatuses.Completed)
        {
            if (context is not null)
                throw new ArgumentException("Failed system turns cannot claim completed context evidence.", nameof(context));
            return;
        }
        if (context is null || context.Profile is not (
                AssistantTurnContextProfiles.SystemReadV1 or AssistantTurnContextProfiles.ApplicationAiV1) ||
            context.Fingerprint.Length != 64 || context.Fingerprint.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')) ||
            !AssistantTurnResponseDispositions.IsKnown(context.Disposition) ||
            context.SourceReferences is null || context.SourceReferences.Count > 24 ||
            context.SourceReferences.Any(value => string.IsNullOrWhiteSpace(value) ||
                value.Length > 320 || value.Any(char.IsControl)) ||
            context.SourceReferences.Distinct(StringComparer.Ordinal).Count() != context.SourceReferences.Count ||
            !context.SourceReferences.SequenceEqual(context.SourceReferences.OrderBy(value => value, StringComparer.Ordinal)))
            throw new ArgumentException("The assistant context completion is invalid.", nameof(context));
        var referencesJson = JsonSerializer.Serialize(context.SourceReferences);
        if (referencesJson.Length > 8_000)
            throw new ArgumentException("The assistant context references are too large.", nameof(context));
        turn.ContextProfile = context.Profile;
        turn.ContextFingerprint = context.Fingerprint;
        turn.ContextSourceReferencesJson = referencesJson;
        turn.ResponseDisposition = context.Disposition;
    }

    private static AssistantTurnActivityDocument Activity(AssistantTurnActivity item) => new(
        item.Id, item.TurnId, item.ExternalItemId, item.Sequence, item.Kind, item.Status,
        item.Summary, item.CreatedAtUtc);

    private static AssistantTurnApprovalDocument Approval(AssistantTurnApproval item) => new(
        item.Id, item.TurnId, item.Revision, item.Kind, item.Status, item.Decision,
        item.Summary, JsonSerializer.Deserialize<CodexApprovalDetails>(item.DetailsJson)
            ?? throw new InvalidOperationException("The stored Codex approval details are invalid."),
        item.CanAccept, item.RequestedAtUtc, item.ExpiresAtUtc, item.DecidedAtUtc,
        item.DispatchedAtUtc, item.ResolvedAtUtc);

    private async Task RestoreRunningWhenNoOtherOpenApproval(
        AssistantTurnApproval approval, DateTime now, CancellationToken cancellationToken)
    {
        var anotherOpen = await db.AssistantTurnApprovals.AnyAsync(item => item.TurnId == approval.TurnId &&
            item.Id != approval.Id && (item.Status == CodexApprovalStatuses.Pending ||
                item.Status == CodexApprovalStatuses.Decided || item.Status == CodexApprovalStatuses.Dispatched),
            cancellationToken);
        if (!anotherOpen && approval.Turn!.Status == AssistantConversationStatuses.AwaitingApproval)
        {
            approval.Turn.Status = AssistantConversationStatuses.Running;
            approval.Conversation!.Status = AssistantConversationStatuses.Running;
            approval.Conversation.UpdatedAtUtc = now;
        }
    }

    private static void BumpConversation(AssistantConversation conversation, DateTime now)
    {
        conversation.Revision = checked(conversation.Revision + 1);
        conversation.UpdatedAtUtc = now;
    }

    private static void ValidateApprovalAppend(CodexApprovalAppend approval)
    {
        if (string.IsNullOrWhiteSpace(approval.ExternalRequestId) || approval.ExternalRequestId.Length > 200 ||
            string.IsNullOrWhiteSpace(approval.ExternalItemId) || approval.ExternalItemId.Length > 200 ||
            approval.ExternalApprovalId?.Length > 200 ||
            approval.Kind is not (CodexApprovalKinds.Command or CodexApprovalKinds.FileChange or
                CodexApprovalKinds.Network or CodexApprovalKinds.Permissions) ||
            approval.RequestFingerprint.Length != 64 || approval.RequestFingerprint.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')) ||
            string.IsNullOrWhiteSpace(approval.Summary) || approval.Summary.Length > 500 ||
            approval.ExpiresAtUtc <= DateTime.UtcNow || approval.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(5))
            throw new ArgumentException("The Codex approval is invalid.", nameof(approval));
    }

    private static void ValidateApprovalDecision(CodexApprovalDecisionRequest request)
    {
        if (request.OperatorId?.Length != 74 || !request.OperatorId.StartsWith("principal.", StringComparison.Ordinal) ||
            request.ExpectedRevision < 1 || request.Decision is not (CodexApprovalDecisions.Accept or
                CodexApprovalDecisions.Decline or CodexApprovalDecisions.Cancel))
            throw new ArgumentException("The Codex approval decision is invalid.", nameof(request));
    }

    private static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("n");
    private static string Title(string message)
    {
        var value = string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value[..Math.Min(value.Length, 120)];
    }
    private static string Bound(string value, int maximum) => string.IsNullOrEmpty(value) ? string.Empty : value[..Math.Min(value.Length, maximum)];
    private static AssistantConversationException Conflict(string code, string message) => new(code, message);
}
