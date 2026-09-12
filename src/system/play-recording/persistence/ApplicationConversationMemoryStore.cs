using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Play;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.DataAccess;

public sealed class ApplicationConversationMemoryStore(DantesRoleplayDbContext db) : IConversationMemoryStore
{
    private const int MaximumMessageText = 16_000;
    private const int MaximumTurnText = 24_000;
    private const int MaximumCandidateBytes = 64_000;
    private readonly SemaphoreSlim writeGate = ApplicationPlayWriteCoordinator.For(db);

    public ConversationMemoryJournalDocument Connect(ConversationMemoryBinding binding)
    {
        var normalized = ValidateBinding(binding);
        writeGate.Wait();
        try
        {
            using var transaction = db.Database.BeginTransaction();
            var existing = Journal(normalized.Scope);
            if (existing is not null)
            {
                EnsureBinding(existing, normalized);
                if (existing.Status is ConversationMemoryStatuses.Archived or ConversationMemoryStatuses.Deleted)
                    throw Failure("CONVERSATION_MEMORY_ARCHIVED", "The archived conversation memory cannot be reconnected.");
                if (existing.Status != ConversationMemoryStatuses.Connected || existing.FailureCode is not null)
                {
                    existing.Status = ConversationMemoryStatuses.Connected;
                    existing.FailureCode = null;
                    existing.Revision++;
                    existing.UpdatedAtUtc = DateTime.UtcNow;
                    db.SaveChanges();
                    transaction.Commit();
                }
                return Document(existing);
            }

            var state = db.Set<ApplicationStateSpaceRecord>().AsNoTracking()
                .SingleOrDefault(value => value.Id == normalized.Scope.StateSpaceId);
            if (state is null || state.ApplicationId != normalized.Scope.ApplicationId)
                throw Failure("CONVERSATION_MEMORY_SCOPE_INVALID", "The application state scope is unavailable.");
            if (db.Set<ApplicationConversationMemoryJournalRecord>().AsNoTracking().Any(value =>
                    value.SourceClient == normalized.SourceClient
                    && value.SourceProjectId == normalized.SourceProjectId
                    && value.SourceThreadId == normalized.SourceThreadId))
                throw Failure("CONVERSATION_MEMORY_SOURCE_CONFLICT", "The source thread is already bound to another gameplay session.");

            var now = DateTime.UtcNow;
            var record = new ApplicationConversationMemoryJournalRecord
            {
                Id = NewId("conversation-memory."),
                PrincipalId = normalized.Scope.PrincipalId,
                ApplicationId = normalized.Scope.ApplicationId,
                StateSpaceId = normalized.Scope.StateSpaceId,
                SessionContextId = normalized.Scope.SessionContextId,
                SourceClient = normalized.SourceClient,
                SourceProjectId = normalized.SourceProjectId,
                RepositoryRoot = normalized.RepositoryRoot,
                SourceThreadId = normalized.SourceThreadId,
                Status = ConversationMemoryStatuses.Connected,
                Revision = 1,
                NextOrdinal = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Add(record);
            db.SaveChanges();
            transaction.Commit();
            return Document(record);
        }
        finally { writeGate.Release(); }
    }

    public ConversationMemoryJournalDocument? GetState(ConversationMemoryScope scope, bool includeArchived = false)
    {
        ValidateScope(scope);
        var journal = Journal(scope, noTracking: true);
        return journal is null || !includeArchived && journal.Status is ConversationMemoryStatuses.Archived or ConversationMemoryStatuses.Deleted
            ? null : Document(journal);
    }

    public ConversationMemoryAppendResult AppendTurn(ConversationMemoryTurnAppend append)
    {
        ArgumentNullException.ThrowIfNull(append);
        var binding = ValidateBinding(append.Binding);
        ValidateToken(append.SourceTurnId, 200, nameof(append.SourceTurnId));
        ValidateToken(append.RequestToken, 128, nameof(append.RequestToken));
        if (append.Messages is null || append.Messages.Count is < 2 or > 32
            || append.Messages.Select(value => value.SourceMessageId).Distinct(StringComparer.Ordinal).Count() != append.Messages.Count
            || !append.Messages.Any(value => value.Role == ConversationMemoryRoles.User)
            || !append.Messages.Any(value => value.Role == ConversationMemoryRoles.Assistant))
            throw Failure("CONVERSATION_MEMORY_MESSAGES_INVALID",
                "A completed capture requires 2 to 32 distinct visible user and assistant messages.");
        foreach (var message in append.Messages) ValidateMessage(message);
        var sourceMessageIds = append.Messages.Select(value => value.SourceMessageId).ToArray();
        ValidateText(append.CaptureProvenance, 1_000, nameof(append.CaptureProvenance));
        ValidateUtc(append.CapturedAtUtc, nameof(append.CapturedAtUtc));
        if (append.Messages.Sum(value => Encoding.UTF8.GetByteCount(value.Text)) > MaximumTurnText)
            throw Failure("CONVERSATION_MEMORY_TURN_TOO_LARGE", "The captured turn exceeds its aggregate text bound.");
        var fingerprint = Fingerprint(new
        {
            binding.Scope, binding.SourceClient, binding.SourceProjectId, binding.RepositoryRoot,
            binding.SourceThreadId, append.SourceTurnId, append.Messages,
            append.CaptureProvenance
        });

        writeGate.Wait();
        try
        {
            using var transaction = db.Database.BeginTransaction();
            var journal = RequiredJournal(binding.Scope);
            EnsureBinding(journal, binding);
            if (journal.Status is ConversationMemoryStatuses.Archived or ConversationMemoryStatuses.Deleted
                or ConversationMemoryStatuses.Disconnected)
                throw Failure("CONVERSATION_MEMORY_NOT_CONNECTED", "The gameplay conversation capture is not connected.");
            var delivery = db.Set<ApplicationConversationMemoryDeliveryRecord>()
                .SingleOrDefault(value => value.JournalId == journal.Id && value.RequestToken == append.RequestToken);
            if (delivery is not null)
            {
                if (delivery.PayloadFingerprint != fingerprint)
                    throw Failure("CONVERSATION_MEMORY_REPLAY_CONFLICT", "The capture request token was reused with different content.");
                var messageIds = JsonSerializer.Deserialize<string[]>(delivery.MessageIdsJson) ?? [];
                var replayed = db.Set<ApplicationConversationMemoryMessageRecord>().AsNoTracking()
                    .Where(value => messageIds.Contains(value.Id))
                    .OrderBy(value => value.Ordinal).ToArray();
                if (replayed.Length != messageIds.Length)
                    throw Failure("CONVERSATION_MEMORY_RECONCILIATION_REQUIRED", "The retained capture receipt is incomplete.");
                return new(Document(journal), replayed.Select(Message).ToArray(), delivery.Id, true);
            }
            if (db.Set<ApplicationConversationMemoryDeliveryRecord>().Any(value =>
                    value.JournalId == journal.Id && value.SourceTurnId == append.SourceTurnId)
                || db.Set<ApplicationConversationMemoryMessageRecord>().Any(value => value.JournalId == journal.Id
                    && sourceMessageIds.Contains(value.SourceMessageId)))
                throw Failure("CONVERSATION_MEMORY_SOURCE_CONFLICT", "The source turn or message identity is already retained.");

            var messages = append.Messages.Select(message => AddMessage(journal, append.SourceTurnId, message,
                append.CaptureProvenance, append.CapturedAtUtc)).ToArray();
            journal.LastSourceTurnId = append.SourceTurnId;
            journal.Status = ConversationMemoryStatuses.Connected;
            journal.FailureCode = null;
            journal.Revision++;
            journal.UpdatedAtUtc = append.CapturedAtUtc;
            var receipt = new ApplicationConversationMemoryDeliveryRecord
            {
                Id = DeterministicId("conversation-memory-delivery.", journal.Id, append.RequestToken),
                JournalId = journal.Id,
                RequestToken = append.RequestToken,
                SourceTurnId = append.SourceTurnId,
                PayloadFingerprint = fingerprint,
                MessageIdsJson = InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(
                    messages.Select(value => value.Id).ToArray())),
                CapturedAtUtc = append.CapturedAtUtc
            };
            db.Add(receipt);
            db.SaveChanges();
            transaction.Commit();
            return new(Document(journal), messages.Select(Message).ToArray(), receipt.Id, false);
        }
        finally { writeGate.Release(); }
    }

