namespace DnD2024CSVersion.Character.CurrentState;

public sealed record DeathSavesComponent
{
    public int Successes { get; set; }
    public int Failures { get; set; }
    public bool IsStable { get; set; }
}
