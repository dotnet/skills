using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Tests.Services;

public sealed class SafeRepositoryPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"skill-catalog-{Guid.NewGuid():N}");
    public SafeRepositoryPathTests() => Directory.CreateDirectory(_root);
    [Fact] public void Resolve_keeps_child_inside_root() => Assert.StartsWith(Path.GetFullPath(_root), SafeRepositoryPath.Resolve(_root, "child", "file.txt"));
    [Fact] public void Resolve_rejects_traversal() => Assert.Throws<InvalidOperationException>(() => SafeRepositoryPath.Resolve(_root, "..", "outside.txt"));
    [Fact] public void Regular_file_honors_size_limit() { var file=Path.Combine(_root,"small.txt");File.WriteAllText(file,"safe");Assert.True(SafeRepositoryPath.IsSafeRegularFile(_root,file,10));Assert.False(SafeRepositoryPath.IsSafeRegularFile(_root,file,2)); }
    public void Dispose() => Directory.Delete(_root, true);
}
