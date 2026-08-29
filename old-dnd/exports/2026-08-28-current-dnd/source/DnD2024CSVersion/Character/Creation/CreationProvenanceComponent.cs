namespace DnD2024CSVersion.Character.Creation;

/// <summary>
/// The immutable selections and inputs from which the initial character state was produced.
/// </summary>
public sealed record CreationProvenanceComponent
{
    public IReadOnlyList<CreationDecision> Decisions { get; init; } = [];
}
