namespace Stays;

public readonly record struct DateRange
{
    public DateRange(DateOnly start, DateOnly end)
    {
        if (end <= start)
        {
            throw new ArgumentException("A stay must last at least one night.", nameof(end));
        }

        Start = start;
        End = end;
    }

    public DateOnly Start { get; }

    public DateOnly End { get; }

    public int Nights => End.DayNumber - Start.DayNumber;

    public bool Contains(DateOnly day) => day >= Start && day < End;

    public bool Overlaps(DateRange other) => Start <= other.End && other.Start <= End;
}
