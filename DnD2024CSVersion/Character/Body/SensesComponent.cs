namespace DnD2024CSVersion.Character.Body;

public sealed record SensesComponent
{
    public IReadOnlyList<SenseRange> Senses { get; init; } = [];
}
