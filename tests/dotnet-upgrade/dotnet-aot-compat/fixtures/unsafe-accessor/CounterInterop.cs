using System.Reflection;

public static class CounterInterop
{
    public static int Read(VendorCounter counter)
    {
        var field = typeof(VendorCounter).GetField("_count", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(VendorCounter).FullName, "_count");
        return (int)field.GetValue(counter)!;
    }
}
