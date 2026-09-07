using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Play;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

public sealed class ApplicationPlayRecordStore(DantesRoleplayDbContext db) : IApplicationPlayRecordStore
{
    private const int RecentMessageLimit = 64;
    private const int KnownTruthLimit = 64;
    private readonly SemaphoreSlim writeGate = ApplicationPlayWriteCoordinator.For(db);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public PlayConversationDocument ResumeOrCreate(PlayConversationIdentity identity)
    {
        ValidateIdentity(identity);
        writeGate.Wait();
        try
        {
            var existing = db.Set<ApplicationPlayConversationRecord>().SingleOrDefault(value =>
                value.PrincipalId == identity.PrincipalId
                && value.ApplicationId == identity.ApplicationId
                && value.StateSpaceId == identity.StateSpaceId
                && value.SessionContextId == identity.SessionContextId);
            if (existing is not null) return Document(existing);

            var now = DateTime.UtcNow;
            var record = new ApplicationPlayConversationRecord
            {
                Id = NewId("play-conversation."),
                PrincipalId = identity.PrincipalId,
                ApplicationId = identity.ApplicationId,
                StateSpaceId = identity.StateSpaceId,
                SessionContextId = identity.SessionContextId,
                Status = "ready",
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Add(record);
            db.SaveChanges();
            return Document(record);
        }
        finally { writeGate.Release(); }
    }

    public PlayConversationDocument? Get(
        string principalId,
        string applicationId,
        string conversationId)
    {
        ValidateId(principalId, 100, nameof(principalId));
        ValidateId(applicationId, 63, nameof(applicationId));
        ValidateId(conversationId, 80, nameof(conversationId));
        var record = db.Set<ApplicationPlayConversationRecord>().AsNoTracking().SingleOrDefault(value =>
            value.Id == conversationId
            && value.PrincipalId == principalId
            && value.ApplicationId == applicationId);
        return record is null ? null : Document(record);
    }

    public PlayConversationDocument? GetSession(PlayConversationIdentity identity)
    {
        ValidateIdentity(identity);
        var record = db.Set<ApplicationPlayConversationRecord>().AsNoTracking().SingleOrDefault(value =>
            value.PrincipalId == identity.PrincipalId
            && value.ApplicationId == identity.ApplicationId
            && value.StateSpaceId == identity.StateSpaceId
            && value.SessionContextId == identity.SessionContextId);
        return record is null ? null : Document(record);
    }

    public PlayConversationDocument AppendMessage(
        string conversationId,
        PlayMessageAppend message,
        string status)
    {
        ValidateMessage(message);
        ValidateStatus(status);
        writeGate.Wait();
        try
        {
            using var transaction = db.Database.BeginTransaction();
            var conversation = RequiredConversation(conversationId);
            AddMessage(conversation, message, status, situationId: conversation.CurrentSituationId);
            db.SaveChanges();
            transaction.Commit();
            return Document(conversation);
        }
        finally { writeGate.Release(); }
    }

    public PlayConversationDocument AppendNarrative(
        string conversationId,
        PlayNarrativeAppend narrative,
        string status)
    {
        ArgumentNullException.ThrowIfNull(narrative);
        ValidateMessage(narrative.Message);
        ValidateStatus(status);
        ValidateSituation(narrative.Situation);
        ValidateTruths(narrative.Truths);
        writeGate.Wait();
        try
        {
            using var transaction = db.Database.BeginTransaction();
            var conversation = RequiredConversation(conversationId);
            var messageSituationId = ApplySituation(conversation, narrative.Situation);
            var message = AddMessage(conversation, narrative.Message, status, messageSituationId);
            AppendTruths(conversation, message, messageSituationId, narrative.Truths);
            db.SaveChanges();
            transaction.Commit();
            return Document(conversation);
        }
        finally { writeGate.Release(); }
    }

    public PlayMessagePage GetMessages(
        string principalId,
        string applicationId,
        string conversationId,
        int? beforeOrdinal,
        int limit)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (beforeOrdinal is < 1) throw new ArgumentOutOfRangeException(nameof(beforeOrdinal));
        if (!db.Set<ApplicationPlayConversationRecord>().AsNoTracking().Any(value =>
                value.Id == conversationId
                && value.PrincipalId == principalId
                && value.ApplicationId == applicationId))
            throw new KeyNotFoundException("PLAY_CONVERSATION_UNKNOWN");
        var query = db.Set<ApplicationPlayMessageRecord>().AsNoTracking()
            .Where(value => value.ConversationId == conversationId);
        if (beforeOrdinal is { } before) query = query.Where(value => value.Ordinal < before);
        var descending = query.OrderByDescending(value => value.Ordinal).Take(limit + 1).ToArray();
        var hasMore = descending.Length > limit;
        var page = (hasMore ? descending[..limit] : descending)
            .OrderBy(value => value.Ordinal).Select(Message).ToArray();
        return new(page, hasMore ? page[0].Ordinal : null);
    }

