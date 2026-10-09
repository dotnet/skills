using System.Diagnostics.CodeAnalysis;

public interface IPlugin
{
    string Run();
}

public sealed class SamplePlugin : IPlugin
{
    public string Run() => "ready";
}

public sealed class PluginDescriptor
{
    public PluginDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type implementationType)
    {
        ImplementationType = implementationType;
    }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type ImplementationType { get; }
}

public static class PluginFactory
{
    public static IPlugin Create(PluginDescriptor descriptor)
        => (IPlugin)Activator.CreateInstance(descriptor.ImplementationType)!;
}
