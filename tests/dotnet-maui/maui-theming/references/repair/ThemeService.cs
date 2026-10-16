namespace ThemeSample;

public static class ThemeService
{
    private static ResourceDictionary? _currentTheme;

    public static void ApplyTheme(ResourceDictionary theme)
    {
        var merged = Application.Current!.Resources.MergedDictionaries;
        if (_currentTheme is not null)
        {
            merged.Remove(_currentTheme);
        }

        merged.Add(theme);
        _currentTheme = theme;
    }
}
