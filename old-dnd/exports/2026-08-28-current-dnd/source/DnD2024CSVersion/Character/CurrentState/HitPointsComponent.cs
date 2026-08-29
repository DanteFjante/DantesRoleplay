namespace DnD2024CSVersion.Character.CurrentState;

public sealed record HitPointsComponent
{
    public int Maximum { get; set; }
    public int Current { get; set; }
    public int Temporary { get; set; }
}