    public ConversationMemoryMessagePage GetMessages(ConversationMemoryScope scope, int? beforeOrdinal,
        int limit, bool includeArchived = false)
    {
        ValidateScope(scope);
        if (limit is < 1 or > 64) throw Failure("CONVERSATION_MEMORY_LIMIT_INVALID", "Message page size must be between 1 and 64.");
        if (beforeOrdinal is < 1) throw Failure("CONVERSATION_MEMORY_CURSOR_INVALID", "The message cursor is invalid.");
        return ReadSnapshot(() =>
        {
            var journal = RequiredJournal(scope, noTracking: true);
            if (!includeArchived && journal.Status is ConversationMemoryStatuses.Archived or ConversationMemoryStatuses.Deleted)
                throw Failure("CONVERSATION_MEMORY_ARCHIVED", "The conversation memory is archived.");
            var query = db.Set<ApplicationConversationMemoryMessageRecord>().AsNoTracking()
                .Where(value => value.JournalId == journal.Id);
            if (!includeArchived) query = query.Where(value => value.Status == "captured");
            if (beforeOrdinal is not null) query = query.Where(value => value.Ordinal < beforeOrdinal.Value);
            var descending = query.OrderByDescending(value => value.Ordinal).Take(limit + 1).ToArray();
            var page = descending.Take(limit).OrderBy(value => value.Ordinal).Select(Message).ToArray();
            return new ConversationMemoryMessagePage(
                page, descending.Length > limit ? page[0].Ordinal : null, journal.Revision);
        });
    }

