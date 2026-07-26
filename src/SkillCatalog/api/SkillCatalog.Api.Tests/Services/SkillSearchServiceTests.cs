using SkillCatalog.Api.Options;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Tests.Services;

public sealed class SkillSearchServiceTests
{
    private static SkillSearchService Create() { var root=FindRoot(); var options=new SkillCatalogOptions { RepositoryRoot=root }; return new(new CatalogSnapshotProvider(options)); }
    [Theory] [InlineData("BLAZOR")] [InlineData("blazor!")] [InlineData("blaz")] public void Search_is_case_punctuation_and_partial_tolerant(string query) { var result=Create().Search(query,null,1,100); Assert.NotEmpty(result.Items); }
    [Fact] public void Filters_intersect_and_paging_is_stable() { var search=Create(); var page1=search.Search("sdk","dotnet",1,2); var page2=search.Search("sdk","dotnet",2,2); Assert.All(page1.Items.Concat(page2.Items),x=>Assert.Equal("dotnet",x.Plugin)); Assert.Empty(page1.Items.Select(x=>x.Name).Intersect(page2.Items.Select(x=>x.Name))); }
    private static string FindRoot() { var dir=new DirectoryInfo(AppContext.BaseDirectory); while(dir is not null&&!Directory.Exists(Path.Combine(dir.FullName,"plugins")))dir=dir.Parent; return dir?.FullName ?? throw new DirectoryNotFoundException(); }
}
