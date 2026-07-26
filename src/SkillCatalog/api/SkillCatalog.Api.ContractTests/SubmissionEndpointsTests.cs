using System.Net;
using System.Net.Http.Json;
using System.Text;
using SkillCatalog.Api.Models;

namespace SkillCatalog.Api.ContractTests;

public sealed class SubmissionEndpointsTests : IClassFixture<CatalogApiFactory>
{
    private readonly HttpClient _client;
    public SubmissionEndpointsTests(CatalogApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Inspect_accepts_single_skill_markdown()
    {
        using var form = Form("---\nname: uploaded-skill\ndescription: Valid uploaded skill.\n---\n# Uploaded\n## Workflow\n1. Inspect.\n2. Report.\n## Validation\nConfirm.");
        var response = await _client.PostAsync("/api/submissions/inspect", form);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<UploadInspection>();
        Assert.NotNull(result);
        Assert.True(result.Valid);
        Assert.Equal("uploaded-skill", result.Preview.Name);
    }

    [Fact]
    public async Task Normalize_revalidates_and_blocks_invalid_upload()
    {
        using var form = Form("not valid frontmatter");
        var response = await _client.PostAsync("/api/submissions/normalize", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
    private static MultipartFormDataContent Form(string markdown) { var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(markdown)), "file", "SKILL.md"); return form; }
}
