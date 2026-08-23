namespace DantesRoleplay.Quest;

public sealed record QuestReference(string EntityId, string Role, string Audience);
public sealed record QuestObjectiveInput(string LocalKey, string Title, string ActionableSummary, bool Required, string Visibility, int DisplayOrder, IReadOnlyList<string> PrerequisiteLocalKeys, IReadOnlyList<QuestReference> References);
public sealed record QuestCreateRequest(string QuestId, string Title, string Premise, string Summary, string Visibility, string CampaignId, string ArcId, IReadOnlyList<string> ChapterIds, IReadOnlyList<QuestObjectiveInput> Objectives);
public sealed record QuestProblem(string Code, string Path, string Reason, string Recovery);
public sealed record QuestCreateResult(string Status, string QuestId, string OperationId, int? StructuralEventCount, IReadOnlyList<QuestProblem> Problems)
{ public bool Created => Status == "created"; }
public interface IQuestCreator { Task<QuestCreateResult> CreateAsync(QuestCreateRequest request, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default); }
public sealed record QuestLifecycleRequest(string Operation, string QuestId, string ExpectedQuestStatus, string Reason);
public sealed record QuestObjectiveTransitionRequest(string Operation, string QuestId, string ExpectedQuestStatus, string ObjectiveId, string ExpectedObjectiveStatus, string? TargetStatus, string Reason);
public sealed record QuestTransitionResult(string Status, string QuestId, string OperationId, int? StructuralEventCount, IReadOnlyList<string> ChangedObjectiveIds, IReadOnlyList<QuestProblem> Problems)
{ public bool Succeeded => Status == "succeeded"; }
public interface IQuestLifecycleRunner
{
    Task<QuestTransitionResult> TransitionAsync(QuestLifecycleRequest request, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default);
    Task<QuestTransitionResult> TransitionObjectiveAsync(QuestObjectiveTransitionRequest request, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default);
}

/// <summary>The deliberately small evidence projection exposed by the trusted-host quest summary.</summary>
public sealed record QuestEvidenceSummary(string TargetId, string Role, string Audience);
public sealed record QuestObjectiveSummary(string Id, string Title, string Status, string ActionableSummary, bool Required, string Visibility, int DisplayOrder, IReadOnlyList<QuestEvidenceSummary> Evidence);
public sealed record QuestTransitionSummary(string EventId, string RootOperationId, DateTime Timestamp, int Sequence, string EntityId, string RecordKind, string BeforeStatus, string AfterStatus);
public sealed record QuestSummary(string QuestId, string Title, string Status, string Summary, string Visibility, IReadOnlyList<QuestObjectiveSummary> Objectives, IReadOnlyList<QuestTransitionSummary> RecentTransitions, string TrustBoundary);

/// <summary>A fixed trusted-host view of one active, valid Q1–Q2 quest. It is not a graph API.</summary>
public interface IQuestSummaryReader
{
    Task<QuestSummary?> GetAsync(string questId, CancellationToken cancellationToken = default);
}
