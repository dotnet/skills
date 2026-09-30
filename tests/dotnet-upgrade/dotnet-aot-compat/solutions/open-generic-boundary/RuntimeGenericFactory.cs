using System.Diagnostics.CodeAnalysis;

public static class RuntimeGenericFactory
{
    [RequiresDynamicCode("Creates a closed generic type that is known only at runtime.")]
    [RequiresUnreferencedCode("The runtime-selected generic type and constructor cannot be statically preserved.")]
    public static object Create(Type openGenericType, Type argumentType)
    {
        var closedType = openGenericType.MakeGenericType(argumentType);
        return Activator.CreateInstance(closedType)!;
    }
}
