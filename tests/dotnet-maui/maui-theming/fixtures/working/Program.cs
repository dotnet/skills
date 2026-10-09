using Microsoft.Maui.Controls;
public sealed class ThemeManager
{
    private ResourceDictionary? _activeTheme;
    public void Apply(ResourceDictionary resources, ResourceDictionary theme)
    {
        if (_activeTheme is not null)
            resources.MergedDictionaries.Remove(_activeTheme);
        resources.MergedDictionaries.Add(theme);
        _activeTheme = theme;
    }
}
