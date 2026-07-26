using System.Net;
using System.Net.Http.Json;
using SkillCatalog.Api.Models;

namespace SkillCatalog.Api.ContractTests;

public sealed class CatalogEndpointsTests : IClassFixture<CatalogApiFactory>
{
    private readonly HttpClient _client;
    public CatalogEndpointsTests(CatalogApiFactory factory) => _client = factory.CreateClient();
    [Fact] public async Task Catalog_exposes_repository_snapshot() { var catalog=await _client.GetFromJsonAsync<CatalogSummary>("/api/catalog"); Assert.NotNull(catalog); Assert.True(catalog.SkillCount >= 90); Assert.NotEmpty(catalog.Revision); Assert.Contains("dotnet", catalog.Plugins); }
    [Fact] public async Task Search_combines_query_filter_and_paging() { var result=await _client.GetFromJsonAsync<PagedSkills>("/api/skills?q=sdk&plugin=dotnet&page=1&pageSize=2"); Assert.NotNull(result); Assert.InRange(result.Items.Count,1,2); Assert.All(result.Items,x=>Assert.Equal("dotnet",x.Plugin)); }
    [Fact] public async Task Empty_search_is_successful() { var result=await _client.GetFromJsonAsync<PagedSkills>("/api/skills?q=this-will-never-exist"); Assert.NotNull(result); Assert.Empty(result.Items); Assert.Equal(0,result.Total); }
}
