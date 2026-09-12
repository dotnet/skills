using Core;

namespace Api;

public class ApiService
{
    public CoreService Dependency { get; } = new();
}
