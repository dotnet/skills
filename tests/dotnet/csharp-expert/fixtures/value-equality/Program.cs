var occupied = new HashSet<Coordinate>
{
    new(12, 34)
};

if (!occupied.Contains(new Coordinate(12, 34)))
{
    throw new InvalidOperationException("Equivalent coordinates did not compare equal.");
}

Console.WriteLine("equality-ok");
