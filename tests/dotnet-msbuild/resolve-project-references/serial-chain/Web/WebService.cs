using Api;

namespace Web;

public class WebService
{
    public ApiService Dependency { get; } = new();
}
