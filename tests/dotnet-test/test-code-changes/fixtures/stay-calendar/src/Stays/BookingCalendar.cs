namespace Stays;

public sealed class BookingCalendar
{
    private readonly List<DateRange> _stays = [];

    public IReadOnlyList<DateRange> Stays => _stays;

    public bool TryBook(DateRange stay)
    {
        if (_stays.Any(existing => existing.Overlaps(stay)))
        {
            return false;
        }

        _stays.Add(stay);
        return true;
    }

    public bool IsOccupied(DateOnly night) => _stays.Any(stay => stay.Contains(night));
}
