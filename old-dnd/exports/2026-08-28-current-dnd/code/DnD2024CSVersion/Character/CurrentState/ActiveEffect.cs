namespace DnD2024CSVersion.Character.CurrentState;

/// <summary>
/// One applied effect. Its definition owns the rule behavior; this record stores only runtime state.
/// </summary>
public sealed record ActiveEffect
{
    public required string InstanceId { get; init; }
    public required string EffectDefinitionId { get; init; }
    public string? SourceId { get; init; }
    public int? RemainingRounds { get; set; }
    public IReadOnlyDictionary<string, string> State { get; init; } =
        new Dictionary<string, string>();
}
