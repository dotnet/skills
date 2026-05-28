using Microsoft.Extensions.DependencyInjection;
using System.Net;
class ApiController
{
    public ApiController([FromKeyedServices("cache")] ICache cache) { }
    void LogAgent(HttpListenerRequest request)
    {
        Console.WriteLine(request.UserAgent.Length);
    }
}
interface ICache { }
