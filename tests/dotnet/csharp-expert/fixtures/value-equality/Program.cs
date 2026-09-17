var occupied = new HashSet<Coordinate>
{
    new(12, 34)
};

if (!occupied.Contains(new Coordinate(12, 34)))
{
    throw new InvalidOperationException("Equivalent coordinates did not compare equal.");
}

Console.WriteLine("equality-ok");

internal sealed class Coordinate
{
    public Coordinate(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }

    public int Y { get; }
}