    public PlayConversationDocument SetStatus(string conversationId, string status)
    {
        ValidateStatus(status);
        writeGate.Wait();
        try
        {
            var conversation = RequiredConversation(conversationId);
            if (conversation.Status != status)
            {
                conversation.Status = status;
                conversation.Revision++;
                conversation.UpdatedAtUtc = DateTime.UtcNow;
                db.SaveChanges();
            }
            return Document(conversation);
        }
        finally { writeGate.Release(); }
    }

    private ApplicationPlayConversationRecord RequiredConversation(string conversationId) =>
        db.Set<ApplicationPlayConversationRecord>().SingleOrDefault(value => value.Id == conversationId)
        ?? throw new KeyNotFoundException("PLAY_CONVERSATION_UNKNOWN");

    private ApplicationPlayMessageRecord AddMessage(
        ApplicationPlayConversationRecord conversation,
        PlayMessageAppend message,
        string status,
        string? situationId)
    {
        var ordinal = (db.Set<ApplicationPlayMessageRecord>()
            .Where(value => value.ConversationId == conversation.Id)
            .Max(value => (int?)value.Ordinal) ?? 0) + 1;
        var row = new ApplicationPlayMessageRecord
        {
            Id = NewId("play-message."),
            ConversationId = conversation.Id,
            Ordinal = ordinal,
            Role = message.Role,
            Text = message.Text,
            Code = message.Code ?? string.Empty,
            SituationId = situationId,
            CreatedAtUtc = message.CreatedAtUtc
        };
        db.Add(row);
        conversation.Status = status;
        conversation.Revision++;
        conversation.UpdatedAtUtc = message.CreatedAtUtc;
        return row;
    }

    private string? ApplySituation(
        ApplicationPlayConversationRecord conversation,
        PlaySituationUpdate? update)
    {
        if (update is null) return conversation.CurrentSituationId;
        var now = DateTime.UtcNow;
        var current = conversation.CurrentSituationId is null
            ? null
            : db.Set<ApplicationPlaySituationRecord>()
                .SingleOrDefault(value => value.Id == conversation.CurrentSituationId
                    && value.ConversationId == conversation.Id);
        if (update.Transition == PlaySituationTransitions.Complete)
        {
            Complete(current, now);
            conversation.CurrentSituationId = null;
            return current?.Id;
        }
        if (update.Transition == PlaySituationTransitions.Replace
            || current is null
            || current.Status != PlaySituationStatuses.Active
            || current.Kind != update.Kind)
        {
            Complete(current, now);
            current = new ApplicationPlaySituationRecord
            {
                Id = NewId("play-situation."),
                ConversationId = conversation.Id,
                Revision = 1,
                Kind = update.Kind,
                Status = PlaySituationStatuses.Active,
                Summary = update.Summary,
                ParticipantsJson = JsonSerializer.Serialize(NormalizeParticipants(update.Participants), Json),
                LocationJson = update.Location is null ? string.Empty : JsonSerializer.Serialize(update.Location, Json),
                StartedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Add(current);
            conversation.CurrentSituationId = current.Id;
            return current.Id;
        }
        current.Summary = update.Summary;
        current.ParticipantsJson = JsonSerializer.Serialize(NormalizeParticipants(update.Participants), Json);
        current.LocationJson = update.Location is null ? string.Empty : JsonSerializer.Serialize(update.Location, Json);
        current.Revision++;
        current.UpdatedAtUtc = now;
        return current.Id;
    }

    private static void Complete(ApplicationPlaySituationRecord? situation, DateTime now)
    {
        if (situation is null || situation.Status == PlaySituationStatuses.Completed) return;
        situation.Status = PlaySituationStatuses.Completed;
        situation.Revision++;
        situation.UpdatedAtUtc = now;
        situation.CompletedAtUtc = now;
    }

    private void AppendTruths(
        ApplicationPlayConversationRecord conversation,
        ApplicationPlayMessageRecord message,
        string? situationId,
        IReadOnlyList<PlayTruthAssertion> truths)
    {
        if (truths.Count == 0) return;
        var known = db.Set<ApplicationPlayTruthRecord>()
            .Where(value => value.ConversationId == conversation.Id)
            .Select(value => value.NormalizedHash).ToHashSet(StringComparer.Ordinal);
        var next = (db.Set<ApplicationPlayTruthRecord>()
            .Where(value => value.ConversationId == conversation.Id)
            .Max(value => (int?)value.Ordinal) ?? 0) + 1;
        foreach (var truth in truths)
        {
            var hash = TruthHash(truth.Statement);
            if (!known.Add(hash)) continue;
            db.Add(new ApplicationPlayTruthRecord
            {
                Id = NewId("play-truth."),
                ConversationId = conversation.Id,
                Ordinal = next++,
                Statement = truth.Statement,
                NormalizedHash = hash,
                SubjectEntityIdsJson = JsonSerializer.Serialize(
                    truth.SubjectEntityIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), Json),
                SourceMessageId = message.Id,
                SituationId = situationId,
                CreatedAtUtc = message.CreatedAtUtc
            });
        }
    }