    public IReadOnlyList<ConversationMemoryMessageDocument> GetSourceMessages(
        ConversationMemoryScope scope, int sourceRevision, IReadOnlyList<string> sourceMessageIds)
    {
        ValidateScope(scope);
        if (sourceRevision < 1 || sourceMessageIds is null || sourceMessageIds.Count is < 1 or > 64
            || sourceMessageIds.Distinct(StringComparer.Ordinal).Count() != sourceMessageIds.Count)
            throw Failure("CONVERSATION_MEMORY_SOURCE_INVALID", "The exact source selection is invalid.");
        foreach (var id in sourceMessageIds) ValidateToken(id, 200, nameof(sourceMessageIds));
        return ReadSnapshot(() =>
        {
            var journal = RequiredJournal(scope, noTracking: true);
            if (journal.Revision != sourceRevision)
                throw Failure("CONVERSATION_MEMORY_SOURCE_STALE", "The conversation source revision changed.");
            if (journal.Status is ConversationMemoryStatuses.Archived or ConversationMemoryStatuses.Deleted)
                throw Failure("CONVERSATION_MEMORY_ARCHIVED", "The conversation memory is archived.");
            var messages = db.Set<ApplicationConversationMemoryMessageRecord>().AsNoTracking()
                .Where(value => value.JournalId == journal.Id && sourceMessageIds.Contains(value.SourceMessageId)
                    && value.Status == "captured").OrderBy(value => value.Ordinal).ToArray();
            if (messages.Length != sourceMessageIds.Count)
                throw Failure("CONVERSATION_MEMORY_SOURCE_INVALID", "One or more exact source messages are unavailable.");
            return messages.Select(Message).ToArray();
        });
    }

    public ConversationMemoryJournalDocument MarkRetryPending(ConversationMemoryScope scope, string failureCode) =>
        SetStatus(scope, ConversationMemoryStatuses.RetryPending,
            ValidateToken(failureCode, 100, nameof(failureCode)),
            allowedCurrentStatuses: [ConversationMemoryStatuses.Connected, ConversationMemoryStatuses.RetryPending]);

    public ConversationMemoryJournalDocument Retry(ConversationMemoryScope scope) =>
        SetStatus(scope, ConversationMemoryStatuses.Connected, null,
            allowedCurrentStatuses: [ConversationMemoryStatuses.Connected, ConversationMemoryStatuses.RetryPending]);

    public ConversationMemoryJournalDocument Disconnect(ConversationMemoryScope scope) =>
        SetStatus(scope, ConversationMemoryStatuses.Disconnected, null);

    public ConversationMemoryJournalDocument Archive(ConversationMemoryScope scope) =>
        SetStatus(scope, ConversationMemoryStatuses.Archived, null, allowArchived: true);

