using System.Net;

namespace SkillCatalog.Api.ContractTests;

public sealed class OpenApiContractTests : IClassFixture<CatalogApiFactory>
{
    private readonly HttpClient _client;
    public OpenApiContractTests(CatalogApiFactory factory)=>_client=factory.CreateClient();
    [Fact] public async Task Generated_contract_contains_all_authoritative_routes() { var json=await _client.GetStringAsync("/openapi/v1.json"); foreach(var route in new[]{"/api/catalog","/api/skills","/api/skills/{plugin}/{skill}","/api/skills/{plugin}/{skill}/resources","/api/skills/{plugin}/{skill}/download"}) Assert.Contains(route,json); }
}
