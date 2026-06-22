namespace Contoso.Services;

public sealed class ServiceRegistry
{
    private readonly Dictionary<Type, object> _services = new();

    public void Register<T>(T service) where T : class
    {
        ArgumentNullException.ThrowIfNull(service);
        _services[typeof(T)] = service;
    }

    public T? Resolve<T>() where T : class =>
        _services.TryGetValue(typeof(T), out var svc) ? (T)svc : null;

    public T ResolveRequired<T>() where T : class =>
        Resolve<T>() ?? throw new InvalidOperationException($"{typeof(T).Name} not registered.");

    public IReadOnlyList<object> GetAll() => [.. _services.Values];

    public void Remove<T>() where T : class => _services.Remove(typeof(T));

    public void Clear() => _services.Clear();
}
