using System.Reflection;

public static class PluginCatalog
{
    public static IReadOnlyList<Type> Discover(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        return assembly.GetTypes()
            .Where(type => type.Name.EndsWith("Plugin", StringComparison.Ordinal))
            .ToArray();
    }
}
