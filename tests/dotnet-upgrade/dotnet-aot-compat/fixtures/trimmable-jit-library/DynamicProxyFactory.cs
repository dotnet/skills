using System.Reflection;
using System.Reflection.Emit;

public static class DynamicProxyFactory
{
    public static Type CreateProxyType(string name)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("GeneratedProxies"),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("GeneratedProxies");
        return module.DefineType(name, TypeAttributes.Public).CreateTypeInfo()!.AsType();
    }
}
