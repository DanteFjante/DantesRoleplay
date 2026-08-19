namespace DantesRoleplay.Events;

public enum SubscriptionStatus { Draft, Active, Disabled, Archived }
public enum SubscriptionMode { Guard, Reaction }

/// <summary>A versioned registration only. Slice 2 does not execute it.</summary>
public sealed class Subscription
{
    public required string Id { get; set; }
    public required string Category { get; set; }
    public string Scope { get; set; } = string.Empty;
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Draft;
    public int CurrentVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<SubscriptionVersion> Versions { get; set; } = new List<SubscriptionVersion>();
}

public sealed class SubscriptionVersion
{
    public int Id { get; set; }
    public required string SubscriptionId { get; set; }
    public int Version { get; set; }
    public required string EventTypeId { get; set; }
    public required string EventMechanicId { get; set; }
    public SubscriptionMode Mode { get; set; }
    public int Order { get; set; }
    public string FixedRoleEntityIdsJson { get; set; } = "{}";
    public string TrackedEntityIdsJson { get; set; } = "[]";
    public string PayloadEqualsJson { get; set; } = "{}";
    public int MaxExecutionsPerChain { get; set; } = 1;
    public string ChangeNote { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = "llm";
    public string SourceHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Subscription? Subscription { get; set; }
}
