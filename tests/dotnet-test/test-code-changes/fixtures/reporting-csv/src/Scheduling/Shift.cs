namespace Scheduling;

public sealed record Shift(int StartHour, int EndHour)
{
    public int Length => EndHour - StartHour;
}
