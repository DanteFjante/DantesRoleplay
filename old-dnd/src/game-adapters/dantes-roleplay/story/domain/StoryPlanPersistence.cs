namespace DantesRoleplay.Story;

/// <summary>EF-mapped durable state. It remains outside the world/entity graph by design.</summary>
public sealed class StoryPlanRun
{
    public required string Id { get; set; }
    public required string RequestToken { get; set; }
    public required string CampaignId { get; set; }
    public required string Objective { get; set; }
    public required string PlanJson { get; set; }
    public required string PrincipalId { get; set; }
    public required string PolicyRevision { get; set; }
    public string Status { get; set; } = StoryPlanStatus.Pending;
    public int Revision { get; set; } = 1;
    public int NextStepIndex { get; set; }
    public int CompletedStepCount { get; set; }
    public bool CancelRequested { get; set; }
    public string StopCode { get; set; } = string.Empty;
    public string StopMessage { get; set; } = string.Empty;
    public string? HandoffJson { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ICollection<StoryPlanStepRun> Steps { get; set; } = new List<StoryPlanStepRun>();
}

public sealed class StoryPlanStepRun
{
    public required string StoryPlanId { get; set; }
    public int StepIndex { get; set; }
    public required string StepId { get; set; }
    public required string Kind { get; set; }
    public required string Intent { get; set; }
    public required string RoleEntityIdsJson { get; set; }
    public required string InputJson { get; set; }
    public string Status { get; set; } = StoryPlanStepStatus.Pending;
    public string ProcedureEvidenceJson { get; set; } = "[]";
    public string MechanicId { get; set; } = string.Empty;
    public int? MechanicVersion { get; set; }
    public string ActionOperationId { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public StoryPlanRun? StoryPlan { get; set; }
}
