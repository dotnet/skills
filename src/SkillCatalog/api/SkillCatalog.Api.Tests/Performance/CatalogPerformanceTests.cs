using System.Diagnostics;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Tests.Performance;

public sealed class CatalogPerformanceTests
{
    [Fact] public void Repeated_search_stays_below_budget() { var root=FindRoot(); var search=new SkillSearchService(new CatalogSnapshotProvider(new SkillCatalogOptions{RepositoryRoot=root})); var samples=new List<double>(); for(var i=0;i<100;i++){var timer=Stopwatch.StartNew();search.Search("dotnet",null,1,24);samples.Add(timer.Elapsed.TotalMilliseconds);} samples.Sort(); Assert.True(samples[94]<250,$"p95 was {samples[94]:F2}ms"); }
    private static string FindRoot(){var dir=new DirectoryInfo(AppContext.BaseDirectory);while(dir is not null&&!Directory.Exists(Path.Combine(dir.FullName,"plugins")))dir=dir.Parent;return dir?.FullName??throw new DirectoryNotFoundException();}
}