    public ConversationMemoryDeleteResult Delete(ConversationMemoryScope scope, string? messageId = null)
    {
        ValidateScope(scope);
        if (messageId is not null) ValidateToken(messageId, 80, nameof(messageId));
        writeGate.Wait();
        try
        {
            using var transaction = db.Database.BeginTransaction();
            var journal = RequiredJournal(scope);
            var messages = db.Set<ApplicationConversationMemoryMessageRecord>()
                .Where(value => value.JournalId == journal.Id && (messageId == null || value.Id == messageId))
                .ToArray();
            if (messageId is not null && messages.Length == 0)
                throw Failure("CONVERSATION_MEMORY_MESSAGE_NOT_FOUND", "The scoped conversation message was not found.");
            var ids = messages.Select(value => value.Id).ToArray();
            var retained = db.Set<ApplicationConversationMemoryDerivedSourceRecord>().AsNoTracking()
                .Where(value => ids.Contains(value.MessageId)).Select(value => value.DerivedId)
                .Distinct().OrderBy(value => value).ToArray();
            if (retained.Length > 0) return new(false, 0, retained);
            var count = 0;
            foreach (var message in messages.Where(value => value.Status != "deleted"))
            {
                message.Text = string.Empty;
                message.CaptureProvenance = "deleted-by-authorized-owner";
                message.Status = "deleted";
                count++;
            }
            if (messageId is null) journal.Status = ConversationMemoryStatuses.Deleted;
            journal.Revision++;
            journal.UpdatedAtUtc = DateTime.UtcNow;
            db.SaveChanges();
            transaction.Commit();
            return new(true, count, []);
        }
        finally { writeGate.Release(); }
    }

    public ConversationMemoryDerivedCandidateDocument AppendDerivedCandidate(
        ConversationMemoryDerivedCandidateAppend append)
    {
        ArgumentNullException.ThrowIfNull(append);
        ValidateScope(append.Scope);
        ArgumentNullException.ThrowIfNull(append.Task);
        if (append.SourceRevision < 1) throw Failure("CONVERSATION_MEMORY_REVISION_INVALID", "The source revision is invalid.");
        if (append.SourceMessageIds is null || append.SourceMessageIds.Count is < 1 or > 64
            || append.SourceMessageIds.Distinct(StringComparer.Ordinal).Count() != append.SourceMessageIds.Count)
            throw Failure("CONVERSATION_MEMORY_SOURCE_INVALID", "A dream candidate requires 1 to 64 distinct source messages.");
        foreach (var id in append.SourceMessageIds) ValidateToken(id, 200, nameof(append.SourceMessageIds));
        var candidate = InteractionCanonicalJson.CanonicalizeObject(append.CandidateJson);
        if (Encoding.UTF8.GetByteCount(candidate) > MaximumCandidateBytes)
            throw Failure("CONVERSATION_MEMORY_CANDIDATE_TOO_LARGE", "The derived memory candidate exceeds its byte bound.");
        ValidateFingerprint(append.ResultSchemaFingerprint, nameof(append.ResultSchemaFingerprint));
        ValidateToken(append.CompletionEvidenceReference, 200, nameof(append.CompletionEvidenceReference));
        ValidateUtc(append.CreatedAtUtc, nameof(append.CreatedAtUtc));

        writeGate.Wait();
        try
        {
            using var transaction = db.Database.BeginTransaction();
            var journal = RequiredJournal(append.Scope);
            if (journal.Status is ConversationMemoryStatuses.Archived or ConversationMemoryStatuses.Deleted)
                throw Failure("CONVERSATION_MEMORY_ARCHIVED", "The conversation memory is archived.");
            if (journal.Revision != append.SourceRevision)
                throw Failure("CONVERSATION_MEMORY_SOURCE_STALE", "The conversation source revision changed before the candidate was retained.");
            var messages = db.Set<ApplicationConversationMemoryMessageRecord>()
                .Where(value => value.JournalId == journal.Id
                    && append.SourceMessageIds.Contains(value.SourceMessageId)
                    && value.Status == "captured")
                .OrderBy(value => value.Ordinal).ToArray();
            if (messages.Length != append.SourceMessageIds.Count)
                throw Failure("CONVERSATION_MEMORY_SOURCE_INVALID", "One or more exact source messages are unavailable.");
            var sourceFingerprint = Fingerprint(messages.Select(value => new
            {
                value.SourceMessageId, value.SourceTurnId, value.Role, value.TextFingerprint
            }).ToArray());
            var id = DeterministicId("conversation-memory-derived.", journal.Id,
                append.Task.TaskId + "\n" + append.Task.CommandId);
            var existing = db.Set<ApplicationConversationMemoryDerivedRecord>()
                .SingleOrDefault(value => value.Id == id);
            if (existing is not null)
            {
                if (existing.SourceRevision != append.SourceRevision || existing.SourceFingerprint != sourceFingerprint
                    || existing.CandidateJson != candidate
                    || existing.ResultSchemaFingerprint != append.ResultSchemaFingerprint
                    || existing.CompletionEvidenceReference != append.CompletionEvidenceReference)
                    throw Failure("CONVERSATION_MEMORY_DERIVED_CONFLICT", "The durable task already retained a different memory candidate.");
                return Derived(existing, messages.Select(value => value.SourceMessageId).ToArray());
            }
            var record = new ApplicationConversationMemoryDerivedRecord
            {
                Id = id,
                JournalId = journal.Id,
                TaskId = append.Task.TaskId,
                CommandId = append.Task.CommandId,
                SourceRevision = append.SourceRevision,
                SourceFingerprint = sourceFingerprint,
                CandidateJson = candidate,
                ResultSchemaFingerprint = append.ResultSchemaFingerprint,
                CompletionEvidenceReference = append.CompletionEvidenceReference,
                Audience = "private",
                Status = "candidate",
                CreatedAtUtc = append.CreatedAtUtc
            };
            db.Add(record);
            foreach (var message in messages)
                db.Add(new ApplicationConversationMemoryDerivedSourceRecord
                    { DerivedId = record.Id, MessageId = message.Id });
            db.SaveChanges();
            transaction.Commit();
            return Derived(record, messages.Select(value => value.SourceMessageId).ToArray());
        }
        finally { writeGate.Release(); }
    }

