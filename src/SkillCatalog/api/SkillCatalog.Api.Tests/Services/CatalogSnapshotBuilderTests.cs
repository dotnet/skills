using SkillCatalog.Api.Options;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Tests.Services;

public sealed class CatalogSnapshotBuilderTests
{
    private static string FixtureRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../api/SkillCatalog.Api.Tests/Fixtures/Repository"));
    [Fact] public void Builds_valid_skills_and_reports_invalid_repository_entries() { var snapshot=new CatalogSnapshotBuilder(new SkillCatalogOptions()).Build(FixtureRoot); Assert.Contains(snapshot.Skills,x=>x.Name=="good-skill"); Assert.DoesNotContain(snapshot.Skills,x=>x.Name=="duplicate"); Assert.Contains(snapshot.Diagnostics,x=>x.Message.Contains("Missing SKILL.md")); Assert.Contains(snapshot.Diagnostics,x=>x.Message.Contains("Duplicate skill identity")); Assert.Contains(snapshot.Diagnostics,x=>x.Severity=="error"); }
    [Fact] public void Empty_catalog_returns_no_skills() { var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root,"plugins")); try { var snapshot=new CatalogSnapshotBuilder(new SkillCatalogOptions()).Build(root); Assert.Empty(snapshot.Skills); } finally { Directory.Delete(root,true); } }
}
