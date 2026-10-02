using System.Diagnostics.CodeAnalysis;
using System.Reflection;

public static class PluginCatalog
{
    [RequiresUnreferencedCode(
        "Loads and scans a caller-selected assembly, so plugin types cannot be preserved statically.")]
    public static IReadOnlyList<Type> Discover(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        return assembly.GetTypes()
            .Where(type => type.Name.EndsWith("Plugin", StringComparison.Ordinal))
            .ToArray();
    }
}
