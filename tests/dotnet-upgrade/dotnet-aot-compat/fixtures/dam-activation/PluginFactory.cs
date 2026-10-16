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
    public PluginDescriptor(Type implementationType)
    {
        ImplementationType = implementationType;
    }

    public Type ImplementationType { get; }
}

public static class PluginFactory
{
    public static IPlugin Create(PluginDescriptor descriptor)
        => (IPlugin)Activator.CreateInstance(descriptor.ImplementationType)!;
}