    public IReadOnlyList<ConversationMemoryDerivedCandidateDocument> GetDerivedCandidates(
        ConversationMemoryScope scope, int limit = 20, bool includeArchived = false)
    {
        ValidateScope(scope);
        if (limit is < 1 or > 64) throw Failure("CONVERSATION_MEMORY_LIMIT_INVALID", "Candidate limit must be between 1 and 64.");
        return ReadSnapshot(() =>
        {
            var journal = RequiredJournal(scope, noTracking: true);
            if (!includeArchived && journal.Status is ConversationMemoryStatuses.Archived or ConversationMemoryStatuses.Deleted)
                throw Failure("CONVERSATION_MEMORY_ARCHIVED", "The conversation memory is archived.");
            var records = db.Set<ApplicationConversationMemoryDerivedRecord>().AsNoTracking()
                .Where(value => value.JournalId == journal.Id && (includeArchived || value.Status == "candidate"))
                .OrderByDescending(value => value.CreatedAtUtc).ThenBy(value => value.Id).Take(limit).ToArray();
            var recordIds = records.Select(value => value.Id).ToArray();
            var links = db.Set<ApplicationConversationMemoryDerivedSourceRecord>().AsNoTracking()
                .Where(value => recordIds.Contains(value.DerivedId)).Join(
                    db.Set<ApplicationConversationMemoryMessageRecord>().AsNoTracking(),
                    link => link.MessageId, message => message.Id,
                    (link, message) => new { link.DerivedId, message.SourceMessageId, message.Ordinal })
                .ToArray().GroupBy(value => value.DerivedId).ToDictionary(value => value.Key,
                    value => value.OrderBy(item => item.Ordinal).Select(item => item.SourceMessageId).ToArray());
            return records.Select(value => Derived(value, links.GetValueOrDefault(value.Id) ?? [])).ToArray();
        });
    }

