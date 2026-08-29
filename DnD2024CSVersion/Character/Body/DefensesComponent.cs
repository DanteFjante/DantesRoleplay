namespace DnD2024CSVersion.Character.Body;

public sealed record DefensesComponent
{
    public IReadOnlySet<string> DamageResistanceIds { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> DamageImmunityIds { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> DamageVulnerabilityIds { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> ConditionImmunityIds { get; init; } = new HashSet<string>();
}
