namespace Inventory;

public sealed record Stock(int Available, int Minimum)
{
    public bool NeedsReorder => Available < Minimum;
}
