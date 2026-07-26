using System.IO.Compression;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Tests.Services;

public sealed class SkillPackageServiceTests
{
    [Fact] public void Archive_is_repeatable_confined_and_contains_manifest() { var root=FindRoot();var options=new SkillCatalogOptions{RepositoryRoot=root};var provider=new CatalogSnapshotProvider(options);var skill=provider.Snapshot.Skills.First(x=>x.Plugin=="dotnet"&&x.Name=="setup-local-sdk");var service=new SkillPackageService(provider,options);foreach(var bytes in new[]{service.Create(skill),service.Create(skill)}){using var archive=new ZipArchive(new MemoryStream(bytes));Assert.Contains(archive.Entries,x=>x.FullName=="SKILL.md");Assert.Contains(archive.Entries,x=>x.FullName=="skill-package.json");Assert.All(archive.Entries,x=>Assert.DoesNotContain("..",x.FullName));}}
    private static string FindRoot(){var dir=new DirectoryInfo(AppContext.BaseDirectory);while(dir is not null&&!Directory.Exists(Path.Combine(dir.FullName,"plugins")))dir=dir.Parent;return dir?.FullName??throw new DirectoryNotFoundException();}
}
