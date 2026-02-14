// Test asset: DynamicallyAccessedMembers annotation only on leaf, not propagated
// Expected: Skill should trace the full chain and identify missing annotations

using System;
using System.Diagnostics.CodeAnalysis;

namespace MyApp;

public class ServiceFactory
{
    // Has annotation — good
    public object CreateInstance(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type serviceType)
    {
        return Activator.CreateInstance(serviceType)!;
    }

    // MISSING annotation on serviceType parameter — will produce warning
    public object CreateFromConfig(Type serviceType)
    {
        // Calls annotated method but doesn't propagate the annotation
        return CreateInstance(serviceType);
    }

    // MISSING annotation on T — will produce warning
    public T CreateService<T>() where T : class
    {
        return (T)CreateInstance(typeof(T));
    }
}

public class ServiceRegistry
{
    private readonly ServiceFactory _factory = new();

    // This is the entry point — developer may not realize annotations
    // need to propagate all the way here
    public object Resolve(Type type)
    {
        return _factory.CreateFromConfig(type);
    }
}
