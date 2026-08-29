namespace DantesRoleplay.SystemFeedback;

/// <summary>Classification supplied by an operating model when it reports a system experience.</summary>
public enum SystemFeedbackCategory { Defect, Friction, Documentation, Suggestion, Positive }

/// <summary>How much the reported experience prevented useful work.</summary>
public enum SystemFeedbackImpact { Blocked, Degraded, Minor, None }

/// <summary>Local developer triage states for an otherwise immutable report.</summary>
public enum SystemFeedbackState { Open, Acknowledged, Resolved, Dismissed }

/// <summary>Whether a local retention hold prevents reversible archival.</summary>
public enum SystemFeedbackHoldState { None, Held }

/// <summary>Closed, append-only actions for local feedback retention staging.</summary>
public enum SystemFeedbackRetentionActionKind { Archive, Restore, PlaceHold, ReleaseHold }

/// <summary>Durable, append-only report about the host system rather than the game world.</summary>
public sealed class SystemFeedbackReport
{
    public required string Id { get; set; }
    public required string RequestToken { get; set; }
    public required string PayloadFingerprint { get; set; }
    public SystemFeedbackCategory Category { get; set; }
    public SystemFeedbackImpact Impact { get; set; }
    public SystemFeedbackState State { get; set; } = SystemFeedbackState.Open;
    /// <summary>Monotonically increasing optimistic-concurrency revision for local triage.</summary>
    public int TriageRevision { get; set; }
    /// <summary>Monotonically increasing optimistic-concurrency revision for local retention.</summary>
    public int RetentionRevision { get; set; }
    /// <summary>Reversible local archival projection; archival does not change feedback lifecycle.</summary>
    public DateTime? ArchivedAt { get; set; }
    public SystemFeedbackHoldState HoldState { get; set; } = SystemFeedbackHoldState.None;
    public required string Summary { get; set; }
    public required string Observed { get; set; }
    public string? Expected { get; set; }
    public DateTime CreatedAt { get; set; }
    public required string SubmissionOperationId { get; set; }
    public ICollection<SystemFeedbackStep> Steps { get; set; } = new List<SystemFeedbackStep>();
    public ICollection<SystemFeedbackOperationReference> OperationReferences { get; set; } = new List<SystemFeedbackOperationReference>();
    public ICollection<SystemFeedbackProcedureReference> ProcedureReferences { get; set; } = new List<SystemFeedbackProcedureReference>();
    public ICollection<SystemFeedbackDisposition> Dispositions { get; set; } = new List<SystemFeedbackDisposition>();
    public ICollection<SystemFeedbackRetentionAction> RetentionActions { get; set; } = new List<SystemFeedbackRetentionAction>();
}

/// <summary>Immutable local record of an accepted state transition.</summary>
public sealed class SystemFeedbackDisposition
{
    public required string Id { get; set; }
    public required string ReportId { get; set; }
    public int Revision { get; set; }
    public SystemFeedbackState FromState { get; set; }
    public SystemFeedbackState ToState { get; set; }
    public required string Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public SystemFeedbackReport? Report { get; set; }
}

/// <summary>Immutable local record of an archive, restore, hold, or hold-release transition.</summary>
public sealed class SystemFeedbackRetentionAction
{
    public required string Id { get; set; }
    public required string ReportId { get; set; }
    public int Revision { get; set; }
    public SystemFeedbackRetentionActionKind Action { get; set; }
    public bool FromArchived { get; set; }
    public bool ToArchived { get; set; }
    public SystemFeedbackHoldState FromHoldState { get; set; }
    public SystemFeedbackHoldState ToHoldState { get; set; }
    public string? Reference { get; set; }
    public required string Note { get; set; }
    public DateTime? EffectiveAsOf { get; set; }
    public DateTime CreatedAt { get; set; }
    public SystemFeedbackReport? Report { get; set; }
}

public sealed class SystemFeedbackStep
{
    public int Id { get; set; }
    public required string ReportId { get; set; }
    public int Ordinal { get; set; }
    public required string Text { get; set; }
    public SystemFeedbackReport? Report { get; set; }
}

