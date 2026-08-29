namespace DnD2024CSVersion.Character.Advancement;

public sealed record SubclassesComponent
{
    public IReadOnlyDictionary<string, string> SubclassIdsByClassId { get; init; } =
        new Dictionary<string, string>();
}
