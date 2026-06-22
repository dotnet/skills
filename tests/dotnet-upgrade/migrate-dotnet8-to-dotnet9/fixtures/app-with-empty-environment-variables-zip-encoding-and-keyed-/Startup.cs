using Microsoft.Extensions.DependencyInjection;
class Startup
{
    void Configure(IServiceCollection services)
    {
        services.AddSingleton<ICache, RedisCache>();
        // Controller expects [FromKeyedServices("cache")] ICache
    }
}
interface ICache { }
class RedisCache : ICache { }
