namespace DantesRoleplay.Events;

/// <summary>A durable, versioned declaration of an event payload; it does not emit events.</summary>
public enum EventTypeStatus { Draft, Active, Deprecated, Archived }

public sealed class EventType
{
    public required string Id { get; set; }
    public required string Category { get; set; }
    public string Scope { get; set; } = string.Empty;
    public EventTypeStatus Status { get; set; } = EventTypeStatus.Draft;
    public int CurrentVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<EventTypeVersion> Versions { get; set; } = new List<EventTypeVersion>();
}

public sealed class EventTypeVersion
{
    public int Id { get; set; }
    public required string EventTypeId { get; set; }
    public int Version { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public required string PayloadSchema { get; set; }
    public string ChangeNote { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = "llm";
    public string SourceHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public EventType? EventType { get; set; }
}
