var plugin = PluginFactory.Create(new PluginDescriptor(typeof(SamplePlugin)));

if (plugin.Run() != "ready")
{
    throw new InvalidOperationException("Activation behavior changed.");
}

Console.WriteLine("PASS");
