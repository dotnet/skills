// Test asset: Mix of safe (reference type) and unsafe (value type) MakeGenericType calls
// Expected: Skill should distinguish between safe and unsafe usages

using System;
using System.Collections.Generic;

namespace MyApp;

public class ConverterFactory
{
    // This is safe — entityType is always a class (reference type)
    public object CreateRepository(Type entityType)
    {
        var repoType = typeof(Repository<>).MakeGenericType(entityType);
        return Activator.CreateInstance(repoType)!;
    }

    // This is UNSAFE — numericType could be int, float, double (value types)
    public object CreateAggregator(Type numericType)
    {
        var aggType = typeof(Aggregator<>).MakeGenericType(numericType);
        return Activator.CreateInstance(aggType)!;
    }

    // This is safe — constrained to class
    public IHandler<T> CreateHandler<T>() where T : class
    {
        var handlerType = typeof(DefaultHandler<>).MakeGenericType(typeof(T));
        return (IHandler<T>)Activator.CreateInstance(handlerType)!;
    }
}

public class Repository<T> where T : class { }
public class Aggregator<T> where T : struct { }
public interface IHandler<T> { }
public class DefaultHandler<T> : IHandler<T> where T : class { }
