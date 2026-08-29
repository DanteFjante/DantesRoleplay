namespace DnD2024CSVersion.Character.Inventory;

public sealed record CurrencyComponent
{
    public int Copper { get; set; }
    public int Silver { get; set; }
    public int Electrum { get; set; }
    public int Gold { get; set; }
    public int Platinum { get; set; }
}