public sealed class SystemFeedbackOperationReference
{
    public int Id { get; set; }
    public required string ReportId { get; set; }
    public required string OperationId { get; set; }
    public int Ordinal { get; set; }
    public SystemFeedbackReport? Report { get; set; }
}

public sealed class SystemFeedbackProcedureReference
{
    public int Id { get; set; }
    public required string ReportId { get; set; }
    public required string ProcedureId { get; set; }
    public int ProcedureVersion { get; set; }
    public int Ordinal { get; set; }
    public SystemFeedbackReport? Report { get; set; }
}

/// <summary>The closed payload accepted by <c>commit(kind: "feedback")</c>.</summary>
public sealed record SystemFeedbackSubmitRequest(
    string? RequestToken,
    string? Category,
    string? Impact,
    string? Summary,
    string? Observed,
    string? Expected = null,
    IReadOnlyList<string>? ReproductionSteps = null,
    IReadOnlyList<string>? RelatedOperationIds = null,
    IReadOnlyList<string>? RelatedProcedureIds = null);

public sealed record SystemFeedbackProcedureView(string Id, int Version);

/// <summary>A reader-safe projection. EF entities never leave the data-access boundary.</summary>
public sealed record SystemFeedbackView(
    string Id,
    string Category,
    string Impact,
    string State,
    string Summary,
    string Observed,
    string? Expected,
    DateTime CreatedAt,
    string SubmissionOperationId,
    IReadOnlyList<string> ReproductionSteps,
    IReadOnlyList<string> RelatedOperationIds,
    IReadOnlyList<SystemFeedbackProcedureView> RelatedProcedures);

public sealed record SystemFeedbackProblem(string Code, string Path, string Message, string Fix);

public sealed record SystemFeedbackSubmitResult(
    SystemFeedbackView? Report,
    string OperationId,
    bool Duplicate,
    SystemFeedbackProblem? Problem = null)
{
    public bool Ok => Problem is null;
}

public sealed record SystemFeedbackFindResult(
    IReadOnlyList<SystemFeedbackView> Reports,
    SystemFeedbackProblem? Problem = null)
{
    public bool Ok => Problem is null;
}

/// <summary>Closed local-administration transition input. It is not part of the MCP protocol.</summary>
public sealed record SystemFeedbackDispositionRequest(
    string? ReportId,
    string? TargetState,
    int ExpectedRevision,
    string? Note);

public sealed record SystemFeedbackDispositionView(
    string Id,
    int Revision,
    string FromState,
    string ToState,
    string Note,
    DateTime CreatedAt);

public sealed record SystemFeedbackAdministrationView(
    SystemFeedbackView Report,
    int TriageRevision,
    IReadOnlyList<SystemFeedbackDispositionView> Dispositions,
    SystemFeedbackRetentionView Retention);

public sealed record SystemFeedbackAdministrationQuery(
    IReadOnlyCollection<string>? Ids = null,
    SystemFeedbackCategory? Category = null,
    SystemFeedbackImpact? Impact = null,
    SystemFeedbackState? State = null,
    DateTime? From = null,
    DateTime? To = null,
    int Limit = 100,
    bool IncludeArchived = true);

public sealed record SystemFeedbackAdministrationFindResult(
    IReadOnlyList<SystemFeedbackAdministrationView> Reports,
    SystemFeedbackProblem? Problem = null)
{
    public bool Ok => Problem is null;
}

public sealed record SystemFeedbackTransitionResult(
    SystemFeedbackAdministrationView? Report,
    SystemFeedbackProblem? Problem = null,
    string? CurrentState = null,
    int? CurrentRevision = null)
{
    public bool Ok => Problem is null;
}

public sealed record SystemFeedbackExportReport(
    string Id,
    DateTime CreatedAt,
    string Category,
    string Impact,
    string State,
    int TriageRevision,
    bool Redacted,
    string Summary,
    string Observed,
    string? Expected,
    IReadOnlyList<string> ReproductionSteps,
    IReadOnlyList<string> RelatedOperationIds,
    IReadOnlyList<SystemFeedbackProcedureView> RelatedProcedures,
    string SubmissionOperationId,
    IReadOnlyList<SystemFeedbackDispositionView> Dispositions);

