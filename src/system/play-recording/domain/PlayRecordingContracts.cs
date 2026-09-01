using System.Text.Json;

namespace DantesRoleplay.Play;

public static class PlaySituationKinds
{
    public const string OutOfCharacter = "out-of-character";
    public const string Conversation = "conversation";
    public const string Combat = "combat";
    public const string Exploration = "exploration";
    public const string Investigation = "investigation";
    public const string Travel = "travel";
    public const string Rest = "rest";
    public const string Downtime = "downtime";
    public const string Other = "other";

    public static bool IsKnown(string? value) => value is OutOfCharacter or Conversation or Combat
        or Exploration or Investigation or Travel or Rest or Downtime or Other;
}

public static class PlaySituationTransitions
{
    public const string Continue = "continue";
    public const string Replace = "replace";
    public const string Complete = "complete";

    public static bool IsKnown(string? value) => value is Continue or Replace or Complete;
}

public static class PlaySituationStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";
}

public sealed record PlayParticipant(string Name, string? EntityId = null);

public sealed record PlayLocation(string Name, string? EntityId = null);

public sealed record PlaySituationUpdate(
    string Transition,
    string Kind,
    string Summary,
    IReadOnlyList<PlayParticipant> Participants,
    PlayLocation? Location = null);

public sealed record PlayTruthAssertion(
    string Statement,
    IReadOnlyList<string> SubjectEntityIds);

public sealed record PlaySituationDocument(
    string Id,
    int Revision,
    string Kind,
    string Status,
    string Summary,
    IReadOnlyList<PlayParticipant> Participants,
    PlayLocation? Location,
    DateTime StartedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record PlayTruthDocument(
    string Id,
    int Ordinal,
    string Statement,
    IReadOnlyList<string> SubjectEntityIds,
    string SourceMessageId,
    string? SituationId,
    DateTime CreatedAtUtc);

public sealed record PlayMessageDocument(
    string Id,
    int Ordinal,
    string Role,
    string Text,
    string? Code,
    string? SituationId,
    DateTime CreatedAtUtc);

public sealed record PlayConversationDocument(
    string Id,
    string PrincipalId,
    string ApplicationId,
    string StateSpaceId,
    string SessionContextId,
    string Status,
    int Revision,
    int TotalMessageCount,
    IReadOnlyList<PlayMessageDocument> RecentMessages,
    PlaySituationDocument? CurrentSituation,
    IReadOnlyList<PlayTruthDocument> KnownTruths,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record PlayMessagePage(
    IReadOnlyList<PlayMessageDocument> Messages,
    int? NextBeforeOrdinal);

public sealed record PlayConversationIdentity(
    string PrincipalId,
    string ApplicationId,
    string StateSpaceId,
    string SessionContextId);

public sealed record PlayMessageAppend(
    string Role,
    string Text,
    string? Code,
    DateTime CreatedAtUtc);

public sealed record PlayNarrativeAppend(
    PlayMessageAppend Message,
    PlaySituationUpdate? Situation,
    IReadOnlyList<PlayTruthAssertion> Truths);

public interface IApplicationPlayRecordStore
{
    PlayConversationDocument ResumeOrCreate(PlayConversationIdentity identity);
    PlayConversationDocument? Get(string principalId, string applicationId, string conversationId);
    PlayConversationDocument? GetSession(PlayConversationIdentity identity);
    PlayConversationDocument AppendMessage(string conversationId, PlayMessageAppend message, string status);
    PlayConversationDocument AppendNarrative(
        string conversationId,
        PlayNarrativeAppend narrative,
        string status);
    PlayConversationDocument SetStatus(string conversationId, string status);
    PlayMessagePage GetMessages(
        string principalId,
        string applicationId,
        string conversationId,
        int? beforeOrdinal,
        int limit);
}

public sealed class ApplicationPlayConversationRecord
{
    public required string Id { get; set; }
    public required string PrincipalId { get; set; }
    public required string ApplicationId { get; set; }
    public required string StateSpaceId { get; set; }
    public required string SessionContextId { get; set; }
    public required string Status { get; set; }
    public int Revision { get; set; }
    public string? CurrentSituationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ICollection<ApplicationPlayMessageRecord> Messages { get; } = new List<ApplicationPlayMessageRecord>();
    public ICollection<ApplicationPlaySituationRecord> Situations { get; } = new List<ApplicationPlaySituationRecord>();
    public ICollection<ApplicationPlayTruthRecord> Truths { get; } = new List<ApplicationPlayTruthRecord>();
}

public sealed class ApplicationPlayMessageRecord
{
    public required string Id { get; set; }
    public required string ConversationId { get; set; }
    public int Ordinal { get; set; }
    public required string Role { get; set; }
    public required string Text { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? SituationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ApplicationPlayConversationRecord? Conversation { get; set; }
}

public sealed class ApplicationPlaySituationRecord
{
    public required string Id { get; set; }
    public required string ConversationId { get; set; }
    public int Revision { get; set; }
    public required string Kind { get; set; }
    public required string Status { get; set; }
    public required string Summary { get; set; }
    public required string ParticipantsJson { get; set; }
    public string LocationJson { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public ApplicationPlayConversationRecord? Conversation { get; set; }
}

public sealed class ApplicationPlayTruthRecord
{
    public required string Id { get; set; }
    public required string ConversationId { get; set; }
    public int Ordinal { get; set; }
    public required string Statement { get; set; }
    public required string NormalizedHash { get; set; }
    public required string SubjectEntityIdsJson { get; set; }
    public required string SourceMessageId { get; set; }
    public string? SituationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ApplicationPlayConversationRecord? Conversation { get; set; }
}
