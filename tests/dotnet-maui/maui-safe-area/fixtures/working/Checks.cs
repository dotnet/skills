public static class Checks
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    public static void Main()
    {
        var grid = ChatLayout.Create();
        Require(grid.ColumnSpacing == 10, "composer columns have wrong spacing");
        Require(grid.SafeAreaEdges == new Microsoft.Maui.SafeAreaEdges(
            Microsoft.Maui.SafeAreaRegions.Container, Microsoft.Maui.SafeAreaRegions.Container,
            Microsoft.Maui.SafeAreaRegions.Container, Microsoft.Maui.SafeAreaRegions.SoftInput),
            "keyboard/screen edge policy changed");
        Console.WriteLine("PASS: behavior-contract");
    }
}
