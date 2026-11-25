public static class Checks
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    public static void Main()
    {
        var resources = new Microsoft.Maui.Controls.ResourceDictionary();
        var styles = new Microsoft.Maui.Controls.ResourceDictionary { ["Primary"] = "brand" };
        resources.MergedDictionaries.Add(styles);
        var light = new Microsoft.Maui.Controls.ResourceDictionary { ["Text"] = "black" };
        var dark = new Microsoft.Maui.Controls.ResourceDictionary { ["Text"] = "white" };
        var manager = new ThemeManager();
        manager.Apply(resources, light);
        Require(resources.MergedDictionaries.Count == 2 &&
            resources.MergedDictionaries.Contains(styles) && resources.MergedDictionaries.Contains(light),
            "light theme was not applied alongside styles");
        manager.Apply(resources, dark);
        Require(resources.MergedDictionaries.Count == 2 &&
            resources.MergedDictionaries.Contains(styles) && resources.MergedDictionaries.Contains(dark) &&
            !resources.MergedDictionaries.Contains(light), "dark theme was not applied or retained the old theme");
        manager.Apply(resources, light);
        Require(resources.MergedDictionaries.Count == 2 &&
            resources.MergedDictionaries.Contains(styles) && resources.MergedDictionaries.Contains(light) &&
            !resources.MergedDictionaries.Contains(dark), "theme swap destroyed styles or retained old themes");
        Console.WriteLine("PASS: behavior-contract");
    }
}
