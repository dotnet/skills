// Test asset: Developer attempts to suppress AOT warnings using #pragma
// Expected: Skill should catch that #pragma doesn't work for trim/AOT warnings

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace MyApp;

public class PluginLoader
{
    #pragma warning disable IL2026
    public object LoadHandler(string typeName)
    {
        var type = Type.GetType(typeName);
        return Activator.CreateInstance(type!);
    }
    #pragma warning restore IL2026

    #pragma warning disable IL3050
    public object CreateGeneric(Type elementType)
    {
        var listType = typeof(List<>).MakeGenericType(elementType);
        return Activator.CreateInstance(listType)!;
    }
    #pragma warning restore IL3050
}
