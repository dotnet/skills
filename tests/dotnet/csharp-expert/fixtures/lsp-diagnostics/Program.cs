namespace DiagnosticsFixture;

public static class Program
{
    public static int CountActive(IEnumerable<string> values)
    {
        int count = values.Where(value => value.Length > 0).ToList();
        return count;
    }
}
