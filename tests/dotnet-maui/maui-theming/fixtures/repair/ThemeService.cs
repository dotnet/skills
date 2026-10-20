namespace ThemeSample;

public static class ThemeService
{
    public static void ApplyTheme(ResourceDictionary theme)
    {
        var merged = Application.Current!.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(theme);
    }
}
