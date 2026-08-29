namespace DnD2024CSVersion.Character.CurrentState;

/// <summary>
/// Mutable state for one granted feature. The feature definition owns all rule behavior.
/// </summary>
public sealed record FeatureState
{
    public required string FeatureId { get; init; }
    public string? SourceId { get; init; }
    public bool IsActive { get; set; }
    public IReadOnlyDictionary<string, string> Values { get; init; } =
        new Dictionary<string, string>();
}