    private PlayConversationDocument Document(ApplicationPlayConversationRecord conversation)
    {
        var messages = db.Set<ApplicationPlayMessageRecord>().AsNoTracking()
            .Where(value => value.ConversationId == conversation.Id)
            .OrderByDescending(value => value.Ordinal).Take(RecentMessageLimit).ToArray()
            .OrderBy(value => value.Ordinal).Select(Message).ToArray();
        var truths = db.Set<ApplicationPlayTruthRecord>().AsNoTracking()
            .Where(value => value.ConversationId == conversation.Id)
            .OrderByDescending(value => value.Ordinal).Take(KnownTruthLimit).ToArray()
            .OrderBy(value => value.Ordinal).Select(Truth).ToArray();
        var count = db.Set<ApplicationPlayMessageRecord>().AsNoTracking()
            .Count(value => value.ConversationId == conversation.Id);
        var situation = conversation.CurrentSituationId is null ? null
            : db.Set<ApplicationPlaySituationRecord>().AsNoTracking()
                .SingleOrDefault(value => value.Id == conversation.CurrentSituationId
                    && value.ConversationId == conversation.Id);
        return new(
            conversation.Id,
            conversation.PrincipalId,
            conversation.ApplicationId,
            conversation.StateSpaceId,
            conversation.SessionContextId,
            conversation.Status,
            conversation.Revision,
            count,
            messages,
            situation is null ? null : Situation(situation),
            truths,
            conversation.CreatedAtUtc,
            conversation.UpdatedAtUtc);
    }

    private static PlayMessageDocument Message(ApplicationPlayMessageRecord value) => new(
        value.Id, value.Ordinal, value.Role, value.Text,
        value.Code.Length == 0 ? null : value.Code,
        value.SituationId, value.CreatedAtUtc);

    private static PlaySituationDocument Situation(ApplicationPlaySituationRecord value) => new(
        value.Id,
        value.Revision,
        value.Kind,
        value.Status,
        value.Summary,
        JsonSerializer.Deserialize<PlayParticipant[]>(value.ParticipantsJson, Json) ?? [],
        value.LocationJson.Length == 0
            ? null
            : JsonSerializer.Deserialize<PlayLocation>(value.LocationJson, Json),
        value.StartedAtUtc,
        value.UpdatedAtUtc,
        value.CompletedAtUtc);

    private static PlayTruthDocument Truth(ApplicationPlayTruthRecord value) => new(
        value.Id,
        value.Ordinal,
        value.Statement,
        JsonSerializer.Deserialize<string[]>(value.SubjectEntityIdsJson, Json) ?? [],
        value.SourceMessageId,
        value.SituationId,
        value.CreatedAtUtc);

