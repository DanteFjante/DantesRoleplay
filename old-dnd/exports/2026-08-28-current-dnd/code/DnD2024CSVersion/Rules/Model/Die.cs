namespace DnD2024CSVersion.Rules.Model;

/// <summary>
/// One kind of polyhedral die.
/// </summary>
public readonly record struct Die
{
    public Die(int sides)
    {
        if (sides < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(sides), "A die must have at least two sides.");
        }

        Sides = sides;
    }

    public int Sides { get; }

    public static Die D4 => new(4);
    public static Die D6 => new(6);
    public static Die D8 => new(8);
    public static Die D10 => new(10);
    public static Die D12 => new(12);
    public static Die D20 => new(20);
    public static Die D100 => new(100);

    public override string ToString() => $"d{Sides}";
}
