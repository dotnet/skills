using System;
using System.Reflection;

namespace Contoso.Core;

public static class BuildMarker
{
    public const string Name = "Contoso";

    public static string JsonVersion =>
        typeof(Newtonsoft.Json.JsonConvert).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("Newtonsoft.Json has no version metadata.");
}
