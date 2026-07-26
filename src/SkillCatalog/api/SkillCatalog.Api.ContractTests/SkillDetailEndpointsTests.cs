using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SkillCatalog.Api.Models;

namespace SkillCatalog.Api.ContractTests;

public sealed class SkillDetailEndpointsTests : IClassFixture<CatalogApiFactory>
{
    private readonly HttpClient _client;
    public SkillDetailEndpointsTests(CatalogApiFactory factory) => _client=factory.CreateClient();
    [Fact] public async Task Detail_contains_markdown_metadata_and_resources() { var detail=await _client.GetFromJsonAsync<SkillDetail>("/api/skills/dotnet/setup-local-sdk"); Assert.NotNull(detail); Assert.NotEmpty(detail.Markdown); Assert.Equal("dotnet",detail.Plugin); Assert.StartsWith("https://",detail.SourceUrl); }
    [Fact] public async Task Missing_skill_returns_not_found() => Assert.Equal(HttpStatusCode.NotFound,(await _client.GetAsync("/api/skills/dotnet/missing-skill")).StatusCode);
}