    private ConversationMemoryJournalDocument SetStatus(ConversationMemoryScope scope, string status,
        string? failureCode, bool allowArchived = false,
        string[]? allowedCurrentStatuses = null)
    {
        ValidateScope(scope);
        writeGate.Wait();
        try
        {
            var journal = RequiredJournal(scope);
            if (!allowArchived && journal.Status is ConversationMemoryStatuses.Archived or ConversationMemoryStatuses.Deleted)
                throw Failure("CONVERSATION_MEMORY_ARCHIVED", "The conversation memory is archived.");
            if (allowedCurrentStatuses is not null && !allowedCurrentStatuses.Contains(journal.Status))
                throw Failure("CONVERSATION_MEMORY_NOT_CONNECTED",
                    "The disconnected gameplay conversation capture requires an explicit connect.");
            if (journal.Status != status || journal.FailureCode != failureCode)
            {
                journal.Status = status;
                journal.FailureCode = failureCode;
                journal.Revision++;
                journal.UpdatedAtUtc = DateTime.UtcNow;
                db.SaveChanges();
            }
            return Document(journal);
        }
        finally { writeGate.Release(); }
    }

    private ApplicationConversationMemoryMessageRecord AddMessage(
        ApplicationConversationMemoryJournalRecord journal, string turnId,
        ConversationMemoryCapturedMessage message, string provenance, DateTime capturedAtUtc)
    {
        var record = new ApplicationConversationMemoryMessageRecord
        {
            Id = DeterministicId("conversation-memory-message.", journal.Id, message.SourceMessageId),
            JournalId = journal.Id,
            Ordinal = journal.NextOrdinal++,
            SourceTurnId = turnId,
            SourceMessageId = message.SourceMessageId,
            Role = message.Role,
            SourceKind = message.SourceKind,
            Text = message.Text,
            TextFingerprint = Hash(message.Text),
            CaptureProvenance = provenance,
            SourceAtUtc = message.SourceAtUtc,
            CapturedAtUtc = capturedAtUtc,
            Status = "captured"
        };
        db.Add(record);
        return record;
    }

    private ApplicationConversationMemoryJournalRecord? Journal(ConversationMemoryScope scope,
        bool noTracking = false)
    {
        var query = db.Set<ApplicationConversationMemoryJournalRecord>().AsQueryable();
        if (noTracking) query = query.AsNoTracking();
        return query.SingleOrDefault(value => value.PrincipalId == scope.PrincipalId
            && value.ApplicationId == scope.ApplicationId && value.StateSpaceId == scope.StateSpaceId
            && value.SessionContextId == scope.SessionContextId);
    }

    private ApplicationConversationMemoryJournalRecord RequiredJournal(ConversationMemoryScope scope,
        bool noTracking = false) => Journal(scope, noTracking)
        ?? throw Failure("CONVERSATION_MEMORY_NOT_FOUND", "The scoped conversation memory was not found.");

    private static ConversationMemoryJournalDocument Document(ApplicationConversationMemoryJournalRecord value) => new(
        value.Id,
        new(new(value.PrincipalId, value.ApplicationId, value.StateSpaceId, value.SessionContextId),
            value.SourceClient, value.SourceProjectId, value.RepositoryRoot, value.SourceThreadId),
        value.Status, value.Revision, value.NextOrdinal - 1, value.LastSourceTurnId, value.FailureCode,
        value.CreatedAtUtc, value.UpdatedAtUtc);

    private static ConversationMemoryMessageDocument Message(ApplicationConversationMemoryMessageRecord value) => new(
        value.Id, value.Ordinal, value.SourceTurnId, value.SourceMessageId, value.Role, value.SourceKind,
        value.Status == "deleted" ? null : value.Text, value.TextFingerprint, value.CaptureProvenance,
        value.SourceAtUtc, value.CapturedAtUtc, value.Status);

    private static ConversationMemoryDerivedCandidateDocument Derived(
        ApplicationConversationMemoryDerivedRecord value, IReadOnlyList<string> sourceIds) => new(
        value.Id, new(value.TaskId, value.CommandId), value.SourceRevision, sourceIds,
        value.SourceFingerprint, value.CandidateJson, value.ResultSchemaFingerprint,
        value.CompletionEvidenceReference, value.Audience, value.Status, value.CreatedAtUtc);