    private static IReadOnlyList<PlayParticipant> NormalizeParticipants(
        IReadOnlyList<PlayParticipant> participants) => participants
        .GroupBy(value => (value.Name, value.EntityId), EqualityComparer<(string, string?)>.Default)
        .Select(value => value.First())
        .OrderBy(value => value.EntityId ?? "~", StringComparer.Ordinal)
        .ThenBy(value => value.Name, StringComparer.Ordinal)
        .ToArray();

    private static string TruthHash(string statement)
    {
        var normalized = string.Join(' ', statement.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("n");

    private static void ValidateIdentity(PlayConversationIdentity value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateId(value.PrincipalId, 100, nameof(value.PrincipalId));
        ValidateId(value.ApplicationId, 63, nameof(value.ApplicationId));
        ValidateId(value.StateSpaceId, 200, nameof(value.StateSpaceId));
        ValidateId(value.SessionContextId, 200, nameof(value.SessionContextId));
    }

    private static void ValidateMessage(PlayMessageAppend value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Role is not ("player" or "assistant")
            || !BoundedText(value.Text, 8_000)
            || value.Code is { Length: > 100 }
            || value.CreatedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A play message is invalid.", nameof(value));
    }

    private static void ValidateStatus(string value)
    {
        if (value is not ("ready" or "planning" or "awaiting-confirmation" or "needs-attention" or "unavailable"))
            throw new ArgumentException("The play conversation status is invalid.", nameof(value));
    }

    private static void ValidateSituation(PlaySituationUpdate? value)
    {
        if (value is null) return;
        if (!PlaySituationTransitions.IsKnown(value.Transition)
            || !PlaySituationKinds.IsKnown(value.Kind)
            || !BoundedText(value.Summary, 1_000)
            || value.Participants.Count > 32
            || value.Participants.Any(participant => !BoundedText(participant.Name, 200)
                || participant.EntityId is not null && !BoundedId(participant.EntityId, 200))
            || value.Location is not null && (!BoundedText(value.Location.Name, 200)
                || value.Location.EntityId is not null && !BoundedId(value.Location.EntityId, 200)))
            throw new ArgumentException("The play situation update is invalid.", nameof(value));
    }

    private static void ValidateTruths(IReadOnlyList<PlayTruthAssertion> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > 12 || values.Any(value => !BoundedText(value.Statement, 1_000)
            || value.SubjectEntityIds.Count > 32
            || value.SubjectEntityIds.Any(entityId => !BoundedId(entityId, 200))))
            throw new ArgumentException("The play truth assertions are invalid.", nameof(values));
    }

    private static void ValidateId(string value, int maximum, string parameterName)
    {
        if (!BoundedId(value, maximum)) throw new ArgumentException(
            "A bounded nonblank identity is required.", parameterName);
    }

    private static bool BoundedId(string value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);

    private static bool BoundedText(string value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum
        && !value.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t');
}

/// <summary>
/// Bounded process-local coordination for SQLite writers. All contexts targeting one database
/// resolve to the same stripe; unrelated database files can progress independently without an
/// unbounded per-path lock dictionary. SQLite transaction ownership remains inside the store.
/// </summary>
internal static class ApplicationPlayWriteCoordinator
{
    internal const int StripeCount = 64;
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, StripeCount)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    internal static SemaphoreSlim For(DantesRoleplayDbContext db) => Gates[StripeFor(db)];

    internal static int StripeFor(DantesRoleplayDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        var connection = db.Database.GetDbConnection();
        var dataSource = connection.DataSource;
        string identity;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource is ":memory:")
        {
            // A SQLite in-memory database is owned by its open connection. Contexts sharing that
            // connection must share the write gate; separate test/runtime databases need not.
            identity = $"memory:{RuntimeHelpers.GetHashCode(connection)}";
        }
        else
        {
            try { dataSource = Path.GetFullPath(dataSource); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            { /* Hash the provider's stable data-source identity as supplied. */ }
            identity = OperatingSystem.IsWindows() ? dataSource.ToUpperInvariant() : dataSource;
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(connection.GetType().FullName + "|" + identity));
        return (int)(BitConverter.ToUInt32(hash, 0) % StripeCount);
    }
}