public sealed record SystemFeedbackExportDocument(
    DateTime? SourceAsOfUtc,
    SystemFeedbackAdministrationQuery Filters,
    IReadOnlyList<SystemFeedbackExportReport> Reports);

public sealed record SystemFeedbackExportResult(
    SystemFeedbackExportDocument? Document,
    SystemFeedbackProblem? Problem = null)
{
    public bool Ok => Problem is null;
}

public sealed record SystemFeedbackRetentionActionRequest(
    string? ReportId,
    string? Action,
    int ExpectedRetentionRevision,
    string? Reference,
    string? Note,
    DateTime? AsOfUtc = null);

public sealed record SystemFeedbackRetentionQuery(
    DateTime? AsOfUtc,
    SystemFeedbackCategory? Category = null,
    SystemFeedbackState? State = null,
    bool IncludeArchived = false,
    int Limit = 100);

public sealed record SystemFeedbackRetentionActionView(
    string Id,
    int Revision,
    string Action,
    bool FromArchived,
    bool ToArchived,
    string FromHoldState,
    string ToHoldState,
    string? Reference,
    string Note,
    DateTime? EffectiveAsOf,
    DateTime CreatedAt);

public sealed record SystemFeedbackRetentionView(
    int RetentionRevision,
    DateTime? ArchivedAt,
    string HoldState,
    IReadOnlyList<SystemFeedbackRetentionActionView> Actions);

public sealed record SystemFeedbackRetentionCandidateView(
    string ReportId,
    string Category,
    string Impact,
    string State,
    DateTime ClosingAt,
    DateTime EligibleAt,
    DateTime? ArchivedAt,
    string HoldState,
    int RetentionRevision,
    string Summary);

public sealed record SystemFeedbackRetentionFindResult(
    IReadOnlyList<SystemFeedbackRetentionCandidateView> Reports,
    SystemFeedbackProblem? Problem = null)
{
    public bool Ok => Problem is null;
}

public sealed record SystemFeedbackRetentionTransitionResult(
    SystemFeedbackRetentionView? Retention,
    SystemFeedbackProblem? Problem = null,
    int? CurrentRevision = null,
    bool? CurrentArchived = null,
    string? CurrentHoldState = null)
{
    public bool Ok => Problem is null;
}

public interface ISystemFeedbackService
{
    Task<SystemFeedbackSubmitResult> SubmitAsync(
        SystemFeedbackSubmitRequest request,
        string intent,
        IReadOnlyList<string> proceduresUsed,
        CancellationToken cancellationToken = default);

    Task<SystemFeedbackFindResult> FindAsync(
        string? id = null,
        SystemFeedbackCategory? category = null,
        SystemFeedbackImpact? impact = null,
        SystemFeedbackState? state = null,
        DateTime? from = null,
        DateTime? to = null,
        int limit = 50,
        CancellationToken cancellationToken = default);
}

/// <summary>Developer-only triage and export reader. Deliberately absent from the MCP surface.</summary>
public interface ISystemFeedbackAdministrationService
{
    Task<SystemFeedbackAdministrationFindResult> FindAsync(
        SystemFeedbackAdministrationQuery query,
        CancellationToken cancellationToken = default);

    Task<SystemFeedbackTransitionResult> TransitionAsync(
        SystemFeedbackDispositionRequest request,
        CancellationToken cancellationToken = default);

    Task<SystemFeedbackExportResult> BuildExportAsync(
        SystemFeedbackAdministrationQuery query,
        IReadOnlySet<string> redactedReportIds,
        CancellationToken cancellationToken = default);
}

/// <summary>Developer-only, reversible retention administration. Deliberately absent from MCP.</summary>
public interface ISystemFeedbackRetentionService
{
    Task<SystemFeedbackRetentionFindResult> FindEligibleAsync(
        SystemFeedbackRetentionQuery query,
        CancellationToken cancellationToken = default);

    Task<SystemFeedbackRetentionTransitionResult> TransitionAsync(
        SystemFeedbackRetentionActionRequest request,
        CancellationToken cancellationToken = default);
}