    private static ConversationMemoryBinding ValidateBinding(ConversationMemoryBinding value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateScope(value.Scope);
        if (value.SourceClient != "codex")
            throw Failure("CONVERSATION_MEMORY_SOURCE_UNSUPPORTED", "Only explicitly linked Codex capture is currently supported.");
        ValidateToken(value.SourceProjectId, 200, nameof(value.SourceProjectId));
        ValidateToken(value.SourceThreadId, 200, nameof(value.SourceThreadId));
        ValidateText(value.RepositoryRoot, 1_024, nameof(value.RepositoryRoot));
        if (!Path.IsPathFullyQualified(value.RepositoryRoot))
            throw Failure("CONVERSATION_MEMORY_REPOSITORY_INVALID", "The linked repository root must be absolute.");
        return value with { RepositoryRoot = Path.GetFullPath(value.RepositoryRoot).TrimEnd(Path.DirectorySeparatorChar) };
    }

    private static void EnsureBinding(ApplicationConversationMemoryJournalRecord journal,
        ConversationMemoryBinding binding)
    {
        if (journal.SourceClient != binding.SourceClient
            || journal.SourceProjectId != binding.SourceProjectId
            || journal.SourceThreadId != binding.SourceThreadId
            || !string.Equals(journal.RepositoryRoot, binding.RepositoryRoot, StringComparison.OrdinalIgnoreCase))
            throw Failure("CONVERSATION_MEMORY_BINDING_CONFLICT", "The gameplay session is linked to a different capture source.");
    }

    private static void ValidateScope(ConversationMemoryScope value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateToken(value.PrincipalId, 100, nameof(value.PrincipalId));
        ValidateToken(value.ApplicationId, 63, nameof(value.ApplicationId));
        ValidateToken(value.StateSpaceId, 200, nameof(value.StateSpaceId));
        ValidateToken(value.SessionContextId, 200, nameof(value.SessionContextId));
    }

    private static void ValidateMessage(ConversationMemoryCapturedMessage value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateToken(value.SourceMessageId, 200, nameof(value.SourceMessageId));
        if (!ConversationMemoryRoles.IsKnown(value.Role) || !ConversationMemoryMessageKinds.IsKnown(value.SourceKind)
            || value.Role == ConversationMemoryRoles.User && value.SourceKind != ConversationMemoryMessageKinds.UserPrompt
            || value.Role == ConversationMemoryRoles.Assistant && value.SourceKind == ConversationMemoryMessageKinds.UserPrompt)
            throw Failure("CONVERSATION_MEMORY_ROLE_INVALID", "The captured visible-message role and source kind do not agree.");
        ValidateText(value.Text, MaximumMessageText, nameof(value.Text));
        ValidateUtc(value.SourceAtUtc, nameof(value.SourceAtUtc));
    }

    private static string ValidateToken(string value, int maximum, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > maximum
            || value.Any(char.IsControl))
            throw Failure("CONVERSATION_MEMORY_ID_INVALID", $"{parameter} is invalid.");
        return value;
    }

    private static void ValidateText(string value, int maximum, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.IndexOf('\0') >= 0)
            throw Failure("CONVERSATION_MEMORY_TEXT_INVALID", $"{parameter} is invalid.");
    }

    private static void ValidateUtc(DateTime value, string parameter)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw Failure("CONVERSATION_MEMORY_TIME_INVALID", $"{parameter} must be UTC.");
    }

    private static void ValidateFingerprint(string value, string parameter)
    {
        if (value is not { Length: 64 } || value.Any(character => character is not (>= '0' and <= '9' or >= 'A' and <= 'F')))
            throw Failure("CONVERSATION_MEMORY_FINGERPRINT_INVALID", $"{parameter} is invalid.");
    }

    private static string Fingerprint<T>(T value) => Hash(
        InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(value)));

    private T ReadSnapshot<T>(Func<T> read)
    {
        db.Database.OpenConnection();
        try
        {
            var connection = db.Database.GetDbConnection() as SqliteConnection
                ?? throw new InvalidOperationException("Conversation memory requires SQLite.");
            using var native = connection.BeginTransaction(deferred: true);
            using var transaction = db.Database.UseTransaction(native)
                ?? throw new InvalidOperationException("The SQLite read snapshot could not be established.");
            var result = read();
            transaction.Commit();
            return result;
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("N");
    private static string DeterministicId(string prefix, string owner, string key) =>
        prefix + Hash(owner + "\n" + key)[..32].ToLowerInvariant();
    private static ConversationMemoryException Failure(string code, string message) => new(code, message);
}
