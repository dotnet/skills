using System.IO.Compression;
using System.Net;

namespace SkillCatalog.Api.ContractTests;

public sealed class SkillDownloadEndpointTests : IClassFixture<CatalogApiFactory>
{
    private readonly HttpClient _client;
    public SkillDownloadEndpointTests(CatalogApiFactory factory) => _client=factory.CreateClient();
    [Fact] public async Task Download_is_zip_with_manifest_and_confined_entries() { var response=await _client.GetAsync("/api/skills/dotnet/setup-local-sdk/download"); response.EnsureSuccessStatusCode(); Assert.Equal("application/zip",response.Content.Headers.ContentType?.MediaType); Assert.Contains("setup-local-sdk",response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName); using var archive=new ZipArchive(await response.Content.ReadAsStreamAsync()); Assert.Contains(archive.Entries,x=>x.FullName=="SKILL.md"); Assert.Contains(archive.Entries,x=>x.FullName=="skill-package.json"); using var manifestReader=new StreamReader(archive.GetEntry("skill-package.json")!.Open()); Assert.Contains("Revision",manifestReader.ReadToEnd()); Assert.All(archive.Entries,x=>Assert.DoesNotContain("..",x.FullName)); }
    [Fact] public async Task Missing_download_returns_not_found() => Assert.Equal(HttpStatusCode.NotFound,(await _client.GetAsync("/api/skills/dotnet/missing/download")).StatusCode);
}
