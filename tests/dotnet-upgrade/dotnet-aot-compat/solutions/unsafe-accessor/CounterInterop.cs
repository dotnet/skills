using System.Runtime.CompilerServices;

public static class CounterInterop
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_count")]
    private static extern ref int GetCountField(VendorCounter counter);

    public static int Read(VendorCounter counter) => GetCountField(counter);
}
