using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using DantesRoleplay.Assistants;
using DantesRoleplay.CodexBridge;

namespace DantesRoleplay.DataAccess;

public sealed class CodexTurnRegistry
{
    private readonly ConcurrentDictionary<string, Entry> active = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim concurrency;

    public CodexTurnRegistry(CodexBridgeOptions options) =>
        concurrency = new(options.MaximumConcurrentTurns, options.MaximumConcurrentTurns);

    public bool TryAcquire(out IDisposable? lease)
    {
        if (!concurrency.Wait(0)) { lease = null; return false; }
        lease = new Lease(concurrency);
        return true;
    }

    public void Register(string turnId, ICodexAppServerSession session)
    {
        if (!active.TryAdd(turnId, new(session)))
            throw new InvalidOperationException("The Codex turn is already registered.");
    }

    public void Remove(string turnId) => active.TryRemove(turnId, out _);

    public async Task<bool> InterruptAsync(string turnId, CancellationToken cancellationToken)
    {
        if (!active.TryGetValue(turnId, out var entry)) return false;
        if (Interlocked.Exchange(ref entry.InterruptRequested, 1) == 0)
            await entry.Session.InterruptAsync(cancellationToken);
        return true;
    }

    public bool WasInterrupted(string turnId) =>
        active.TryGetValue(turnId, out var entry) && Volatile.Read(ref entry.InterruptRequested) != 0;

    public async Task<bool> RespondApprovalAsync(
        string turnId, string externalRequestId, string decision, CancellationToken cancellationToken)
    {
        if (!active.TryGetValue(turnId, out var entry)) return false;
        await entry.Session.RespondApprovalAsync(externalRequestId, decision, cancellationToken);
        if (decision == CodexApprovalDecisions.Cancel)
            Interlocked.Exchange(ref entry.InterruptRequested, 1);
        return true;
    }

    private sealed class Entry(ICodexAppServerSession session)
    {
        public ICodexAppServerSession Session { get; } = session;
        public int InterruptRequested;
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) semaphore.Release();
        }
    }
}

