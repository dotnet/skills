using System.Net;

namespace SkillCatalog.Api.ContractTests;

public sealed class SecurityTests : IClassFixture<CatalogApiFactory>
{
    private readonly HttpClient _client;
    public SecurityTests(CatalogApiFactory factory) => _client=factory.CreateClient(new() { AllowAutoRedirect=false });
    [Fact] public async Task Responses_set_security_headers() { var response=await _client.GetAsync("/api/catalog"); response.EnsureSuccessStatusCode(); Assert.Equal("nosniff",response.Headers.GetValues("X-Content-Type-Options").Single()); Assert.Equal("no-referrer",response.Headers.GetValues("Referrer-Policy").Single()); }
    [Theory] [InlineData("../../appsettings.json")] [InlineData("%2e%2e%2fappsettings.json")] public async Task Resource_traversal_fails_closed(string path) { var response=await _client.GetAsync($"/api/skills/dotnet/setup-local-sdk/resources?path={path}"); Assert.Contains(response.StatusCode,new[]{HttpStatusCode.NotFound,HttpStatusCode.BadRequest}); }
}
