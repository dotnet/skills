namespace Dispatch;

public static class Policy
{
    public static int Authorize(int amount, bool approved, bool locked, bool expired,
        bool guest, bool flagged, bool suspended)
    {
        if (amount < 0)
            return -1;
        if (!approved)
            return -2;
        if (locked)
            return -3;
        if (expired)
            return -4;
        if (guest)
            return -5;
        if (flagged)
            return -6;
        if (suspended)
            return -7;
        return amount;
    }

    public static string Label(bool express)
    {
        if (express)
            return "Express";
        return "Standard";
    }
}