public sealed partial class CodexConversationService(
    IAssistantConversationStore store,
    ICodexAppServerFactory appServer,
    CodexTurnRegistry registry,
    CodexBridgeOptions options) : ICodexConversationService
{
    public const string Provider = "codex";
    public const int MaximumMessageLength = 8_000;

    public Task<CodexBridgeStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        appServer.GetStatusAsync(cancellationToken);

    public IAsyncEnumerable<CodexConversationEvent> CreateAsync(
        string operatorId, AssistantConversationCreate request, CancellationToken cancellationToken = default)
    {
        ValidateOperator(operatorId);
        if (request.Provider != Provider) throw Invalid(
            "ASSISTANT_PROVIDER_INVALID", "The Codex stream requires provider 'codex'.");
        var message = NormalizeMessage(request.Message);
        ValidateIdempotencyKey(request.IdempotencyKey);
        return ExecuteStream(operatorId, null, null, message, request.IdempotencyKey, cancellationToken);
    }

    public IAsyncEnumerable<CodexConversationEvent> SendAsync(
        string operatorId, string conversationId, AssistantConversationTurnCreate request,
        CancellationToken cancellationToken = default)
    {
        ValidateOperator(operatorId);
        ValidateConversationId(conversationId);
        if (request.ExpectedRevision < 1) throw Invalid(
            "ASSISTANT_REVISION_INVALID", "expectedRevision must be positive.");
        var message = NormalizeMessage(request.Message);
        ValidateIdempotencyKey(request.IdempotencyKey);
        return ExecuteStream(operatorId, conversationId, request.ExpectedRevision,
            message, request.IdempotencyKey, cancellationToken);
    }

    public async Task<CodexCancelResult> CancelAsync(
        string operatorId, string conversationId, string turnId,
        CancellationToken cancellationToken = default)
    {
        ValidateOperator(operatorId);
        ValidateConversationId(conversationId);
        ValidateTurnId(turnId);
        var conversation = await store.GetAsync(operatorId, conversationId, cancellationToken)
            ?? throw Invalid("ASSISTANT_CONVERSATION_UNKNOWN", "The conversation was not found.");
        if (conversation.Summary.Provider != Provider)
            throw Invalid("ASSISTANT_PROVIDER_INVALID", "The conversation is not a Codex conversation.");
        var turn = conversation.Turns.SingleOrDefault(item => item.Id == turnId)
            ?? throw Invalid("ASSISTANT_TURN_UNKNOWN", "The turn was not found.");
        if (turn.Status is not (AssistantConversationStatuses.Pending or AssistantConversationStatuses.Running or
                AssistantConversationStatuses.AwaitingApproval) ||
            !await registry.InterruptAsync(turnId, cancellationToken))
            throw Invalid("ASSISTANT_TURN_NOT_ACTIVE", "The Codex turn is not active in this host.");
        return new(true, conversationId, turnId);
    }

    public async Task<CodexApprovalResult> ApproveAsync(
        string operatorId, string conversationId, string turnId, string approvalId,
        CodexApprovalDecisionInput request, CancellationToken cancellationToken = default)
    {
        ValidateOperator(operatorId);
        ValidateConversationId(conversationId);
        ValidateTurnId(turnId);
        ValidateApprovalId(approvalId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedRevision < 1 || request.Decision is not (
                CodexApprovalDecisions.Accept or CodexApprovalDecisions.Decline or CodexApprovalDecisions.Cancel))
            throw Invalid("CODEX_APPROVAL_DECISION_INVALID", "The Codex approval decision is invalid.");

        var claim = await store.DecideCodexApprovalAsync(new(
            operatorId, conversationId, turnId, approvalId, request.ExpectedRevision, request.Decision),
            cancellationToken);
        try
        {
            if (!await registry.RespondApprovalAsync(
                    turnId, claim.ExternalRequestId, claim.Decision, CancellationToken.None))
            {
                await store.CloseOpenCodexApprovalsAsync(
                    turnId, CodexApprovalStatuses.Failed, CancellationToken.None);
                throw Invalid("CODEX_APPROVAL_SESSION_UNKNOWN",
                    "The Codex process is no longer available for this approval.");
            }
            await store.MarkCodexApprovalDispatchedAsync(approvalId, CancellationToken.None);
        }
        catch
        {
            try
            {
                await store.CloseOpenCodexApprovalsAsync(
                    turnId, CodexApprovalStatuses.Failed, CancellationToken.None);
            }
            catch
            {
                // Preserve the original dispatch failure. Startup reconciliation will close any
                // approval whose failure marker could not be persisted here.
            }
            throw;
        }

        var conversation = await RequiredConversation(operatorId, conversationId, CancellationToken.None);
        var approval = conversation.Approvals.Single(item => item.Id == approvalId);
        return new(approval, conversation);
    }

    private async IAsyncEnumerable<CodexConversationEvent> ExecuteStream(
        string operatorId, string? conversationId, int? expectedRevision,
        string message, string idempotencyKey,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<CodexConversationEvent>(new BoundedChannelOptions(32)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        var producer = ProduceAsync(channel.Writer, operatorId, conversationId, expectedRevision,
            message, idempotencyKey, cancellationToken);
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken)) yield return item;
            await producer;
        }
        finally
        {
            if (!producer.IsCompleted)
            {
                try { await producer.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            }
        }
    }

    private async Task ProduceAsync(
        ChannelWriter<CodexConversationEvent> writer,
        string operatorId, string? conversationId, int? expectedRevision,
        string message, string idempotencyKey, CancellationToken requestCancellation)
    {
        AssistantTurnBeginResult? begin = null;
        ICodexAppServerSession? session = null;
        IDisposable? lease = null;
        var watch = Stopwatch.StartNew();
        var model = string.Empty;
        var modelProvider = "openai";
        var reply = string.Empty;
        var externalStatus = string.Empty;
        var terminalStatus = AssistantConversationStatuses.Failed;
        var errorCode = string.Empty;
        var errorMessage = string.Empty;
        var sequence = 0;
        var approvalIds = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var bridgeStatus = await appServer.GetStatusAsync(requestCancellation);
            if (!bridgeStatus.Ready) throw Invalid(bridgeStatus.ErrorCode, bridgeStatus.ErrorMessage);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Provider + "\0" + message)));
            begin = await store.BeginTurnAsync(new(
                operatorId, Provider, conversationId, expectedRevision, message, idempotencyKey, hash), requestCancellation);
            if (begin.Replay)
            {
                var replay = await RequiredConversation(operatorId, begin.ConversationId, requestCancellation);
                await writer.WriteAsync(new("completed", StreamDocument(replay)), requestCancellation);
                writer.TryComplete();
                return;
            }
            if (!registry.TryAcquire(out lease))
            {
                errorCode = "CODEX_SATURATED";
                errorMessage = "The maximum of two concurrent Codex turns is already active.";
                await CompleteFailedAsync(begin.TurnId, errorCode, errorMessage, watch.ElapsedMilliseconds);
                await writer.WriteAsync(new("completed",
                    StreamDocument(await RequiredConversation(
                        operatorId, begin.ConversationId, CancellationToken.None))), requestCancellation);
                writer.TryComplete();
                return;
            }

            await store.MarkRunningAsync(begin.TurnId, requestCancellation);
            var current = await RequiredConversation(operatorId, begin.ConversationId, requestCancellation);
            session = await appServer.CreateAsync(requestCancellation);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation);
            timeout.CancelAfter(options.EffectiveTurnTimeout);
            var started = await session.StartTurnAsync(current.ExternalThreadId, message, timeout.Token);
            model = Bound(started.Model, 200);
            modelProvider = Bound(string.IsNullOrWhiteSpace(started.ModelProvider) ? "openai" : started.ModelProvider, 50);
            externalStatus = started.Status;
            await store.BindCodexTurnAsync(new(
                begin.TurnId, started.ExternalThreadId, started.ExternalTurnId, started.Status), timeout.Token);
            registry.Register(begin.TurnId, session);
            await writer.WriteAsync(new("accepted",
                StreamDocument(await RequiredConversation(operatorId, begin.ConversationId, timeout.Token))), timeout.Token);

            await foreach (var protocolEvent in session.ReadEventsAsync(timeout.Token))
            {
                switch (protocolEvent.Type)
                {
                    case "delta" when !string.IsNullOrEmpty(protocolEvent.Delta):
                        await writer.WriteAsync(new("delta", Delta: protocolEvent.Delta), timeout.Token);
                        break;
                    case "reply":
                        reply = protocolEvent.Reply;
                        break;
                    case "activity" when protocolEvent.Activity is not null:
                        var activity = await store.AppendCodexActivityAsync(new(
                            begin.TurnId, protocolEvent.Activity.ExternalItemId, ++sequence,
                            protocolEvent.Activity.Kind, protocolEvent.Activity.Status,
                            protocolEvent.Activity.Summary), timeout.Token);
                        await writer.WriteAsync(new("activity", Activity: activity), timeout.Token);
                        break;
                    case "approval" when protocolEvent.Approval is not null:
                        var pendingApproval = await store.AppendCodexApprovalAsync(new(
                            begin.TurnId,
                            protocolEvent.Approval.ExternalRequestId,
                            protocolEvent.Approval.ExternalItemId,
                            string.IsNullOrWhiteSpace(protocolEvent.Approval.ExternalApprovalId)
                                ? null : protocolEvent.Approval.ExternalApprovalId,
                            protocolEvent.Approval.Kind,
                            protocolEvent.Approval.RequestFingerprint,
                            protocolEvent.Approval.Summary,
                            protocolEvent.Approval.Details,
                            protocolEvent.Approval.CanAccept,
                            DateTime.UtcNow.Add(options.EffectiveApprovalTimeout)), timeout.Token);
                        approvalIds[protocolEvent.Approval.ExternalRequestId] = pendingApproval.Id;
                        await writer.WriteAsync(new(
                            "approval",
                            StreamDocument(await RequiredConversation(
                                operatorId, begin.ConversationId, timeout.Token)),
                            Approval: pendingApproval), timeout.Token);
                        break;
                    case "approval-resolved" when !string.IsNullOrWhiteSpace(protocolEvent.ExternalRequestId):
                        await store.ResolveCodexApprovalAsync(
                            begin.TurnId, protocolEvent.ExternalRequestId, timeout.Token);
                        break;
                    case "approval-expired" when !string.IsNullOrWhiteSpace(protocolEvent.ExternalRequestId) &&
                        approvalIds.TryGetValue(protocolEvent.ExternalRequestId, out var localApprovalId):
                        var expired = await store.ExpireCodexApprovalAsync(localApprovalId, timeout.Token);
                        if (expired is not null)
                        {
                            await registry.RespondApprovalAsync(
                                begin.TurnId, expired.ExternalRequestId,
                                CodexApprovalDecisions.Cancel, timeout.Token);
                            var expiredDocument = await RequiredConversation(
                                operatorId, begin.ConversationId, timeout.Token);
                            await writer.WriteAsync(new(
                                "approval", StreamDocument(expiredDocument),
                                Approval: expiredDocument.Approvals.Single(item => item.Id == localApprovalId)),
                                timeout.Token);
                        }
                        break;
                    case "terminal":
                        externalStatus = protocolEvent.Status;
                        errorCode = protocolEvent.ErrorCode;
                        errorMessage = protocolEvent.ErrorMessage;
                        break;
                }
            }

            if (externalStatus == "completed" && !string.IsNullOrWhiteSpace(reply) && reply.Length <= MaximumMessageLength)
                terminalStatus = AssistantConversationStatuses.Completed;
            else if (externalStatus == "interrupted" && registry.WasInterrupted(begin.TurnId))
            {
                terminalStatus = AssistantConversationStatuses.Cancelled;
                errorCode = "CODEX_TURN_CANCELLED";
                errorMessage = "The Codex turn was cancelled by the operator.";
            }
            else
            {
                terminalStatus = AssistantConversationStatuses.Failed;
                if (string.IsNullOrWhiteSpace(errorCode)) errorCode = externalStatus == "completed"
                    ? "CODEX_RESPONSE_INVALID" : "CODEX_TURN_FAILED";
                if (string.IsNullOrWhiteSpace(errorMessage)) errorMessage = externalStatus == "completed"
                    ? "Codex completed without a bounded visible agent reply."
                    : "The Codex turn did not complete successfully.";
            }
        }
        catch (OperationCanceledException) when (begin is not null)
        {
            if (session is not null)
                try { await session.InterruptAsync(CancellationToken.None); } catch (Exception) { }
            terminalStatus = requestCancellation.IsCancellationRequested
                ? AssistantConversationStatuses.Cancelled : AssistantConversationStatuses.Failed;
            externalStatus = "interrupted";
            errorCode = requestCancellation.IsCancellationRequested ? "CODEX_REQUEST_CANCELLED" : "CODEX_TURN_TIMEOUT";
            errorMessage = requestCancellation.IsCancellationRequested
                ? "The Codex request was cancelled." : "The Codex turn exceeded ten minutes.";
        }
        catch (Exception exception) when (begin is not null)
        {
            terminalStatus = AssistantConversationStatuses.Failed;
            externalStatus = "failed";
            errorCode = exception is CodexBridgeException bridge ? bridge.Code :
                exception is AssistantConversationException assistant ? assistant.Code : "CODEX_PROCESS_FAILURE";
            errorMessage = exception is CodexBridgeException or AssistantConversationException
                ? exception.Message : "The Codex bridge failed unexpectedly.";
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
            return;
        }
        finally
        {
            if (begin is not null) registry.Remove(begin.TurnId);
            if (session is not null)
                try { await session.DisposeAsync(); } catch (Exception) { }
            lease?.Dispose();
        }

        if (begin is not null && terminalStatus is AssistantConversationStatuses.Completed or
            AssistantConversationStatuses.Failed or AssistantConversationStatuses.Cancelled)
        {
            try
            {
                await store.CompleteTurnAsync(new(
                    begin.TurnId, terminalStatus,
                    terminalStatus == AssistantConversationStatuses.Completed ? reply : null,
                    errorCode, Bound(errorMessage, 500), modelProvider, model, options.PinnedVersion,
                    "read-only-approval-gated", watch.ElapsedMilliseconds, 0, 0, externalStatus), CancellationToken.None);
                if (!requestCancellation.IsCancellationRequested)
                    await writer.WriteAsync(new("completed",
                        StreamDocument(await RequiredConversation(
                            operatorId, begin.ConversationId, CancellationToken.None))), CancellationToken.None);
            }
            catch (Exception exception) { writer.TryComplete(exception); return; }
        }
        writer.TryComplete();
    }

    private Task CompleteFailedAsync(string turnId, string code, string message, long elapsed) =>
        store.CompleteTurnAsync(new(
            turnId, AssistantConversationStatuses.Failed, null, code, message,
            "openai", "", options.PinnedVersion, "read-only-approval-gated", elapsed, 0, 0, "failed"), CancellationToken.None);

    private async Task<AssistantConversationDocument> RequiredConversation(
        string operatorId, string conversationId, CancellationToken cancellationToken) =>
        await store.GetAsync(operatorId, conversationId, cancellationToken)
        ?? throw new InvalidOperationException("The Codex conversation disappeared.");

    private static AssistantConversationDocument StreamDocument(AssistantConversationDocument document) => new(
        document.Summary,
        document.ExternalThreadId,
        document.Turns.TakeLast(6).ToArray(),
        document.Messages.TakeLast(6).ToArray(),
        document.Activities.TakeLast(12).ToArray(),
        document.Approvals.TakeLast(12).ToArray());

    private static string NormalizeMessage(string value)
    {
        if (value is null) throw Invalid("ASSISTANT_MESSAGE_INVALID", "A message is required.");
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Length is 0 or > MaximumMessageLength)
            throw Invalid("ASSISTANT_MESSAGE_INVALID", $"The message must contain 1 to {MaximumMessageLength} characters.");
        return normalized;
    }
    private static void ValidateIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || !IdempotencyPattern().IsMatch(value))
            throw Invalid("ASSISTANT_IDEMPOTENCY_KEY_INVALID", "The idempotency key is invalid.");
    }
    private static void ValidateOperator(string value)
    {
        if (value?.Length != 74 || !value.StartsWith("principal.", StringComparison.Ordinal))
            throw new ArgumentException("The assistant operator identity is invalid.", nameof(value));
    }
    private static void ValidateConversationId(string value)
    {
        if (value?.Length != 45 || !value.StartsWith("conversation.", StringComparison.Ordinal) ||
            value[13..].Any(character => !char.IsAsciiHexDigitLower(character)))
            throw Invalid("ASSISTANT_CONVERSATION_ID_INVALID", "The conversation ID is invalid.");
    }
    private static void ValidateTurnId(string value)
    {
        if (value?.Length != 37 || !value.StartsWith("turn.", StringComparison.Ordinal) ||
            value[5..].Any(character => !char.IsAsciiHexDigitLower(character)))
            throw Invalid("ASSISTANT_TURN_ID_INVALID", "The turn ID is invalid.");
    }
    private static void ValidateApprovalId(string value)
    {
        if (value?.Length != 41 || !value.StartsWith("approval.", StringComparison.Ordinal) ||
            value[9..].Any(character => !char.IsAsciiHexDigitLower(character)))
            throw Invalid("CODEX_APPROVAL_ID_INVALID", "The approval ID is invalid.");
    }
    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
    private static CodexBridgeException Invalid(string code, string message) => new(code, message);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyPattern();
}
